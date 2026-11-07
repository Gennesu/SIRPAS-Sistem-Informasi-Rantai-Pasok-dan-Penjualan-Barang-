using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;

namespace Platform.Tests.SharedKernel;

public sealed class TenantFilterTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly FakeBusinessContext _context = new();

    public TenantFilterTests()
    {
        _connection.Open();

        using var db = CreateDb();
        db.Database.EnsureCreated();
        db.Probes.AddRange(
            new TenantProbe { BusinessId = 1, Name = "A-1" },
            new TenantProbe { BusinessId = 1, Name = "A-2" },
            new TenantProbe { BusinessId = 2, Name = "B-1" });
        db.SaveChanges();
    }

    public void Dispose() => _connection.Dispose();

    private TenantProbeDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TenantProbeDbContext>().UseSqlite(_connection).Options;
        return new TenantProbeDbContext(options, _context);
    }

    [Fact]
    public void Warung_hanya_melihat_data_usahanya_sendiri()
    {
        _context.AsRetail(businessId: 1);
        using var db = CreateDb();

        var names = db.Probes.OrderBy(p => p.Name).Select(p => p.Name).ToList();

        Assert.Equal(new[] { "A-1", "A-2" }, names);
    }

    [Fact]
    public void Admin_melihat_data_semua_usaha()
    {
        _context.AsAdmin();
        using var db = CreateDb();

        Assert.Equal(3, db.Probes.Count());
    }

    [Fact]
    public void Pengguna_anonim_tidak_melihat_data_apa_pun()
    {
        _context.AsAnonymous();
        using var db = CreateDb();

        Assert.Empty(db.Probes.ToList());
    }
}
