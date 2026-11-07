using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Sales.Domain;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Sales.Services;

public sealed class SalesService : ISalesService
{
    private const int MaxLines = 50;
    private static readonly string[] AllowedMethods = { "CASH", "QRIS", "TRANSFER" };

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IInventoryService _inventory;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<SalesService> _logger;
    private readonly IUnitOfWork _uow;

    public SalesService(
        AppDbContext db,
        IBusinessContext context,
        IInventoryService inventory,
        IAuditLogService audit,
        TimeProvider time,
        ILogger<SalesService> logger,
        IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _inventory = inventory;
        _audit = audit;
        _time = time;
        _logger = logger;
        _uow = uow;
    }

    public async Task<Result<SaleReceiptDto>> ConfirmAsync(ConfirmSaleRequest request, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.IsAdmin || businessId is null)
        {
            return Fail(ErrorKind.Forbidden, "Hanya akun warung yang dapat memakai POS.");
        }

        var rawLines = request.Lines ?? Array.Empty<SaleLineInput>();
        if (rawLines.Count == 0)
        {
            return Fail(ErrorKind.Validation, "Keranjang masih kosong.");
        }

        if (rawLines.Count > MaxLines)
        {
            return Fail(ErrorKind.Validation, $"Maksimal {MaxLines} baris per transaksi.");
        }

        var method = (request.PaymentMethod ?? string.Empty).Trim().ToUpperInvariant();
        if (!AllowedMethods.Contains(method))
        {
            return Fail(ErrorKind.Validation, "Metode pembayaran tidak valid.");
        }

        if (rawLines.Any(l => l.QtyBase <= 0 || l.QtyBase > 1_000_000))
        {
            return Fail(ErrorKind.Validation, "Jumlah harus 1–1.000.000 per produk.");
        }

        // Baris dengan produk sama digabung.
        var lines = rawLines
            .GroupBy(l => l.ProductId)
            .Select(g => new SaleLineInput(g.Key, g.Sum(x => x.QtyBase)))
            .ToList();

