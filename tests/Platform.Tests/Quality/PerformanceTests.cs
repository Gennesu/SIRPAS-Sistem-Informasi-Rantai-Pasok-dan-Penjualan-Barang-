using System.Diagnostics;
using Platform.Tests.TestSupport;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;

namespace Platform.Tests.Quality;

/// <summary>
/// Performance efficiency (ISO 25010): waktu respons layanan katalog pada KONDISI UJI TERDEFINISI dan dapat diulang:
///   - satu warung dengan 300 produk beraktif stok dan harga jual;
///   - satu grosir dengan 300 produk dan 10 grosir lain dengan 30 produk;
///   - SQLite file di folder sementara, satu proses, tanpa jaringan.
/// Setiap operasi diukur 7 kali; nilai yang dinilai adalah MEDIAN dan nilai maksimum. Target NFR: median dan maksimum <= 3.000 ms.
/// Catatan: ini mengukur lapisan layanan + database (tanpa render Razor/jaringan); pengukuran HTTP ada di skrip uji HTTP.
/// </summary>
public sealed class PerformanceTests : IDisposable
{
    private const int TargetMs = 3_000;
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    private static async Task<(double Median, double Max)> MeasureAsync(string name, Func<Task> action, List<string> report)
    {
        await action(); // pemanasan (kompilasi query/JIT) tidak dihitung
        var samples = new List<double>();
        for (var i = 0; i < 7; i++)
        {
            var sw = Stopwatch.StartNew();
            await action();
            samples.Add(sw.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var result = (samples[samples.Count / 2], samples[^1]);
        report.Add($"{name}: median {result.Item1:F0} ms, maks {result.Item2:F0} ms (7 pengukuran)");
        return result;
    }

    private async Task<int> SeedProductsAsync(V3World.Actor owner, int count, string prefix, bool retailPrices)
    {
        var s = _w.Open(owner);
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var p = await s.Products.CreateAsync(new SaveProductRequest(
                $"{prefix} {i:D3}", $"{prefix[..3].ToUpperInvariant()}-{i:D3}", 1, null, "Pcs", 1_000m + i,
                new[] { new UnitInput("Dus", 10, 10_000m + i * 10) }));
            Assert.True(p.Succeeded, p.Error);
            ids.Add(p.Value!.Id);
        }

        for (var offset = 0; offset < ids.Count; offset += 30)
        {
            var lines = ids.Skip(offset).Take(30).Select(id => new PurchaseLineInput(id, "Dus", 10, 8_000m, null)).ToArray();
            var r = await s.Purchasing.RecordAsync(new RecordPurchaseRequest("Distributor", null, null, lines));
            Assert.True(r.Succeeded, r.Error);
        }

        if (retailPrices)
        {
            foreach (var id in ids)
            {
                Assert.True((await s.Inventory.UpdateSettingsAsync(id, 1_500m, 5)).Succeeded);
            }
        }

        return ids.Count;
    }

    [Fact]
    public async Task Halaman_katalog_merespons_maksimal_3_detik_pada_300_produk_per_usaha()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Besar");
        await _w.SetDeliveryAsync(shop);
        await SeedProductsAsync(shop, 300, "Barang Retail", retailPrices: true);

        var big = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Besar", -6.915, 107.605);
        await _w.SetDeliveryAsync(big);
        await SeedProductsAsync(big, 300, "Barang Grosir", retailPrices: false);
        for (var i = 0; i < 10; i++)
        {
            var other = await _w.RegisterBusinessAsync(MarketRole.Wholesale, $"Grosir Kecil {i}", -6.91 - i * 0.003, 107.60 + i * 0.003);
            await _w.SetDeliveryAsync(other);
            await SeedProductsAsync(other, 30, $"Barang G{i}", retailPrices: false);
        }

        var consumer = await _w.RegisterConsumerAsync();
        var report = new List<string> { "Kondisi uji: 1 warung x 300 produk; 1 grosir x 300 produk + 10 grosir x 30 produk; SQLite file; satu proses." };
        var all = new List<(double Median, double Max)>
        {
            await MeasureAsync("Katalog produk warung (Products.ListAsync, 300 produk)", () => _w.Open(shop).Products.ListAsync(null, false), report),
            await MeasureAsync("Halaman stok warung (Inventory.GetStockAsync, 300 produk)", () => _w.Open(shop).Inventory.GetStockAsync(), report),
            await MeasureAsync("Marketplace: cari grosir (11 grosir, 600 produk)", () => _w.Open(shop).Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, null)), report),
            await MeasureAsync("Marketplace: katalog satu grosir 300 produk", () => _w.Open(shop).Marketplace.GetWholesalerAsync(big.BusinessId!.Value), report),
            await MeasureAsync("Katalog pembeli: cari warung", () => _w.Open(consumer).ConsumerCatalog.SearchShopsAsync(null), report),
            await MeasureAsync("Katalog pembeli: toko 300 produk", () => _w.Open(consumer).ConsumerCatalog.GetShopAsync(shop.BusinessId!.Value), report)
        };

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "platform-perf-results.txt"), string.Join(Environment.NewLine, report) + Environment.NewLine);
        foreach (var line in report)
        {
            Console.WriteLine("[PERF] " + line);
        }

        Assert.All(all, m =>
        {
            Assert.True(m.Median <= TargetMs, $"median {m.Median:F0} ms melebihi {TargetMs} ms");
            Assert.True(m.Max <= TargetMs, $"maks {m.Max:F0} ms melebihi {TargetMs} ms");
        });
    }
}
