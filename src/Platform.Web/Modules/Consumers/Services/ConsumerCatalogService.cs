using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Services;

/// <summary>
/// Katalog eceran untuk pembeli akhir. Membaca stok warung secara lintas usaha dengan sengaja (katalog toko memang publik bagi
/// pembeli), tetapi HANYA data yang layak ditampilkan: harga jual, jumlah yang boleh dijual, dan tanggal kedaluwarsa terdekat.
/// Modal, stok internal, dan laporan warung tidak pernah ikut terbaca.
/// </summary>
public sealed class ConsumerCatalogService : IConsumerCatalogService
{
    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IDeliveryService _delivery;
    private readonly TimeProvider _time;

    public ConsumerCatalogService(AppDbContext db, IBusinessContext context, IDeliveryService delivery, TimeProvider time)
    {
        _db = db;
        _context = context;
        _delivery = delivery;
        _time = time;
    }

    private sealed record ShopData(Business Business, DeliveryOptionDto Delivery, List<ShopProductDto> Products);

    private bool IsConsumer => _context.IsAuthenticated && _context.Role == RoleNames.Consumer;

    public async Task<IReadOnlyList<ShopCardDto>> SearchShopsAsync(string? query, CancellationToken ct = default)
    {
        if (!IsConsumer)
        {
            return Array.Empty<ShopCardDto>();
        }

        var term = query?.Trim();
        var origin = await GetOriginAsync(ct);
        var cards = new List<ShopCardDto>();

        foreach (var shop in await LoadAsync(null, ct))
        {
            var matching = new List<string>();
            if (!string.IsNullOrEmpty(term))
            {
                var nameMatch = shop.Business.Name.Contains(term, StringComparison.OrdinalIgnoreCase);
                matching = shop.Products
                    .Where(p => p.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                                || (p.Sku?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Select(p => p.Name)
                    .ToList();
                if (!nameMatch && matching.Count == 0)
                {
                    continue;
                }
            }

            cards.Add(ToCard(shop, origin, matching));
        }

        return cards
            .OrderBy(c => c.DistanceKm ?? double.MaxValue)
            .ThenBy(c => c.Name)
            .ToList();
    }

    public async Task<Result<ShopDetailDto>> GetShopAsync(int businessId, CancellationToken ct = default)
    {
        if (!IsConsumer)
        {
            return Result<ShopDetailDto>.Fail(ErrorKind.Forbidden, "Hanya akun pembeli yang dapat melihat katalog eceran.");
        }

        var shop = (await LoadAsync(businessId, ct)).FirstOrDefault();
        if (shop is null)
        {
            return Result<ShopDetailDto>.Fail(ErrorKind.NotFound, "Warung tidak ditemukan atau belum memiliki barang yang dijual.");
        }

        var origin = await GetOriginAsync(ct);
        return Result<ShopDetailDto>.Ok(new ShopDetailDto(
            ToCard(shop, origin, new List<string>()),
            shop.Delivery.DeliveryInfo,
            shop.Delivery.PickupInfo,
            origin.Address,
            shop.Products));
    }

    public async Task<IReadOnlyList<PromoDealDto>> ListPromosAsync(CancellationToken ct = default)
    {
        if (!IsConsumer)
        {
            return Array.Empty<PromoDealDto>();
        }

        var today = WibTime.TodayWib(_time);
        var deals = new List<PromoDealDto>();
        foreach (var shop in await LoadAsync(null, ct))
        {
            foreach (var p in shop.Products.Where(p => p.PromoQty > 0 && p.NearestExpiry is not null))
            {
                deals.Add(new PromoDealDto(
                    shop.Business.Id, shop.Business.Name, p.ProductId, p.Name, p.BaseUnitName, p.UnitPrice, p.PromoUnitPrice,
                    p.PromoPercent, p.PromoQty, p.NearestExpiry!.Value, p.NearestExpiry.Value.DayNumber - today.DayNumber, p.ImagePath));
            }
        }

        return deals.OrderBy(d => d.DaysLeft).ThenByDescending(d => d.Percent).ToList();
    }

    // ------------------------------------------------------------------ helper

    private sealed record Origin(double? Latitude, double? Longitude, string Address);

    private async Task<Origin> GetOriginAsync(CancellationToken ct)
    {
        var profile = await _db.ConsumerProfiles.AsNoTracking().FirstOrDefaultAsync(ct); // filter: hanya profil sendiri
        return new Origin(profile?.Latitude, profile?.Longitude, profile?.Address ?? string.Empty);
    }

    private static ShopCardDto ToCard(ShopData shop, Origin origin, IReadOnlyList<string> matching)
    {
        double? straight = origin.Latitude is double la && origin.Longitude is double lo
                           && shop.Business.Latitude is double sla && shop.Business.Longitude is double slo
            ? Geo.DistanceKm(la, lo, sla, slo)
            : null;
        var quote = DeliveryFeeCalculator.Quote(shop.Delivery, straight);
        return new ShopCardDto(
            shop.Business.Id, shop.Business.Name, shop.Business.Address, straight,
            shop.Delivery.OffersDelivery, shop.Delivery.OffersPickup, quote.Fee, quote.Available, quote.Reason,
            shop.Products.Count, shop.Products.Count(p => p.PromoQty > 0), matching,
            shop.Business.Latitude, shop.Business.Longitude);
    }

    private async Task<List<ShopData>> LoadAsync(int? onlyBusinessId, CancellationToken ct)
    {
        var today = WibTime.TodayWib(_time);
        var businesses = await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole == MarketRole.Retail && b.VerificationStatus == VerificationStatus.Verified && b.IsActive
                        && (onlyBusinessId == null || b.Id == onlyBusinessId))
            .ToListAsync(ct);
        if (businesses.Count == 0)
        {
            return new List<ShopData>();
        }

        var ids = businesses.Select(b => b.Id).ToList();
        var products = await _db.Products.IgnoreQueryFilters().AsNoTracking()
            .Include(p => p.Category)
            .Where(p => p.IsActive && ids.Contains(p.BusinessId))
            .ToListAsync(ct);
        var inventories = (await _db.Inventories.IgnoreQueryFilters().AsNoTracking()
                .Where(i => ids.Contains(i.BusinessId) && i.SellingPriceBase > 0 && i.QtyBase > i.ReservedBase)
                .ToListAsync(ct))
            .ToDictionary(i => i.ProductId);
        var batches = (await _db.StockBatches.IgnoreQueryFilters().AsNoTracking()
                .Where(b => ids.Contains(b.BusinessId) && b.QtyRemainingBase > 0)
                .ToListAsync(ct))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var promos = (await _db.ConsumerPromos.IgnoreQueryFilters().AsNoTracking()
                .Where(p => ids.Contains(p.BusinessId) && p.IsActive)
                .ToListAsync(ct))
            .ToDictionary(p => p.ProductId);
        var delivery = await _delivery.GetPublicAsync(ids, ct);

        var result = new List<ShopData>();
        foreach (var business in businesses)
        {
            var list = new List<ShopProductDto>();
            foreach (var p in products.Where(x => x.BusinessId == business.Id).OrderBy(x => x.Name))
            {
                if (!inventories.TryGetValue(p.Id, out var inv))
                {
                    continue;
                }

                var productBatches = batches.TryGetValue(p.Id, out var pb) ? pb : new List<Stock.Domain.StockBatch>();

                // Produk kedaluwarsa tidak boleh ditawarkan (NFR-13).
                var expiredFree = productBatches.Where(b => b.ExpiryDate < today).Sum(b => b.QtyRemainingBase - b.ReservedBase);
                var sellable = Math.Max(0, inv.QtyBase - inv.ReservedBase - expiredFree);
                if (sellable <= 0)
                {
                    continue;
                }

                var live = productBatches
                    .Where(b => b.ExpiryDate >= today && b.QtyRemainingBase - b.ReservedBase > 0)
                    .OrderBy(b => b.ExpiryDate)
                    .ToList();
                DateOnly? nearest = live.Count > 0 ? live[0].ExpiryDate : null;

                var promoPercent = 0;
                var promoQty = 0;
                var promoPrice = inv.SellingPriceBase;
                if (promos.TryGetValue(p.Id, out var promo))
                {
                    var limit = today.AddDays(promo.WithinDays);
                    promoQty = Math.Min(sellable, live.Where(b => b.ExpiryDate <= limit).Sum(b => b.QtyRemainingBase - b.ReservedBase));
                    if (promoQty > 0)
                    {
                        promoPercent = promo.DiscountPercent;
                        promoPrice = ConsumerPricing.PromoPrice(inv.SellingPriceBase, promo.DiscountPercent);
                    }
                }

                list.Add(new ShopProductDto(
                    p.Id, p.Name, p.Sku, p.Category.Name, p.BaseUnitName, inv.SellingPriceBase, sellable,
                    nearest, nearest is DateOnly d ? d.DayNumber - today.DayNumber : null,
                    promoPercent, promoQty, promoPrice, p.ImagePath));
            }

            if (list.Count > 0)
            {
                result.Add(new ShopData(business, delivery[business.Id], list));
            }
        }

        return result;
    }
}

/// <summary>Aturan harga promo (murni, mudah diuji).</summary>
public static class ConsumerPricing
{
    public static decimal PromoPrice(decimal regularUnitPrice, int discountPercent) =>
        Money.Round(regularUnitPrice * (100 - discountPercent) / 100m);
}

/// <summary>Pengelolaan promo stok mendekati kedaluwarsa oleh warung.</summary>
public sealed class ConsumerPromoService : IConsumerPromoService
{
    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly Audit.Contracts.IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly IUnitOfWork _uow;

