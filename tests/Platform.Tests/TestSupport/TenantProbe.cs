using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.SharedKernel;

namespace Platform.Tests.TestSupport;

/// <summary>Entity uji milik tenant (IBusinessOwned) untuk memverifikasi global query filter.</summary>
public class TenantProbe : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>AppDbContext + satu DbSet uji. Filter tenant diterapkan otomatis oleh AppDbContext.</summary>
public sealed class TenantProbeDbContext : AppDbContext
{
    public TenantProbeDbContext(DbContextOptions<TenantProbeDbContext> options, IBusinessContext context)
        : base(options, context)
    {
    }

    public DbSet<TenantProbe> Probes => Set<TenantProbe>();
}
