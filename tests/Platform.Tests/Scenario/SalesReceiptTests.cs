using Platform.Tests.TestSupport;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Scenario;

/// <summary>Struk kasir: uang diterima, kembalian, identitas toko dan kasir, teks untuk dibagikan, dan FEFO di kasir.</summary>
public sealed class SalesReceiptTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    // Warung dengan 24 Kotak susu @ Rp5.000, tanpa/dengan tanggal kadaluarsa.
    private async Task<(Harness.Actor Shop, int ProductId)> ShopWithMilkAsync(params int?[] expiryDays)
    {
        var shop = await _h.NewRetailerAsync();
        var product = await shop.S.Products.CreateAsync(new SaveProductRequest(
            "Susu UHT", "SUS-1", 1, null, "Kotak", 3_300m, new[] { new UnitInput("Dus", 24, 80_000m) }));
        Assert.True(product.Succeeded, product.Error);
        foreach (var days in expiryDays)
        {
            await _h.StockAsync(shop, product.Value!.Id, "Dus", 1, 80_000m, days);
        }

        Assert.True((await shop.S.Inventory.UpdateSettingsAsync(product.Value!.Id, 5_000m, 5)).Succeeded);
        return (shop, product.Value.Id);
    }

    [Fact]
    public async Task Struk_tunai_menampilkan_uang_diterima_kembalian_toko_dan_kasir()
    {
        var (shop, productId) = await ShopWithMilkAsync((int?)null);

        var result = await shop.S.Sales.ConfirmAsync(new ConfirmSaleRequest(
            new[] { new SaleLineInput(productId, 3) }, "CASH", null, 20_000m));

        Assert.True(result.Succeeded, result.Error);
        var receipt = result.Value!;
        Assert.Equal(15_000m, receipt.Total);
        Assert.Equal(20_000m, receipt.Paid);
        Assert.Equal(5_000m, receipt.Change);
        Assert.Contains("Warung Uji", receipt.StoreName);
        Assert.Equal("Jl. Uji No. 1, Bandung", receipt.StoreAddress);
        Assert.Equal("081200000001", receipt.StorePhone);
        Assert.Contains("Pemilik Warung Uji", receipt.CashierName);
        var line = Assert.Single(receipt.Lines);
        Assert.Equal("Kotak", line.UnitName);
        Assert.Equal(5_000m, line.UnitPrice);

        // Struk yang dibuka lagi sama persis.
        var again = (await shop.S.Sales.GetReceiptAsync(receipt.Id)).Value!;
        Assert.Equal(5_000m, again.Change);
    }

    [Fact]
    public async Task Uang_diterima_kurang_dari_total_ditolak_dan_stok_tidak_berubah()
    {
        var (shop, productId) = await ShopWithMilkAsync((int?)null);

        var result = await shop.S.Sales.ConfirmAsync(new ConfirmSaleRequest(
            new[] { new SaleLineInput(productId, 3) }, "CASH", null, 14_999m));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
        Assert.Equal(24, (await shop.S.Inventory.GetStockAsync()).Single().QtyBase);
    }

    [Fact]
    public async Task Tanpa_uang_diterima_dianggap_pas_dan_pembayaran_non_tunai_tidak_ada_kembalian()
    {
        var (shop, productId) = await ShopWithMilkAsync((int?)null);

        var exact = (await shop.S.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 2) }, "CASH", null))).Value!;
        Assert.Equal(10_000m, exact.Paid);
        Assert.Equal(0m, exact.Change);

        var qris = (await shop.S.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 2) }, "QRIS", null, 50_000m))).Value!;
        Assert.Equal(10_000m, qris.Paid); // uang diterima diabaikan untuk non-tunai
        Assert.Equal(0m, qris.Change);
        Assert.Equal("QRIS", PaymentLabels.Method(qris.PaymentMethod));
        Assert.Equal("Tunai", PaymentLabels.Method("CASH"));
    }

    [Fact]
    public async Task Teks_struk_untuk_dibagikan_berisi_toko_item_total_dan_kembalian()
    {
        var (shop, productId) = await ShopWithMilkAsync((int?)null);
        var receipt = (await shop.S.Sales.ConfirmAsync(new ConfirmSaleRequest(
            new[] { new SaleLineInput(productId, 3) }, "CASH", null, 20_000m))).Value!;

        var text = ReceiptText.Build(receipt);

        Assert.Contains("Warung Uji", text);
        Assert.Contains(receipt.Number, text);
        Assert.Contains("Susu UHT", text);
        Assert.Contains("3 x Rp5.000", text);
        Assert.Contains("Rp15.000", text);
        Assert.Contains("Kembali", text);
        Assert.Contains("Rp5.000", text);
        Assert.True(text.Split('\n').All(l => l.TrimEnd('\r').Length <= ReceiptText.Width), "Lebar baris struk harus muat di kertas kecil.");
    }

    [Fact]
    public async Task Kasir_menjual_barang_yang_paling_dekat_kadaluarsa_lebih_dulu()
    {
        var (shop, productId) = await ShopWithMilkAsync(40, 5);

        var sale = await shop.S.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 10) }, "CASH", null));
        Assert.True(sale.Succeeded, sale.Error);

        var batches = await shop.S.Expiry.GetBatchesAsync();
        Assert.Equal(new[] { (5, 14), (40, 24) }, batches.Select(b => (b.DaysLeft, b.QtyBase)).ToArray());
    }
}

public sealed class ReceiptTextWrapTests
{
    [Fact]
    public void Teks_panjang_dibungkus_per_kata_dan_kata_sangat_panjang_dipotong()
    {
        Assert.Equal(new[] { "Susu UHT Cokelat", "Kemasan Kotak" }, ReceiptText.Wrap("Susu UHT Cokelat Kemasan Kotak", 16).ToArray());
        Assert.Equal(new[] { "ABCDE", "FGHIJ", "K" }, ReceiptText.Wrap("ABCDEFGHIJK", 5).ToArray());
        Assert.Empty(ReceiptText.Wrap("   ", 10));
    }
}
