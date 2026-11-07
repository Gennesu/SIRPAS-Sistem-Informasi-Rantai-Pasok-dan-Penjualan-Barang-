using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Delivery.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Delivery.Controllers;

[Authorize(Roles = RoleNames.Grossier + "," + RoleNames.Retail + "," + RoleNames.Livestock)]
public class DeliveryController : Controller
{
    private readonly IDeliveryService _delivery;

    public DeliveryController(IDeliveryService delivery)
    {
        _delivery = delivery;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var current = await _delivery.GetMineAsync(ct);
        return View(new DeliverySettingsViewModel
        {
            OffersDelivery = current.OffersDelivery,
            OffersPickup = current.OffersPickup,
            DeliveryFee = current.DeliveryFee,
            FreeKm = current.FreeKm,
            FeePerKm = current.FeePerKm,
            MaxDistanceKm = current.MaxDistanceKm,
            MinOrderAmount = current.MinOrderAmount,
            DeliveryInfo = current.DeliveryInfo,
            PickupInfo = current.PickupInfo
        });
    }

    [HttpPost]
    public async Task<IActionResult> Index(DeliverySettingsViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _delivery.SaveMineAsync(
            new SaveDeliveryRequest(
                model.OffersDelivery, model.OffersPickup, model.DeliveryFee, model.MinOrderAmount,
                model.DeliveryInfo, model.PickupInfo, model.FeePerKm, model.FreeKm, model.MaxDistanceKm),
            ct);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Pengaturan gagal disimpan.");
            return View(model);
        }

        TempData["Success"] = "Pengaturan pengiriman disimpan.";
        return RedirectToAction(nameof(Index));
    }
}
