using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;

namespace Platform.Web.Modules.Stock.Services;

/// <inheritdoc />
public sealed class StockIntegrityService : IStockIntegrityService
{
    private readonly AppDbContext _db;

    public StockIntegrityService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<IntegrityViolation>> CheckAsync(int? businessId = null, CancellationToken ct = default)
    {
        var violations = new List<IntegrityViolation>();

        var inventories = await _db.Inventories.IgnoreQueryFilters().AsNoTracking()
            .Where(i => businessId == null || i.BusinessId == businessId)
            .ToListAsync(ct);
        var batches = await _db.StockBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(b => businessId == null || b.BusinessId == businessId)
            .ToListAsync(ct);
        var movementSums = (await _db.InventoryMovements.IgnoreQueryFilters().AsNoTracking()
                .Where(m => businessId == null || m.BusinessId == businessId)
                .GroupBy(m => m.InventoryId)
                .Select(g => new { g.Key, Sum = g.Sum(m => m.QtyBaseSigned) })
                .ToListAsync(ct))
            .ToDictionary(x => x.Key, x => x.Sum);
        var active = await _db.StockReservations.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Status == ReservationStatus.Active && (businessId == null || r.BusinessId == businessId))
            .ToListAsync(ct);

        var activeByInventory = active.GroupBy(r => r.InventoryId).ToDictionary(g => g.Key, g => g.Sum(r => r.QtyBase));
        var activeByBatch = active.Where(r => r.BatchId != null).GroupBy(r => r.BatchId!.Value).ToDictionary(g => g.Key, g => g.Sum(r => r.QtyBase));
        var batchesByProduct = batches.GroupBy(b => (b.BusinessId, b.ProductId)).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var inv in inventories)
        {
            if (inv.QtyBase < 0)
            {
                violations.Add(new(inv.BusinessId, inv.ProductId, "STOK_NEGATIF", $"QtyBase={inv.QtyBase}"));
            }

            if (inv.ReservedBase < 0 || inv.ReservedBase > inv.QtyBase)
            {
                violations.Add(new(inv.BusinessId, inv.ProductId, "RESERVASI_DILUAR_RENTANG", $"Reserved={inv.ReservedBase}, Qty={inv.QtyBase}"));
            }

            var movementSum = movementSums.TryGetValue(inv.Id, out var ms) ? ms : 0;
            if (movementSum != inv.QtyBase)
            {
                violations.Add(new(inv.BusinessId, inv.ProductId, "SALDO_TIDAK_SAMA_MOVEMENT", $"Qty={inv.QtyBase}, jumlah movement={movementSum}"));
            }

            var activeSum = activeByInventory.TryGetValue(inv.Id, out var a) ? a : 0;
            if (activeSum != inv.ReservedBase)
            {
                violations.Add(new(inv.BusinessId, inv.ProductId, "RESERVED_TIDAK_SAMA_RESERVASI_AKTIF", $"Reserved={inv.ReservedBase}, reservasi aktif={activeSum}"));
            }

            if (batchesByProduct.TryGetValue((inv.BusinessId, inv.ProductId), out var productBatches)
                && productBatches.Sum(b => b.QtyRemainingBase) > inv.QtyBase)
            {
                violations.Add(new(inv.BusinessId, inv.ProductId, "BATCH_MELEBIHI_STOK",
                    $"Jumlah batch={productBatches.Sum(b => b.QtyRemainingBase)}, stok={inv.QtyBase}"));
            }
        }

        foreach (var batch in batches)
        {
            if (batch.QtyRemainingBase < 0 || batch.ReservedBase < 0 || batch.ReservedBase > batch.QtyRemainingBase)
            {
                violations.Add(new(batch.BusinessId, batch.ProductId, "BATCH_DILUAR_RENTANG",
                    $"Batch {batch.Id}: sisa={batch.QtyRemainingBase}, dicadangkan={batch.ReservedBase}"));
            }

            var activeSum = activeByBatch.TryGetValue(batch.Id, out var a) ? a : 0;
            if (activeSum != batch.ReservedBase)
            {
                violations.Add(new(batch.BusinessId, batch.ProductId, "BATCH_RESERVED_TIDAK_SAMA_RESERVASI_AKTIF",
                    $"Batch {batch.Id}: dicadangkan={batch.ReservedBase}, reservasi aktif={activeSum}"));
            }
        }

        return violations;
    }
}
