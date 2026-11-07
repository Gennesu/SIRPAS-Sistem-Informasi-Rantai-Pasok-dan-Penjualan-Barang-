using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Audit.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Audit.Services;

public sealed class AuditLogService : IAuditLogService
{
    private const int MaxDetailLength = 1000;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly TimeProvider _time;

    public AuditLogService(AppDbContext db, IBusinessContext context, TimeProvider time)
    {
        _db = db;
        _context = context;
        _time = time;
    }

    public void Record(
        string action,
        string? entityType = null,
        string? entityId = null,
        string? detail = null,
        int? userId = null,
        int? businessId = null)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Detail = detail is { Length: > MaxDetailLength } ? detail[..MaxDetailLength] : detail,
            UserId = userId ?? _context.UserId,
            BusinessId = businessId ?? _context.BusinessId,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        });
    }
}
