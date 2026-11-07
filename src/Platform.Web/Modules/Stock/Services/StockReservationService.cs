using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Services;

/// <inheritdoc />
public sealed class StockReservationService : IStockReservationService
{
    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly TimeProvider _time;
    private readonly IUnitOfWork _uow;

    public StockReservationService(AppDbContext db, IBusinessContext context, TimeProvider time, IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _time = time;
        _uow = uow;
    }

    public async Task<Result<IReadOnlyList<ReservedAllocation>>> ReserveAsync(
        int sellerBusinessId, string refType, int refId, IReadOnlyList<ReserveLine> lines, CancellationToken ct = default)
    {
        if (lines.Count == 0 || lines.Any(l => l.QtyBase <= 0))
        {
            return Result<IReadOnlyList<ReservedAllocation>>.Fail(ErrorKind.Validation, "Jumlah yang dicadangkan harus lebih dari 0.");
        }

        return await _uow.ExecuteAsync<IReadOnlyList<ReservedAllocation>>(async token =>
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var today = WibTime.TodayWib(_time);
            var allocations = new List<ReservedAllocation>();

            // Baris dengan batch yang ditentukan (paket anti-mubazir) diproses lebih dulu agar FEFO baris biasa tidak mengambil batch itu.
            foreach (var line in lines.OrderByDescending(l => l.BatchId.HasValue))
            {
                var inventory = await _db.Inventories
                    .IgnoreQueryFilters()
                    .Include(i => i.Product)
                    .FirstOrDefaultAsync(i => i.BusinessId == sellerBusinessId && i.ProductId == line.ProductId, token);

                if (inventory is null)
                {
                    return Result<IReadOnlyList<ReservedAllocation>>.Fail(ErrorKind.Conflict, "Produk tidak memiliki stok di penjual ini.");
                }

                var name = inventory.Product.Name;

                // Tersedia = fisik - dicadangkan. Dua pemesan serentak tidak dapat sama-sama mengambil bagian yang sama
                // karena seluruh unit kerja ini berjalan di bawah kunci tulis (dan dijaga concurrency token + CHECK database).
                var available = inventory.QtyBase - inventory.ReservedBase;
                if (available < line.QtyBase)
                {
                    return Result<IReadOnlyList<ReservedAllocation>>.Fail(
                        ErrorKind.Conflict, $"Stok {name} tidak cukup (tersedia {Math.Max(0, available)}).");
                }

                var batches = await _db.StockBatches
                    .IgnoreQueryFilters()
                    .Where(b => b.BusinessId == sellerBusinessId && b.ProductId == line.ProductId && b.QtyRemainingBase > 0)
                    .OrderBy(b => b.ExpiryDate)
                    .ThenBy(b => b.Id)
                    .ToListAsync(token);

                var picks = new List<(StockBatch? Batch, int Qty)>();
                if (line.BatchId is int batchId)
                {
                    var batch = batches.FirstOrDefault(b => b.Id == batchId);
                    if (batch is null)
                    {
                        return Result<IReadOnlyList<ReservedAllocation>>.Fail(ErrorKind.Conflict, $"Batch {name} sudah habis atau tidak ditemukan.");
                    }

                    if (batch.ExpiryDate < today)
                    {
                        return Result<IReadOnlyList<ReservedAllocation>>.Fail(
                            ErrorKind.Conflict, $"Batch {name} sudah kedaluwarsa ({batch.ExpiryDate:dd/MM/yyyy}) dan tidak dapat dipesan.");
                    }

                    var free = batch.QtyRemainingBase - batch.ReservedBase;
                    if (free < line.QtyBase)
                    {
                        return Result<IReadOnlyList<ReservedAllocation>>.Fail(
                            ErrorKind.Conflict, $"Stok {name} pada batch {batch.ExpiryDate:dd/MM/yyyy} tidak cukup (tersedia {Math.Max(0, free)}).");
                    }

                    picks.Add((batch, line.QtyBase));
                }
                else
                {
                    // FEFO atas batch yang belum kedaluwarsa; sisanya dari stok tanpa tanggal.
                    var batchedAll = batches.Sum(b => b.QtyRemainingBase);
                    var batchedReserved = batches.Sum(b => b.ReservedBase);
                    var unbatchedFree = Math.Max(0, inventory.QtyBase - batchedAll - (inventory.ReservedBase - batchedReserved));

                    var left = line.QtyBase;
                    foreach (var batch in batches.Where(b => b.ExpiryDate >= today))
                    {
                        var free = batch.QtyRemainingBase - batch.ReservedBase;
                        if (free <= 0 || left <= 0)
                        {
                            continue;
                        }

                        var take = Math.Min(free, left);
                        picks.Add((batch, take));
                        left -= take;
                    }

                    if (left > unbatchedFree)
                    {
                        var sellable = line.QtyBase - left + unbatchedFree;
                        return Result<IReadOnlyList<ReservedAllocation>>.Fail(
                            ErrorKind.Conflict, $"Stok {name} yang layak jual tidak cukup (tersedia {sellable}).");
                    }

                    if (left > 0)
                    {
                        picks.Add((null, left));
                    }
                }

                foreach (var (batch, qty) in picks)
                {
                    if (batch is not null)
                    {
                        batch.ReservedBase += qty;
                    }

                    inventory.ReservedBase += qty;
                    _db.StockReservations.Add(new StockReservation
                    {
                        BusinessId = sellerBusinessId,
                        InventoryId = inventory.Id,
                        ProductId = line.ProductId,
                        BatchId = batch?.Id,
                        ExpiryDate = batch?.ExpiryDate,
                        QtyBase = qty,
                        Status = ReservationStatus.Active,
                        RefType = refType,
                        RefId = refId,
                        RefItemId = line.RefItemId,
                        CreatedAt = now
                    });
                    allocations.Add(new ReservedAllocation(line.RefItemId, line.ProductId, batch?.Id, batch?.ExpiryDate, qty));
                }
            }

            await _db.SaveChangesAsync(token);
            return Result<IReadOnlyList<ReservedAllocation>>.Ok(allocations);
        }, ct);
    }

    public async Task<Result> ReleaseAsync(int sellerBusinessId, string refType, int refId, CancellationToken ct = default)
    {
        return await _uow.ExecuteAsync(async token =>
        {
            var rows = await ActiveRowsAsync(sellerBusinessId, refType, refId, token);
            var now = _time.GetUtcNow().UtcDateTime;

            foreach (var row in rows)
            {
                var inventory = await _db.Inventories.IgnoreQueryFilters().FirstAsync(i => i.Id == row.InventoryId, token);
                inventory.ReservedBase = Math.Max(0, inventory.ReservedBase - row.QtyBase);

                if (row.BatchId is int batchId)
                {
                    var batch = await _db.StockBatches.IgnoreQueryFilters().FirstAsync(b => b.Id == batchId, token);
                    batch.ReservedBase = Math.Max(0, batch.ReservedBase - row.QtyBase);
                }

                row.Status = ReservationStatus.Released;
                row.ClosedAt = now;
            }

            await _db.SaveChangesAsync(token);
            return Result.Ok(); // tidak ada baris Active = sudah pernah dilepas: aman diulang
        }, ct);
    }

    public async Task<Result<IReadOnlyList<ConsumedItem>>> ConsumeAsync(
        int sellerBusinessId, string refType, int refId, string? reason, CancellationToken ct = default)
    {
        return await _uow.ExecuteAsync<IReadOnlyList<ConsumedItem>>(async token =>
        {
            var rows = await ActiveRowsAsync(sellerBusinessId, refType, refId, token);
            if (rows.Count == 0)
            {
                return Result<IReadOnlyList<ConsumedItem>>.Fail(
                    ErrorKind.Conflict, "Tidak ada reservasi stok aktif untuk pesanan ini (mungkin sudah diproses).");
            }

            var now = _time.GetUtcNow().UtcDateTime;
            var today = WibTime.TodayWib(_time);
            var refName = refType == ReservationRefTypes.ConsumerOrder ? "ConsumerOrder" : "Order";
            var consumed = new List<ConsumedItem>();

            foreach (var item in rows.GroupBy(r => (r.RefItemId, r.ProductId)))
            {
                var inventory = await _db.Inventories
                    .IgnoreQueryFilters()
                    .Include(i => i.Product)
                    .FirstAsync(i => i.BusinessId == sellerBusinessId && i.ProductId == item.Key.ProductId, token);

                var allocations = new List<ReservedAllocation>();
                var total = 0;
                foreach (var row in item)
                {
                    if (row.BatchId is int batchId)
                    {
                        var batch = await _db.StockBatches.IgnoreQueryFilters().FirstAsync(b => b.Id == batchId, token);
                        if (batch.ExpiryDate < today)
                        {
                            return Result<IReadOnlyList<ConsumedItem>>.Fail(
                                ErrorKind.Conflict,
                                $"Barang {inventory.Product.Name} dari batch kedaluwarsa {batch.ExpiryDate:dd/MM/yyyy} tidak boleh dikirim. Tolak atau batalkan pesanan ini.");
                        }

                        batch.QtyRemainingBase -= row.QtyBase;
                        batch.ReservedBase -= row.QtyBase;
                    }

                    inventory.QtyBase -= row.QtyBase;
                    inventory.ReservedBase -= row.QtyBase;
                    row.Status = ReservationStatus.Consumed;
                    row.ClosedAt = now;
                    total += row.QtyBase;
                    allocations.Add(new ReservedAllocation(row.RefItemId ?? 0, row.ProductId, row.BatchId, row.ExpiryDate, row.QtyBase));
                }

                _db.InventoryMovements.Add(new InventoryMovement
                {
                    BusinessId = sellerBusinessId,
                    Inventory = inventory,
                    ProductId = item.Key.ProductId,
                    Type = MovementType.OrderOut,
                    QtyBaseSigned = -total,
                    UnitCost = inventory.AvgCostBase,
                    BalanceAfter = inventory.QtyBase,
                    RefType = refName,
                    RefId = refId,
                    Reason = reason is { Length: > 200 } ? reason[..200] : reason,
                    CreatedAt = now,
                    CreatedByUserId = _context.UserId
                });
                consumed.Add(new ConsumedItem(item.Key.RefItemId ?? 0, item.Key.ProductId, inventory.AvgCostBase, allocations));
            }

            await _db.SaveChangesAsync(token);
            return Result<IReadOnlyList<ConsumedItem>>.Ok(consumed);
        }, ct);
    }

    private Task<List<StockReservation>> ActiveRowsAsync(int sellerBusinessId, string refType, int refId, CancellationToken ct) =>
        _db.StockReservations
            .IgnoreQueryFilters()
            .Where(r => r.BusinessId == sellerBusinessId && r.RefType == refType && r.RefId == refId && r.Status == ReservationStatus.Active)
            .OrderBy(r => r.Id)
            .ToListAsync(ct);
}
