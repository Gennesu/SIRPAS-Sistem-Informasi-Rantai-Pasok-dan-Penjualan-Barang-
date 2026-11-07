using Platform.Tests.TestSupport;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Modules.Stock;

/// <summary>Batch kadaluarsa: FEFO, batch tertentu, penandaan stok lama, membuang batch, dan ringkasan risiko.</summary>
public sealed class ExpiryServiceTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    // Susu: 1 Dus = 24 Kotak @ Rp96.000 (modal Rp88.000 per Dus = Rp3.666,6667 per Kotak).
    private async Task<(Harness.Actor Seller, int ProductId)> SellerWithMilkAsync()
    {
        var seller = await _h.NewWholesalerAsync();
        var productId = await _h.AddProductAsync(seller, "Susu UHT", "SUS-1", "Kotak", "Dus", 24, 96_000m);
        return (seller, productId);
    }

    [Fact]
    public async Task Stok_masuk_dengan_tanggal_membuat_batch_dan_tanggal_yang_sama_digabung()
    {
        var (seller, productId) = await SellerWithMilkAsync();

        await _h.StockAsync(seller, productId, "Dus", 2, 88_000m, 20);
        await _h.StockAsync(seller, productId, "Dus", 1, 90_000m, 20); // tanggal sama: digabung
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, 6);

        var batches = await seller.S.Expiry.GetBatchesAsync();

        Assert.Equal(new[] { (6, 24), (20, 72) }, batches.Select(b => (b.DaysLeft, b.QtyBase)).ToArray());
        Assert.Equal(96, (await seller.S.Inventory.GetStockAsync()).Single().QtyBase);
    }

    [Fact]
    public async Task Barang_keluar_memakai_FEFO_batch_paling_dekat_kadaluarsa_lebih_dulu()
    {
        var (seller, productId) = await SellerWithMilkAsync();
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, 20);
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, 6);

        var result = await seller.S.Inventory.StockOutAsync(new StockOutRequest(productId, 30, MovementType.OtherOut));

        Assert.True(result.Succeeded, result.Error);
        var allocations = result.Value!.Allocations!;
        Assert.Equal(new[] { (_h.Today.AddDays(6), 24), (_h.Today.AddDays(20), 6) }, allocations.Select(a => (a.ExpiryDate, a.QtyBase)).ToArray());
        Assert.Equal(new[] { (20, 18) }, (await seller.S.Expiry.GetBatchesAsync()).Select(b => (b.DaysLeft, b.QtyBase)).ToArray());
        Assert.Equal(18, result.Value.NewQtyBase);
    }

    [Fact]
    public async Task Barang_keluar_dari_batch_tertentu_tidak_menyentuh_batch_lain_dan_ditolak_bila_batch_kurang()
    {
        var (seller, productId) = await SellerWithMilkAsync();
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, 20);
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, 6);
        var farBatch = (await seller.S.Expiry.GetBatchesAsync()).Single(b => b.DaysLeft == 20);

        var ok = await seller.S.Inventory.StockOutAsync(new StockOutRequest(productId, 10, MovementType.OtherOut, BatchId: farBatch.BatchId));
        Assert.True(ok.Succeeded, ok.Error);
        Assert.Equal(new[] { (6, 24), (20, 14) }, (await seller.S.Expiry.GetBatchesAsync()).Select(b => (b.DaysLeft, b.QtyBase)).ToArray());

        var tooMany = await seller.S.Inventory.StockOutAsync(new StockOutRequest(productId, 15, MovementType.OtherOut, BatchId: farBatch.BatchId));
        Assert.False(tooMany.Succeeded);
        Assert.Equal(ErrorKind.Conflict, tooMany.Kind);
        Assert.Equal(38, (await seller.S.Inventory.GetStockAsync()).Single().QtyBase); // tidak berubah
    }

    [Fact]
    public async Task Stok_tanpa_tanggal_keluar_setelah_batch_bertanggal_dan_dapat_ditandai_kemudian()
    {
        var (seller, productId) = await SellerWithMilkAsync();
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, null); // 24 tanpa tanggal
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, 6);    // 24 bertanggal

        // 30 keluar: 24 dari batch bertanggal (FEFO), 6 dari yang tanpa tanggal.
        await seller.S.Inventory.StockOutAsync(new StockOutRequest(productId, 30, MovementType.OtherOut));
        Assert.Empty(await seller.S.Expiry.GetBatchesAsync());

        // Sisa 18 belum bertanggal: boleh ditandai paling banyak 18.
        var tooMany = await seller.S.Expiry.TagStockAsync(new TagStockRequest(productId, 19, _h.Today.AddDays(30)));
        Assert.Equal(ErrorKind.Conflict, tooMany.Kind);

        var tagged = await seller.S.Expiry.TagStockAsync(new TagStockRequest(productId, 18, _h.Today.AddDays(30)));
        Assert.True(tagged.Succeeded, tagged.Error);
        Assert.Equal(new[] { (30, 18) }, (await seller.S.Expiry.GetBatchesAsync()).Select(b => (b.DaysLeft, b.QtyBase)).ToArray());
        Assert.Equal(18, (await seller.S.Inventory.GetStockAsync()).Single().QtyBase); // jumlah stok tidak berubah
    }

    [Fact]
    public async Task Jumlah_batch_tidak_boleh_melebihi_jumlah_stok_masuk()
    {
        var (seller, productId) = await SellerWithMilkAsync();

        var result = await seller.S.Inventory.StockInAsync(new StockInRequest(
            productId, 10, 3_000m, MovementType.ManualIn, Batches: new[] { new BatchPortion(_h.Today.AddDays(5), 11) }));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
        Assert.Equal(0, (await seller.S.Inventory.GetStockAsync()).Single().QtyBase);
    }

    [Fact]
    public async Task Membuang_batch_kadaluarsa_mencatat_pergerakan_Kadaluarsa_dan_mengosongkan_batch()
    {
        var (seller, productId) = await SellerWithMilkAsync();
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, -2);
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, 40);
        var expired = Assert.Single(await seller.S.Expiry.GetBatchesAsync(), b => b.Band == ExpiryBand.Expired);
        Assert.Equal(-2, expired.DaysLeft);

        var result = await seller.S.Expiry.WriteOffAsync(expired.BatchId, null);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(new[] { (40, 24) }, (await seller.S.Expiry.GetBatchesAsync()).Select(b => (b.DaysLeft, b.QtyBase)).ToArray());
        Assert.Equal(24, (await seller.S.Inventory.GetStockAsync()).Single().QtyBase);
        var movement = (await seller.S.Inventory.GetMovementsAsync(productId)).First();
        Assert.Equal(-24, movement.QtyBaseSigned);
        Assert.Contains("kadaluarsa", movement.Reason!.ToLowerInvariant());
    }

    [Fact]
    public async Task Ringkasan_mengelompokkan_batch_menurut_sisa_umur_dan_menghitung_nilai_berisiko()
    {
        var (seller, productId) = await SellerWithMilkAsync();
        foreach (var days in new[] { -2, 3, 10, 25, 90 })
        {
            await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, days);
        }

        var summary = await seller.S.Expiry.GetSummaryAsync();

        Assert.Equal(1, summary.Expired.Batches);
        Assert.Equal(1, summary.Critical.Batches);
        Assert.Equal(1, summary.Soon.Batches);
        Assert.Equal(1, summary.Watch.Batches);
        Assert.Equal(1, summary.Safe.Batches);
        Assert.Equal(72, summary.UnitsAtRisk);
        Assert.Equal(3 * 88_000m, summary.ValueAtRisk, 0);
    }

    [Fact]
    public void Aturan_pita_umur_dan_saran_diskon()
    {
        Assert.Equal(ExpiryBand.Expired, ExpiryBands.Of(-1));
        Assert.Equal(ExpiryBand.Critical, ExpiryBands.Of(0));
        Assert.Equal(ExpiryBand.Critical, ExpiryBands.Of(7));
        Assert.Equal(ExpiryBand.Soon, ExpiryBands.Of(14));
        Assert.Equal(ExpiryBand.Watch, ExpiryBands.Of(30));
        Assert.Equal(ExpiryBand.Safe, ExpiryBands.Of(31));
        Assert.Equal(50, ExpiryBands.SuggestedDiscountPercent(2));
        Assert.Equal(40, ExpiryBands.SuggestedDiscountPercent(6));
        Assert.Equal(20, ExpiryBands.SuggestedDiscountPercent(20));
        Assert.Equal("Lewat 3 hari", ExpiryBands.DaysText(-3));
        Assert.Equal("Besok", ExpiryBands.DaysText(1));
        Assert.Equal("20 hari lagi", ExpiryBands.DaysText(20));
    }
}
