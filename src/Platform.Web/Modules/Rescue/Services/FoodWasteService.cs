using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Sales.Domain;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Rescue.Services;

/// <inheritdoc />
public sealed class FoodWasteService : IFoodWasteService
{
    private const int AtRiskDays = 7;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly TimeProvider _time;

    public FoodWasteService(AppDbContext db, IBusinessContext context, TimeProvider time)
    {
        _db = db;
        _context = context;
        _time = time;
    }

    public async Task<FoodWasteSummaryDto> GetSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        // Hari WIB -> rentang UTC.
        var fromUtc = from.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var today = WibTime.TodayWib(_time);
        var businessId = _context.BusinessId;
        var isBusiness = _context.IsAuthenticated && (_context.IsAdmin || businessId is not null);
        if (!isBusiness)
        {
            return new FoodWasteSummaryDto(from, to, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Array.Empty<FoodWasteProductDto>());
        }

        // Semua query otomatis dibatasi filter tenant (usaha sendiri) atau seluruh data (admin).
        var movements = await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.CreatedAt >= fromUtc && m.CreatedAt < toUtc && (m.Type == MovementType.ExpiredOut || m.Type == MovementType.OtherOut))
            .ToListAsync(ct);
        var names = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var expired = movements.Where(m => m.Type == MovementType.ExpiredOut).ToList();
        var other = movements.Where(m => m.Type == MovementType.OtherOut).ToList();

        // Terjual: POS + pesanan pembeli akhir yang selesai (lewat Sales) + pesanan usaha yang dikirim (sebagai penjual).
        var saleItems = await (from i in _db.SaleItems.AsNoTracking()
                               join s in _db.Sales.AsNoTracking() on i.SaleId equals s.Id
                               where s.SoldAt >= fromUtc && s.SoldAt < toUtc && s.Status == SaleStatus.Confirmed
                               select new { i.QtyBase, i.Subtotal })
            .ToListAsync(ct);
        var shippedOrders = (await _db.Orders.AsNoTracking()
                .Where(o => o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                            && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PickedUp || o.Status == OrderStatus.Completed)
                            && (_context.IsAdmin || o.SellerBusinessId == businessId))
                .Select(o => o.Id)
                .ToListAsync(ct)).ToHashSet();
        var orderItems = await _db.OrderItems.AsNoTracking().Where(i => shippedOrders.Contains(i.OrderId)).ToListAsync(ct);

        // Tersalurkan: unit dari paket anti-mubazir (grosir) dan promo pembeli (warung) yang benar-benar terjual.
        var bundleItems = orderItems.Where(i => i.BundleId != null).ToList();
        var promoItems = await (from i in _db.ConsumerOrderItems.AsNoTracking()
                                join o in _db.ConsumerOrders.AsNoTracking() on i.ConsumerOrderId equals o.Id
                                where i.IsPromo && o.CreatedAt >= fromUtc && o.CreatedAt < toUtc
                                      && o.Status != OrderStatus.Rejected && o.Status != OrderStatus.Cancelled
                                select new { i.QtyBase, i.Subtotal, i.RegularUnitPrice })
            .ToListAsync(ct);

        var limit = today.AddDays(AtRiskDays);
        var atRisk = await _db.StockBatches.AsNoTracking().Where(b => b.QtyRemainingBase > 0 && b.ExpiryDate <= limit).ToListAsync(ct);

        var wastedQty = expired.Sum(m => -m.QtyBaseSigned);
        var wastedValue = Money.Round(expired.Sum(m => -m.QtyBaseSigned * m.UnitCost));
        var soldQty = saleItems.Sum(i => i.QtyBase) + orderItems.Sum(i => i.QtyBase);
        var soldValue = saleItems.Sum(i => i.Subtotal) + orderItems.Sum(i => i.Subtotal);
        var rescuedQty = bundleItems.Sum(i => i.QtyBase) + promoItems.Sum(i => i.QtyBase);
        var rescuedValue = bundleItems.Sum(i => i.Subtotal) + promoItems.Sum(i => i.Subtotal);
        var savings = bundleItems.Sum(i => (i.NormalSubtotal ?? i.Subtotal) - i.Subtotal)
                      + promoItems.Sum(i => Money.Round(i.RegularUnitPrice * i.QtyBase) - i.Subtotal);

        var top = expired
            .GroupBy(m => m.ProductId)
            .Select(g => new FoodWasteProductDto(
                names.TryGetValue(g.Key, out var n) ? n : "(produk)", g.Sum(m => -m.QtyBaseSigned), Money.Round(g.Sum(m => -m.QtyBaseSigned * m.UnitCost))))
            .OrderByDescending(x => x.CostValue)
            .Take(5)
            .ToList();

        var denominator = soldQty + wastedQty;
        return new FoodWasteSummaryDto(
            from, to, soldQty, soldValue, rescuedQty, rescuedValue, savings, wastedQty, wastedValue,
            other.Sum(m => -m.QtyBaseSigned), Money.Round(other.Sum(m => -m.QtyBaseSigned * m.UnitCost)),
            atRisk.Sum(b => b.QtyRemainingBase), Money.Round(atRisk.Sum(b => b.QtyRemainingBase * b.UnitCost)),
            denominator == 0 ? 0m : Math.Round(wastedQty * 100m / denominator, 1), top);
    }
}
