using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Scenario;

/// <summary>
/// Alur Food Waste: grosir membuat paket dari batch yang mendekati kadaluarsa, mengirim penawaran ke warung yang paling
/// laku menjualnya, warung memesan, dan batch yang benar keluar dari grosir lalu masuk ke stok warung.
/// </summary>
public sealed class RescueFlowTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    // Susu: 1 Dus = 24 Kotak @ Rp96.000 -> harga normal Rp4.000 per Kotak.
    private async Task<(Harness.Actor Seller, int ProductId)> SellerWithMilkAsync(int[] batchDays, int packsPerBatch = 10)
    {
        var seller = await _h.NewWholesalerAsync();
        var productId = await _h.AddProductAsync(seller, "Susu UHT", "SUS-1", "Kotak", "Dus", 24, 96_000m);
        foreach (var days in batchDays)
        {
            await _h.StockAsync(seller, productId, "Dus", packsPerBatch, 88_000m, days);
        }

        return (seller, productId);
    }

    private static async Task<int> CreateBundleAsync(
        Harness.Actor seller, int batchId, int qtyPerBundle, int? discount = 20, decimal? price = null,
        IReadOnlyList<OfferTarget>? targets = null, bool privateToTargets = false, int validDays = 3)
    {
        var result = await seller.S.Rescue.CreateBundleAsync(new CreateBundleRequest(
            null, null, new[] { new RescueItemInput(batchId, qtyPerBundle) }, price, price is null ? discount : null,
            WibTime.TodayWib(TimeProvider.System).AddDays(validDays), targets, "Harga khusus untuk Anda.", privateToTargets));
        Assert.True(result.Succeeded, result.Error);
        return result.Value;
    }

    private static async Task<BatchDto> BatchAsync(Harness.Actor seller, int daysLeft) =>
        (await seller.S.Expiry.GetBatchesAsync()).Single(b => b.DaysLeft == daysLeft);

    [Fact]
    public async Task Paket_dari_batch_dihitung_diskon_harga_normal_dan_ketersediaannya()
    {
        var (seller, _) = await SellerWithMilkAsync(new[] { 20 });
        var batch = await BatchAsync(seller, 20);

        var bundleId = await CreateBundleAsync(seller, batch.BatchId, 24, 20);

        var bundle = Assert.Single(await seller.S.Rescue.ListMineAsync(false));
        Assert.Equal(bundleId, bundle.Id);
        Assert.Equal(96_000m, bundle.NormalPrice);      // 24 x Rp4.000
        Assert.Equal(76_800m, bundle.Price);            // diskon 20%
        Assert.Equal(20, bundle.DiscountPercent);
        Assert.Equal(19_200m, bundle.Savings);
        Assert.Equal(10, bundle.Available);             // 240 Kotak / 24
        Assert.Equal(BundleState.Active, bundle.State);
        Assert.Equal(20, bundle.EarliestExpiryDays);
    }

    [Fact]
    public async Task Harga_tetap_dibatasi_harga_normal_dan_batch_kadaluarsa_atau_kurang_stok_ditolak()
    {
        var (seller, productId) = await SellerWithMilkAsync(new[] { 20 });
        await _h.StockAsync(seller, productId, "Dus", 1, 88_000m, -3);
        var good = await BatchAsync(seller, 20);
        var expired = await BatchAsync(seller, -3);

        var overPrice = await seller.S.Rescue.CreateBundleAsync(new CreateBundleRequest(
            null, null, new[] { new RescueItemInput(good.BatchId, 24) }, 96_001m, null, null));
        Assert.Equal(ErrorKind.Validation, overPrice.Kind);

        var onExpired = await seller.S.Rescue.CreateBundleAsync(new CreateBundleRequest(
            null, null, new[] { new RescueItemInput(expired.BatchId, 24) }, null, 20, null));
        Assert.Equal(ErrorKind.Conflict, onExpired.Kind);

        var tooMany = await seller.S.Rescue.CreateBundleAsync(new CreateBundleRequest(
            null, null, new[] { new RescueItemInput(good.BatchId, 241) }, null, 20, null));
        Assert.Equal(ErrorKind.Conflict, tooMany.Kind);

        var pastExpiry = await seller.S.Rescue.CreateBundleAsync(new CreateBundleRequest(
            null, null, new[] { new RescueItemInput(good.BatchId, 24) }, null, 20, _h.Today.AddDays(21)));
        Assert.Equal(ErrorKind.Validation, pastExpiry.Kind); // paket tidak boleh berlaku melewati kadaluarsa

        Assert.Empty(await seller.S.Rescue.ListMineAsync(true));
    }

    [Fact]
    public async Task Paket_publik_tampil_di_marketplace_dan_paket_privat_hanya_untuk_penerima_penawaran()
    {
        var (seller, _) = await SellerWithMilkAsync(new[] { 20, 9 });
        var maju = await _h.NewRetailerAsync("maju@uji.test");
        var other = await _h.NewRetailerAsync("lain@uji.test");
        var farBatch = await BatchAsync(seller, 20);
        var nearBatch = await BatchAsync(seller, 9);

        var publicId = await CreateBundleAsync(seller, farBatch.BatchId, 24, 20);
        var privateId = await CreateBundleAsync(
            seller, nearBatch.BatchId, 24, 35, targets: new[] { new OfferTarget(maju.BusinessId, 2) }, privateToTargets: true);

        // Publik terlihat semua warung; privat hanya oleh penerima.
        Assert.Equal(new[] { publicId }, (await other.S.Rescue.ListPublicAsync()).Select(b => b.Id).ToArray());
        Assert.Equal(new[] { publicId }, (await maju.S.Rescue.ListPublicAsync()).Select(b => b.Id).ToArray());
        Assert.True((await maju.S.Rescue.GetForBuyerAsync(privateId)).Succeeded);
        Assert.Equal(ErrorKind.NotFound, (await other.S.Rescue.GetForBuyerAsync(privateId)).Kind);

        // Ringkasan promo per grosir dipakai kartu marketplace dan peta.
        var summary = (await maju.S.Rescue.GetDealSummaryAsync())[seller.BusinessId];
        Assert.Equal(1, summary.Deals);
        Assert.Equal(20, summary.BestDiscountPercent);

        // Penawaran masuk ke kotak masuk penerima saja.
        Assert.Equal(1, await maju.S.Rescue.CountOpenOffersAsync());
        Assert.Equal(0, await other.S.Rescue.CountOpenOffersAsync());
        var offer = Assert.Single(await maju.S.Rescue.ListReceivedAsync(false));
        Assert.Equal(2, offer.SuggestedBundles);
        Assert.Equal(OfferState.Sent, offer.State);
        Assert.Equal(35, offer.Bundle!.DiscountPercent);
    }

    [Fact]
    public async Task Pesanan_paket_mengambil_stok_dari_batch_paket_bukan_FEFO_dan_batch_ikut_ke_stok_warung()
    {
        var (seller, productId) = await SellerWithMilkAsync(new[] { 20, 6 });
        var buyer = await _h.NewRetailerAsync();
        var farBatch = await BatchAsync(seller, 20);
        var bundleId = await CreateBundleAsync(seller, farBatch.BatchId, 24, 20);

        var created = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 2) }));
        Assert.True(created.Succeeded, created.Error);
        var orderId = created.Value;

        var order = (await buyer.S.Orders.GetAsync(orderId)).Value!;
        Assert.Equal(2 * 76_800m, order.Subtotal);
        Assert.Equal(2 * 19_200m, order.Savings);
        var line = Assert.Single(order.Items);
        Assert.Equal(48, line.QtyBase);
        Assert.NotNull(line.BundleName);

        await Harness.CompleteAsync(seller, buyer, orderId);

        // Batch 20 hari berkurang 48; batch 6 hari (yang FEFO ambil lebih dulu) tidak tersentuh.
        Assert.Equal(new[] { (6, 240), (20, 192) }, (await seller.S.Expiry.GetBatchesAsync()).Select(b => (b.DaysLeft, b.QtyBase)).ToArray());

        // Warung menerima 48 Kotak dengan tanggal kadaluarsa batch yang sama.
        var received = Assert.Single(await buyer.S.Expiry.GetBatchesAsync());
        Assert.Equal(48, received.QtyBase);
        Assert.Equal(_h.Today.AddDays(20), received.ExpiryDate);
        Assert.Equal(48, (await buyer.S.Inventory.GetStockAsync()).Single().QtyBase);
        Assert.Equal(OrderStatus.Completed, (await buyer.S.Orders.GetAsync(orderId)).Value!.Status);
    }

    [Fact]
    public async Task Pesanan_paket_melebihi_ketersediaan_ditolak_dan_minimal_pesanan_tidak_berlaku_untuk_paket()
    {
        var seller = await _h.NewWholesalerAsync("grosir-min@uji.test", minOrder: 500_000m);
        var productId = await _h.AddProductAsync(seller, "Susu UHT", "SUS-1", "Kotak", "Dus", 24, 96_000m);
        await _h.StockAsync(seller, productId, "Dus", 2, 88_000m, 20); // 48 Kotak = 2 paket
        var buyer = await _h.NewRetailerAsync();
        var bundleId = await CreateBundleAsync(seller, (await BatchAsync(seller, 20)).BatchId, 24, 20);

        var tooMany = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 3) }));
        Assert.Equal(ErrorKind.Conflict, tooMany.Kind);

        // 1 paket = Rp76.800, jauh di bawah minimal Rp500.000, tetapi pesanan khusus paket tidak dikenai minimal.
        var ok = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 1) }));
        Assert.True(ok.Succeeded, ok.Error);

        // Barang biasa tetap terkena minimal.
        var regular = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, new[] { new OrderLineInput(productId, "Dus", 1) }));
        Assert.Equal(ErrorKind.Validation, regular.Kind);
    }

    [Fact]
    public async Task Penawaran_diterima_menjadi_pesanan_dan_tidak_bisa_dipakai_dua_kali()
    {
        var (seller, _) = await SellerWithMilkAsync(new[] { 20 });
        var buyer = await _h.NewRetailerAsync();
        var bundleId = await CreateBundleAsync(
            seller, (await BatchAsync(seller, 20)).BatchId, 24, 20, targets: new[] { new OfferTarget(buyer.BusinessId, 1) }, privateToTargets: true);
        var offer = Assert.Single(await buyer.S.Rescue.ListReceivedAsync(false));

        var created = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 1) }, offer.Id));
        Assert.True(created.Succeeded, created.Error);

        var accepted = Assert.Single(await buyer.S.Rescue.ListReceivedAsync(true));
        Assert.Equal(OfferState.Accepted, accepted.State);
        Assert.Equal(created.Value, accepted.OrderId);
        Assert.Equal(0, await buyer.S.Rescue.CountOpenOffersAsync());

        // Grosir melihat penawarannya sudah diambil.
        Assert.Equal(OfferState.Accepted, Assert.Single(await seller.S.Rescue.ListSentAsync()).State);

        var again = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 1) }, offer.Id));
        Assert.Equal(ErrorKind.Conflict, again.Kind);
    }

    [Fact]
    public async Task Penawaran_ditolak_atau_ditarik_dan_menghentikan_paket_menarik_penawaran_yang_terbuka()
    {
        var (seller, _) = await SellerWithMilkAsync(new[] { 20, 9 });
        var a = await _h.NewRetailerAsync("a@uji.test");
        var b = await _h.NewRetailerAsync("b@uji.test");
        var bundle1 = await CreateBundleAsync(
            seller, (await BatchAsync(seller, 20)).BatchId, 24, 20, targets: new[] { new OfferTarget(a.BusinessId, 1), new OfferTarget(b.BusinessId, 1) });
        var bundle2 = await CreateBundleAsync(
            seller, (await BatchAsync(seller, 9)).BatchId, 24, 30, targets: new[] { new OfferTarget(a.BusinessId, 1) });

        // Warung A menolak penawaran bundle1.
        var offerA1 = (await a.S.Rescue.ListReceivedAsync(false)).Single(o => o.BundleId == bundle1);
        Assert.True((await a.S.Rescue.DeclineOfferAsync(offerA1.Id)).Succeeded);
        Assert.Equal(OfferState.Declined, (await a.S.Rescue.ListReceivedAsync(true)).Single(o => o.Id == offerA1.Id).State);
        Assert.Equal(ErrorKind.Conflict, (await a.S.Rescue.DeclineOfferAsync(offerA1.Id)).Kind);

        // Grosir menarik penawaran B.
        var sentToB = (await seller.S.Rescue.ListSentAsync()).Single(o => o.BuyerBusinessId == b.BusinessId);
        Assert.True((await seller.S.Rescue.WithdrawOfferAsync(sentToB.Id)).Succeeded);
        Assert.Equal(0, await b.S.Rescue.CountOpenOffersAsync());

        // Menghentikan bundle2 menarik penawaran terbukanya.
        Assert.Equal(1, await a.S.Rescue.CountOpenOffersAsync());
        Assert.True((await seller.S.Rescue.EndBundleAsync(bundle2)).Succeeded);
        Assert.Equal(0, await a.S.Rescue.CountOpenOffersAsync());
        var blocked = await a.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundle2, 1) }));
        Assert.False(blocked.Succeeded);
    }

    [Fact]
    public async Task Grosir_membatalkan_pesanan_paket_batch_asal_kembali_dan_paket_tersedia_lagi()
    {
        var (seller, _) = await SellerWithMilkAsync(new[] { 20 }, packsPerBatch: 2);
        var buyer = await _h.NewRetailerAsync();
        var bundleId = await CreateBundleAsync(seller, (await BatchAsync(seller, 20)).BatchId, 24, 20);
        var orderId = (await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 2) }))).Value;

        await Harness.ActOkAsync(seller, orderId, OrderAction.Accept);
        Assert.Equal(0, Assert.Single(await seller.S.Rescue.ListMineAsync(true)).Available);
        Assert.Equal(BundleState.SoldOut, Assert.Single(await seller.S.Rescue.ListMineAsync(true)).State);

        await Harness.ActOkAsync(seller, orderId, OrderAction.SellerCancel, "Barang rusak");

        var batch = Assert.Single(await seller.S.Expiry.GetBatchesAsync());
        Assert.Equal(48, batch.QtyBase);
        Assert.Equal(20, batch.DaysLeft);
        Assert.Equal(2, Assert.Single(await seller.S.Rescue.ListMineAsync(false)).Available);
    }

    [Fact]
    public async Task Paket_yang_batchnya_sudah_kadaluarsa_atau_melewati_batas_tanggal_tidak_dapat_dipesan()
    {
        var (seller, _) = await SellerWithMilkAsync(new[] { 3 });
        var buyer = await _h.NewRetailerAsync();
        var bundleId = await CreateBundleAsync(seller, (await BatchAsync(seller, 3)).BatchId, 24, 40, validDays: 1);

        // Seolah sudah lewat batas tanggal paket: ubah langsung di database.
        using (var db = _h.Db.CreateDb())
        {
            var bundle = db.RescueBundles.IgnoreQueryFilters().Single(b => b.Id == bundleId);
            bundle.ValidUntil = _h.Today.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var result = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Pickup, null, null, Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 1) }));
        Assert.False(result.Succeeded);
        Assert.Empty(await buyer.S.Rescue.ListPublicAsync());
    }

    // ------------------------------------------------------------------ rekomendasi dari data penjualan

    // Warung membeli susu dari grosir lalu menjualnya (data penjualan yang dibaca grosir).
    private async Task BuyAndSellAsync(Harness.Actor seller, int sellerProductId, Harness.Actor buyer, int packs, int sellUnits)
    {
        var orderId = await Harness.PlaceOrderAsync(buyer, seller, sellerProductId, "Dus", packs);
        await Harness.CompleteAsync(seller, buyer, orderId);
        var product = (await buyer.S.Products.ListAsync(null, true)).Single(p => p.SourceProductId == sellerProductId);
        Assert.True((await buyer.S.Inventory.UpdateSettingsAsync(product.Id, 5_000m, 5)).Succeeded);
        var sale = await buyer.S.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(product.Id, sellUnits) }, "CASH", null));
        Assert.True(sale.Succeeded, sale.Error);
    }

    [Fact]
    public async Task Dashboard_merekomendasikan_warung_terlaris_menjual_susu_untuk_batch_20_hari()
    {
        // Skenario: susu terjual 80 di Warung A dan 60 di Warung B; grosir punya susu yang 20 hari lagi kadaluarsa.
        var (seller, productId) = await SellerWithMilkAsync(new[] { 120 }, packsPerBatch: 20);
        var a = await _h.NewRetailerAsync("a@uji.test");
        var b = await _h.NewRetailerAsync("b@uji.test", -6.9300, 107.6250);
        var prospect = await _h.NewRetailerAsync("calon@uji.test", -6.8900, 107.5950); // belum pernah memesan
        await BuyAndSellAsync(seller, productId, a, packs: 4, sellUnits: 80); // sisa 16
        await BuyAndSellAsync(seller, productId, b, packs: 3, sellUnits: 60); // sisa 12

        await _h.StockAsync(seller, productId, "Dus", 10, 88_000m, 20); // batch baru yang mendekati kadaluarsa

        var dashboard = await seller.S.Rescue.GetDashboardAsync();
        var candidate = Assert.Single(dashboard.Candidates);
        Assert.Equal(20, candidate.Batch.DaysLeft);
        Assert.Equal(20, candidate.SuggestedDiscountPercent);
        Assert.Equal(3_200m, candidate.SuggestedUnitPrice);

        // Urutan target = kecepatan jual: A (80) lalu B (60). Calon yang belum pernah memesan tidak muncul.
        Assert.Equal(new[] { a.BusinessId, b.BusinessId }, candidate.Targets.Select(t => t.BusinessId).ToArray());
        var first = candidate.Targets[0];
        Assert.Equal(80, first.SoldQtyBase);
        Assert.Equal(16, first.StockAtWarungBase);
        // Dalam 17 hari (20 - 3 hari penyangga) Warung A menjual +-45; stok 16 -> disarankan 29.
        Assert.Equal(29, first.SuggestedQtyBase);
        Assert.False(first.HasOpenOffer);

        // Kirim penawaran ke Warung A; dashboard menandainya.
        var sent = await seller.S.Rescue.CreateBundleAsync(new CreateBundleRequest(
            null, null, new[] { new RescueItemInput(candidate.Batch.BatchId, 24) }, null, candidate.SuggestedDiscountPercent, _h.Today.AddDays(5),
            new[] { new OfferTarget(a.BusinessId, 1) }, null, true));
        Assert.True(sent.Succeeded, sent.Error);

        var after = Assert.Single((await seller.S.Rescue.GetDashboardAsync()).Candidates);
        Assert.True(after.Targets.Single(t => t.BusinessId == a.BusinessId).HasOpenOffer);
        Assert.False(after.Targets.Single(t => t.BusinessId == b.BusinessId).HasOpenOffer);
        Assert.Equal(sent.Value, after.ActiveBundleId);
    }

    [Fact]
    public async Task Warung_yang_tidak_membagikan_data_penjualan_tidak_dipakai_untuk_rekomendasi()
    {
        var (seller, productId) = await SellerWithMilkAsync(new[] { 120 }, packsPerBatch: 20);
        var a = await _h.NewRetailerAsync("a@uji.test");
        var b = await _h.NewRetailerAsync("b@uji.test", -6.9300, 107.6250);
        await BuyAndSellAsync(seller, productId, a, 4, 80);
        await BuyAndSellAsync(seller, productId, b, 3, 60);

        var profile = (await b.S.Businesses.GetProfileAsync(b.BusinessId)).Value!;
        Assert.True(profile.ShareSalesData); // bawaan: berbagi
        var update = await b.S.Businesses.UpdateProfileAsync(
            b.BusinessId, new Web.Modules.BusinessProfile.Contracts.UpdateBusinessRequest(
                profile.Name, profile.Address, profile.Latitude, profile.Longitude, profile.Phone, false));
        Assert.True(update.Succeeded, update.Error);

        var demand = Assert.Single(await seller.S.SellThrough.GetDemandAsync(30));
        Assert.Equal(new[] { a.BusinessId }, demand.Warungs.Select(w => w.BusinessId).ToArray());
        Assert.Equal(80, demand.TotalSoldBase);
        Assert.Equal(1, await seller.S.SellThrough.CountHiddenCustomersAsync());
    }

    [Fact]
    public async Task Data_penjualan_hanya_untuk_grosir_yang_pernah_menerima_pesanan_dari_warung_itu()
    {
        var (seller, productId) = await SellerWithMilkAsync(new[] { 120 }, packsPerBatch: 20);
        var a = await _h.NewRetailerAsync("a@uji.test");
        await BuyAndSellAsync(seller, productId, a, 4, 80);

        // Grosir lain tidak punya hubungan dagang dengan warung A: tidak melihat angka apa pun.
        var rival = await _h.NewWholesalerAsync("saingan@uji.test");
        await _h.AddProductAsync(rival, "Susu UHT", "SUS-1", "Kotak", "Dus", 24, 95_000m);
        Assert.Empty(await rival.S.SellThrough.GetDemandAsync(30));

        // Warung tidak boleh memakai layanan ini.
        Assert.Empty(await a.S.SellThrough.GetDemandAsync(30));

        var mine = Assert.Single(await seller.S.SellThrough.GetDemandAsync(30));
        Assert.Equal(2.67, mine.Warungs.Single().AvgPerDay, 2); // 80 terjual / 30 hari
    }
}
