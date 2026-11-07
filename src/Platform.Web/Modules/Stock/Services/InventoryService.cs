using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Services;

public sealed class InventoryService : IInventoryService
{
    private const string NoBusinessMessage = "Hanya akun usaha yang dapat mengelola stok.";
    private const string ProductNotFoundMessage = "Produk tidak ditemukan.";
    private const decimal MaxMoney = 1_000_000_000_000m;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<InventoryService> _logger;
    private readonly IUnitOfWork _uow;

    public InventoryService(
        AppDbContext db,
        IBusinessContext context,
        IAuditLogService audit,
        TimeProvider time,
        ILogger<InventoryService> logger,
        IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _audit = audit;
        _time = time;
        _logger = logger;
        _uow = uow;
    }

    // ------------------------------------------------------------------ baca

    public async Task<IReadOnlyList<StockItemDto>> GetStockAsync(string? search = null, CancellationToken ct = default)
    {
        var products = await _db.Products
            .AsNoTracking()
            .Include(p => p.Category)
            .Include(p => p.Units)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var inventories = (await _db.Inventories.AsNoTracking().ToListAsync(ct)).ToDictionary(i => i.ProductId);
        var batchesByProduct = (await _db.StockBatches.AsNoTracking().Where(b => b.QtyRemainingBase > 0).ToListAsync(ct))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var term = search?.Trim();
        var result = new List<StockItemDto>();

        foreach (var p in products)
        {
            if (!string.IsNullOrEmpty(term)
                && !p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                && !(p.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            {
                continue;
            }

            inventories.TryGetValue(p.Id, out var inv);
            var qty = inv?.QtyBase ?? 0;
            var min = inv?.MinStockBase ?? 0;
            var units = p.Units
                .Select(u => new ProductUnitDto(u.UnitName, u.ConversionToBase, u.IsBase, u.WholesalePrice))
                .ToList();

            result.Add(new StockItemDto(
                p.Id,
                p.Name,
                p.Sku,
                p.Category.Name,
                p.BaseUnitName,
                qty,
                PackagingMath.Breakdown(qty, units),
                inv?.AvgCostBase ?? 0m,
                inv?.SellingPriceBase ?? 0m,
                min,
                StatusOf(qty, min),
                inv?.LastPurchaseAt,
                batchesByProduct.TryGetValue(p.Id, out var batches) ? batches.Min(b => b.ExpiryDate) : null,
                batchesByProduct.TryGetValue(p.Id, out var tracked) ? tracked.Sum(b => b.QtyRemainingBase) : 0,
                inv?.ReservedBase ?? 0));
        }

        return result;
    }

    public async Task<IReadOnlyList<MovementDto>> GetMovementsAsync(int? productId, int take = 50, CancellationToken ct = default)
    {
        IQueryable<InventoryMovement> query = _db.InventoryMovements.AsNoTracking().Include(m => m.Product);
        if (productId is not null)
        {
            query = query.Where(m => m.ProductId == productId);
        }

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows
            .Select(m => new MovementDto(
                m.Id, m.CreatedAt, m.Product.Name, MovementTypes.Label(m.Type),
                m.QtyBaseSigned, m.UnitCost, m.BalanceAfter, m.Reason))
            .ToList();
    }

    public async Task<IReadOnlyList<SellableProductDto>> SearchSellableAsync(string? query, int take = 20, CancellationToken ct = default)
    {
        var all = await LoadSellableAsync(null, ct);
        var term = query?.Trim();

        return all
            .Where(s => s.QtyBase > 0 && s.SellingPriceBase > 0)
            .Where(s => string.IsNullOrEmpty(term)
                        || s.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                        || (s.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(s => s.Name)
            .Take(take)
            .ToList();
    }

    public async Task<IReadOnlyList<SellableProductDto>> GetSellableByIdsAsync(
        IReadOnlyCollection<int> productIds, CancellationToken ct = default)
    {
        return await LoadSellableAsync(productIds, ct);
    }

    // ------------------------------------------------------------------ tulis

    public async Task<Result<int>> StockInAsync(StockInRequest request, CancellationToken ct = default)
    {
        if (!TryGetBusiness(out var businessId))
        {
            return Result<int>.Fail(ErrorKind.Forbidden, NoBusinessMessage);
        }

        if (!MovementTypes.IsIn(request.Type))
        {
            return Result<int>.Fail(ErrorKind.Validation, "Jenis stok masuk tidak valid.");
        }

        if (request.QtyBase <= 0)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Jumlah harus lebih dari 0.");
        }

        if (request.CostPerBase < 0 || request.CostPerBase > MaxMoney)
        {
            return Result<int>.Fail(ErrorKind.Validation, "Biaya per satuan tidak valid.");
        }

        var batchError = ValidatePortions(request.Batches, request.QtyBase);
        if (batchError is not null)
        {
            return Result<int>.Fail(ErrorKind.Validation, batchError);
        }

        var key = CleanKey(request.OperationKey);

        return await _uow.ExecuteAsync<int>(async token =>
        {
            // Idempotensi: operasi dengan kunci yang sama sudah pernah selesai -> kembalikan hasilnya, jangan tambah stok lagi.
            if (key is not null && await _db.InventoryMovements.AsNoTracking().AnyAsync(m => m.OperationKey == key, token))
            {
                var current = await _db.Inventories.AsNoTracking().FirstOrDefaultAsync(i => i.ProductId == request.ProductId, token);
                return Result<int>.Ok(current?.QtyBase ?? 0);
            }

            if (!await _db.Products.AnyAsync(p => p.Id == request.ProductId, token))
            {
                return Result<int>.Fail(ErrorKind.NotFound, ProductNotFoundMessage);
            }

            var inventory = await GetOrCreateAsync(businessId, request.ProductId, token);
            var newQty = (long)inventory.QtyBase + request.QtyBase;
            if (newQty > int.MaxValue)
            {
                return Result<int>.Fail(ErrorKind.Validation, "Jumlah stok melebihi batas.");
            }

            var cost = Money.RoundCost(request.CostPerBase);
            var now = _time.GetUtcNow().UtcDateTime;

            // Moving average: dihitung saat stok MASUK saja. Stok keluar tidak mengubah average cost.
            inventory.AvgCostBase = inventory.QtyBase <= 0
                ? cost
                : Money.RoundCost((inventory.QtyBase * inventory.AvgCostBase + request.QtyBase * cost) / newQty);
            inventory.QtyBase = (int)newQty;
            if (request.Type == MovementType.PurchaseIn)
            {
                inventory.LastPurchaseAt = now;
            }

            var movement = NewMovement(
                businessId, inventory, request.ProductId, request.Type, request.QtyBase, cost,
                request.RefType, request.RefId, request.Reason, now);
            movement.OperationKey = key;
            _db.InventoryMovements.Add(movement);

            await UpsertBatchesAsync(businessId, request.ProductId, request.Batches, cost, now, request.RefType, request.RefId, token);
            await _db.SaveChangesAsync(token);
            return Result<int>.Ok(inventory.QtyBase);
        }, ct);
    }

    public async Task<Result<StockOutResult>> StockOutAsync(StockOutRequest request, CancellationToken ct = default)
    {
        if (!TryGetBusiness(out var businessId))
        {
            return Result<StockOutResult>.Fail(ErrorKind.Forbidden, NoBusinessMessage);
        }

        if (MovementTypes.IsIn(request.Type))
        {
            return Result<StockOutResult>.Fail(ErrorKind.Validation, "Jenis stok keluar tidak valid.");
        }

        if (request.QtyBase <= 0)
        {
            return Result<StockOutResult>.Fail(ErrorKind.Validation, "Jumlah harus lebih dari 0.");
        }

        var key = CleanKey(request.OperationKey);

        // Pemeriksaan stok dan pengurangannya terjadi DI DALAM satu unit kerja (kunci tulis diambil di awal),
        // sehingga dua permintaan serentak tidak dapat membaca stok yang sama (tidak ada lost update).
        return await _uow.ExecuteAsync<StockOutResult>(async token =>
        {
            if (key is not null)
            {
                var prior = await _db.InventoryMovements.AsNoTracking().FirstOrDefaultAsync(m => m.OperationKey == key, token);
                if (prior is not null)
                {
                    var inv0 = await _db.Inventories.AsNoTracking().FirstOrDefaultAsync(i => i.ProductId == request.ProductId, token);
                    return Result<StockOutResult>.Ok(new StockOutResult(inv0?.QtyBase ?? 0, prior.UnitCost));
                }
            }

            var inventory = await _db.Inventories
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.ProductId == request.ProductId, token);

            if (inventory is null)
            {
                var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.ProductId, token);
                return product is null
                    ? Result<StockOutResult>.Fail(ErrorKind.NotFound, ProductNotFoundMessage)
                    : Result<StockOutResult>.Fail(ErrorKind.Conflict, $"Stok {product.Name} tidak cukup (tersisa 0).");
            }

            // Stok tidak boleh negatif dan tidak boleh memakai bagian yang sudah dicadangkan pesanan lain.
            var available = inventory.QtyBase - inventory.ReservedBase;
            if (available < request.QtyBase)
            {
                return Result<StockOutResult>.Fail(
                    ErrorKind.Conflict, $"Stok {inventory.Product.Name} tidak cukup (tersisa {Math.Max(0, available)}).");
            }

            var allocations = new List<BatchAllocation>();
            var batches = await _db.StockBatches
                .Where(b => b.ProductId == request.ProductId && b.QtyRemainingBase > 0)
                .OrderBy(b => b.ExpiryDate)
                .ThenBy(b => b.Id)
                .ToListAsync(token);

            if (request.BatchId is int batchId)
            {
                var batch = batches.FirstOrDefault(b => b.Id == batchId)
                            ?? await _db.StockBatches.FirstOrDefaultAsync(b => b.Id == batchId && b.ProductId == request.ProductId, token);
                if (batch is null)
                {
                    return Result<StockOutResult>.Fail(ErrorKind.NotFound, "Batch stok tidak ditemukan.");
                }

                var free = batch.QtyRemainingBase - batch.ReservedBase;
                if (free < request.QtyBase)
                {
                    return Result<StockOutResult>.Fail(
                        ErrorKind.Conflict,
                        $"Stok {inventory.Product.Name} untuk batch kadaluarsa {batch.ExpiryDate:dd/MM/yyyy} tidak cukup (tersisa {Math.Max(0, free)}).");
                }

                batch.QtyRemainingBase -= request.QtyBase;
                allocations.Add(new BatchAllocation(batch.Id, batch.ExpiryDate, request.QtyBase));
            }
            else
            {
                // FEFO. Penjualan (kasir/pesanan) TIDAK boleh mengambil dari batch yang sudah kedaluwarsa (keamanan pangan);
                // pengurangan manual (koreksi, rusak, buang kadaluarsa) tetap boleh mengambil batch tertua.
                var skipExpired = request.Type is MovementType.SaleOut or MovementType.OrderOut or MovementType.LivestockSaleOut;
                var today = WibTime.TodayWib(_time);

                var batchedAll = batches.Sum(b => b.QtyRemainingBase);
                var batchedReserved = batches.Sum(b => b.ReservedBase);
                var unbatchedFree = Math.Max(0, inventory.QtyBase - batchedAll - (inventory.ReservedBase - batchedReserved));
                var eligible = batches
                    .Where(b => !skipExpired || b.ExpiryDate >= today)
                    .Select(b => (Batch: b, Free: b.QtyRemainingBase - b.ReservedBase))
                    .Where(x => x.Free > 0)
                    .ToList();

                var capacity = eligible.Sum(x => x.Free) + unbatchedFree;
                if (capacity < request.QtyBase)
                {
                    var note = skipExpired && capacity < available ? " karena sebagian sudah kedaluwarsa" : string.Empty;
                    return Result<StockOutResult>.Fail(
                        ErrorKind.Conflict, $"Stok {inventory.Product.Name} yang layak jual tidak cukup (tersisa {capacity}{note}).");
                }

                var left = request.QtyBase;
                foreach (var (batch, free) in eligible)
                {
                    if (left <= 0)
                    {
                        break;
                    }

                    var take = Math.Min(free, left);
                    batch.QtyRemainingBase -= take;
                    left -= take;
                    allocations.Add(new BatchAllocation(batch.Id, batch.ExpiryDate, take));
                }
            }

            var unitCost = inventory.AvgCostBase;
            inventory.QtyBase -= request.QtyBase;

            var movement = NewMovement(
                businessId, inventory, request.ProductId, request.Type, -request.QtyBase, unitCost,
                request.RefType, request.RefId, request.Reason, _time.GetUtcNow().UtcDateTime);
            movement.OperationKey = key;
            _db.InventoryMovements.Add(movement);

            await _db.SaveChangesAsync(token);
            return Result<StockOutResult>.Ok(new StockOutResult(inventory.QtyBase, unitCost, allocations));
        }, ct);
    }

    public async Task<Result> AdjustAsync(AdjustStockRequest request, CancellationToken ct = default)
    {
        if (!TryGetBusiness(out _))
        {
            return Result.Fail(ErrorKind.Forbidden, NoBusinessMessage);
        }

        if (!MovementTypes.ManualChoices.Contains(request.Type) && request.Type != MovementType.ProductionIn)
        {
            return Result.Fail(ErrorKind.Validation, "Jenis penyesuaian tidak valid.");
        }

        if (MovementTypes.IsIn(request.Type))
        {
            var cost = request.CostPerBase;
            if (cost is null)
            {
                var existing = await _db.Inventories.AsNoTracking().FirstOrDefaultAsync(i => i.ProductId == request.ProductId, ct);
                cost = existing?.AvgCostBase ?? 0m;
            }

            var portions = request.ExpiryDate is DateOnly expiry
                ? new[] { new BatchPortion(expiry, request.QtyBase, Clean(request.LotNumber), request.ProductionDate) }
                : null;
            var added = await StockInAsync(
                new StockInRequest(
                    request.ProductId, request.QtyBase, cost.Value, request.Type, "Adjustment", null, request.Reason, portions, request.OperationKey),
                ct);
            return added.Succeeded ? Result.Ok() : Result.Fail(added.Kind, added.Error ?? "Gagal menambah stok.");
        }

        var removed = await StockOutAsync(
            new StockOutRequest(
                request.ProductId, request.QtyBase, request.Type, "Adjustment", null, request.Reason, request.BatchId, request.OperationKey),
            ct);
        return removed.Succeeded ? Result.Ok() : Result.Fail(removed.Kind, removed.Error ?? "Gagal mengurangi stok.");
    }

    public async Task<Result> UpdateSettingsAsync(int productId, decimal? sellingPriceBase, int? minStockBase, CancellationToken ct = default)
    {
        if (!TryGetBusiness(out var businessId))
        {
            return Result.Fail(ErrorKind.Forbidden, NoBusinessMessage);
        }

        if (sellingPriceBase is < 0 or > MaxMoney)
        {
            return Result.Fail(ErrorKind.Validation, "Harga jual tidak valid.");
        }

        if (minStockBase is < 0 or > 1_000_000)
        {
            return Result.Fail(ErrorKind.Validation, "Stok minimum tidak valid.");
        }

        return await _uow.ExecuteAsync(async token =>
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, token);
            if (product is null)
            {
                return Result.Fail(ErrorKind.NotFound, ProductNotFoundMessage);
            }

            var inventory = await GetOrCreateAsync(businessId, productId, token);
            if (sellingPriceBase.HasValue)
            {
                inventory.SellingPriceBase = Money.Round(sellingPriceBase.Value);
                _audit.Record("INVENTORY_PRICE_SET", "Product", productId.ToString(), $"{product.Name}; harga jual={inventory.SellingPriceBase}");
            }

            if (minStockBase.HasValue)
            {
                inventory.MinStockBase = minStockBase.Value;
            }

            await _db.SaveChangesAsync(token);
            return Result.Ok();
        }, ct);
    }

    // ------------------------------------------------------------------ helper

    private bool TryGetBusiness(out int businessId)
    {
        businessId = _context.BusinessId ?? 0;
        return _context.IsAuthenticated && !_context.IsAdmin && _context.BusinessId is not null;
    }

    private async Task<Inventory> GetOrCreateAsync(int businessId, int productId, CancellationToken ct)
    {
        var inventory = await _db.Inventories.FirstOrDefaultAsync(i => i.ProductId == productId, ct);
        if (inventory is null)
        {
            inventory = new Inventory { BusinessId = businessId, ProductId = productId };
            _db.Inventories.Add(inventory);
        }

        return inventory;
    }

    private InventoryMovement NewMovement(
        int businessId,
        Inventory inventory,
        int productId,
        MovementType type,
        int qtySigned,
        decimal unitCost,
        string? refType,
        int? refId,
        string? reason,
        DateTime now) => new()
    {
        BusinessId = businessId,
        Inventory = inventory,
        ProductId = productId,
        Type = type,
        QtyBaseSigned = qtySigned,
        UnitCost = unitCost,
        BalanceAfter = inventory.QtyBase,
        RefType = refType,
        RefId = refId,
        Reason = reason is { Length: > 200 } ? reason[..200] : reason,
        CreatedAt = now,
        CreatedByUserId = _context.UserId
    };

    private static string? CleanKey(string? key)
    {
        var clean = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        return clean is { Length: > 64 } ? clean[..64] : clean;
    }

    private static string? Clean(string? value)
    {
        var clean = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return clean is { Length: > 40 } ? clean[..40] : clean;
    }

    private async Task<List<SellableProductDto>> LoadSellableAsync(IReadOnlyCollection<int>? ids, CancellationToken ct)
    {
        var query = _db.Products.AsNoTracking().Where(p => p.IsActive);
        if (ids is not null)
        {
            var list = ids.ToList();
            query = query.Where(p => list.Contains(p.Id));
        }

        var products = await query.ToListAsync(ct);
        var inventories = (await _db.Inventories.AsNoTracking().ToListAsync(ct)).ToDictionary(i => i.ProductId);

        // Stok yang boleh dijual = fisik - dicadangkan pesanan - bagian batch yang sudah kedaluwarsa (NFR-13).
        var today = WibTime.TodayWib(_time);
        var expiredFree = (await _db.StockBatches.AsNoTracking()
                .Where(b => b.ExpiryDate < today && b.QtyRemainingBase > b.ReservedBase)
                .ToListAsync(ct))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.QtyRemainingBase - b.ReservedBase));

        return products
            .Select(p =>
            {
                inventories.TryGetValue(p.Id, out var inv);
                var sellable = Math.Max(
                    0, (inv?.QtyBase ?? 0) - (inv?.ReservedBase ?? 0) - (expiredFree.TryGetValue(p.Id, out var ex) ? ex : 0));
                return new SellableProductDto(p.Id, p.Name, p.Sku, p.BaseUnitName, sellable, inv?.SellingPriceBase ?? 0m, p.ImagePath);
            })
            .ToList();
    }

    private static string? ValidatePortions(IReadOnlyList<BatchPortion>? portions, int totalQty)
    {
        if (portions is null || portions.Count == 0)
        {
            return null;
        }

        long sum = 0;
        foreach (var portion in portions)
        {
            if (portion.QtyBase <= 0)
            {
                return "Jumlah barang pada tanggal kadaluarsa harus lebih dari 0.";
            }

            if (portion.ExpiryDate.Year is < 2000 or > 2100)
            {
                return "Tanggal kadaluarsa tidak valid.";
            }

            sum += portion.QtyBase;
        }

        return sum > totalQty ? "Jumlah barang bertanggal kadaluarsa melebihi jumlah yang masuk." : null;
    }

    // Menambah barang ke batch dengan tanggal kadaluarsa yang sama (atau membuat batch baru).
    private async Task UpsertBatchesAsync(
        int businessId, int productId, IReadOnlyList<BatchPortion>? portions, decimal unitCost, DateTime now,
        string? refType, int? refId, CancellationToken ct)
    {
        if (portions is null || portions.Count == 0)
        {
            return;
        }

        var source = refType is null ? "Manual" : (refId is null ? refType : $"{refType} #{refId}");
        foreach (var group in portions.GroupBy(p => p.ExpiryDate))
        {
            var qty = group.Sum(p => p.QtyBase);
            var date = group.Key;
            var batch = await _db.StockBatches.FirstOrDefaultAsync(b => b.ProductId == productId && b.ExpiryDate == date, ct);
            if (batch is null)
            {
                _db.StockBatches.Add(new StockBatch
                {
                    BusinessId = businessId,
                    ProductId = productId,
                    ExpiryDate = date,
                    QtyRemainingBase = qty,
                    QtyReceivedBase = qty,
                    UnitCost = unitCost,
                    ReceivedAt = now,
                    SourceRef = source,
                    LotNumber = group.Select(g => g.LotNumber).FirstOrDefault(l => l is not null),
                    ProductionDate = group.Select(g => g.ProductionDate).FirstOrDefault(d => d is not null)
                });
                continue;
            }

            batch.LotNumber ??= group.Select(g => g.LotNumber).FirstOrDefault(l => l is not null);
            batch.ProductionDate ??= group.Select(g => g.ProductionDate).FirstOrDefault(d => d is not null);

            var total = batch.QtyRemainingBase + qty;
            batch.UnitCost = total <= 0
                ? unitCost
                : Money.RoundCost((batch.QtyRemainingBase * batch.UnitCost + qty * unitCost) / total);
            batch.QtyRemainingBase = total;
            batch.QtyReceivedBase += qty;
        }
    }

    private static StockStatus StatusOf(int qty, int min) =>
        qty <= 0 ? StockStatus.Out : (min > 0 && qty <= min ? StockStatus.Low : StockStatus.Ok);
}
