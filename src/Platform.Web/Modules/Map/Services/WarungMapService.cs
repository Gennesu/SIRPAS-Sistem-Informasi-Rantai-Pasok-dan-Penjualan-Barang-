using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Insights.Contracts;
using Platform.Web.Modules.Map.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Map.Services;

public sealed class WarungMapService : IWarungMapService
{
    private const int MaxProspects = 120;

    private static readonly OrderStatus[] CustomerStatuses =
    {
        OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready,
        OrderStatus.Delivered, OrderStatus.PickedUp, OrderStatus.Completed
    };

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IDeliveryService _delivery;
    private readonly ISellThroughService _sellThrough;

    public WarungMapService(AppDbContext db, IBusinessContext context, IDeliveryService delivery, ISellThroughService sellThrough)
    {
        _db = db;
        _context = context;
        _delivery = delivery;
        _sellThrough = sellThrough;
    }

    public async Task<WarungMapDto> GetAsync(int? productId, CancellationToken ct = default)
    {
        var empty = new WarungMapDto(string.Empty, null, null, false, null, Array.Empty<WarungPointDto>(), Array.Empty<MapProductOption>(), null, null, null);
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Grossier || _context.BusinessId is not int me)
        {
            return empty;
        }

        var origin = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == me, ct);
        if (origin is null)
        {
            return empty;
        }

        var option = await _delivery.GetMineAsync(ct);

        // Pesanan milik grosir ini (filter dua pihak) -> siapa saja langganan dan seberapa aktif.
        var orders = await _db.Orders.AsNoTracking()
            .Where(o => o.SellerBusinessId == me && CustomerStatuses.Contains(o.Status))
            .Select(o => new { o.BuyerBusinessId, o.TotalAmount, o.CreatedAt })
            .ToListAsync(ct);
        var customers = orders
            .GroupBy(o => o.BuyerBusinessId)
            .ToDictionary(g => g.Key, g => (Count: g.Count(), Total: g.Sum(x => x.TotalAmount), Last: g.Max(x => x.CreatedAt)));

        // Warung terverifikasi dan aktif yang punya koordinat (nama dan lokasi usaha memang bagian dari marketplace).
        var retail = await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole == MarketRole.Retail && b.VerificationStatus == VerificationStatus.Verified && b.IsActive
                        && b.Latitude != null && b.Longitude != null)
            .ToListAsync(ct);

        var quotes = await _delivery.QuoteForSellerAsync(retail.Select(b => b.Id).ToList(), ct);

        // Pilihan produk + penjualan per warung (hanya yang membagikan data).
        var demandAll = await _sellThrough.GetDemandAsync(30, null, ct);
        var products = demandAll
            .Select(d => new MapProductOption(d.ProductId, d.ProductName, d.BaseUnitName, d.TotalSoldBase))
            .OrderByDescending(p => p.TotalSoldBase)
            .ToList();
        var selected = productId is int pid ? demandAll.FirstOrDefault(d => d.ProductId == pid) : null;
        var soldByWarung = selected?.Warungs.ToDictionary(w => w.BusinessId) ?? new Dictionary<int, WarungDemandDto>();

        var points = new List<WarungPointDto>();
        foreach (var b in retail)
        {
            var isCustomer = customers.TryGetValue(b.Id, out var c);
            double? distance = origin.Latitude is double la && origin.Longitude is double lo
                ? Math.Round(Geo.DistanceKm(la, lo, b.Latitude!.Value, b.Longitude!.Value), 2)
                : null;
            var quote = quotes.GetValueOrDefault(b.Id);
            soldByWarung.TryGetValue(b.Id, out var demand);

            points.Add(new WarungPointDto(
                b.Id, b.Name,
                isCustomer ? b.Address : string.Empty,
                b.Latitude!.Value, b.Longitude!.Value,
                distance, quote?.RoadKm, quote?.Available == true ? quote.Fee : null, quote?.Available ?? true,
                isCustomer, isCustomer ? c.Count : 0, isCustomer ? c.Total : 0m, isCustomer ? c.Last : null,
                isCustomer ? b.Phone : null,
                selected is null ? null : demand?.SoldQtyBase,
                selected is null ? null : demand?.AvgPerDay));
        }

        // Langganan semuanya; calon pelanggan hanya yang terdekat agar peta tetap jelas.
        var ordered = points.Where(p => p.IsCustomer).OrderBy(p => p.DistanceKm ?? double.MaxValue).ThenBy(p => p.Name).ToList();
        ordered.AddRange(points.Where(p => !p.IsCustomer).OrderBy(p => p.DistanceKm ?? double.MaxValue).Take(MaxProspects));

        double? radius = option.MaxDistanceKm is decimal max && max > 0 ? Math.Round((double)max / Geo.RoadFactor, 2) : null;
        return new WarungMapDto(
            origin.Name, origin.Latitude, origin.Longitude, option.OffersDelivery, radius,
            ordered, products, selected?.ProductId, selected?.ProductName, selected?.BaseUnitName);
    }
}
