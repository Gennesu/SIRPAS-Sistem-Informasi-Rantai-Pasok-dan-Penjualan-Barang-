using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Marketplace.Contracts;

/// <summary>
/// Marketplace warung menampilkan grosir terverifikasi dan aktif.
/// </summary>
public sealed record MarketplaceFilter(string? Query, int? CategoryId, double? MaxDistanceKm, FulfillmentType? Fulfillment);

public sealed record WholesalerCardDto(
    int BusinessId,
    string Name,
    string Address,
    double? Latitude,
    double? Longitude,
    double? DistanceKm,
    bool OffersDelivery,
    bool OffersPickup,
    decimal DeliveryFee,
    decimal MinOrderAmount,
    int ProductCount,
    IReadOnlyList<string> MatchingProducts,
    double? RoadKm = null,
    bool DeliveryInRange = true,
    string? DeliveryNote = null,
    int RescueDeals = 0,
    int BestDiscountPercent = 0,
    decimal FeePerKm = 0m,
    decimal FreeKm = 0m,
    int? TravelMinutes = null,
    IReadOnlyList<string>? PreviewImages = null);

/// <summary>Satuan yang bisa dipesan (punya harga grosir).</summary>
public sealed record WholesaleUnitDto(string UnitName, int ConversionToBase, decimal Price);

public sealed record WholesaleProductDto(
    int ProductId,
    string Name,
    string? Sku,
    int CategoryId,
    string CategoryName,
    string BaseUnitName,
    string UnitsText,
    int StockBase,
    string StockText,
    IReadOnlyList<WholesaleUnitDto> Units,
    int MoqBase = 1,
    string? ImagePath = null);

public sealed record WholesalerDetailDto(
    WholesalerCardDto Card,
    string? DeliveryInfo,
    string? PickupInfo,
    string BuyerAddress,
    IReadOnlyList<WholesaleProductDto> Products,
    IReadOnlyList<BundleDto>? Bundles = null);

public sealed record MapPointDto(
    int BusinessId,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    double? DistanceKm,
    bool OffersDelivery,
    bool OffersPickup,
    decimal DeliveryFee = 0m,
    decimal MinOrderAmount = 0m,
    double? RoadKm = null,
    int ProductCount = 0,
    int RescueDeals = 0,
    int BestDiscountPercent = 0,
    bool DeliveryInRange = true);

/// <summary>
/// Katalog grosir yang terlihat oleh warung. Hanya grosir VERIFIED + aktif; hanya produk aktif
/// yang punya harga grosir. Membaca data lintas usaha secara sengaja (IgnoreQueryFilters), dan hanya
/// data yang memang publik di marketplace (harga, kemasan, stok, info pengiriman).
/// </summary>
public interface IMarketplaceService
{
    Task<IReadOnlyList<WholesalerCardDto>> SearchAsync(MarketplaceFilter filter, CancellationToken ct = default);
    Task<Result<WholesalerDetailDto>> GetWholesalerAsync(int businessId, CancellationToken ct = default);
    Task<IReadOnlyList<MapPointDto>> GetMapPointsAsync(CancellationToken ct = default);
}
