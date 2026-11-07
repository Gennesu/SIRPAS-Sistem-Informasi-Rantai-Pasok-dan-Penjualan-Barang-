using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Admin.Contracts;
using Platform.Web.Modules.Audit.Domain;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Sales.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Admin.Services;

public sealed class AdminService : IAdminService
{
    private static readonly string[] SupportedRoles =
        { RoleNames.Admin, RoleNames.Grossier, RoleNames.Retail, RoleNames.Consumer, RoleNames.Livestock };

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly Stock.Contracts.IStockIntegrityService _integrity;

    // Kompatibilitas dengan pemanggil V2 (mis. tes) yang membuat AdminService hanya dengan DbContext dan konteks.
    public AdminService(AppDbContext db, IBusinessContext context)
        : this(db, context, new Stock.Services.StockIntegrityService(db))
    {
    }

    public AdminService(AppDbContext db, IBusinessContext context, Stock.Contracts.IStockIntegrityService integrity)
    {
        _db = db;
        _context = context;
        _integrity = integrity;
    }

    private bool IsAdmin => _context.IsAuthenticated && _context.IsAdmin;

    public async Task<AdminDashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        if (!IsAdmin)
        {
            return new AdminDashboardDto(0, 0, 0, 0, 0, 0, 0, 0m, 0m, 0);
        }

        var businesses = await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole != MarketRole.Retired)
            .ToListAsync(ct);
        var orders = await _db.Orders.AsNoTracking().ToListAsync(ct);
        var sales = await _db.Sales.AsNoTracking().Where(s => s.Status == SaleStatus.Confirmed).ToListAsync(ct);

        var closed = new[] { OrderStatus.Completed, OrderStatus.Rejected, OrderStatus.Cancelled };

        return new AdminDashboardDto(
            await _db.Users.CountAsync(u => SupportedRoles.Contains(u.Role.Name), ct),
            businesses.Count(b => b.MarketRole == MarketRole.Retail),
            businesses.Count(b => b.MarketRole == MarketRole.Wholesale),
            businesses.Count(b => b.VerificationStatus == VerificationStatus.Pending),
            businesses.Count(b => !b.IsActive),
            orders.Count,
            orders.Count(o => !closed.Contains(o.Status)),
            orders.Where(o => o.Status == OrderStatus.Completed).Sum(o => o.TotalAmount),
            sales.Sum(s => s.TotalAmount),
            await _db.AuditLogs.CountAsync(ct),
            await _db.Users.CountAsync(u => u.Role.Name == RoleNames.Consumer, ct),
            await _db.ConsumerOrders.CountAsync(ct),
            (await _integrity.CheckAsync(null, ct)).Count);
    }

    public async Task<IReadOnlyList<AdminUserDto>> ListUsersAsync(CancellationToken ct = default)
    {
        if (!IsAdmin)
        {
            return Array.Empty<AdminUserDto>();
        }

        var users = await _db.Users.AsNoTracking()
            .Where(u => SupportedRoles.Contains(u.Role.Name))
            .Include(u => u.Role)
            .ToListAsync(ct);
        var names = (await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole != MarketRole.Retired)
            .ToListAsync(ct)).ToDictionary(b => b.Id, b => b.Name);

        return users
            .OrderBy(u => u.Role.Name)
            .ThenBy(u => u.Email)
            .Select(u => new AdminUserDto(
                u.Id,
                u.FullName,
                u.Email,
                u.Role.Name,
                u.BusinessId is int id && names.TryGetValue(id, out var name) ? name : null,
                u.IsActive,
                u.CreatedAt,
                u.LastLoginAt))
            .ToList();
    }

    public async Task<IReadOnlyList<AdminBusinessDto>> ListBusinessesAsync(VerificationStatus? status, CancellationToken ct = default)
    {
        if (!IsAdmin)
        {
            return Array.Empty<AdminBusinessDto>();
        }

        var businesses = await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole != MarketRole.Retired)
            .Include(b => b.BusinessType)
            .ToListAsync(ct);
        var owners = (await _db.Users.AsNoTracking().Where(u => u.BusinessId != null).ToListAsync(ct))
            .GroupBy(u => u.BusinessId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(u => u.Id).First().Email);

        return businesses
            .Where(b => status is null || b.VerificationStatus == status)
            .OrderBy(b => b.VerificationStatus == VerificationStatus.Pending ? 0 : 1)
            .ThenBy(b => b.Name)
            .Select(b => new AdminBusinessDto(
                b.Id,
                b.Name,
                b.BusinessType.Name,
                b.MarketRole.ToString(),
                b.Address,
                b.Phone,
                b.VerificationStatus,
                b.IsActive,
                b.CreatedAt,
                owners.TryGetValue(b.Id, out var email) ? email : null))
            .ToList();
    }

    public async Task<IReadOnlyList<AuditLogDto>> ListAuditAsync(string? actionPrefix, int take = 200, CancellationToken ct = default)
    {
        if (!IsAdmin)
        {
            return Array.Empty<AuditLogDto>();
        }

        IQueryable<AuditLog> query = _db.AuditLogs.AsNoTracking();
        var prefix = actionPrefix?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(prefix))
        {
            query = query.Where(a => a.Action.StartsWith(prefix));
        }

        var rows = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(take)
            .ToListAsync(ct);

        return rows
            .Select(a => new AuditLogDto(a.Id, a.CreatedAt, a.Action, a.EntityType, a.EntityId, a.Detail, a.UserId, a.BusinessId))
            .ToList();
    }
}
