using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Map.Contracts;
using Platform.Web.Modules.Map.ViewModels;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Map.Controllers;

/// <summary>
/// Peta warung: grosir terdekat. Peternakan: grosir calon pembeli hasil ternak. Agen: warung pelanggan dan peternakan pemasok terdekat.
/// </summary>
[Authorize(Roles = RoleNames.Retail + "," + RoleNames.Grossier + "," + RoleNames.Livestock + "," + RoleNames.Consumer)]
public class MapController : Controller
{
    private readonly IMarketplaceService _marketplace;
    private readonly IBusinessService _business;
    private readonly IBusinessContext _context;
    private readonly IWarungMapService _warungMap;
    private readonly ILivestockMapService _livestockMap;
    private readonly IExpiryService _expiry;
    private readonly IConsumerService _consumers;
    private readonly IConsumerCatalogService _consumerCatalog;

    public MapController(
        IMarketplaceService marketplace, IBusinessService business, IBusinessContext context,
        IWarungMapService warungMap, ILivestockMapService livestockMap, IExpiryService expiry,
        IConsumerService consumers, IConsumerCatalogService consumerCatalog)
    {
        _marketplace = marketplace;
        _business = business;
        _context = context;
        _warungMap = warungMap;
        _livestockMap = livestockMap;
        _expiry = expiry;
        _consumers = consumers;
        _consumerCatalog = consumerCatalog;
    }

    // Daftar di samping peta tetap berfungsi tanpa internet; ubin peta dimuat dari internet.
    public async Task<IActionResult> Index(int? productId, string? mapType, CancellationToken ct)
    {
        if (User.IsInRole(RoleNames.Grossier))
        {
            if (string.Equals(mapType, "peternakan", StringComparison.OrdinalIgnoreCase))
            {
                ViewData["MapType"] = "peternakan";
                return View("Livestock", await _livestockMap.GetForAgentAsync(ct));
            }

            ViewData["MapType"] = "warung";
            var data = await _warungMap.GetAsync(productId, ct);
            BatchDto? batch = null;
            if (data.SelectedProductId is int selected)
            {
                batch = (await _expiry.GetBatchesAsync(30, false, selected, ct)).OrderBy(b => b.DaysLeft).FirstOrDefault();
            }

            return View("Warung", new WarungMapViewModel(data, batch));
        }

        if (User.IsInRole(RoleNames.Consumer))
        {
            var profile = await _consumers.GetProfileAsync(ct);
            var shops = await _consumerCatalog.SearchShopsAsync(null, ct);
            var consumer = profile.Succeeded ? profile.Value : null;
            return View("Consumer", new ConsumerMapViewModel(
                consumer?.FullName, consumer?.Latitude, consumer?.Longitude, shops));
        }

        var own = await GetOwnAsync(ct);
        return View(new MapViewModel
        {
            Wholesalers = await _marketplace.SearchAsync(new MarketplaceFilter(null, null, null, null), ct),
            HasOwnLocation = own is not null,
            OwnLat = own?.Lat,
            OwnLon = own?.Lon,
            OwnName = own?.Name
        });
    }

    [Authorize(Roles = RoleNames.Grossier)]
    public IActionResult Livestock() => RedirectToAction(nameof(Index), new { mapType = "peternakan" });

    // JSON titik grosir (dipertahankan untuk integrasi; tampilan peta memakai data di kartu).
    [Authorize(Roles = RoleNames.Retail)]
    [HttpGet("map/points")]
    public async Task<IActionResult> Points(CancellationToken ct)
    {
        var own = await GetOwnAsync(ct);
        var points = await _marketplace.GetMapPointsAsync(ct);

        return Json(new
        {
            own = own is null ? null : new { name = own.Value.Name, lat = own.Value.Lat, lon = own.Value.Lon },
            points = points.Select(p => new
            {
                id = p.BusinessId,
                name = p.Name,
                address = p.Address,
                lat = p.Latitude,
                lon = p.Longitude,
                distanceKm = p.DistanceKm,
                roadKm = p.RoadKm,
                delivery = p.OffersDelivery,
                pickup = p.OffersPickup,
                deliveryFee = p.DeliveryFee,
                inRange = p.DeliveryInRange,
                minOrder = p.MinOrderAmount,
                deals = p.RescueDeals,
                bestDiscount = p.BestDiscountPercent,
                url = Url.Action("Wholesaler", "Marketplace", new { id = p.BusinessId })
            })
        });
    }

    private async Task<(string Name, double Lat, double Lon)?> GetOwnAsync(CancellationToken ct)
    {
        if (_context.BusinessId is not int businessId)
        {
            return null;
        }

        var profile = await _business.GetProfileAsync(businessId, ct);
        if (!profile.Succeeded || profile.Value!.Latitude is null || profile.Value.Longitude is null)
        {
            return null;
        }

        return (profile.Value.Name, profile.Value.Latitude.Value, profile.Value.Longitude.Value);
    }
}
