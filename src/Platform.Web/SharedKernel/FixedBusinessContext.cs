namespace Platform.Web.SharedKernel;

/// <summary>
/// IBusinessContext tetap untuk proses di luar HTTP (seeder data demo, test).
/// Berperan sebagai pengguna tertentu tanpa perlu login.
/// </summary>
public sealed class FixedBusinessContext : IBusinessContext
{
    private FixedBusinessContext(bool authenticated, string? role, int? businessId, int? userId)
    {
        IsAuthenticated = authenticated;
        Role = role;
        BusinessId = businessId;
        UserId = userId;
    }

    public bool IsAuthenticated { get; }
    public int? UserId { get; }
    public int? BusinessId { get; }
    public string? Role { get; }
    public bool IsAdmin => Role == RoleNames.Admin;

    public static FixedBusinessContext Anonymous() => new(false, null, null, null);
    public static FixedBusinessContext ForRetail(int businessId, int userId) => new(true, RoleNames.Retail, businessId, userId);
    public static FixedBusinessContext ForGrossier(int businessId, int userId) => new(true, RoleNames.Grossier, businessId, userId);
    public static FixedBusinessContext ForAdmin(int userId) => new(true, RoleNames.Admin, null, userId);
    public static FixedBusinessContext ForLivestock(int businessId, int userId) => new(true, RoleNames.Livestock, businessId, userId);
    public static FixedBusinessContext ForConsumer(int userId) => new(true, RoleNames.Consumer, null, userId);
}

/// <summary>TimeProvider yang waktunya bisa diatur; dipakai seeder untuk membuat riwayat transaksi hari-hari lalu.</summary>
public sealed class SettableTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public SettableTimeProvider(DateTimeOffset now)
    {
        _now = now;
    }

    public void Set(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;
}
