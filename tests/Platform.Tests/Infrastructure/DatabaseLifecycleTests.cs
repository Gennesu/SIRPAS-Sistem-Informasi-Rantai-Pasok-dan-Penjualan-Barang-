using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Tests.TestSupport;
using Platform.Web.Data;
using Platform.Web.Modules.Catalog.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Infrastructure;

/// <summary>Kelompok F: database (restart, seeder idempoten, reset eksplisit, penjaga skema, database baru).</summary>
public sealed class DatabaseLifecycleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "platform-v3-db-" + Guid.NewGuid().ToString("N"));

    public DatabaseLifecycleTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { /* abaikan */ }
    }

    private string DbFile => Path.Combine(_dir, "app.db");

    private ServiceProvider Services(bool seedDemo, bool reset)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBusinessContext>(FixedBusinessContext.Anonymous());
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={DbFile}"));
        services.AddSingleton<IOptions<DemoOptions>>(Options.Create(new DemoOptions { SeedDummyData = seedDemo, ResetDatabaseOnStart = reset }));
        return services.BuildServiceProvider();
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection().Build();

    private async Task InitAsync(bool seedDemo, bool reset)
    {
        using var sp = Services(seedDemo, reset);
        await DbSeeder.InitializeAsync(sp, Config());
        SqliteConnection.ClearAllPools();
    }

    private async Task<int> CountAsync(string table)
    {
        await using var c = new SqliteConnection($"Data Source={DbFile};Mode=ReadOnly");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task F5_Database_baru_dapat_dibuat_diberi_penanda_skema_dan_roles_enam_aktor_tersedia()
    {
        await InitAsync(seedDemo: false, reset: false);

        Assert.Equal(5, await CountAsync("Roles"));
        using var sp = Services(false, false);
        using var db = sp.GetRequiredService<AppDbContext>();
        Assert.Equal(DbSeeder.SchemaVersion, await DbSeeder.ReadSchemaVersionAsync(db));
        var names = await db.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync();
        Assert.Equal(new[] { "ADMIN", "CONSUMER", "GROSSIER", "LIVESTOCK", "RETAIL" }, names);
    }

    [Fact]
    public async Task F2_F3_Restart_mempertahankan_data_dan_seeder_berulang_tidak_membuat_data_ganda()
    {
        await InitAsync(seedDemo: true, reset: false); // pertama: V2 + V3 dummy

        var users = await CountAsync("Users");
        var businesses = await CountAsync("Businesses");
        var products = await CountAsync("Products");
        var orders = await CountAsync("Orders");
        var consumers = await CountAsync("ConsumerProfiles");
        Assert.True(users >= 12, $"akun demo V2+V3 (ditemukan {users})");
        Assert.Equal(2, consumers);

        // Transaksi baru yang dibuat pengguna di antara dua restart.
        using (var sp = Services(false, false))
        using (var db = sp.GetRequiredService<AppDbContext>())
        {
            db.Categories.Add(new Category { Name = "Kategori Buatan Pengguna" });
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();
        for (var i = 0; i < 2; i++)
        {
            await InitAsync(seedDemo: true, reset: false); // restart normal berulang kali
        }

        Assert.Equal(users, await CountAsync("Users"));
        Assert.Equal(businesses, await CountAsync("Businesses"));
        Assert.Equal(products, await CountAsync("Products"));
        Assert.Equal(orders, await CountAsync("Orders"));
        Assert.Equal(consumers, await CountAsync("ConsumerProfiles"));
        await using var c = new SqliteConnection($"Data Source={DbFile};Mode=ReadOnly");
        await c.OpenAsync();
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Categories WHERE Name='Kategori Buatan Pengguna'";
        Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!); // data pengguna tetap ada setelah restart
    }

    [Fact]
    public async Task F4_Reset_hanya_berjalan_melalui_pengaturan_eksplisit_dan_membuat_cadangan_lebih_dulu()
    {
        await InitAsync(seedDemo: false, reset: false);
        using (var sp = Services(false, false))
        using (var db = sp.GetRequiredService<AppDbContext>())
        {
            db.Categories.Add(new Category { Name = "Data Penting" });
            await db.SaveChangesAsync();
        }

        SqliteConnection.ClearAllPools();
        await InitAsync(seedDemo: false, reset: false); // tanpa reset: data tetap
        await using (var c1 = new SqliteConnection($"Data Source={DbFile};Mode=ReadOnly"))
        {
            await c1.OpenAsync();
            await using var cmd = c1.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM Categories WHERE Name='Data Penting'";
            Assert.Equal(1L, (long)(await cmd.ExecuteScalarAsync())!);
        }

        Assert.Empty(Directory.GetFiles(_dir, "*.bak-*"));

        await InitAsync(seedDemo: false, reset: true); // reset eksplisit
        await using var c2 = new SqliteConnection($"Data Source={DbFile};Mode=ReadOnly");
        await c2.OpenAsync();
        await using var cmd2 = c2.CreateCommand();
        cmd2.CommandText = "SELECT COUNT(*) FROM Categories WHERE Name='Data Penting'";
        Assert.Equal(0L, (long)(await cmd2.ExecuteScalarAsync())!);
        Assert.Single(Directory.GetFiles(_dir, "*.bak-*")); // cadangan sebelum menghapus
    }

    [Fact]
    public async Task F7_Database_dari_skema_lain_misalnya_V2_ditolak_dengan_pesan_jelas_dan_tidak_diubah()
    {
        // Meniru file V2: tabel ada tetapi tidak ada penanda skema V3.
        await InitAsync(seedDemo: false, reset: false);
        await using (var c = new SqliteConnection($"Data Source={DbFile}"))
        {
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = $"DROP TABLE \"{DbSeeder.SchemaTable}\"";
            await cmd.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var rolesBefore = await CountAsync("Roles");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => InitAsync(false, false));
        Assert.Contains("versi aplikasi yang lebih lama", ex.Message);
        Assert.Contains("platform-demo-v3.db", ex.Message);
        Assert.Equal(rolesBefore, await CountAsync("Roles"));

        // Versi skema yang berbeda juga ditolak.
        SqliteConnection.ClearAllPools();
        await using (var c = new SqliteConnection($"Data Source={DbFile}"))
        {
            await c.OpenAsync();
            await using var cmd = c.CreateCommand();
            cmd.CommandText = $"CREATE TABLE \"{DbSeeder.SchemaTable}\" (\"Key\" VARCHAR(40) NOT NULL PRIMARY KEY, \"Value\" VARCHAR(200) NOT NULL); INSERT INTO \"{DbSeeder.SchemaTable}\" VALUES ('SchemaVersion','2')";
            await cmd.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var ex2 = await Assert.ThrowsAsync<InvalidOperationException>(() => InitAsync(false, false));
        Assert.Contains("versi 2", ex2.Message);
    }
}
