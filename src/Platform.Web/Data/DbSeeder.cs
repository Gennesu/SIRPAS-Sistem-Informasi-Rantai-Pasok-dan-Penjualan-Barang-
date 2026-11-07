using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Data;

/// <summary>
/// Menyiapkan database saat aplikasi start. Aturan V3 (berbeda dari V2):
///  1. Database V3 memakai skema tersendiri sehingga database V2 tetap aman untuk rollback.
///  2. Restart biasa TIDAK menghapus apa pun. Reset hanya bila Demo:ResetDatabaseOnStart = true (bawaan false); peringatan dicatat
///     dan file SQLite dicadangkan dulu bila provider yang dipakai SQLite.
///  3. Penjaga skema: database yang dibuat oleh skema lain (mis. V2) ditolak dengan pesan jelas, bukan diam-diam dipakai.
///     EnsureCreated TIDAK memperbarui skema database yang sudah ada, jadi ketidakcocokan harus dideteksi sendiri.
///  4. Seeder idempoten: dijalankan berkali-kali tidak menambah akun/produk ganda dan tidak menyentuh data transaksi.
/// </summary>
public static class DbSeeder
{
    /// <summary>Versi skema database yang dipahami aplikasi ini. Naikkan bila skema berubah tidak kompatibel.</summary>
    public const int SchemaVersion = 3;

