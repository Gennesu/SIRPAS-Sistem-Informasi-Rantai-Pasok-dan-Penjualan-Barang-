using Platform.Tests.TestSupport;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Modules.Catalog;

/// <summary>Katalog grosir dan warung serta kepemilikan gambar produk.</summary>
public sealed class ProductOwnershipAndImageTests : IDisposable
{
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    private static SaveProductRequest Req(string name, string? sku, int categoryId, string unit = "Item", decimal? price = 2_500m, string? image = null) =>
        new(name, sku, categoryId, null, unit, price, Array.Empty<UnitInput>(), null, 1, image);

    [Fact]
    public async Task Grosir_dan_warung_tetap_boleh_menjual_beragam_kategori()
    {
        foreach (var role in new[] { MarketRole.Wholesale, MarketRole.Retail })
        {
            var owner = await _w.RegisterBusinessAsync(role, "Usaha " + role);
            var s = _w.Open(owner);
            Assert.True((await s.Products.CreateAsync(Req("Mi Goreng", "A-1", 1, "Pcs", role == MarketRole.Retail ? null : 2_500m))).Succeeded);
            var other = await s.Products.CreateAsync(Req("Teh Botol", "A-2", 3, "Botol", role == MarketRole.Retail ? null : 3_900m));
            Assert.True(other.Succeeded, role + ": " + other.Error);
        }
    }

    [Fact]
    public async Task Gambar_produk_disimpan_dipertahankan_diganti_dan_dihapus_oleh_pemiliknya()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Gambar");
        var s = _w.Open(shop);

        var created = await s.Products.CreateAsync(Req("Kopi Sachet", null, 3, "Sachet", null, "/uploads/products/5/a1.png"));
        Assert.True(created.Succeeded, created.Error);
        var id = created.Value!.Id;
        Assert.Equal("/uploads/products/5/a1.png", created.Value.ImagePath);

        // Perubahan tanpa gambar baru tidak menghapus gambar yang ada.
        var kept = await s.Products.UpdateAsync(id, Req("Kopi Sachet Baru", null, 3, "Sachet", null));
        Assert.True(kept.Succeeded, kept.Error);
        Assert.Equal("/uploads/products/5/a1.png", kept.Value!.ImagePath);

        // Gambar baru menggantikan yang lama.
        var replaced = await s.Products.UpdateAsync(id, Req("Kopi Sachet Baru", null, 3, "Sachet", null, "/uploads/products/5/b2.jpg"));
        Assert.Equal("/uploads/products/5/b2.jpg", replaced.Value!.ImagePath);

        // Permintaan hapus mengosongkan gambar.
        var cleared = await s.Products.UpdateAsync(id, Req("Kopi Sachet Baru", null, 3, "Sachet", null) with { ClearImage = true });
        Assert.True(cleared.Succeeded, cleared.Error);
        Assert.Null(cleared.Value!.ImagePath);
    }

    [Fact]
    public async Task Alamat_gambar_di_luar_folder_resmi_ditolak_dan_usaha_lain_tidak_dapat_mengubah_gambar()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung A");
        var rival = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung B");

        var bad = await _w.Open(shop).Products.CreateAsync(Req("Kopi", null, 3, "Sachet", null, "/etc/passwd"));
        Assert.False(bad.Succeeded);
        Assert.Equal(ErrorKind.Validation, bad.Kind);

        var traversal = await _w.Open(shop).Products.CreateAsync(Req("Kopi", null, 3, "Sachet", null, "/uploads/products/../../app.db"));
        Assert.False(traversal.Succeeded);

        var good = await _w.Open(shop).Products.CreateAsync(Req("Kopi", null, 3, "Sachet", null, "/uploads/products/1/x.png"));
        Assert.True(good.Succeeded, good.Error);

        // Produk milik usaha lain tidak terlihat sehingga gambarnya tidak dapat diganti.
        var hijack = await _w.Open(rival).Products.UpdateAsync(good.Value!.Id, Req("Kopi", null, 3, "Sachet", null, "/uploads/products/2/y.png"));
        Assert.False(hijack.Succeeded);
        Assert.Equal(ErrorKind.NotFound, hijack.Kind);
    }
}
