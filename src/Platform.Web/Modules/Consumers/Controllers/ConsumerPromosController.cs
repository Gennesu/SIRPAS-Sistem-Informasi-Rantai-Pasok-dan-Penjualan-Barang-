using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Controllers;

/// <summary>Warung mengatur promo untuk stok yang mendekati kedaluwarsa agar terjual ke pembeli akhir (mengurangi food waste).</summary>
[Authorize(Roles = RoleNames.Retail)]
public class ConsumerPromosController : Controller
{
    private readonly IConsumerPromoService _promos;

    public ConsumerPromosController(IConsumerPromoService promos)
    {
        _promos = promos;
    }

    public async Task<IActionResult> Index(CancellationToken ct) => View(await _promos.ListMineAsync(ct));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int productId, int? discountPercent, int? withinDays, bool remove, CancellationToken ct)
    {
        var result = await _promos.SetAsync(productId, remove ? null : discountPercent, remove ? null : withinDays, ct);
        if (result.Succeeded)
        {
            TempData["Success"] = remove ? "Promo dihapus." : "Promo disimpan.";
        }
        else
        {
            TempData["Error"] = result.Error;
        }

        return RedirectToAction(nameof(Index));
    }
}
