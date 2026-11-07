using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Marketplace.Services;

public sealed class MarketplaceService : IMarketplaceService
{
    private const string NotFoundMessage = "Penjual tidak ditemukan atau belum terverifikasi.";

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IBusinessService _business;
    private readonly IDeliveryService _delivery;
    private readonly IRescueService _rescue;
    private readonly TimeProvider _time;

    public MarketplaceService(
        AppDbContext db, IBusinessContext context, IBusinessService business, IDeliveryService delivery, IRescueService rescue,
        TimeProvider time)
    {
        _db = db;
        _context = context;
        _business = business;
        _delivery = delivery;
        _rescue = rescue;
        _time = time;
    }

    private sealed record Eligible(
        int BusinessId, string Name, string Address, double? Latitude, double? Longitude,
        DeliveryOptionDto Delivery, List<WholesaleProductDto> Products);

    private sealed record Origin(double? Latitude, double? Longitude, string Address);

    public async Task<IReadOnlyList<WholesalerCardDto>> SearchAsync(MarketplaceFilter filter, CancellationToken ct = default)
    {
        var origin = await GetOriginAsync(ct);
        var term = filter.Query?.Trim();
        var cards = new List<WholesalerCardDto>();

        var eligible = await LoadEligibleAsync(null, ct);
        var quotes = await _delivery.QuoteForBuyerAsync(eligible.Select(w => w.BusinessId).ToList(), ct);
        var deals = await _rescue.GetDealSummaryAsync(ct);

        foreach (var w in eligible)
        {
            var matching = new List<string>();
            if (!string.IsNullOrEmpty(term))
            {
                var nameMatch = w.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
                matching = w.Products
                    .Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || (p.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(p => p.Name)
                    .ToList();
                if (!nameMatch && matching.Count == 0)
                {
                    continue;
                }
            }

            if (filter.CategoryId is int categoryId && !w.Products.Any(p => p.CategoryId == categoryId))
            {
                continue;
            }

            if (filter.Fulfillment == FulfillmentType.Delivery
                && (!w.Delivery.OffersDelivery || (quotes.TryGetValue(w.BusinessId, out var q) && !q.Available)))
            {
                continue;
            }

            if (filter.Fulfillment == FulfillmentType.Pickup && !w.Delivery.OffersPickup)
            {
                continue;
            }

            var distance = DistanceTo(origin, w);
            if (filter.MaxDistanceKm is double maxKm && (distance is null || distance > maxKm))
            {
                continue;
            }

            cards.Add(ToCard(w, distance, matching.Take(4).ToList(), quotes.GetValueOrDefault(w.BusinessId), deals.GetValueOrDefault(w.BusinessId)));
        }

        return cards
            .OrderBy(c => c.DistanceKm ?? double.MaxValue)
            .ThenBy(c => c.Name)
            .ToList();
    }

    public async Task<Result<WholesalerDetailDto>> GetWholesalerAsync(int businessId, CancellationToken ct = default)
    {
        var w = (await LoadEligibleAsync(businessId, ct)).FirstOrDefault();
        if (w is null)
        {
            return Result<WholesalerDetailDto>.Fail(ErrorKind.NotFound, NotFoundMessage);
        }

        var origin = await GetOriginAsync(ct);
        var info = await _delivery.GetPublicAsync(new[] { businessId }, ct);
        var option = info[businessId];
        var quote = (await _delivery.QuoteForBuyerAsync(new[] { businessId }, ct)).GetValueOrDefault(businessId);
        var bundles = await _rescue.ListPublicAsync(businessId, ct);

        return Result<WholesalerDetailDto>.Ok(new WholesalerDetailDto(
            ToCard(
                w, DistanceTo(origin, w), new List<string>(), quote,
                bundles.Count == 0 ? default : (bundles.Count, bundles.Max(b => b.DiscountPercent))),
            option.DeliveryInfo,
            option.PickupInfo,
            origin.Address,
            w.Products,
            bundles));
    }

    public async Task<IReadOnlyList<MapPointDto>> GetMapPointsAsync(CancellationToken ct = default)
    {
        var origin = await GetOriginAsync(ct);
        var eligible = (await LoadEligibleAsync(null, ct))
            .Where(w => w.Latitude is not null && w.Longitude is not null)
            .ToList();
        var quotes = await _delivery.QuoteForBuyerAsync(eligible.Select(w => w.BusinessId).ToList(), ct);
        var deals = await _rescue.GetDealSummaryAsync(ct);

        return eligible
            .Select(w =>
            {
                var quote = quotes.GetValueOrDefault(w.BusinessId);
                var deal = deals.GetValueOrDefault(w.BusinessId);
                return new MapPointDto(
                    w.BusinessId, w.Name, w.Address, w.Latitude!.Value, w.Longitude!.Value,
                    DistanceTo(origin, w), w.Delivery.OffersDelivery, w.Delivery.OffersPickup,
                    quote?.Fee ?? w.Delivery.DeliveryFee, w.Delivery.MinOrderAmount, quote?.RoadKm,
                    w.Products.Count, deal.Deals, deal.BestDiscountPercent, quote?.Available ?? true);
            })
            .OrderBy(p => p.DistanceKm ?? double.MaxValue)
            .ToList();
    }

    // ------------------------------------------------------------------ helper

    private async Task<Origin> GetOriginAsync(CancellationToken ct)
    {
        var businessId = _context.BusinessId;
        if (businessId is null)
        {
            return new Origin(null, null, string.Empty);
        }

        var profile = await _business.GetProfileAsync(businessId.Value, ct);
        return profile.Succeeded
            ? new Origin(profile.Value!.Latitude, profile.Value.Longitude, profile.Value.Address)
            : new Origin(null, null, string.Empty);
    }

    private static double? DistanceTo(Origin origin, Eligible w)
    {
        if (origin.Latitude is null || origin.Longitude is null || w.Latitude is null || w.Longitude is null)
        {
            return null;
        }

        return Math.Round(Geo.DistanceKm(origin.Latitude.Value, origin.Longitude.Value, w.Latitude.Value, w.Longitude.Value), 2);
    }

    // DeliveryFee pada kartu = ongkir untuk lokasi warung yang sedang login (bukan lagi tarif tetap).
    private static WholesalerCardDto ToCard(
        Eligible w, double? distance, IReadOnlyList<string> matching, DeliveryQuote? quote, (int Deals, int BestDiscountPercent) deals = default) => new(
        w.BusinessId, w.Name, w.Address, w.Latitude, w.Longitude, distance,
        w.Delivery.OffersDelivery, w.Delivery.OffersPickup, quote?.Fee ?? w.Delivery.DeliveryFee, w.Delivery.MinOrderAmount,
        w.Products.Count, matching,
        quote?.RoadKm, quote?.Available ?? true, quote?.Reason,
        deals.Deals, deals.BestDiscountPercent, w.Delivery.FeePerKm, w.Delivery.FreeKm, quote?.TravelMinutes,
        PreviewOf(w));

    private static IReadOnlyList<string> PreviewOf(Eligible w) =>
        w.Products.Where(p => !string.IsNullOrEmpty(p.ImagePath)).Select(p => p.ImagePath!).Take(4).ToList();

    private async Task<List<Eligible>> LoadEligibleAsync(int? onlyBusinessId, CancellationToken ct)
    {
        var businesses = await _db.Businesses
            .AsNoTracking()
            .Where(b => b.MarketRole == MarketRole.Wholesale && b.VerificationStatus == VerificationStatus.Verified && b.IsActive)
            .ToListAsync(ct);

        if (onlyBusinessId is int only)
        {
            businesses = businesses.Where(b => b.Id == only).ToList();
        }

        if (businesses.Count == 0)
        {
            return new List<Eligible>();
        }

        var ids = businesses.Select(b => b.Id).ToList();

        // Baca lintas usaha secara sengaja: katalog grosir memang publik bagi warung.
        var products = await _db.Products
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(p => p.Units)
            .Include(p => p.Category)
            .Where(p => p.IsActive && ids.Contains(p.BusinessId))
            .ToListAsync(ct);

        var inventories = await _db.Inventories
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(i => ids.Contains(i.BusinessId))
            .ToListAsync(ct);

        // Stok yang bisa dipesan = fisik - dicadangkan pesanan lain - bagian batch yang sudah kedaluwarsa.
        var today = WibTime.TodayWib(_time);
        var expiredFree = (await _db.StockBatches
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(b => ids.Contains(b.BusinessId) && b.ExpiryDate < today && b.QtyRemainingBase > b.ReservedBase)
                .ToListAsync(ct))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(b => b.QtyRemainingBase - b.ReservedBase));
        var stockByProduct = inventories.ToDictionary(
            i => i.ProductId,
            i => Math.Max(0, i.QtyBase - i.ReservedBase - (expiredFree.TryGetValue(i.ProductId, out var ex) ? ex : 0)));

        var delivery = await _delivery.GetPublicAsync(ids, ct);

        var result = new List<Eligible>();
        foreach (var business in businesses)
        {
            var list = new List<WholesaleProductDto>();
            foreach (var p in products.Where(x => x.BusinessId == business.Id).OrderBy(x => x.Name))
            {
                var ordered = p.Units.OrderBy(u => u.ConversionToBase).ToList();
                var priced = ordered
                    .Where(u => u.WholesalePrice is > 0)
                    .Select(u => new WholesaleUnitDto(u.UnitName, u.ConversionToBase, u.WholesalePrice!.Value))
                    .ToList();
                if (priced.Count == 0)
                {
                    continue; // tanpa harga grosir = belum dijual di marketplace
                }

                var stock = stockByProduct.TryGetValue(p.Id, out var qty) ? qty : 0;
                var allUnits = ordered.Select(u => new ProductUnitDto(u.UnitName, u.ConversionToBase, u.IsBase, u.WholesalePrice)).ToList();

                list.Add(new WholesaleProductDto(
                    p.Id, p.Name, p.Sku, p.CategoryId, p.Category.Name, p.BaseUnitName,
                    string.Join("|", ordered.Select(u => $"{u.UnitName}:{u.ConversionToBase}")),
                    stock, PackagingMath.Breakdown(stock, allUnits), priced, p.MoqBase, p.ImagePath));
            }

            result.Add(new Eligible(
                business.Id, business.Name, business.Address, business.Latitude, business.Longitude,
                delivery[business.Id], list));
        }

        return result.Where(w => w.Products.Count > 0).ToList();
    }
}
