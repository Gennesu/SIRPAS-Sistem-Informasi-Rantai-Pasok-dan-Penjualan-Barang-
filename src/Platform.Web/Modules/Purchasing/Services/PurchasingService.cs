using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Purchasing.Domain;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Purchasing.Services;

public sealed class PurchasingService : IPurchasingService
{
    private const decimal MaxMoney = 1_000_000_000_000m;
    private const int MaxLines = 30;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IProductService _products;
    private readonly IInventoryService _inventory;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<PurchasingService> _logger;
    private readonly IUnitOfWork _uow;

    public PurchasingService(
        AppDbContext db,
        IBusinessContext context,
        IProductService products,
        IInventoryService inventory,
        IAuditLogService audit,
        TimeProvider time,
        ILogger<PurchasingService> logger,
        IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _products = products;
        _inventory = inventory;
        _audit = audit;
        _time = time;
        _logger = logger;
        _uow = uow;
    }

    public async Task<Result<int>> RecordAsync(RecordPurchaseRequest request, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.IsAdmin || businessId is null)
        {
            return Result<int>.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat mencatat pembelian.");
        }

        var supplier = (request.SupplierName ?? string.Empty).Trim();
        if (supplier.Length is < 2 or > 120)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Nama pemasok harus 2–120 karakter.");
        }

        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (note is { Length: > 200 })
        {
            return Result<int>.Fail(ErrorKind.Validation, "Catatan maksimal 200 karakter.");
        }

        var lines = request.Lines ?? Array.Empty<PurchaseLineInput>();
        if (lines.Count == 0)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Tambahkan minimal satu barang.");
        }

        if (lines.Count > MaxLines)
        {
            return Result<int>.Fail(ErrorKind.Validation, $"Maksimal {MaxLines} baris per pembelian.");
        }

        // Validasi seluruh baris dulu dan hitung snapshot (isi kemasan, jumlah dasar, biaya per satuan dasar).
        var items = new List<PurchaseItem>();
        foreach (var line in lines)
        {
            if (line.QtyPack is <= 0 or > 1_000_000)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Jumlah harus 1–1.000.000.");
            }

            if (line.UnitPricePack <= 0 || line.UnitPricePack > MaxMoney)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Harga harus lebih dari 0.");
            }

            var productResult = await _products.GetAsync(line.ProductId, ct);
            if (!productResult.Succeeded)
            {
                return Result<int>.Fail(ErrorKind.NotFound, "Produk tidak ditemukan.");
            }

            var product = productResult.Value!;
            var unitName = (line.UnitName ?? string.Empty).Trim();
            var unit = product.Units.FirstOrDefault(u => string.Equals(u.UnitName, unitName, StringComparison.OrdinalIgnoreCase));
            if (unit is null)
            {
                return Result<int>.Fail(ErrorKind.Validation, $"Satuan \"{unitName}\" tidak dikenal untuk {product.Name}.");
            }

            int qtyBase;
            try
            {
                qtyBase = PackagingMath.ToBase(line.QtyPack, unit.ConversionToBase);
            }
            catch (OverflowException)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Jumlah terlalu besar.");
            }

            items.Add(new PurchaseItem
            {
                BusinessId = businessId.Value,
                ProductId = product.Id,
                ProductName = product.Name,
                UnitName = unit.UnitName,
                QtyPack = line.QtyPack,
                ConversionSnapshot = unit.ConversionToBase,
                UnitPricePack = Money.Round(line.UnitPricePack),
                QtyBase = qtyBase,
                CostPerBase = PackagingMath.CostPerBase(line.UnitPricePack, unit.ConversionToBase),
                Subtotal = Money.Round(line.QtyPack * line.UnitPricePack),
                ExpiryDate = line.ExpiryDate
            });
        }

        var key = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? null : request.IdempotencyKey.Trim();
        if (key is { Length: > 64 })
        {
            key = key[..64];
        }

        // Satu unit kerja: pembelian + seluruh stok masuk + audit tersimpan bersama, atau tidak sama sekali.
        return await _uow.ExecuteAsync<int>(async token =>
        {
            if (key is not null)
            {
                var existing = await _db.Purchases.AsNoTracking()
                    .Where(p => p.IdempotencyKey == key)
                    .Select(p => (int?)p.Id)
                    .FirstOrDefaultAsync(token);
                if (existing is int purchaseId)
                {
                    return Result<int>.Ok(purchaseId); // dikirim ulang (klik ganda/refresh): jangan tambah stok lagi
                }
            }

            var purchase = new Purchase
            {
                BusinessId = businessId.Value,
                SupplierName = supplier,
                PurchasedAt = request.PurchasedAtUtc ?? _time.GetUtcNow().UtcDateTime,
                TotalAmount = items.Sum(i => i.Subtotal),
                Note = note,
                Source = "MANUAL",
                IdempotencyKey = key
            };
            foreach (var item in items)
            {
                purchase.Items.Add(item);
            }

            _db.Purchases.Add(purchase);
            await _db.SaveChangesAsync(token);

            foreach (var item in items)
            {
                var added = await _inventory.StockInAsync(
                    new StockInRequest(
                        item.ProductId, item.QtyBase, item.CostPerBase, MovementType.PurchaseIn, "Purchase", purchase.Id, supplier,
                        item.ExpiryDate is DateOnly expiry ? new[] { new BatchPortion(expiry, item.QtyBase) } : null),
                    token);

                if (!added.Succeeded)
                {
                    return Result<int>.Fail(added.Kind, added.Error ?? "Stok gagal ditambahkan.");
                }
            }

            _audit.Record("PURCHASE_RECORDED", "Purchase", purchase.Id.ToString(), $"{supplier}; total={purchase.TotalAmount}");
            await _db.SaveChangesAsync(token);
            return Result<int>.Ok(purchase.Id);
        }, ct);
    }

    public async Task<Result<int>> RecordFromOrderAsync(
        int orderId, string supplierName, IReadOnlyList<OrderPurchaseLine> lines, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || _context.IsAdmin || businessId is null)
        {
            return Result<int>.Fail(ErrorKind.Forbidden, "Hanya akun usaha yang dapat mencatat pembelian.");
        }

        if (lines.Count == 0)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Pesanan tidak memiliki barang.");
        }

        var supplier = (supplierName ?? string.Empty).Trim();
        if (supplier.Length > 120)
        {
            supplier = supplier[..120];
        }

        var items = new List<PurchaseItem>();
        var portionsByItem = new List<IReadOnlyList<BatchPortion>?>();
        foreach (var line in lines)
        {
            if (line.QtyPack <= 0 || line.ConversionToBase <= 0 || line.UnitPricePack <= 0)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Data barang pesanan tidak valid.");
            }

            int qtyBase;
            try
            {
                qtyBase = PackagingMath.ToBase(line.QtyPack, line.ConversionToBase);
            }
            catch (OverflowException)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Jumlah terlalu besar.");
            }

            items.Add(new PurchaseItem
            {
                BusinessId = businessId.Value,
                ProductId = line.ProductId,
                ProductName = line.ProductName,
                UnitName = line.UnitName,
                QtyPack = line.QtyPack,
                ConversionSnapshot = line.ConversionToBase,
                UnitPricePack = Money.Round(line.UnitPricePack),
                QtyBase = qtyBase,
                CostPerBase = PackagingMath.CostPerBase(line.UnitPricePack, line.ConversionToBase),
                Subtotal = Money.Round(line.QtyPack * line.UnitPricePack),
                ExpiryDate = line.Batches is { Count: > 0 } ? line.Batches.Min(b => b.ExpiryDate) : null
            });
            portionsByItem.Add(line.Batches);
        }

        return await _uow.ExecuteAsync<int>(async token =>
        {
            // Satu pesanan hanya boleh menjadi SATU pembelian (juga dijaga unique index (BusinessId, OrderId)):
            // penerimaan ulang atas pesanan yang sama tidak menambah stok lagi.
            var already = await _db.Purchases.AsNoTracking()
                .Where(p => p.OrderId == orderId)
                .Select(p => (int?)p.Id)
                .FirstOrDefaultAsync(token);
            if (already is int existingId)
            {
                return Result<int>.Ok(existingId);
            }

            var purchase = new Purchase
            {
                BusinessId = businessId.Value,
                SupplierName = supplier,
                PurchasedAt = _time.GetUtcNow().UtcDateTime,
                TotalAmount = items.Sum(i => i.Subtotal),
                Note = "Pesanan marketplace",
                Source = "ORDER",
                OrderId = orderId
            };
            foreach (var item in items)
            {
                purchase.Items.Add(item);
            }

            _db.Purchases.Add(purchase);
            await _db.SaveChangesAsync(token);

            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var added = await _inventory.StockInAsync(
                    new StockInRequest(
                        item.ProductId, item.QtyBase, item.CostPerBase, MovementType.PurchaseIn, "Order", orderId, supplier,
                        portionsByItem[index]),
                    token);

                if (!added.Succeeded)
                {
                    return Result<int>.Fail(added.Kind, added.Error ?? "Stok gagal ditambahkan.");
                }
            }

            _audit.Record("PURCHASE_FROM_ORDER", "Purchase", purchase.Id.ToString(), $"order={orderId}; total={purchase.TotalAmount}");
            await _db.SaveChangesAsync(token);
            return Result<int>.Ok(purchase.Id);
        }, ct);
    }

    public async Task<IReadOnlyList<PurchaseSummaryDto>> ListAsync(int take = 30, CancellationToken ct = default)
    {
        var rows = await _db.Purchases
            .AsNoTracking()
            .Include(p => p.Items)
            .OrderByDescending(p => p.PurchasedAt)
            .Take(take)
            .ToListAsync(ct);

        return rows
            .Select(p => new PurchaseSummaryDto(
                p.Id,
                p.PurchasedAt,
                p.SupplierName,
                string.Join(", ", p.Items.Select(i => $"{i.ProductName} {i.QtyPack} {i.UnitName}")),
                p.TotalAmount,
                p.Source))
            .ToList();
    }
}
