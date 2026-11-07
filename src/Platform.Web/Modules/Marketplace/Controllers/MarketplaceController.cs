using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Marketplace.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Marketplace.Controllers;

[Authorize(Roles = RoleNames.Retail)]
public class MarketplaceController : Controller
{
    private readonly IMarketplaceService _marketplace;
    private readonly IProductService _products;

    public MarketplaceController(IMarketplaceService marketplace, IProductService products)
    {
        _marketplace = marketplace;
        _products = products;
    }

    public async Task<IActionResult> Index(string? q, int? categoryId, double? maxKm, FulfillmentType? fulfillment, CancellationToken ct)
    {
        var categories = await _products.GetCategoriesAsync(ct);
        var model = new MarketplaceIndexViewModel
        {
            Query = q,
            CategoryId = categoryId,
            MaxKm = maxKm,
            Fulfillment = fulfillment,
            Cards = await _marketplace.SearchAsync(new MarketplaceFilter(q, categoryId, maxKm, fulfillment), ct),
            Categories = categories.Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == categoryId)).ToList()
        };

        return View(model);
    }

    public async Task<IActionResult> Wholesaler(int id, CancellationToken ct)
    {
        var result = await _marketplace.GetWholesalerAsync(id, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        return View(result.Value);
    }
}
