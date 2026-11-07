using Microsoft.AspNetCore.Mvc.Rendering;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;

namespace Platform.Web.Modules.Marketplace.ViewModels;

public class MarketplaceIndexViewModel
{
    public string? Query { get; set; }
    public int? CategoryId { get; set; }
    public double? MaxKm { get; set; }
    public FulfillmentType? Fulfillment { get; set; }
    public IReadOnlyList<WholesalerCardDto> Cards { get; set; } = Array.Empty<WholesalerCardDto>();
    public List<SelectListItem> Categories { get; set; } = new();
}
