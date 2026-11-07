using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Insights.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Insights.Controllers;

public sealed record InsightsViewModel(IReadOnlyList<ProductDemandDto> Products, int Days, int HiddenCustomers, int? SelectedProductId);

/// <summary>Grosir: produk apa yang paling laku di warung langganan, dan di warung mana.</summary>
[Authorize(Roles = RoleNames.Grossier)]
public class InsightsController : Controller
{
    private readonly ISellThroughService _sellThrough;

    public InsightsController(ISellThroughService sellThrough) => _sellThrough = sellThrough;

    public async Task<IActionResult> Index(int days = 30, int? productId = null, CancellationToken ct = default)
    {
        days = days is 7 or 14 or 30 or 90 ? days : 30;
        var products = await _sellThrough.GetDemandAsync(days, null, ct);
        return View(new InsightsViewModel(products, days, await _sellThrough.CountHiddenCustomersAsync(ct), productId));
    }
}