    public ConsumerPromoService(
        AppDbContext db, IBusinessContext context, Audit.Contracts.IAuditLogService audit, TimeProvider time, IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _audit = audit;
        _time = time;
        _uow = uow;
    }

    private bool IsRetail => _context.IsAuthenticated && _context.Role == RoleNames.Retail && _context.BusinessId is not null;

    public async Task<IReadOnlyList<PromoSettingDto>> ListMineAsync(CancellationToken ct = default)
    {
        if (!IsRetail)
        {
            return Array.Empty<PromoSettingDto>();
        }

        var today = WibTime.TodayWib(_time);
        var products = await _db.Products.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);
        var inventories = (await _db.Inventories.AsNoTracking().ToListAsync(ct)).ToDictionary(i => i.ProductId);
        var batches = (await _db.StockBatches.AsNoTracking().Where(b => b.QtyRemainingBase > 0).ToListAsync(ct))
            .GroupBy(b => b.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var promos = (await _db.ConsumerPromos.AsNoTracking().ToListAsync(ct)).ToDictionary(p => p.ProductId);

        var result = new List<PromoSettingDto>();
        foreach (var p in products)
        {
            if (!inventories.TryGetValue(p.Id, out var inv) || inv.QtyBase <= 0)
            {
                continue;
            }

            promos.TryGetValue(p.Id, out var promo);
            var within = promo?.WithinDays ?? 7;
            var limit = today.AddDays(within);
            var near = batches.TryGetValue(p.Id, out var list)
                ? list.Where(b => b.ExpiryDate >= today && b.ExpiryDate <= limit).Sum(b => b.QtyRemainingBase - b.ReservedBase)
                : 0;
            result.Add(new PromoSettingDto(
                p.Id, p.Name, p.BaseUnitName, inv.SellingPriceBase, Math.Max(0, inv.QtyBase - inv.ReservedBase),
                promo?.DiscountPercent, promo?.WithinDays, promo?.IsActive ?? false, Math.Max(0, near), p.ImagePath));
        }

        return result;
    }

