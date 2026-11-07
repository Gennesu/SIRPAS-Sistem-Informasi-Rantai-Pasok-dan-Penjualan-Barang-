namespace Platform.Web.Modules.Notifications.Contracts;

public enum NotificationTone
{
    Info,
    Warn,
    Danger,
    Success
}

/// <summary>Satu butir pemberitahuan. Dihitung dari data terkini, jadi tidak perlu tabel tambahan.</summary>
public sealed record NotificationItem(string Key, NotificationTone Tone, string Icon, string Title, string Detail, string Url, int Count);

public sealed record NotificationSummary(int Total, IReadOnlyList<NotificationItem> Items);

/// <summary>
/// Pusat notifikasi hemat biaya: setiap kali dibuka (atau diperiksa berkala oleh browser) angka-angkanya dihitung ulang
/// dari pesanan, penawaran, stok, dan kadaluarsa. Tanpa WebSocket, tanpa layanan push berbayar.
/// </summary>
public interface INotificationService
{
    Task<NotificationSummary> GetAsync(CancellationToken ct = default);
}
