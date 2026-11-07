using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Tests.Quality;
using Platform.Web.Data;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Catalog.Services;

namespace Platform.Tests.Modules.Catalog;

/// <summary>Gambar produk diunggah pemilik usaha: validasi format/ukuran/isi, lokasi simpan, penghapusan aman, dan gambar demo bawaan.</summary>
public sealed class ProductImageStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "imgstore-" + Guid.NewGuid().ToString("N"));
    private readonly ProductImageStore _store;

    public ProductImageStoreTests()
    {
        Directory.CreateDirectory(_root);
        _store = new ProductImageStore(new FakeEnv(_root), NullLogger<ProductImageStore>.Instance);
    }

    public void Dispose() => Directory.Delete(_root, true);

    private sealed class FakeEnv : IWebHostEnvironment
    {
        public FakeEnv(string root)
        {
            ContentRootPath = root;
            WebRootPath = Path.Combine(root, "wwwroot");
            Directory.CreateDirectory(WebRootPath);
        }

        public string WebRootPath { get; set; }
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "uji";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Development";
    }

    private static byte[] Padded(byte[] head)
    {
        var bytes = new byte[64];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    private static readonly byte[] Png = Padded(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    private static readonly byte[] Jpg = Padded(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });
    private static readonly byte[] Gif = Padded(Encoding.ASCII.GetBytes("GIF89a"));

    private static byte[] Webp()
    {
        var bytes = new byte[64];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        return bytes;
    }

    private static IFormFile File(byte[] data, string name) => new FormFile(new MemoryStream(data), 0, data.Length, "Image", name);

    private string DiskPath(string url) =>
        Path.Combine(_root, "wwwroot", url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void Alamat_gambar_hanya_boleh_di_folder_unggahan_atau_gambar_demo()
    {
        Assert.True(ProductImagePaths.IsValid(null));
        Assert.True(ProductImagePaths.IsValid(""));
        Assert.True(ProductImagePaths.IsValid("/uploads/products/12/abc123.jpg"));
        Assert.True(ProductImagePaths.IsValid("/img/products/mie-goreng.svg"));

        foreach (var bad in new[]
                 {
                     "/uploads/products/../secret.txt", "http://evil.example/a.png", "/other/a.png", "/uploads/products/a b.png",
                     "/uploads/products/a\\b.png", "javascript:alert(1)", "/img/products/a.png?x=1"
                 })
        {
            Assert.False(ProductImagePaths.IsValid(bad), bad);
        }
    }

    [Fact]
    public async Task Gambar_valid_tersimpan_di_folder_usaha_dengan_nama_acak_dan_dapat_dihapus()
    {
        foreach (var (data, name) in new[] { (Png, "foto.png"), (Jpg, "FOTO.JPG"), (Gif, "anim.gif"), (Webp(), "web.webp") })
        {
            var result = await _store.SaveAsync(File(data, name), 7);

            Assert.True(result.Succeeded, name + ": " + result.Error);
            Assert.StartsWith("/uploads/products/7/", result.Value);
            var stem = Path.GetFileNameWithoutExtension(result.Value!);
            Assert.True(stem.Length == 32 && stem.All(Uri.IsHexDigit), "nama berkas di disk harus acak, bukan nama asli: " + result.Value);
            Assert.True(ProductImagePaths.IsValid(result.Value), result.Value);
            Assert.True(System.IO.File.Exists(DiskPath(result.Value!)));

            _store.Delete(result.Value);
            Assert.False(System.IO.File.Exists(DiskPath(result.Value!)));
        }
    }

    [Fact]
    public async Task Unggahan_ditolak_bila_isi_tidak_cocok_ekstensi_asing_terlalu_besar_atau_kosong()
    {
        Assert.False((await _store.SaveAsync(File(Png, "disamarkan.jpg"), 7)).Succeeded);    // isi PNG, ekstensi JPG
        Assert.False((await _store.SaveAsync(File(Png, "virus.exe"), 7)).Succeeded);         // ekstensi tidak diizinkan
        Assert.False((await _store.SaveAsync(File(new byte[ProductImageStore.MaxBytes + 1], "besar.png"), 7)).Succeeded);
        Assert.False((await _store.SaveAsync(File(Array.Empty<byte>(), "kosong.png"), 7)).Succeeded);
        Assert.False((await _store.SaveAsync(File(new byte[] { 0x89, 0x50 }, "pendek.png"), 7)).Succeeded);
    }

    [Fact]
    public void Delete_tidak_menyentuh_berkas_di_luar_folder_unggahan_maupun_gambar_demo()
    {
        var outside = Path.Combine(_root, "wwwroot", "rahasia.txt");
        System.IO.File.WriteAllText(outside, "jangan dihapus");

        _store.Delete("/uploads/products/../../rahasia.txt");
        _store.Delete("/img/products/mie-goreng.svg");
        _store.Delete(null);
        _store.Delete("");

        Assert.True(System.IO.File.Exists(outside));
    }

    [Fact]
    public void Gambar_demo_dicocokkan_lewat_nama_produk_dan_berkasnya_ada_di_wwwroot()
    {
        var webRoot = OfflineAssetsTests.FindWebRootForTests();
        Assert.NotNull(webRoot);

        var expected = new Dictionary<string, string>
        {
            ["Mi Instan Goreng"] = "mie-goreng.svg", ["Mi Instan Kuah Soto"] = "mie-soto.svg", ["Mi Instan Kuah Ayam Bawang"] = "mie-ayam.svg",
            ["Teh Botol 350ml"] = "teh-botol.svg", ["Teh Lemon 350ml"] = "teh-lemon.svg", ["Kopi Sachet"] = "kopi-sachet.svg",
            ["Kopi Susu Sachet"] = "kopi-susu.svg", ["Minyak Goreng 1L"] = "minyak-goreng.svg", ["Gula Pasir 1kg"] = "gula.svg",
            ["Beras Premium 5kg"] = "beras.svg", ["Sabun Cuci Piring 800ml"] = "sabun-cuci.svg", ["Susu UHT Cokelat 200ml"] = "susu-uht.svg",
            ["Yogurt Drink 150ml"] = "yogurt.svg", ["Biskuit Kelapa 300g"] = "biskuit.svg", ["Keripik Singkong Pedas"] = "keripik.svg",
            ["Wafer Cokelat 150g"] = "wafer.svg"
        };

        foreach (var (name, file) in expected)
        {
            var url = DemoDataSeeder.DemoImageFor(name);
            Assert.Equal("/img/products/" + file, url);
            Assert.True(ProductImagePaths.IsValid(url));
            Assert.True(System.IO.File.Exists(Path.Combine(webRoot!, "wwwroot", "img", "products", file)), file);
        }

        Assert.Null(DemoDataSeeder.DemoImageFor("Produk tanpa gambar bawaan"));
        Assert.Null(DemoDataSeeder.DemoImageFor(null));
    }
}
