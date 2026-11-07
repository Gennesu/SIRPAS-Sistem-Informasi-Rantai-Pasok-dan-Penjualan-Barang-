using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Auth.Contracts;

namespace Platform.Web.Modules.BusinessProfile.Services;

/// <summary>Menolak login/sesi pengguna dari usaha yang dinonaktifkan admin.</summary>
public sealed class BusinessLoginGate : ILoginGate
{
    private readonly AppDbContext _db;

    public BusinessLoginGate(AppDbContext db)
    {
        _db = db;
    }

    public async Task<string?> CheckAsync(AuthenticatedUser user, CancellationToken ct = default)
    {
        if (user.BusinessId is not int businessId)
        {
            return null;
        }

        var active = await _db.Businesses.AsNoTracking().AnyAsync(b => b.Id == businessId && b.IsActive, ct);
        return active ? null : "Usaha Anda dinonaktifkan oleh admin platform. Silakan hubungi admin.";
    }
}
