using System.Text.RegularExpressions;

namespace Platform.Tests.Quality;

/// <summary>
/// Mode lokal tanpa internet (NFR-09): kode sumber aplikasi tidak boleh bergantung pada CDN, font, ikon, tile peta, routing,
/// atau geocoding eksternal untuk alur inti. Pemindaian statis atas seluruh view, JS, dan CSS milik aplikasi.
/// Tile OpenStreetMap hanya tampilan (aktif bawaan lewat Map:OnlineTiles); tanpa internet kisi lokal menggantikannya dan transaksi tidak terpengaruh.
/// </summary>
public sealed class OfflineAssetsTests
{
    internal static string? FindWebRootForTests() => FindWebRoot();

    private static string? FindWebRoot()
    {
        var env = Environment.GetEnvironmentVariable("PLATFORM_WEB_ROOT");
        if (env is not null && Directory.Exists(env))
        {
            return env;
        }

        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Platform.Web");
            if (Directory.Exists(Path.Combine(candidate, "wwwroot")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> OwnFiles(string root) =>
        Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cshtml") || f.EndsWith(".js") || f.EndsWith(".css"))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}"));

    [Fact]
    public void Tidak_ada_CDN_font_atau_stylesheet_eksternal_dan_tidak_ada_pemanggilan_routing_atau_geocoding_eksternal()
    {
        var root = FindWebRoot();
        Assert.NotNull(root);

        // w3.org: namespace XML SVG. wa.me: tautan WhatsApp yang diklik pengguna (bukan API, tidak dimuat otomatis).
        // tile.openstreetmap.org + www.openstreetmap.org: tile peta OpenStreetMap (Map:OnlineTiles) dan tautan atribusi lisensinya.
        var allowedHosts = new[] { "www.w3.org", "wa.me", "tile.openstreetmap.org", "www.openstreetmap.org" };
        var offenders = new List<string>();
        foreach (var file in OwnFiles(root!))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"https?://([A-Za-z0-9\.\-]+)"))
            {
                var host = m.Groups[1].Value;
                if (!allowedHosts.Contains(host))
                {
                    offenders.Add($"{Path.GetRelativePath(root!, file)} -> {host}");
                }
            }

            Assert.DoesNotContain("router.project-osrm.org", text);
            Assert.DoesNotContain("nominatim.openstreetmap.org", text);
            Assert.DoesNotContain("fonts.googleapis.com", text);
            Assert.DoesNotContain("cdn.jsdelivr.net", text);
            Assert.DoesNotContain("unpkg.com", text);
            Assert.DoesNotContain("cdnjs.cloudflare.com", text);
        }

        Assert.True(offenders.Count == 0, "Host eksternal tidak diizinkan: " + string.Join("; ", offenders.Distinct()));
    }

    [Fact]
    public void Tile_peta_online_hanya_ada_di_map_core_dan_dijaga_flag_konfigurasi()
    {
        var root = FindWebRoot();
        Assert.NotNull(root);

        var withTile = OwnFiles(root!).Where(f => File.ReadAllText(f).Contains("tile.openstreetmap.org")).Select(Path.GetFileName).ToList();
        Assert.Equal(new[] { "map-core.js" }, withTile.ToArray());

        var core = File.ReadAllText(Path.Combine(root!, "wwwroot", "js", "map-core.js"));
        Assert.Contains("if (!ONLINE) { return new LocalGrid", core);          // Map:OnlineTiles = false -> kisi lokal
        Assert.Contains("new LocalGrid({ maxZoom: 19, zIndex: 0 })", core);     // kisi lokal selalu jadi lapisan dasar (cadangan bila tile gagal)
        Assert.DoesNotContain("fetch(", core);                                  // tidak ada panggilan jaringan di inti peta (rute dihitung lokal)
        var settings = File.ReadAllText(Path.Combine(root!, "appsettings.json"));
        Assert.Contains("\"OnlineTiles\": true", settings);                     // bawaan konfigurasi: tile OpenStreetMap aktif
    }

    [Fact]
    public void Setiap_script_dan_stylesheet_pada_view_dimuat_dari_proyek_sendiri()
    {
        var root = FindWebRoot();
        Assert.NotNull(root);
        foreach (var file in OwnFiles(root!).Where(f => f.EndsWith(".cshtml")))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"<(?:script|link|img)[^>]+(?:src|href)=""([^""]+)"""))
            {
                var url = m.Groups[1].Value;
                Assert.False(url.StartsWith("http://") || url.StartsWith("https://") || url.StartsWith("//"),
                    $"{Path.GetRelativePath(root!, file)} memuat sumber eksternal: {url}");
            }
        }
    }
}
