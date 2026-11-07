using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;

namespace Platform.Tests.TestSupport;

/// <summary>
/// SQLite in-memory dengan satu koneksi bersama. Skema dibuat dari model EF
/// (termasuk seed Role dan BusinessType). Setiap CreateDb() = DbContext baru pada database yang sama.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public TestDb()
    {
        _connection.Open();
        Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public FakeBusinessContext Context { get; } = new();

    /// <summary>Opsi DbContext yang dipakai bersama (dipakai juga oleh ModuleServices).</summary>
    public DbContextOptions<AppDbContext> Options { get; }

    public AppDbContext CreateDb() => new(Options, Context);

    public void Dispose() => _connection.Dispose();
}