    public async Task<Result> SetAsync(int productId, int? discountPercent, int? withinDays, CancellationToken ct = default)
    {
        if (!IsRetail)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun warung yang dapat mengatur promo pembeli.");
        }

        if (discountPercent is not null && (discountPercent is < 5 or > 90))
        {
            return Result.Fail(ErrorKind.Validation, "Diskon promo harus 5–90%.");
        }

        if (discountPercent is not null && (withinDays is null or < 1 or > 90))
        {
            return Result.Fail(ErrorKind.Validation, "Batas hari menuju kedaluwarsa harus 1–90 hari.");
        }

        return await _uow.ExecuteAsync(async token =>
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId, token);
            if (product is null)
            {
                return Result.Fail(ErrorKind.NotFound, "Produk tidak ditemukan.");
            }

            var existing = await _db.ConsumerPromos.FirstOrDefaultAsync(p => p.ProductId == productId, token);
            var now = _time.GetUtcNow().UtcDateTime;
            if (discountPercent is null)
            {
                if (existing is not null)
                {
                    _db.ConsumerPromos.Remove(existing);
                    _audit.Record("CONSUMER_PROMO_REMOVED", "Product", productId.ToString(), product.Name);
                }
            }
            else if (existing is null)
            {
                _db.ConsumerPromos.Add(new ConsumerPromo
                {
                    BusinessId = _context.BusinessId!.Value,
                    ProductId = productId,
                    DiscountPercent = discountPercent.Value,
                    WithinDays = withinDays!.Value,
                    IsActive = true,
                    CreatedAt = now,
                    UpdatedAt = now
                });
                _audit.Record("CONSUMER_PROMO_SET", "Product", productId.ToString(), $"{product.Name}; diskon={discountPercent}%; <= {withinDays} hari");
            }
            else
            {
                existing.DiscountPercent = discountPercent.Value;
                existing.WithinDays = withinDays!.Value;
                existing.IsActive = true;
                existing.UpdatedAt = now;
                _audit.Record("CONSUMER_PROMO_SET", "Product", productId.ToString(), $"{product.Name}; diskon={discountPercent}%; <= {withinDays} hari");
            }

            await _db.SaveChangesAsync(token);
            return Result.Ok();
        }, ct);
    }
}
