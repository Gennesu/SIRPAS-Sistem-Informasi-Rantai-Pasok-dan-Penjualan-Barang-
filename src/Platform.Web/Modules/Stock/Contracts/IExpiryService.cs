using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Contracts;

/// <summary>Kelompok sisa umur barang. Dipakai konsisten di dashboard, stok, bundle, dan penawaran.</summary>
public enum ExpiryBand
{
    /// <summary>Sudah lewat tanggal kadaluarsa.</summary>
    Expired,

    /// <summary>0–7 hari lagi: harus segera dijual/diselamatkan.</summary>
    Critical,

    /// <summary>8–14 hari lagi.</summary>
    Soon,

    /// <summary>15–30 hari lagi: waktu terbaik membuat penawaran.</summary>
    Watch,

    /// <summary>Lebih dari 30 hari.</summary>
    Safe
}

/// <summary>Aturan sisa umur barang (murni, tanpa database).</summary>
public static class ExpiryBands
{
    public static ExpiryBand Of(int daysLeft) => daysLeft switch
    {
        < 0 => ExpiryBand.Expired,
        <= 7 => ExpiryBand.Critical,
        <= 14 => ExpiryBand.Soon,
        <= 30 => ExpiryBand.Watch,
        _ => ExpiryBand.Safe
    };

    public static string Label(ExpiryBand band) => band switch
    {
        ExpiryBand.Expired => "Kadaluarsa",
        ExpiryBand.Critical => "Segera",
        ExpiryBand.Soon => "Mendekati",
        ExpiryBand.Watch => "Pantau",
        _ => "Aman"
    };

    /// <summary>"Lewat 3 hari", "Hari ini", "20 hari lagi".</summary>
    public static string DaysText(int daysLeft) => daysLeft switch
    {
        < 0 => $"Lewat {-daysLeft} hari",
        0 => "Hari ini",
        1 => "Besok",
        _ => $"{daysLeft} hari lagi"
    };

    /// <summary>
    /// Saran diskon menurut sisa umur barang: makin dekat kadaluarsa makin besar diskonnya,
    /// karena warung butuh waktu menjual sebelum barang habis masa berlakunya.
    /// </summary>
    public static int SuggestedDiscountPercent(int daysLeft) => daysLeft switch
    {
        <= 3 => 50,
        <= 7 => 40,
        <= 14 => 30,
        <= 30 => 20,
        _ => 10
    };

    public static int DaysLeft(DateOnly expiry, DateOnly today) => expiry.DayNumber - today.DayNumber;
}

public sealed record BatchDto(
    int BatchId,
    int ProductId,
    string ProductName,
    string? Sku,
    string CategoryName,
    string BaseUnitName,
    DateOnly ExpiryDate,
    int DaysLeft,
    ExpiryBand Band,
    int QtyBase,
    string QtyBreakdown,
    decimal UnitCost,
    decimal ValueAtCost,
    decimal? NormalPriceBase,
    DateTime ReceivedAt,
    string? SourceRef,
    int ReservedBase = 0,
    string? LotNumber = null,
    DateOnly? ProductionDate = null)
{
    /// <summary>Bagian batch yang masih bebas (tidak dicadangkan pesanan).</summary>
    public int FreeBase => Math.Max(0, QtyBase - ReservedBase);
}

public sealed record ExpiryBandTotal(int Batches, int QtyBase, decimal ValueAtCost);

public sealed record ExpirySummaryDto(
    ExpiryBandTotal Expired,
    ExpiryBandTotal Critical,
    ExpiryBandTotal Soon,
    ExpiryBandTotal Watch,
    ExpiryBandTotal Safe,
    int TrackedProducts)
{
    /// <summary>Nilai (modal) barang yang akan kadaluarsa dalam 30 hari dan masih bisa diselamatkan.</summary>
    public decimal ValueAtRisk => Critical.ValueAtCost + Soon.ValueAtCost + Watch.ValueAtCost;

    public int UnitsAtRisk => Critical.QtyBase + Soon.QtyBase + Watch.QtyBase;
}

/// <summary>Menandai sebagian stok yang sudah ada (belum bertanggal) dengan tanggal kadaluarsa.</summary>
public sealed record TagStockRequest(int ProductId, int QtyBase, DateOnly ExpiryDate);

/// <summary>Batch kadaluarsa milik usaha yang sedang login (grosir maupun warung).</summary>
public interface IExpiryService
{
    /// <summary>Batch yang masih ada isinya, diurutkan dari yang paling dekat kadaluarsa.</summary>
    Task<IReadOnlyList<BatchDto>> GetBatchesAsync(
        int? maxDaysLeft = null, bool includeExpired = true, int? productId = null, CancellationToken ct = default);

    Task<ExpirySummaryDto> GetSummaryAsync(CancellationToken ct = default);

    /// <summary>Stok lama yang belum bertanggal ditandai dengan tanggal kadaluarsa (tidak mengubah jumlah stok).</summary>
    Task<Result> TagStockAsync(TagStockRequest request, CancellationToken ct = default);

    /// <summary>Membuang sisa batch (mis. sudah kadaluarsa): stok keluar dengan jenis Kadaluarsa, tercatat di riwayat.</summary>
    /// <summary>
    /// Hanya bagian yang TIDAK sedang dicadangkan pesanan yang dibuang. operationKey (opsional) menjadikan pengiriman ulang
    /// (klik ganda/refresh) tidak membuang dua kali dan tidak menampilkan error palsu.
    /// </summary>
    Task<Result> WriteOffAsync(int batchId, string? reason, CancellationToken ct = default, string? operationKey = null);
}
