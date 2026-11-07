using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Rescue.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Rescue.Controllers;

/// <summary>Sisi warung: promo anti-mubazir publik, penawaran khusus dari grosir, dan memesannya.</summary>
[Authorize(Roles = RoleNames.Retail)]
public class DealsController : Controller
{
    private readonly IRescueService _rescue;
    private readonly IOrderService _orders;
    private readonly IBusinessService _business;
    private readonly IBusinessContext _context;

    public DealsController(IRescueService rescue, IOrderService orders, IBusinessService business, IBusinessContext context)
    {
        _rescue = rescue;
        _orders = orders;
        _business = business;
        _context = context;
    }

    public async Task<IActionResult> Index(string? sort, int? seller, CancellationToken ct)
    {
        sort = sort is "discount" or "near" or "price" ? sort : "expiry";
        var all = await _rescue.ListPublicAsync(null, ct);
        var sellers = all.Where(b => b.Seller is not null).Select(b => (b.Seller!.BusinessId, b.Seller.Name)).Distinct().OrderBy(s => s.Name).ToList();

        IEnumerable<BundleDto> filtered = seller is int sid ? all.Where(b => b.Seller?.BusinessId == sid) : all;
        filtered = sort switch
        {
            "discount" => filtered.OrderByDescending(b => b.DiscountPercent),
            "near" => filtered.OrderBy(b => b.Seller?.DistanceKm ?? double.MaxValue),
            "price" => filtered.OrderBy(b => b.Price),
            _ => filtered.OrderBy(b => b.EarliestExpiryDays)
        };

        return View(new DealsViewModel(await _rescue.ListReceivedAsync(false, ct), filtered.ToList(), sort, seller, sellers));
    }

    public async Task<IActionResult> Offers(CancellationToken ct) => View(await _rescue.ListReceivedAsync(true, ct));

    public async Task<IActionResult> Detail(int id, int? offer, CancellationToken ct)
    {
        var found = await _rescue.GetForBuyerAsync(id, ct);
        if (!found.Succeeded)
        {
            return NotFound();
        }

        var buyerAddress = string.Empty;
        if (_context.BusinessId is int mine)
        {
            var profile = await _business.GetProfileAsync(mine, ct);
            buyerAddress = profile.Succeeded ? profile.Value!.Address : string.Empty;
        }

        var offers = await _rescue.ListReceivedAsync(true, ct);
        var matching = offers.FirstOrDefault(o => o.BundleId == id && (offer is null || o.Id == offer));
        var suggested = matching is { State: OfferState.Sent } ? matching.SuggestedBundles : 1;
        return View(new DealDetailViewModel
        {
            Bundle = found.Value!,
            Offer = matching,
            SuggestedQty = Math.Clamp(suggested, 1, Math.Max(found.Value!.Available, 1)),
            BuyerAddress = buyerAddress
        });
    }

    [HttpPost]
    public async Task<IActionResult> Decline(int id, CancellationToken ct)
    {
        var result = await _rescue.DeclineOfferAsync(id, ct);
        TempData[result.Succeeded ? "Success" : "Error"] = result.Succeeded ? "Penawaran ditolak." : result.Error;
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Order(DealOrderInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Data pesanan tidak valid. Periksa jumlah paket dan alamat.";
            return RedirectToAction(nameof(Detail), new { id = input.BundleId, offer = input.OfferId });
        }

        var bundle = await _rescue.GetForBuyerAsync(input.BundleId, ct);
        if (!bundle.Succeeded)
        {
            TempData["Error"] = "Paket tidak ditemukan atau sudah tidak tersedia.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _orders.CreateAsync(
            new CreateOrderRequest(
                bundle.Value!.Seller!.BusinessId, input.Fulfillment, input.Note, input.DeliveryAddress,
                Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(input.BundleId, input.Qty) }, input.OfferId),
            ct);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error ?? "Pesanan gagal dibuat.";
            return RedirectToAction(nameof(Detail), new { id = input.BundleId, offer = input.OfferId });
        }

        TempData["Success"] = "Pesanan paket terkirim ke grosir. Pantau fasenya di sini.";
        return RedirectToAction("Detail", "Orders", new { id = result.Value });
    }
}
