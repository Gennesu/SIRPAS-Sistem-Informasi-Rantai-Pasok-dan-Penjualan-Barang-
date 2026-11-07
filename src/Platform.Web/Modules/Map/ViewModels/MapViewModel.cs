using Platform.Web.Modules.Marketplace.Contracts;

namespace Platform.Web.Modules.Map.ViewModels;

public class MapViewModel
{
    public IReadOnlyList<WholesalerCardDto> Wholesalers { get; set; } = Array.Empty<WholesalerCardDto>();
    public bool HasOwnLocation { get; set; }
    public double? OwnLat { get; set; }
    public double? OwnLon { get; set; }
    public string? OwnName { get; set; }
}

/// <summary>Peta persebaran warung untuk grosir. OfferBatch = batch yang paling cocok ditawarkan untuk produk terpilih (bila ada).</summary>
public sealed record WarungMapViewModel(
    Platform.Web.Modules.Map.Contracts.WarungMapDto Data,
    Platform.Web.Modules.Stock.Contracts.BatchDto? OfferBatch);