    public const string SchemaTable = "__PlatformSchema";

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DbSeeder));
        var db = sp.GetRequiredService<AppDbContext>();
        var demo = sp.GetRequiredService<IOptions<DemoOptions>>().Value;

        if (demo.ResetDatabaseOnStart)
        {
            logger.LogWarning(
                "PERINGATAN: Demo:ResetDatabaseOnStart = true. SELURUH DATA di database ini akan DIHAPUS lalu dibuat ulang. " +
                "Matikan pengaturan ini (false) setelah selesai agar transaksi Anda tidak hilang.");
            BackupSqliteFile(db, logger);
            await db.Database.EnsureDeletedAsync();
        }

        var creator = db.GetService<IRelationalDatabaseCreator>();
        var existedWithTables = await creator.ExistsAsync() && await creator.HasTablesAsync();
        if (existedWithTables)
        {
            await EnsureCompatibleAsync(db, logger); // tolak database dari skema lain SEBELUM ada yang diubah
        }

        var mode = config["Database:Mode"] ?? "EnsureCreated";
        if (mode.Equals("Migrate", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.MigrateAsync();
        }
        else
        {
            await db.Database.EnsureCreatedAsync();
        }

        await RemoveUnusedRolesAsync(db, logger);
        await EnsureAdditiveColumnsAsync(db, logger);
        await StampSchemaAsync(db, logger);
        await SeedAdminFromConfigAsync(sp, config, db, logger);

        if (demo.SeedDummyData)
        {
            await DemoDataSeeder.SeedAsync(sp, logger);
            await DemoDataSeeder.SeedV3Async(sp, logger);
            await DemoDataSeeder.SeedProductImagesAsync(sp, logger);
        }
    }

    /// <summary>Membersihkan role lama yang tidak lagi didukung bila tidak ada akun yang menggunakannya.</summary>
    private static async Task RemoveUnusedRolesAsync(AppDbContext db, ILogger logger)
    {
        var supportedRoles = new[]
        {
            RoleNames.Admin, RoleNames.Grossier, RoleNames.Retail,
            RoleNames.Consumer, RoleNames.Livestock
        };
        var unused = await db.Roles
            .Where(role => !supportedRoles.Contains(role.Name) && !db.Users.Any(user => user.RoleId == role.Id))
            .ToListAsync();
        if (unused.Count == 0)
        {
            return;
        }

        db.Roles.RemoveRange(unused);
        await db.SaveChangesAsync();
        logger.LogInformation("Removed {Count} unsupported role records with no users.", unused.Count);
    }

    /// <summary>
    /// Menambahkan kolom baru yang opsional (boleh kosong) ke database V3 yang SUDAH ada. EnsureCreated tidak memperbarui skema,
    /// jadi tanpa langkah ini database lama gagal membaca kolom baru (Products.ImagePath = gambar produk). Aman dijalankan berulang:
    /// hanya menambah kolom yang belum ada, tidak mengubah atau menghapus data.
    /// </summary>
    internal static async Task EnsureAdditiveColumnsAsync(AppDbContext db, ILogger logger)
    {
        try
        {
            if (db.Database.IsSqlite())
            {
                var columns = await db.Database
                    .SqlQueryRaw<string>("SELECT name AS \"Value\" FROM pragma_table_info('Products')")
                    .ToListAsync();
                if (columns.Count > 0 && !columns.Contains("ImagePath", StringComparer.OrdinalIgnoreCase))
                {
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Products\" ADD COLUMN \"ImagePath\" TEXT NULL");
                    logger.LogInformation("Kolom Products.ImagePath (gambar produk) ditambahkan ke database yang sudah ada.");
                }
            }
            else if (IsMySql(db))
            {
                var columns = await db.Database
                    .SqlQueryRaw<string>("SELECT COLUMN_NAME AS `Value` FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND LOWER(TABLE_NAME) = 'products'")
                    .ToListAsync();
                if (columns.Count > 0 && !columns.Contains("ImagePath", StringComparer.OrdinalIgnoreCase))
                {
                    await db.Database.ExecuteSqlRawAsync("ALTER TABLE `Products` ADD COLUMN `ImagePath` VARCHAR(260) NULL");
                    logger.LogInformation("Kolom Products.ImagePath (gambar produk) ditambahkan ke database yang sudah ada.");
                }
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Products\" ADD COLUMN IF NOT EXISTS \"ImagePath\" character varying(260) NULL");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Pemeriksaan kolom tambahan database dilewati.");
        }
    }

    /// <summary>Menolak database yang tidak bertanda skema V3 (mis. file V2) dengan pesan yang dapat ditindaklanjuti.</summary>
    internal static async Task EnsureCompatibleAsync(AppDbContext db, ILogger logger)
    {
        var version = await ReadSchemaVersionAsync(db);
        if (version == SchemaVersion)
        {
            return;
        }

        var message = version is null
            ? "Database ini dibuat oleh versi aplikasi yang lebih lama (tidak ada penanda skema V3) dan tidak dapat dipakai langsung. " +
              "Gunakan file database baru (bawaan: platform-demo-v3.db) lewat ConnectionStrings:Default. File lama tidak diubah sehingga aman untuk rollback."
            : $"Skema database versi {version} tidak cocok dengan aplikasi ini (membutuhkan versi {SchemaVersion}). " +
              "Gunakan file database baru atau pulihkan aplikasi yang sesuai.";
        logger.LogCritical("{Message}", message);
        throw new InvalidOperationException(message);
    }

    public static async Task<int?> ReadSchemaVersionAsync(AppDbContext db)
    {
        try
        {
            var schema = QuoteIdentifier(db, SchemaTable);
            var key = QuoteIdentifier(db, "Key");
            var value = QuoteIdentifier(db, "Value");
            var sql = string.Concat("SELECT ", value, " AS ", value, " FROM ", schema, " WHERE ", key, " = @p0");
            var rows = await db.Database
                .SqlQueryRaw<string>(sql, "SchemaVersion")
                .ToListAsync();
            return rows.Count == 1 && int.TryParse(rows[0], out var v) ? v : null;
        }
        catch (Exception)
        {
            return null; // tabel penanda belum ada
        }
    }

    private static async Task StampSchemaAsync(AppDbContext db, ILogger logger)
    {
        var schema = QuoteIdentifier(db, SchemaTable);
        var key = QuoteIdentifier(db, "Key");
        var value = QuoteIdentifier(db, "Value");
        var createTableSql = string.Concat(
            "CREATE TABLE IF NOT EXISTS ", schema, " (", key, " VARCHAR(40) NOT NULL PRIMARY KEY, ", value, " VARCHAR(200) NOT NULL)");
        await db.Database.ExecuteSqlRawAsync(createTableSql);

        var insertMarkerSql = string.Concat(
            "INSERT INTO ", schema, " (", key, ", ", value, ") SELECT @p0, @p1 ",
            "WHERE NOT EXISTS (SELECT 1 FROM ", schema, " WHERE ", key, " = @p0)");
        await db.Database.ExecuteSqlRawAsync(insertMarkerSql, "SchemaVersion", SchemaVersion.ToString(CultureInfo.InvariantCulture));
        await db.Database.ExecuteSqlRawAsync(insertMarkerSql, "CreatedAtUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));

        // Pastikan penanda sesuai (mis. database baru saja dibuat).
        var version = await ReadSchemaVersionAsync(db);
        if (version != SchemaVersion)
        {
            throw new InvalidOperationException($"Penanda skema database tidak valid (ditemukan: {version?.ToString() ?? "kosong"}).");
        }

        logger.LogInformation("Skema database V{Version} siap.", SchemaVersion);
    }

    internal static bool IsMySql(AppDbContext db) =>
        db.Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true;

    internal static string QuoteIdentifier(AppDbContext db, string identifier) => IsMySql(db)
        ? $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`"
        : $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static void BackupSqliteFile(AppDbContext db, ILogger logger)
    {
        try
        {
            if (!db.Database.IsSqlite())
            {
                return;
            }

            var source = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
            if (!File.Exists(source))
            {
                return;
            }

            var backup = $"{source}.bak-{DateTime.UtcNow:yyyyMMddHHmmss}";
            SqliteConnection.ClearAllPools();
            File.Copy(source, backup, overwrite: false);
            logger.LogWarning("Cadangan database sebelum reset: {Backup}", backup);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cadangan database sebelum reset gagal; reset tetap dilanjutkan sesuai konfigurasi.");
        }
    }

    private static async Task SeedAdminFromConfigAsync(IServiceProvider sp, IConfiguration config, AppDbContext db, ILogger logger)
    {
        var email = config["Seed:AdminEmail"]?.Trim().ToLowerInvariant();
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (await db.Users.AnyAsync(u => u.Email == email))
        {
            return;
        }

        var result = await sp.GetRequiredService<IAuthService>().CreateUserAsync(
            new CreateUserRequest("Administrator", email, password, RoleNames.Admin, null));

        if (result.Succeeded)
        {
            logger.LogInformation("Akun admin dari konfigurasi dibuat: {Email}", email);
        }
        else
        {
            logger.LogWarning("Seed admin dari konfigurasi gagal: {Error}", result.Error);
        }
    }
}
