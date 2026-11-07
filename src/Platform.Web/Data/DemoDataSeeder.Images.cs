using Microsoft.EntityFrameworkCore;
using Platform.Web.SharedKernel;

namespace Platform.Web.Data;

/// <summary>
/// Gambar demo produk (wwwroot/img/products/*.svg). Hanya untuk produk milik AKUN DEMO (@demo.test) yang belum punya gambar, dan hanya SEKALI
/// (ditandai di tabel penanda skema): seolah-olah masing-masing pemilik sudah mengunggah gambarnya. Gambar yang diunggah pemilik usaha sungguhan
/// tidak pernah disentuh, dan gambar yang sengaja dihapus pemilik demo tidak dipasang ulang saat restart.
/// </summary>
public static partial class DemoDataSeeder
{
    private const string DemoImagesMarker = "DemoImagesV1";

    // Urutan penting: kata kunci yang lebih spesifik diperiksa lebih dulu.
    private static readonly (string Keyword, string File)[] ProductImageRules =
    {
        ("telur", "telur-ayam.svg"),
        ("ayam potong", "ayam-potong.svg"),
        ("teh hitam", "teh-pucuk.svg"),
        ("teh hijau", "teh-pucuk.svg"),
        ("biji kopi", "biji-kopi.svg"),
        ("kuah soto", "mie-soto.svg"),
        ("ayam bawang", "mie-ayam.svg"),
        ("mi instan", "mie-goreng.svg"),
        ("teh lemon", "teh-lemon.svg"),
        ("teh botol", "teh-botol.svg"),
        ("kopi susu", "kopi-susu.svg"),
        ("kopi", "kopi-sachet.svg"),
        ("minyak", "minyak-goreng.svg"),
        ("gula", "gula.svg"),
        ("beras", "beras.svg"),
        ("sabun", "sabun-cuci.svg"),
        ("susu", "susu-uht.svg"),
        ("yogurt", "yogurt.svg"),
        ("biskuit", "biskuit.svg"),
        ("keripik", "keripik.svg"),
        ("wafer", "wafer.svg")
    };

    /// <summary>Alamat gambar demo untuk nama produk tertentu, atau null bila tidak ada yang cocok.</summary>
    public static string? DemoImageFor(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName))
        {
            return null;
        }

        foreach (var (keyword, file) in ProductImageRules)
        {
            if (productName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return "/img/products/" + file;
            }
        }

        return null;
    }

    public static async Task SeedProductImagesAsync(IServiceProvider sp, ILogger logger)
    {
        var options = sp.GetRequiredService<DbContextOptions<AppDbContext>>();
        using var root = new ModuleServices(options, FixedBusinessContext.Anonymous(), TimeProvider.System);

        var schema = DbSeeder.QuoteIdentifier(root.Db, DbSeeder.SchemaTable);
        var key = DbSeeder.QuoteIdentifier(root.Db, "Key");
        var value = DbSeeder.QuoteIdentifier(root.Db, "Value");
        var markerQuery = string.Concat("SELECT ", value, " AS ", value, " FROM ", schema, " WHERE ", key, " = @p0");
        var done = await root.Db.Database
            .SqlQueryRaw<string>(markerQuery, DemoImagesMarker)
            .ToListAsync();
        if (done.Count > 0)
        {
            return;
        }

        var demoBusinessIds = await root.Db.Users
            .Where(u => u.Email.EndsWith("@demo.test") && u.BusinessId != null)
            .Select(u => u.BusinessId!.Value)
            .Distinct()
            .ToListAsync();

        var products = await root.Db.Products
            .IgnoreQueryFilters()
            .Where(p => p.ImagePath == null && demoBusinessIds.Contains(p.BusinessId))
            .ToListAsync();

        var filled = 0;
        foreach (var product in products)
        {
            var path = DemoImageFor(product.Name);
            if (path is not null)
            {
                product.ImagePath = path;
                filled++;
            }
        }

        if (filled > 0)
        {
            await root.Db.SaveChangesAsync();
        }

        var insertMarkerSql = string.Concat(
            "INSERT INTO ", schema, " (", key, ", ", value, ") SELECT @p0, @p1 ",
            "WHERE NOT EXISTS (SELECT 1 FROM ", schema, " WHERE ", key, " = @p0)");
        await root.Db.Database.ExecuteSqlRawAsync(insertMarkerSql, DemoImagesMarker, "done");
        logger.LogInformation("Gambar demo dipasang pada {Count} produk akun demo.", filled);
    }
}
