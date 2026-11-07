using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Services;

public sealed class ExpiryService : IExpiryService
{
    private const string NoBusinessMessage = "Hanya akun usaha yang dapat mengelola stok.";

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IInventoryService _inventory;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly IUnitOfWork _uow;

    public ExpiryService(
        AppDbContext db, IBusinessContext context, IInventoryService inventory, IAuditLogService audit, TimeProvider time,
        IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _inventory = inventory;
        _audit = audit;
        _time = time;
        _uow = uow;
    }

    private bool IsBusiness => _context.IsAuthenticated && !_context.IsAdmin && _context.BusinessId is not null;

    public async Task<IReadOnlyList<BatchDto>> GetBatchesAsync(
        int? maxDaysLeft = null, bool includeExpired = true, int? productId = null, CancellationToken ct = default)
    {
        if (!IsBusiness)
        {
            return Array.Empty<BatchDto>();
        }

        var today = WibTime.TodayWib(_time);
        var query = _db.StockBatches
            .AsNoTracking()
            .Include(b => b.Product).ThenInclude(p => p.Category)
            .Include(b => b.Product).ThenInclude(p => p.Units)
            .Where(b => b.QtyRemainingBase > 0 && b.Product.IsActive);

        if (productId is int pid)
        {
            query = query.Where(b => b.ProductId == pid);
        }

        var rows = await query.ToListAsync(ct);
        var sellingPrices = _context.Role == RoleNames.Retail
            ? (await _db.Inventories.AsNoTracking().ToListAsync(ct)).ToDictionary(i => i.ProductId, i => i.SellingPriceBase)
            : new Dictionary<int, decimal>();

        var result = new List<BatchDto>();
        foreach (var b in rows.OrderBy(x => x.ExpiryDate).ThenBy(x => x.Id))
        {
            var days = ExpiryBands.DaysLeft(b.ExpiryDate, today);
            if (!includeExpired && days < 0)
            {
                continue;
            }

            if (maxDaysLeft is int max && days > max)
            {
                continue;
            }

            var units = b.Product.Units
                .Select(u => new ProductUnitDto(u.UnitName, u.ConversionToBase, u.IsBase, u.WholesalePrice))
                .ToList();

            decimal? normal = null;
            if (_context.Role == RoleNames.Grossier)
            {
                normal = NormalWholesalePerBase(b.Product.Units);
            }
            else if (sellingPrices.TryGetValue(b.ProductId, out var retail) && retail > 0)
            {
                normal = retail;
            }

            result.Add(new BatchDto(
                b.Id, b.ProductId, b.Product.Name, b.Product.Sku, b.Product.Category.Name, b.Product.BaseUnitName,
                b.ExpiryDate, days, ExpiryBands.Of(days), b.QtyRemainingBase,
                PackagingMath.Breakdown(b.QtyRemainingBase, units),
                b.UnitCost, Money.Round(b.QtyRemainingBase * b.UnitCost), normal, b.ReceivedAt, b.SourceRef,
                b.ReservedBase, b.LotNumber, b.ProductionDate));
        }

        return result;
    }

    public async Task<ExpirySummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var batches = await GetBatchesAsync(null, true, null, ct);

        static ExpiryBandTotal Total(IEnumerable<BatchDto> list)
        {
            var items = list.ToList();
            return new ExpiryBandTotal(items.Count, items.Sum(x => x.QtyBase), items.Sum(x => x.ValueAtCost));
        }

