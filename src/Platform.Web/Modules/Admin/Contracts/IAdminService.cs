using Platform.Web.Modules.BusinessProfile.Domain;

namespace Platform.Web.Modules.Admin.Contracts;

public sealed record AdminDashboardDto(
    int Users,
    int Retailers,
    int Wholesalers,
    int PendingVerification,
    int InactiveBusinesses,
    int OrdersTotal,
    int OrdersOpen,
    decimal OrdersCompletedValue,
    decimal PosRevenueTotal,
    int AuditEvents,
    int Consumers = 0,
    int ConsumerOrders = 0,
    int StockIntegrityViolations = 0);

public sealed record AdminUserDto(
    int Id,
    string FullName,
    string Email,
    string Role,
    string? BusinessName,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);

public sealed record AdminBusinessDto(
    int Id,
    string Name,
    string TypeName,
    string MarketRole,
    string Address,
    string Phone,
    VerificationStatus Verification,
    bool IsActive,
    DateTime CreatedAt,
    string? OwnerEmail);

public sealed record AuditLogDto(
    long Id,
    DateTime At,
    string Action,
    string? EntityType,
    string? EntityId,
    string? Detail,
    int? UserId,
    int? BusinessId);

/// <summary>
/// Query baca untuk halaman admin (pengguna, usaha, log, ringkasan). Perubahan data (verifikasi, aktif/nonaktif,
/// kategori) dilakukan lewat layanan milik modul masing-masing agar aturan dan audit tetap satu pintu.
/// </summary>
public interface IAdminService
{
    Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdminUserDto>> ListUsersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AdminBusinessDto>> ListBusinessesAsync(VerificationStatus? status, CancellationToken ct = default);
    Task<IReadOnlyList<AuditLogDto>> ListAuditAsync(string? actionPrefix, int take = 200, CancellationToken ct = default);
}
