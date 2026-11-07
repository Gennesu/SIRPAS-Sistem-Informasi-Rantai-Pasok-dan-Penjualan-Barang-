using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Notifications.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Rescue.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Notifications.Services;

public sealed class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly TimeProvider _time;

    public NotificationService(AppDbContext db, IBusinessContext context, TimeProvider time)
    {
        _db = db;
        _context = context;
        _time = time;
    }

    public async Task<NotificationSummary> GetAsync(CancellationToken ct = default)
    {
        var items = new List<NotificationItem>();
        if (!_context.IsAuthenticated)
        {
            return new NotificationSummary(0, items);
        }

        if (_context.IsAdmin)
        {
            var pending = await _db.Businesses.AsNoTracking().CountAsync(b => b.VerificationStatus == VerificationStatus.Pending, ct);
            if (pending > 0)
            {
                items.Add(new NotificationItem(
                    "verify", NotificationTone.Warn, "shield", $"{pending} usaha menunggu verifikasi",
                    "Setujui atau tolak agar usaha bisa tampil di marketplace.", "/Admin/Businesses?status=Pending", pending));
            }

            return Build(items);
        }

        if (_context.BusinessId is not int me)
        {
            return Build(items);
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var today = WibTime.TodayWib(_time);

        if (_context.Role == RoleNames.Grossier)
        {
            var pendingOrders = await _db.Orders.AsNoTracking().CountAsync(o => o.SellerBusinessId == me && o.Status == OrderStatus.Pending, ct);
            if (pendingOrders > 0)
            {
                items.Add(new NotificationItem(
                    "orders-pending", NotificationTone.Warn, "inbox", $"{pendingOrders} pesanan menunggu Anda",
                    "Terima atau tolak pesanan dari warung.", "/Orders?status=Pending", pendingOrders));
            }

            var farmDeliveries = await _db.Orders.AsNoTracking().CountAsync(
                o => o.BuyerBusinessId == me
                     && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PickedUp)
                     && _db.Businesses.Any(b => b.Id == o.SellerBusinessId && b.MarketRole == MarketRole.Livestock), ct);
            if (farmDeliveries > 0)
            {
                items.Add(new NotificationItem(
                    "agent-sales-confirm", NotificationTone.Warn, "package-check", $"{farmDeliveries} pasokan peternakan menunggu konfirmasi",
                    "Konfirmasi penerimaan agar stok agen bertambah.", "/Orders", farmDeliveries));
            }

            var processing = await _db.Orders.AsNoTracking().CountAsync(
                o => o.SellerBusinessId == me
                     && (o.Status == OrderStatus.Accepted || o.Status == OrderStatus.Preparing || o.Status == OrderStatus.Ready), ct);
            if (processing > 0)
            {
                items.Add(new NotificationItem(
                    "orders-processing", NotificationTone.Info, "box", $"{processing} pesanan perlu diproses",
                    "Siapkan, kemas, dan kirim barang sesuai fase.", "/Orders", processing));
            }
        }
        else
        {
            var offers = await _db.RescueOffers.AsNoTracking()
                .CountAsync(o => o.BuyerBusinessId == me && o.Status == OfferStatus.Sent && o.ExpiresAt > now, ct);
            if (offers > 0)
            {
                items.Add(new NotificationItem(
                    "offers", NotificationTone.Success, "tag", $"{offers} penawaran baru untuk Anda",
                    "Grosir menawarkan barang dengan harga khusus.", "/Deals/Offers", offers));
            }

            var confirm = await _db.Orders.AsNoTracking().CountAsync(
                o => o.BuyerBusinessId == me && (o.Status == OrderStatus.Delivered || o.Status == OrderStatus.PickedUp), ct);
            if (confirm > 0)
            {
                items.Add(new NotificationItem(
                    "orders-confirm", NotificationTone.Warn, "truck", $"{confirm} pesanan menunggu konfirmasi",
                    "Konfirmasi setelah barang Anda terima agar stok bertambah.", "/Orders", confirm));
            }

            var low = await _db.Inventories.AsNoTracking()
                .CountAsync(i => i.Product.IsActive && i.QtyBase <= i.MinStockBase && i.MinStockBase > 0, ct);
            if (low > 0)
            {
                items.Add(new NotificationItem(
                    "stock-low", NotificationTone.Warn, "alert", $"{low} barang stok menipis",
                    "Pesan ke grosir sebelum barang habis.", "/Stock", low));
            }
        }

        // Kadaluarsa: berlaku untuk grosir dan warung (batch milik sendiri lewat filter tenant).
        var batches = await _db.StockBatches.AsNoTracking()
            .Where(b => b.QtyRemainingBase > 0 && b.Product.IsActive)
            .Select(b => b.ExpiryDate)
            .ToListAsync(ct);
        var expired = batches.Count(d => d < today);
        var critical = batches.Count(d => d >= today && ExpiryDays(d, today) <= 7);
        if (expired > 0)
        {
            items.Add(new NotificationItem(
                "expired", NotificationTone.Danger, "alert", $"{expired} batch sudah kadaluarsa",
                "Keluarkan dari stok agar tidak terjual.", "/Rescue#kadaluarsa", expired));
        }

        if (critical > 0)
        {
            items.Add(new NotificationItem(
                "expiring", NotificationTone.Danger, "clock", $"{critical} batch kadaluarsa dalam 7 hari",
                _context.Role == RoleNames.Grossier ? "Buat paket atau kirim penawaran sebelum terbuang." : "Jual lebih dulu atau beri diskon di kasir.",
                "/Rescue", critical));
        }

        return Build(items);
    }

    private static int ExpiryDays(DateOnly expiry, DateOnly today) => expiry.DayNumber - today.DayNumber;

    private static NotificationSummary Build(List<NotificationItem> items) =>
        new(items.Sum(i => i.Count), items);
}