        // Anti-duplikat: kunci yang sudah pernah dipakai mengembalikan struk yang sama.
        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key is { Length: > 64 })
        {
            key = key[..64];
        }

        // Penyelesaian transaksi = SATU unit kerja (kunci tulis diambil di awal): cek kunci operasi, validasi harga/stok,
        // kurangi stok, buat Sale + SaleItems + StockMovements + audit, lalu commit. Gagal di langkah mana pun = rollback penuh.
        // Dua permintaan dengan kunci sama yang datang serentak diproses bergantian: yang kedua menemukan penjualan yang sudah
        // ada dan menerima struk yang sama (bukan error, bukan penjualan kedua).
        return await _uow.ExecuteAsync<SaleReceiptDto>(async token =>
        {
            if (key is not null)
            {
                var existing = await _db.Sales.AsNoTracking().FirstOrDefaultAsync(s => s.IdempotencyKey == key, token);
                if (existing is not null)
                {
                    return await GetReceiptAsync(existing.Id, token);
                }
            }

            // Validasi harga dan stok SEBELUM mengubah apa pun (stok yang boleh dijual = fisik - dicadangkan - kedaluwarsa).
            var sellable = (await _inventory.GetSellableByIdsAsync(lines.Select(l => l.ProductId).ToList(), token))
                .ToDictionary(s => s.ProductId);

            foreach (var line in lines)
            {
                if (!sellable.TryGetValue(line.ProductId, out var info))
                {
                    return Fail(ErrorKind.NotFound, "Produk tidak ditemukan atau sudah tidak aktif.");
                }

                if (info.SellingPriceBase <= 0)
                {
                    return Fail(ErrorKind.Validation, $"Harga jual {info.Name} belum diatur.");
                }

                if (info.QtyBase < line.QtyBase)
                {
                    return Fail(ErrorKind.Conflict, $"Stok {info.Name} tidak cukup (tersisa {info.QtyBase}).");
                }
            }

            // Uang diterima (tunai) harus cukup; non-tunai dianggap pas.
            var expectedTotal = lines.Sum(l => Money.Round(sellable[l.ProductId].SellingPriceBase * l.QtyBase));
            decimal? paid = null;
            if (method == "CASH" && request.PaidAmount is decimal given)
            {
                if (given < expectedTotal)
                {
                    return Fail(ErrorKind.Validation, $"Uang diterima kurang dari total belanja ({Rupiah.Format(expectedTotal)}).");
                }

                if (given > 1_000_000_000m)
                {
                    return Fail(ErrorKind.Validation, "Jumlah uang diterima tidak valid.");
                }

                paid = Money.Round(given);
            }

            var sale = new Sale
            {
                BusinessId = businessId.Value,
                Number = "TMP",
                SoldAt = _time.GetUtcNow().UtcDateTime,
                PaymentMethod = method,
                PaidAmount = paid,
                Status = SaleStatus.Confirmed,
                IdempotencyKey = key,
                CreatedByUserId = _context.UserId
            };
            _db.Sales.Add(sale);
            await _db.SaveChangesAsync(token);
            sale.Number = $"S{sale.Id:D6}";

            var total = 0m;
            foreach (var line in lines)
            {
                var info = sellable[line.ProductId];
                var stockOut = await _inventory.StockOutAsync(
                    new StockOutRequest(line.ProductId, line.QtyBase, MovementType.SaleOut, "Sale", sale.Id, sale.Number),
                    token);

                if (!stockOut.Succeeded)
                {
                    return Fail(stockOut.Kind, stockOut.Error ?? "Stok gagal dikurangi.");
                }

                // Harga dan modal dibekukan pada saat transaksi.
                var unitCost = stockOut.Value!.UnitCostBase;
                var subtotal = Money.Round(info.SellingPriceBase * line.QtyBase);
                sale.Items.Add(new SaleItem
                {
                    BusinessId = businessId.Value,
                    ProductId = line.ProductId,
                    ProductName = info.Name,
                    QtyBase = line.QtyBase,
                    UnitPrice = info.SellingPriceBase,
                    UnitCost = unitCost,
                    Subtotal = subtotal,
                    CostTotal = Money.Round(unitCost * line.QtyBase)
                });
                total += subtotal;
            }

            sale.TotalAmount = total;
            sale.PaidAmount ??= total;
            _audit.Record("SALE_CONFIRMED", "Sale", sale.Id.ToString(), $"{sale.Number}; total={total}");
            await _db.SaveChangesAsync(token);

            return await GetReceiptAsync(sale.Id, token);
        }, ct);
    }

    public async Task<Result<int>> RecordConsumerOrderSaleAsync(
        int sellerBusinessId, int consumerOrderId, string orderNumber, DateTime soldAtUtc, string paymentMethod,
        IReadOnlyList<ConsumerSaleLine> lines, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || lines.Count == 0)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Data penjualan pesanan tidak valid.");
        }

        return await _uow.ExecuteAsync<int>(async token =>
        {
            const string source = "CONSUMER_ORDER";

            // Penjualan milik penjual; pemanggil adalah pembeli, sehingga pencarian ini sengaja melewati filter tenant (terbatas pada satu penjual).
            var existing = await _db.Sales.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.BusinessId == sellerBusinessId && s.SourceType == source && s.SourceId == consumerOrderId)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(token);
            if (existing is int saleId)
            {
                return Result<int>.Ok(saleId); // sudah dicatat: omzet tidak dicatat dua kali
            }

            var sale = new Sale
            {
                BusinessId = sellerBusinessId,
                Number = "TMP",
                SoldAt = soldAtUtc,
                PaymentMethod = paymentMethod,
                Status = SaleStatus.Confirmed,
                SourceType = source,
                SourceId = consumerOrderId
            };
            foreach (var line in lines)
            {
                var subtotal = Money.Round(line.UnitPrice * line.QtyBase);
                sale.Items.Add(new SaleItem
                {
                    BusinessId = sellerBusinessId,
                    ProductId = line.ProductId,
                    ProductName = line.ProductName,
                    QtyBase = line.QtyBase,
                    UnitPrice = line.UnitPrice,
                    UnitCost = line.UnitCost,
                    Subtotal = subtotal,
                    CostTotal = Money.Round(line.UnitCost * line.QtyBase)
                });
            }

            sale.TotalAmount = sale.Items.Sum(i => i.Subtotal);
            sale.PaidAmount = sale.TotalAmount;
            _db.Sales.Add(sale);
            await _db.SaveChangesAsync(token);
            sale.Number = $"S{sale.Id:D6}";
            _audit.Record("SALE_FROM_CONSUMER_ORDER", "Sale", sale.Id.ToString(), $"{sale.Number}; pesanan={orderNumber}; total={sale.TotalAmount}", null, sellerBusinessId);
            await _db.SaveChangesAsync(token);
            return Result<int>.Ok(sale.Id);
        }, ct);
    }

    public async Task<Result<SaleReceiptDto>> GetReceiptAsync(int saleId, CancellationToken ct = default)
    {
        var sale = await _db.Sales
            .AsNoTracking()
            .Include(s => s.Items)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct);

        if (sale is null)
        {
            return Fail(ErrorKind.NotFound, "Transaksi tidak ditemukan.");
        }

        var productIds = sale.Items.Select(i => i.ProductId).ToList();
        var units = await _db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.BaseUnitName, ct);
        var lines = sale.Items
            .Select(i => new SaleLineDto(
                i.ProductId, i.ProductName, i.QtyBase, i.UnitPrice, i.Subtotal, units.TryGetValue(i.ProductId, out var unit) ? unit : string.Empty))
            .ToList();

        var store = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == sale.BusinessId, ct);
        var cashier = sale.CreatedByUserId is int userId
            ? await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.FullName).FirstOrDefaultAsync(ct)
            : null;
        var paidAmount = sale.PaidAmount ?? sale.TotalAmount;

        return Result<SaleReceiptDto>.Ok(new SaleReceiptDto(
            sale.Id, sale.Number, sale.SoldAt, sale.PaymentMethod, sale.TotalAmount, lines.Sum(l => l.QtyBase), lines,
            paidAmount, Math.Max(0m, paidAmount - sale.TotalAmount),
            store?.Name ?? string.Empty, store?.Address ?? string.Empty, store?.Phone, cashier,
            sale.Status == SaleStatus.Voided));
    }

    private static Result<SaleReceiptDto> Fail(ErrorKind kind, string message) =>
        Result<SaleReceiptDto>.Fail(kind, message);
}
