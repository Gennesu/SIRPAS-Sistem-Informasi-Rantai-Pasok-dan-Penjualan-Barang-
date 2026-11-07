using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Rescue.Contracts;

public enum BundleState
{
    Active,
    SoldOut,
    Expired,
    Ended
}

public enum OfferState
{
    Sent,
    Accepted,
    Declined,
    Withdrawn,
    Expired
}

public static class RescueLabels
{
    public static string Bundle(BundleState state) => state switch
    {
        BundleState.Active => "Tersedia",
        BundleState.SoldOut => "Habis",
        BundleState.Expired => "Berakhir",
        _ => "Dihentikan"
    };

    public static string Offer(OfferState state) => state switch
    {
        OfferState.Sent => "Menunggu jawaban",
        OfferState.Accepted => "Diambil",
        OfferState.Declined => "Ditolak",
        OfferState.Withdrawn => "Ditarik",
        _ => "Kedaluwarsa"
    };
}

public sealed record BundleItemDto(
    int ProductId,
    string ProductName,
    string BaseUnitName,
    int QtyBase,
    DateOnly ExpiryDate,
    int DaysLeft,
    decimal NormalUnitPriceBase,
    int BatchId,
    int BatchRemainingBase);

/// <summary>Info penjual yang ditampilkan di kartu promo untuk warung.</summary>
public sealed record DealSellerDto(
    int BusinessId,
    string Name,
    string Address,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    bool OffersDelivery,
    bool OffersPickup,
    decimal DeliveryFee,
    double? RoadKm = null,
    string? DeliveryNote = null,
    decimal MinOrderAmount = 0m);

public sealed record BundleDto(
    int Id,
    string Title,
    string? Description,
    decimal Price,
    decimal NormalPrice,
    int DiscountPercent,
    DateOnly ValidUntil,
    DateOnly EarliestExpiry,
    int EarliestExpiryDays,
    int Available,
    int Sold,
    bool IsPrivate,
    BundleState State,
    DateTime CreatedAt,
    IReadOnlyList<BundleItemDto> Items,
    DealSellerDto? Seller = null)
{
    public decimal Savings => NormalPrice - Price;
    public int TotalUnits => Items.Sum(i => i.QtyBase);
}

public sealed record OfferDto(
    int Id,
    int BundleId,
    string BundleTitle,
    int SellerBusinessId,
    string SellerName,
    int BuyerBusinessId,
    string BuyerName,
    string? Message,
    int SuggestedBundles,
    OfferState State,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    DateTime? RespondedAt,
    int? OrderId,
    BundleDto? Bundle);

/// <summary>Isi paket: satu batch stok dan jumlah per paket (satuan dasar).</summary>
public sealed record RescueItemInput(int BatchId, int QtyBase);

public sealed record OfferTarget(int BusinessId, int SuggestedBundles);

public sealed record CreateBundleRequest(
    string? Title,
    string? Description,
    IReadOnlyList<RescueItemInput> Items,
    decimal? BundlePrice,
    int? DiscountPercent,
    DateOnly? ValidUntil,
    IReadOnlyList<OfferTarget>? OfferTargets = null,
    string? OfferMessage = null,
    bool PrivateToTargets = false);

// ---------------------------------------------------------------- dashboard grosir

/// <summary>Warung yang cocok ditawari satu batch: dihitung dari kecepatan jual dan sisa stoknya.</summary>
public sealed record RescueTargetDto(
    int BusinessId,
    string Name,
    string Address,
    double? DistanceKm,
    int SoldQtyBase,
    double AvgPerDay,
    int StockAtWarungBase,
    double? DaysOfCover,
    int SuggestedQtyBase,
    bool HasOpenOffer);

public sealed record RescueCandidateDto(
    BatchDto Batch,
    int SuggestedDiscountPercent,
    decimal? SuggestedUnitPrice,
    int? ActiveBundleId,
    IReadOnlyList<RescueTargetDto> Targets);

public sealed record RescueStatsDto(
    int ActiveBundles,
    int OpenOffers,
    int AcceptedOffers30d,
    int RescuedUnits30d,
    decimal RescuedRevenue30d,
    decimal BuyerSavings30d,
    int WrittenOffUnits30d,
    decimal WrittenOffValue30d)
{
    /// <summary>Bagian barang berisiko yang berhasil terjual (0–100), atau null bila belum ada data.</summary>
    public int? RescueRatePercent
    {
        get
        {
            var total = RescuedUnits30d + WrittenOffUnits30d;
            return total <= 0 ? null : (int)Math.Round(RescuedUnits30d * 100.0 / total);
        }
    }
}

public sealed record RescueDashboardDto(
    ExpirySummaryDto Expiry,
    IReadOnlyList<RescueCandidateDto> Candidates,
    IReadOnlyList<BatchDto> Expired,
    RescueStatsDto Stats,
    int HiddenCustomers);

// ---------------------------------------------------------------- dipakai modul Orders

public sealed record BundleOrderLine(
    int ProductId,
    string ProductName,
    string? Sku,
    int CategoryId,
    string BaseUnitName,
    string UnitsText,
    int BatchId,
    DateOnly ExpiryDate,
    int QtyBase,
    decimal UnitPriceBase,
    decimal Subtotal,
    decimal NormalSubtotal);

public sealed record BundleOrderInfo(
    int BundleId,
    string Title,
    int SellerBusinessId,
    int Qty,
    decimal Total,
    IReadOnlyList<BundleOrderLine> Lines);

/// <summary>
/// Paket anti-mubazir dan penawaran. Grosir membuat paket dari batch stok yang mendekati kadaluarsa dan mengirim
/// penawaran ke warung yang paling cocok; warung melihat promo publik dan penawaran yang ditujukan kepadanya.
/// Pembacaan lintas usaha (paket publik/penawaran) hanya untuk grosir yang terverifikasi dan aktif.
/// </summary>
public interface IRescueService
{
    // ---- grosir
    Task<RescueDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<Result<int>> CreateBundleAsync(CreateBundleRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<BundleDto>> ListMineAsync(bool includeClosed, CancellationToken ct = default);
    Task<Result> EndBundleAsync(int bundleId, CancellationToken ct = default);
    Task<IReadOnlyList<OfferDto>> ListSentAsync(CancellationToken ct = default);
    Task<Result> WithdrawOfferAsync(int offerId, CancellationToken ct = default);

    // ---- warung
    Task<IReadOnlyList<BundleDto>> ListPublicAsync(int? sellerBusinessId = null, CancellationToken ct = default);
    Task<Result<BundleDto>> GetForBuyerAsync(int bundleId, CancellationToken ct = default);
    Task<IReadOnlyList<OfferDto>> ListReceivedAsync(bool includeClosed, CancellationToken ct = default);
    Task<Result> DeclineOfferAsync(int offerId, CancellationToken ct = default);
    Task<int> CountOpenOffersAsync(CancellationToken ct = default);

    /// <summary>Jumlah promo publik yang masih tersedia dan diskon terbesar per grosir (untuk kartu marketplace/peta).</summary>
    Task<IReadOnlyDictionary<int, (int Deals, int BestDiscountPercent)>> GetDealSummaryAsync(CancellationToken ct = default);

    // ---- dipakai OrderService
    Task<Result<BundleOrderInfo>> ResolveForOrderAsync(int bundleId, int qty, int? offerId, CancellationToken ct = default);
    Task<Result> MarkOfferAcceptedAsync(int offerId, int orderId, CancellationToken ct = default);
}
