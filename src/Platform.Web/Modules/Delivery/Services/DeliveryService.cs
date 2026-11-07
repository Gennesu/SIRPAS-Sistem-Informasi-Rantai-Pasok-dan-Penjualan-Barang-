using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Delivery.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Delivery.Services;

public sealed class DeliveryService : IDeliveryService
{
    private const decimal MaxMoney = 1_000_000_000m;
    private static readonly DeliveryOptionDto Default = new(false, true, 0m, 0m, null, null);

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IAuditLogService _audit;

    public DeliveryService(AppDbContext db, IBusinessContext context, IAuditLogService audit)
    {
        _db = db;
        _context = context;
        _audit = audit;
    }

    public async Task<DeliveryOptionDto> GetMineAsync(CancellationToken ct = default)
    {
        var option = await _db.DeliveryOptions.AsNoTracking().FirstOrDefaultAsync(ct);
        return option is null ? Default : ToDto(option);
    }

    public async Task<Result> SaveMineAsync(SaveDeliveryRequest request, CancellationToken ct = default)
    {
        var businessId = _context.BusinessId;
        if (!_context.IsAuthenticated || businessId is null
            || _context.Role is not (RoleNames.Grossier or RoleNames.Retail or RoleNames.Livestock))
        {
            return Result.Fail(ErrorKind.Forbidden, "Akun ini tidak dapat mengatur pengiriman.");
        }

        if (!request.OffersDelivery && !request.OffersPickup)
        {
            return Result.Fail(ErrorKind.Validation, "Aktifkan minimal satu: pengiriman atau ambil sendiri.");
        }

        if (request.DeliveryFee < 0 || request.DeliveryFee > MaxMoney)
        {
            return Result.Fail(ErrorKind.Validation, "Biaya pengiriman tidak valid.");
        }

        if (request.MinOrderAmount < 0 || request.MinOrderAmount > MaxMoney)
        {
            return Result.Fail(ErrorKind.Validation, "Minimum pesanan tidak valid.");
        }

        if (request.FeePerKm < 0 || request.FeePerKm > 1_000_000m)
        {
            return Result.Fail(ErrorKind.Validation, "Tarif per km tidak valid.");
        }

        if (request.FreeKm < 0 || request.FreeKm > 1_000m)
        {
            return Result.Fail(ErrorKind.Validation, "Jarak gratis tarif dasar tidak valid.");
        }

        if (request.MaxDistanceKm is decimal maxKm && (maxKm <= 0 || maxKm > 5_000m))
        {
            return Result.Fail(ErrorKind.Validation, "Jangkauan maksimal harus lebih dari 0 km.");
        }

        var deliveryInfo = Clean(request.DeliveryInfo);
        var pickupInfo = Clean(request.PickupInfo);
        if (deliveryInfo is { Length: > 200 } || pickupInfo is { Length: > 200 })
        {
            return Result.Fail(ErrorKind.Validation, "Keterangan maksimal 200 karakter.");
        }

        var option = await _db.DeliveryOptions.FirstOrDefaultAsync(ct);
        if (option is null)
        {
            option = new DeliveryOption { BusinessId = businessId.Value };
            _db.DeliveryOptions.Add(option);
        }

        option.OffersDelivery = request.OffersDelivery;
        option.OffersPickup = request.OffersPickup;
        option.DeliveryFee = Money.Round(request.DeliveryFee);
        option.MinOrderAmount = Money.Round(request.MinOrderAmount);
        option.FeePerKm = Money.Round(request.FeePerKm);
        option.FreeKm = Math.Round(request.FreeKm, 2);
        option.MaxDistanceKm = request.MaxDistanceKm is decimal km ? Math.Round(km, 2) : null;
        option.DeliveryInfo = deliveryInfo;
        option.PickupInfo = pickupInfo;
        _audit.Record("DELIVERY_OPTIONS_SAVED", "DeliveryOption", null, $"delivery={option.OffersDelivery}; pickup={option.OffersPickup}; fee={option.DeliveryFee}; perKm={option.FeePerKm}; freeKm={option.FreeKm}; max={option.MaxDistanceKm}");
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyDictionary<int, DeliveryOptionDto>> GetPublicAsync(
        IReadOnlyCollection<int> businessIds, CancellationToken ct = default)
    {
        var ids = businessIds.ToList();
        var rows = await _db.DeliveryOptions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(o => ids.Contains(o.BusinessId))
            .ToListAsync(ct);

        var byBusiness = rows.ToDictionary(o => o.BusinessId, ToDto);
        return ids.ToDictionary(id => id, id => byBusiness.TryGetValue(id, out var dto) ? dto : Default);
    }

    public async Task<IReadOnlyDictionary<int, DeliveryQuote>> QuoteForBuyerAsync(
        IReadOnlyCollection<int> sellerIds, CancellationToken ct = default)
    {
        var buyerId = _context.BusinessId;
        var result = new Dictionary<int, DeliveryQuote>();
        if (buyerId is null || sellerIds.Count == 0)
        {
            return result;
        }

        var ids = sellerIds.Distinct().ToList();
        var options = await GetPublicAsync(ids, ct);
        var buyer = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == buyerId, ct);
        var sellers = await _db.Businesses.AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        foreach (var seller in sellers)
        {
            result[seller.Id] = DeliveryFeeCalculator.Quote(options[seller.Id], StraightKm(buyer, seller));
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<int, DeliveryQuote>> QuoteForSellerAsync(
        IReadOnlyCollection<int> buyerIds, CancellationToken ct = default)
    {
        var sellerId = _context.BusinessId;
        var result = new Dictionary<int, DeliveryQuote>();
        if (sellerId is null || buyerIds.Count == 0)
        {
            return result;
        }

        var ids = buyerIds.Distinct().ToList();
        var option = (await GetPublicAsync(new[] { sellerId.Value }, ct))[sellerId.Value];
        var seller = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == sellerId, ct);
        var buyers = await _db.Businesses.AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync(ct);
        foreach (var buyer in buyers)
        {
            result[buyer.Id] = DeliveryFeeCalculator.Quote(option, StraightKm(buyer, seller));
        }

        return result;
    }

    private static double? StraightKm(BusinessProfile.Domain.Business? a, BusinessProfile.Domain.Business? b) =>
        a?.Latitude is double la && a.Longitude is double lo && b?.Latitude is double bla && b.Longitude is double blo
            ? Math.Round(Geo.DistanceKm(la, lo, bla, blo), 2)
            : null;

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DeliveryOptionDto ToDto(DeliveryOption o) =>
        new(o.OffersDelivery, o.OffersPickup, o.DeliveryFee, o.MinOrderAmount, o.DeliveryInfo, o.PickupInfo, o.FeePerKm, o.FreeKm, o.MaxDistanceKm);
}
