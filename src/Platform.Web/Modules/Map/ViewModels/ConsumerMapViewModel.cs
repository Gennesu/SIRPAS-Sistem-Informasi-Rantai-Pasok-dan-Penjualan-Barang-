using Platform.Web.Modules.Consumers.Contracts;

namespace Platform.Web.Modules.Map.ViewModels;

public sealed record ConsumerMapViewModel(
    string? OriginName,
    double? OriginLat,
    double? OriginLon,
    IReadOnlyList<ShopCardDto> Shops);