        return new ExpirySummaryDto(
            Total(batches.Where(b => b.Band == ExpiryBand.Expired)),
            Total(batches.Where(b => b.Band == ExpiryBand.Critical)),
            Total(batches.Where(b => b.Band == ExpiryBand.Soon)),
            Total(batches.Where(b => b.Band == ExpiryBand.Watch)),
            Total(batches.Where(b => b.Band == ExpiryBand.Safe)),
            batches.Select(b => b.ProductId).Distinct().Count());
    }

    public async Task<Result> TagStockAsync(TagStockRequest request, CancellationToken ct = default)
    {
        if (!IsBusiness)
        {
            return Result.Fail(ErrorKind.Forbidden, NoBusinessMessage);
        }

        if (request.QtyBase <= 0)
        {
            return Result.Fail(ErrorKind.Validation, "Jumlah harus lebih dari 0.");
        }

        if (request.ExpiryDate.Year is < 2000 or > 2100)
        {
            return Result.Fail(ErrorKind.Validation, "Tanggal kadaluarsa tidak valid.");
        }

        return await _uow.ExecuteAsync(async token =>
        {
            var inventory = await _db.Inventories.Include(i => i.Product).FirstOrDefaultAsync(i => i.ProductId == request.ProductId, token);
            if (inventory is null)
            {
                return Result.Fail(ErrorKind.NotFound, "Produk belum punya stok.");
            }

            var batches = await _db.StockBatches.Where(b => b.ProductId == request.ProductId && b.QtyRemainingBase > 0).ToListAsync(token);
            var tracked = batches.Sum(b => b.QtyRemainingBase);

            // Semua unit tanpa tanggal (bebas + sedang dicadangkan pesanan) boleh diberi tanggal. Unit yang sedang dicadangkan
            // ikut dipindahkan ke batch baru supaya reservasi tetap menunjuk batch yang benar (invarian stok terjaga).
            var untrackedAll = inventory.QtyBase - tracked;
            if (request.QtyBase > untrackedAll)
            {
                return Result.Fail(
                    ErrorKind.Conflict,
                    $"Hanya {Math.Max(untrackedAll, 0)} {inventory.Product.BaseUnitName} {inventory.Product.Name} yang belum bertanggal kadaluarsa.");
            }

            var reservedUnbatched = Math.Max(0, inventory.ReservedBase - batches.Sum(b => b.ReservedBase));
            var freeUnbatched = untrackedAll - reservedUnbatched;
            var needConvert = Math.Max(0, request.QtyBase - freeUnbatched);

            var now = _time.GetUtcNow().UtcDateTime;
            var batch = await _db.StockBatches.FirstOrDefaultAsync(b => b.ProductId == request.ProductId && b.ExpiryDate == request.ExpiryDate, token);
            if (batch is null)
            {
                batch = new StockBatch
                {
                    BusinessId = inventory.BusinessId,
                    ProductId = request.ProductId,
                    ExpiryDate = request.ExpiryDate,
                    QtyRemainingBase = request.QtyBase,
                    QtyReceivedBase = request.QtyBase,
                    UnitCost = inventory.AvgCostBase,
                    ReceivedAt = now,
                    SourceRef = "Ditandai"
                };
                _db.StockBatches.Add(batch);
            }
            else
            {
                batch.QtyRemainingBase += request.QtyBase;
                batch.QtyReceivedBase += request.QtyBase;
            }

            if (needConvert > 0)
            {
                await _db.SaveChangesAsync(token); // batch baru perlu Id untuk ditunjuk reservasi
                var rows = await _db.StockReservations
                    .Where(r => r.ProductId == request.ProductId && r.Status == ReservationStatus.Active && r.BatchId == null)
                    .OrderBy(r => r.Id)
                    .ToListAsync(token);
                foreach (var row in rows)
                {
                    if (needConvert <= 0)
                    {
                        break;
                    }

                    var take = Math.Min(row.QtyBase, needConvert);
                    if (take == row.QtyBase)
                    {
                        row.BatchId = batch.Id;
                        row.ExpiryDate = batch.ExpiryDate;
                    }
                    else
                    {
                        row.QtyBase -= take; // pecah: sisanya tetap tanpa tanggal
                        _db.StockReservations.Add(new StockReservation
                        {
                            BusinessId = row.BusinessId, InventoryId = row.InventoryId, ProductId = row.ProductId, BatchId = batch.Id,
                            ExpiryDate = batch.ExpiryDate, QtyBase = take, Status = ReservationStatus.Active, RefType = row.RefType,
                            RefId = row.RefId, RefItemId = row.RefItemId, CreatedAt = row.CreatedAt
                        });
                    }

                    batch.ReservedBase += take;
                    needConvert -= take;
                }
            }

            _audit.Record(
                "STOCK_EXPIRY_TAGGED", "Product", request.ProductId.ToString(),
                $"{inventory.Product.Name}; {request.QtyBase} {inventory.Product.BaseUnitName}; kadaluarsa {request.ExpiryDate:yyyy-MM-dd}");

            await _db.SaveChangesAsync(token);
            return Result.Ok();
        }, ct);
    }

    public async Task<Result> WriteOffAsync(int batchId, string? reason, CancellationToken ct = default, string? operationKey = null)
    {
        if (!IsBusiness)
        {
            return Result.Fail(ErrorKind.Forbidden, NoBusinessMessage);
        }

        var key = string.IsNullOrWhiteSpace(operationKey) ? null : operationKey.Trim();
        if (key is { Length: > 64 })
        {
            key = key[..64];
        }

        // Pembuangan batch = satu unit kerja: stok keluar + movement + audit atomik. Hanya bagian yang tidak dicadangkan
        // pesanan yang dibuang (reservasi pesanan lain tidak dilanggar).
        return await _uow.ExecuteAsync(async token =>
        {
            if (key is not null && await _db.InventoryMovements.AsNoTracking().AnyAsync(m => m.OperationKey == key, token))
            {
                return Result.Ok(); // pengiriman ulang: sudah dibuang, jangan buang/catat lagi
            }

            var batch = await _db.StockBatches.AsNoTracking().FirstOrDefaultAsync(b => b.Id == batchId, token);
            if (batch is null || batch.QtyRemainingBase <= 0)
            {
                return Result.Fail(ErrorKind.NotFound, "Batch tidak ditemukan atau sudah kosong.");
            }

            var free = batch.QtyRemainingBase - batch.ReservedBase;
            if (free <= 0)
            {
                return Result.Fail(
                    ErrorKind.Conflict,
                    "Seluruh sisa batch ini sedang dicadangkan pesanan. Tolak atau batalkan pesanan terkait lebih dulu.");
            }

            var today = WibTime.TodayWib(_time);
            var expired = batch.ExpiryDate < today;
            var note = string.IsNullOrWhiteSpace(reason) ? (expired ? "Dibuang: kadaluarsa" : "Dibuang sebelum kadaluarsa") : reason.Trim();
            var result = await _inventory.StockOutAsync(
                new StockOutRequest(
                    batch.ProductId, free, expired ? MovementType.ExpiredOut : MovementType.OtherOut,
                    "Batch", batch.Id, note, batch.Id, key),
                token);

            if (!result.Succeeded)
            {
                return Result.Fail(result.Kind, result.Error ?? "Batch gagal dibuang.");
            }

            _audit.Record("STOCK_WRITE_OFF", "StockBatch", batch.Id.ToString(), $"produk={batch.ProductId}; qty={free}; {note}");
            await _db.SaveChangesAsync(token);
            return Result.Ok();
        }, ct);
    }

    /// <summary>Harga grosir termurah per satuan dasar: patokan "harga normal" untuk menghitung diskon.</summary>
    public static decimal? NormalWholesalePerBase(IEnumerable<Catalog.Domain.ProductUnit> units)
    {
        var prices = units
            .Where(u => u.WholesalePrice is > 0 && u.ConversionToBase > 0)
            .Select(u => u.WholesalePrice!.Value / u.ConversionToBase)
            .ToList();
        return prices.Count == 0 ? null : Money.RoundCost(prices.Min());
    }
}
