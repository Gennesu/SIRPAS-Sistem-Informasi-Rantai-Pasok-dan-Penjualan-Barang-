namespace Platform.Web.Modules.Map.Contracts;

/// <summary>Satu warung di peta persebaran milik grosir.</summary>
public sealed record WarungPointDto(
    int BusinessId,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    double? DistanceKm,
    double? RoadKm,
    decimal? DeliveryFee,
    bool InRange,
    bool IsCustomer,
    int OrdersCount,
    decimal TotalSpent,
    DateTime? LastOrderAt,
    string? Phone,
    int? SoldQtyBase,
    double? AvgPerDay);

public sealed record MapProductOption(int ProductId, string Name, string BaseUnitName, int TotalSoldBase);

public sealed record WarungMapDto(
    string OriginName,
    double? OriginLat,
    double? OriginLon,
    bool OffersDelivery,
    double? ServiceRadiusKm,
    IReadOnlyList<WarungPointDto> Warungs,
    IReadOnlyList<MapProductOption> Products,
    int? SelectedProductId,
    string? SelectedProductName,
    string? SelectedUnit);

public sealed record LivestockMapPointDto(
    int BusinessId,
    string Name,
    string Address,
    string Phone,
    double Latitude,
    double Longitude,
    double? DistanceKm);

public sealed record LivestockMapDto(
    string? OriginName,
    double? OriginLat,
    double? OriginLon,
    int FarmsWithoutLocation,
    IReadOnlyList<LivestockMapPointDto> Farms);

/// <summary>
/// Peta persebaran warung untuk grosir: warung langganan (pernah memesan) dan calon pelanggan di sekitar,
/// lengkap dengan jarak dan ongkir menurut tarif grosir sendiri. Alamat dan telepon hanya tampil untuk langganan.
/// Bila grosir memilih produk, tiap warung menampilkan jumlah terjual produk itu (hanya dari warung yang berbagi data).
/// </summary>
public interface IWarungMapService
{
    Task<WarungMapDto> GetAsync(int? productId, CancellationToken ct = default);
}

public interface ILivestockMapService
{
    Task<LivestockMapDto> GetForAgentAsync(CancellationToken ct = default);
}
