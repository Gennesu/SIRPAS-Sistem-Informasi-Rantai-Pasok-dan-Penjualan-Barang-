using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Insights.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Sales.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Insights.Services;

public sealed class SellThroughService : ISellThroughService
{
    private static readonly OrderStatus[] RelationshipStatuses =
    {
        OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready,
        OrderStatus.Delivered, OrderStatus.PickedUp, OrderStatus.Completed
    };

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly TimeProvider _time;

    public SellThroughService(AppDbContext db, IBusinessContext context, TimeProvider time)
    {
        _db = db;
        _context = context;
        _time = time;
    }

    private bool TryGetGrossier(out int businessId)
    {
        businessId = _context.BusinessId ?? 0;
        return _context.IsAuthenticated && _context.Role == RoleNames.Grossier && _context.BusinessId is not null;
    }

    private sealed record Customer(int BusinessId, string Name, string Address, double? Lat, double? Lon, bool Shares, DateTime LastOrderAt);

    private async Task<List<Customer>> LoadCustomersAsync(int me, CancellationToken ct)
    {
        // Filter dua pihak membatasi pesanan hanya pada pesanan milik grosir ini.
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.SellerBusinessId == me && RelationshipStatuses.Contains(o.Status))
            .Select(o => new { o.BuyerBusinessId, o.CreatedAt })
            .ToListAsync(ct);

        var lastByBuyer = orders
            .GroupBy(o => o.BuyerBusinessId)
            .ToDictionary(g => g.Key, g => g.Max(x => x.CreatedAt));
        if (lastByBuyer.Count == 0)
        {
            return new List<Customer>();
        }

        var ids = lastByBuyer.Keys.ToList();
        var businesses = await _db.Businesses.AsNoTracking().Where(b => ids.Contains(b.Id) && b.IsActive).ToListAsync(ct);
        return businesses
            .Select(b => new Customer(b.Id, b.Name, b.Address, b.Latitude, b.Longitude, b.ShareSalesData, lastByBuyer[b.Id]))
            .ToList();
    }

    public async Task<int> CountHiddenCustomersAsync(CancellationToken ct = default)
    {
        if (!TryGetGrossier(out var me))
        {
            return 0;
        }

        return (await LoadCustomersAsync(me, ct)).Count(c => !c.Shares);
    }

    public async Task<IReadOnlyList<ProductDemandDto>> GetDemandAsync(int days = 30, int? productId = null, CancellationToken ct = default)
    {
        if (!TryGetGrossier(out var me))
        {
            return Array.Empty<ProductDemandDto>();
        }

        days = Math.Clamp(days, 1, 365);

        var myProducts = await _db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        if (productId is int pid)
        {
            myProducts = myProducts.Where(p => p.Id == pid).ToList();
        }

        if (myProducts.Count == 0)
        {
            return Array.Empty<ProductDemandDto>();
        }

        var sharing = (await LoadCustomersAsync(me, ct)).Where(c => c.Shares).ToList();
        if (sharing.Count == 0)
        {
            return Array.Empty<ProductDemandDto>();
        }

        var sharingIds = sharing.Select(c => c.BusinessId).ToList();
        var origin = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == me, ct);

        // ---- Pembacaan lintas usaha yang disengaja (lihat komentar antarmuka) ----
        var warungProducts = await _db.Products.IgnoreQueryFilters().AsNoTracking()
            .Where(p => sharingIds.Contains(p.BusinessId))
            .ToListAsync(ct);

        var mapping = new Dictionary<int, int>(); // id produk warung -> id produk grosir
        foreach (var wp in warungProducts)
        {
            var match =
                myProducts.FirstOrDefault(m => wp.SourceProductId == m.Id)
                ?? myProducts.FirstOrDefault(m => !string.IsNullOrEmpty(m.Sku) && string.Equals(m.Sku, wp.Sku, StringComparison.OrdinalIgnoreCase))
                ?? myProducts.FirstOrDefault(m => string.Equals(m.Name, wp.Name, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                mapping[wp.Id] = match.Id;
            }
        }

        var since = _time.GetUtcNow().UtcDateTime.AddDays(-days);
        var sales = await _db.Sales.IgnoreQueryFilters().AsNoTracking().Include(s => s.Items)
            .Where(s => sharingIds.Contains(s.BusinessId) && s.Status == SaleStatus.Confirmed && s.SoldAt >= since)
            .ToListAsync(ct);

        var warungStock = await _db.Inventories.IgnoreQueryFilters().AsNoTracking()
            .Where(i => sharingIds.Contains(i.BusinessId))
            .ToListAsync(ct);

        var myStock = (await _db.Inventories.AsNoTracking().ToListAsync(ct)).ToDictionary(i => i.ProductId, i => i.QtyBase);

        // (produk grosir, warung) -> jumlah terjual
        var soldByPair = new Dictionary<(int Product, int Business), int>();
        foreach (var item in sales.SelectMany(s => s.Items.Select(i => (s.BusinessId, Item: i))))
        {
            if (!mapping.TryGetValue(item.Item.ProductId, out var mine))
            {
                continue;
            }

            var key = (mine, item.BusinessId);
            soldByPair[key] = (soldByPair.TryGetValue(key, out var current) ? current : 0) + item.Item.QtyBase;
        }

        var result = new List<ProductDemandDto>();
        foreach (var product in myProducts)
        {
            // Warung yang menjual produk ini = punya produk yang cocok dengan produk grosir ini.
            var carriers = warungProducts
                .Where(wp => mapping.TryGetValue(wp.Id, out var mine) && mine == product.Id)
                .GroupBy(wp => wp.BusinessId)
                .ToList();

            var rows = new List<WarungDemandDto>();
            foreach (var carrier in carriers)
            {
                var customer = sharing.First(c => c.BusinessId == carrier.Key);
                var sold = soldByPair.TryGetValue((product.Id, carrier.Key), out var s) ? s : 0;
                var stock = warungStock
                    .Where(i => i.BusinessId == carrier.Key && carrier.Any(wp => wp.Id == i.ProductId))
                    .Sum(i => i.QtyBase);
                var perDay = sold / (double)days;

                double? distance = origin?.Latitude is double la && origin.Longitude is double lo && customer.Lat is double wla && customer.Lon is double wlo
                    ? Math.Round(Geo.DistanceKm(la, lo, wla, wlo), 2)
                    : null;

                rows.Add(new WarungDemandDto(
                    customer.BusinessId, customer.Name, customer.Address, distance, sold,
                    Math.Round(perDay, 2), stock, perDay > 0 ? Math.Round(stock / perDay, 1) : null, customer.LastOrderAt));
            }

            if (rows.Count == 0)
            {
                continue;
            }

            result.Add(new ProductDemandDto(
                product.Id, product.Name, product.BaseUnitName,
                rows.Sum(r => r.SoldQtyBase), rows.Count(r => r.SoldQtyBase > 0),
                myStock.TryGetValue(product.Id, out var mine) ? mine : 0,
                rows.OrderByDescending(r => r.SoldQtyBase).ThenBy(r => r.Name).ToList()));
        }

        return result.OrderByDescending(r => r.TotalSoldBase).ThenBy(r => r.ProductName).ToList();
    }
}
