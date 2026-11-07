using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Contracts;

public enum StockStatus
{
    Ok,
    Low,
    Out
}

public sealed record StockItemDto(
    int ProductId,
    string ProductName,
    string? Sku,
    string CategoryName,
    string BaseUnitName,
    int QtyBase,
    string QtyBreakdown,
    decimal AvgCostBase,
    decimal SellingPriceBase,
    int MinStockBase,
    StockStatus Status,
    DateTime? LastPurchaseAt,
    DateOnly? NearestExpiry = null,
    int TrackedQtyBase = 0,
    int ReservedBase = 0)
{
    /// <summary>V3: stok yang masih bisa dijual/dipesan = fisik - dicadangkan pesanan.</summary>
    public int AvailableBase => Math.Max(0, QtyBase - ReservedBase);
}

public sealed record MovementDto(
    long Id,
    DateTime At,
    string ProductName,
    string TypeLabel,
    int QtyBaseSigned,
    decimal UnitCost,
    int BalanceAfter,
    string? Reason);

/// <summary>
/// Sebagian barang yang masuk dengan tanggal kadaluarsa tertentu (membentuk/menambah satu batch).
/// Nomor lot dan tanggal produksi opsional untuk penelusuran asal batch produksi.
/// </summary>
public sealed record BatchPortion(DateOnly ExpiryDate, int QtyBase, string? LotNumber = null, DateOnly? ProductionDate = null);

/// <summary>Dari batch mana barang keluar (aturan FEFO): dipakai untuk riwayat, pembatalan, dan pengiriman ke warung.</summary>
public sealed record BatchAllocation(int BatchId, DateOnly ExpiryDate, int QtyBase);

/// <summary>
/// Stok masuk. Batches (opsional) menandai sebagian jumlah masuk dengan tanggal kadaluarsa;
/// jumlah semua portion tidak boleh melebihi QtyBase.
/// </summary>
public sealed record StockInRequest(
    int ProductId,
    int QtyBase,
    decimal CostPerBase,
    MovementType Type,
    string? RefType = null,
    int? RefId = null,
    string? Reason = null,
    IReadOnlyList<BatchPortion>? Batches = null,
    string? OperationKey = null);

/// <summary>
/// Stok keluar. Tanpa BatchId barang diambil dengan FEFO (paling dekat kadaluarsa lebih dulu);
/// dengan BatchId barang diambil dari batch itu saja (mis. pesanan bundle, membuang batch kadaluarsa).
/// </summary>
public sealed record StockOutRequest(
    int ProductId,
    int QtyBase,
    MovementType Type,
    string? RefType = null,
    int? RefId = null,
    string? Reason = null,
    int? BatchId = null,
    string? OperationKey = null);

public sealed record StockOutResult(int NewQtyBase, decimal UnitCostBase, IReadOnlyList<BatchAllocation>? Allocations = null);

/// <summary>Koreksi manual dari layar Stok: tambah manual, koreksi, rusak, kadaluarsa, pengurangan lain.</summary>
public sealed record AdjustStockRequest(
    int ProductId,
    MovementType Type,
    int QtyBase,
    decimal? CostPerBase,
    string? Reason,
    DateOnly? ExpiryDate = null,
    int? BatchId = null,
    string? OperationKey = null,
    string? LotNumber = null,
    DateOnly? ProductionDate = null);

public sealed record SellableProductDto(
    int ProductId, string Name, string? Sku, string BaseUnitName, int QtyBase, decimal SellingPriceBase, string? ImagePath = null);

public interface IInventoryService
{
    Task<IReadOnlyList<StockItemDto>> GetStockAsync(string? search = null, CancellationToken ct = default);

    /// <summary>Stok masuk + update average cost + catat movement. Mengembalikan stok baru.</summary>
    Task<Result<int>> StockInAsync(StockInRequest request, CancellationToken ct = default);

    /// <summary>Stok keluar. Ditolak bila stok tidak cukup (stok tidak pernah negatif). Mengembalikan average cost saat itu.</summary>
    Task<Result<StockOutResult>> StockOutAsync(StockOutRequest request, CancellationToken ct = default);

    Task<Result> AdjustAsync(AdjustStockRequest request, CancellationToken ct = default);
    Task<Result> UpdateSettingsAsync(int productId, decimal? sellingPriceBase, int? minStockBase, CancellationToken ct = default);
    Task<IReadOnlyList<MovementDto>> GetMovementsAsync(int? productId, int take = 50, CancellationToken ct = default);

    /// <summary>Produk yang bisa dijual di POS: aktif, stok > 0, harga jual sudah diatur.</summary>
    Task<IReadOnlyList<SellableProductDto>> SearchSellableAsync(string? query, int take = 20, CancellationToken ct = default);

    Task<IReadOnlyList<SellableProductDto>> GetSellableByIdsAsync(IReadOnlyCollection<int> productIds, CancellationToken ct = default);
}

public static class MovementTypes
{
    public static readonly MovementType[] ManualChoices =
    {
        MovementType.ManualIn,
        MovementType.CorrectionOut,
        MovementType.DamagedOut,
        MovementType.ExpiredOut,
        MovementType.OtherOut
    };

    public static bool IsIn(MovementType type) =>
        type is MovementType.PurchaseIn or MovementType.ManualIn or MovementType.OrderReturnIn or MovementType.ProductionIn;

    public static string Label(MovementType type) => type switch
    {
        MovementType.PurchaseIn => "Pembelian (masuk)",
        MovementType.ManualIn => "Tambah manual",
        MovementType.SaleOut => "Penjualan POS",
        MovementType.CorrectionOut => "Koreksi (kurang)",
        MovementType.DamagedOut => "Barang rusak",
        MovementType.ExpiredOut => "Kadaluarsa",
        MovementType.OtherOut => "Pengurangan lain",
        MovementType.OrderOut => "Pesanan diterima (stok dipesan)",
        MovementType.OrderReturnIn => "Pesanan dibatalkan (stok kembali)",
        MovementType.ProductionIn => "Hasil produksi (masuk)",
        MovementType.LivestockSaleOut => "Penjualan ke agen",
        _ => type.ToString()
    };
}
