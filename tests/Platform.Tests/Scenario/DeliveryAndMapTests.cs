using Platform.Tests.TestSupport;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Notifications.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Scenario;

/// <summary>Ongkir menurut jarak, peta persebaran warung untuk grosir, pelacakan per fase, dan notifikasi.</summary>
public sealed class DeliveryAndMapTests : IDisposable
{
    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    // Grosir di (-6,9150; 107,6050): Rp8.000 untuk 2 km pertama, Rp2.500 per km berikutnya, maksimal 12 km jalan.
    private async Task<(Harness.Actor Seller, int ProductId)> SellerAsync()
    {
        var seller = await _h.NewWholesalerAsync();
        var productId = await _h.AddProductAsync(seller, "Mi Instan", "MIE-1", "Item", "Box", 40, 120_000m);
        await _h.StockAsync(seller, productId, "Box", 20, 105_000m, null);
        return (seller, productId);
    }

    private static Task<Result<int>> DeliveryOrderAsync(Harness.Actor buyer, Harness.Actor seller, int productId, int boxes = 1) =>
        buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, FulfillmentType.Delivery, null, "Jl. Tujuan No. 9", new[] { new OrderLineInput(productId, "Box", boxes) }));

    [Fact]
    public async Task Ongkir_dihitung_dari_jarak_dan_dicatat_pada_pesanan_serta_kartu_marketplace()
    {
        var (seller, productId) = await SellerAsync();
        var buyer = await _h.NewRetailerAsync(); // sekitar 1,6 km garis lurus = 2,1 km jalan

        var card = Assert.Single(await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, null)));
        Assert.Equal(2.1, card.RoadKm);
        Assert.Equal(8_500m, card.DeliveryFee);   // 8.000 + 0,1 km x 2.500 = 8.250 -> 8.500
        Assert.True(card.DeliveryInRange);
        Assert.Equal(2_500m, card.FeePerKm);

        var created = await DeliveryOrderAsync(buyer, seller, productId);
        Assert.True(created.Succeeded, created.Error);
        var order = (await buyer.S.Orders.GetAsync(created.Value)).Value!;
        Assert.Equal(8_500m, order.DeliveryFee);
        Assert.Equal(2.1, order.DeliveryDistanceKm);
        Assert.Equal(order.Subtotal + 8_500m, order.TotalAmount);
        Assert.Equal(2.1, order.Tracking!.RoadKm);
        Assert.True(order.Tracking.TravelMinutes > 0);
    }

    [Fact]
    public async Task Warung_jauh_membayar_ongkir_lebih_besar_dan_yang_di_luar_jangkauan_hanya_bisa_ambil_sendiri()
    {
        var (seller, productId) = await SellerAsync();
        var far = await _h.NewRetailerAsync("jauh@uji.test", -6.9650, 107.6600);      // 10,7 km jalan
        var outside = await _h.NewRetailerAsync("luar@uji.test", -6.9167, 107.7000);  // 13,7 km jalan > 12

        var farCard = Assert.Single(await far.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, null)));
        Assert.Equal(30_000m, farCard.DeliveryFee); // 8.000 + 8,7 x 2.500 = 29.750 -> 30.000
        Assert.True((await DeliveryOrderAsync(far, seller, productId)).Succeeded);

        var card = Assert.Single(await outside.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, null)));
        Assert.False(card.DeliveryInRange);
        Assert.Contains("jangkauan", card.DeliveryNote);

        var denied = await DeliveryOrderAsync(outside, seller, productId);
        Assert.Equal(ErrorKind.Validation, denied.Kind);
        Assert.Contains("jangkauan", denied.Error);

        // Filter "diantar" menyembunyikan grosir yang tidak menjangkau warung ini; ambil sendiri tetap bisa.
        Assert.Empty(await outside.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, FulfillmentType.Delivery)));
        Assert.True((await Harness.PlaceOrderAsync(outside, seller, productId, "Box", 1)) > 0);
    }

    [Fact]
    public async Task Tarif_pengiriman_yang_tidak_masuk_akal_ditolak()
    {
        var seller = await _h.NewWholesalerAsync();

        Assert.False((await seller.S.Delivery.SaveMineAsync(new SaveDeliveryRequest(true, true, 5_000m, 0m, null, null, -1m, 0m, null))).Succeeded);
        Assert.False((await seller.S.Delivery.SaveMineAsync(new SaveDeliveryRequest(true, true, 5_000m, 0m, null, null, 1_000m, 0m, 0m))).Succeeded);
        Assert.True((await seller.S.Delivery.SaveMineAsync(new SaveDeliveryRequest(true, true, 5_000m, 0m, null, null, 1_000m, 1m, 20m))).Succeeded);

        var saved = await seller.S.Delivery.GetMineAsync();
        Assert.Equal(1_000m, saved.FeePerKm);
        Assert.Equal(1m, saved.FreeKm);
        Assert.Equal(20m, saved.MaxDistanceKm);
    }

    [Fact]
    public async Task Peta_persebaran_menampilkan_langganan_calon_pelanggan_dan_ongkir_menurut_tarif_grosir()
    {
        var (seller, productId) = await SellerAsync();
        var customer = await _h.NewRetailerAsync("langganan@uji.test");
        var prospect = await _h.NewRetailerAsync("calon@uji.test", -6.8900, 107.5950);
        var outside = await _h.NewRetailerAsync("luar@uji.test", -6.9167, 107.7000);
        await _h.NewRetailerAsync("belum-verifikasi@uji.test", -6.9100, 107.6000, verify: false);
        var orderId = await Harness.PlaceOrderAsync(customer, seller, productId, "Box", 1);
        await Harness.ActOkAsync(seller, orderId, OrderAction.Accept);

        var map = await seller.S.WarungMap.GetAsync(null);

        Assert.Equal(3, map.Warungs.Count); // warung yang belum terverifikasi tidak tampil
        Assert.Equal(12 / Geo.RoadFactor, map.ServiceRadiusKm!.Value, 2);

        var first = map.Warungs[0]; // langganan lebih dulu
        Assert.Equal(customer.BusinessId, first.BusinessId);
        Assert.True(first.IsCustomer);
        Assert.Equal(1, first.OrdersCount);
        Assert.Equal(120_000m, first.TotalSpent);
        Assert.Equal("Jl. Uji No. 1, Bandung", first.Address);
        Assert.Equal(8_500m, first.DeliveryFee);

        var calon = map.Warungs.Single(w => w.BusinessId == prospect.BusinessId);
        Assert.False(calon.IsCustomer);
        Assert.Equal(string.Empty, calon.Address); // alamat hanya untuk langganan
        Assert.Null(calon.Phone);
        Assert.True(calon.InRange);

        var luar = map.Warungs.Single(w => w.BusinessId == outside.BusinessId);
        Assert.False(luar.InRange);
        Assert.Null(luar.DeliveryFee);

        // Warung tidak boleh memakai peta persebaran.
        Assert.Empty((await customer.S.WarungMap.GetAsync(null)).Warungs);
    }

    [Fact]
    public async Task Pelacakan_menampilkan_fase_jejak_kejadian_waktu_berangkat_dan_perkiraan_tiba()
    {
        var (seller, productId) = await SellerAsync();
        var buyer = await _h.NewRetailerAsync();
        var orderId = (await DeliveryOrderAsync(buyer, seller, productId)).Value;

        var pending = (await buyer.S.Orders.GetAsync(orderId)).Value!;
        Assert.Equal(TrackingState.Current, pending.Steps![0].State);
        Assert.Equal("Menunggu grosir menerima pesanan", pending.Tracking!.WaitingFor);
        Assert.Null(pending.Tracking.ShippedAtUtc);
        Assert.Single(pending.Events!);

        await Harness.ActOkAsync(seller, orderId, OrderAction.Accept);
        await Harness.ActOkAsync(seller, orderId, OrderAction.Prepare);
        await Harness.ActOkAsync(seller, orderId, OrderAction.MarkReady);
        await Harness.ActOkAsync(seller, orderId, OrderAction.HandOver);

        var shipped = (await buyer.S.Orders.GetAsync(orderId)).Value!;
        Assert.Equal(5, shipped.Events!.Count);
        Assert.Equal(new[] { OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready, OrderStatus.Delivered },
            shipped.Events.Select(e => e.Status).ToArray());
        Assert.Equal("Dalam pengiriman", shipped.Steps!.Single(s => s.State == TrackingState.Current).Title);
        Assert.NotNull(shipped.Tracking!.ShippedAtUtc);
        Assert.True(shipped.Tracking.EtaUtc > shipped.Tracking.ShippedAtUtc);
        Assert.Equal("Menunggu warung mengonfirmasi barang diterima", shipped.Tracking.WaitingFor);
        Assert.Equal(seller.S.Businesses.GetProfileAsync(seller.BusinessId).Result.Value!.Name, shipped.Tracking.CounterpartName);
        Assert.Equal(-6.9150, shipped.Tracking.SellerLat!.Value, 4);

        await Harness.ActOkAsync(buyer, orderId, OrderAction.ConfirmReceived);
        Assert.True((await seller.S.Orders.GetAsync(orderId)).Value!.Steps!.All(s => s.State == TrackingState.Done));
    }

    [Fact]
    public async Task Pesanan_ditolak_mencatat_alasan_pada_jejak_kejadian()
    {
        var (seller, productId) = await SellerAsync();
        var buyer = await _h.NewRetailerAsync();
        var orderId = (await DeliveryOrderAsync(buyer, seller, productId)).Value;

        await Harness.ActOkAsync(seller, orderId, OrderAction.Reject, "Stok sedang opname");

        var detail = (await buyer.S.Orders.GetAsync(orderId)).Value!;
        var steps = detail.Steps!;
        Assert.Equal(TrackingState.Failed, steps.Last().State);
        Assert.Contains("Stok sedang opname", steps.Last().Description);
        Assert.Equal("Stok sedang opname", detail.Events!.Last().Note);
    }

    [Fact]
    public async Task Notifikasi_dihitung_dari_pesanan_penawaran_dan_kadaluarsa_terkini()
    {
        var (seller, productId) = await SellerAsync();
        var buyer = await _h.NewRetailerAsync();
        await _h.StockAsync(seller, productId, "Box", 1, 105_000m, 3); // batch 3 hari lagi
        await _h.StockAsync(seller, productId, "Box", 1, 105_000m, -1); // sudah kadaluarsa

        Assert.Empty((await buyer.S.Notifications.GetAsync()).Items);

        var orderId = await Harness.PlaceOrderAsync(buyer, seller, productId, "Box", 1);
        var sellerNotes = await seller.S.Notifications.GetAsync();
        Assert.Equal(1, sellerNotes.Items.Single(i => i.Key == "orders-pending").Count);
        Assert.Equal(1, sellerNotes.Items.Single(i => i.Key == "expiring").Count);
        Assert.Equal(1, sellerNotes.Items.Single(i => i.Key == "expired").Count);
        Assert.Equal(NotificationTone.Danger, sellerNotes.Items.Single(i => i.Key == "expired").Tone);

        await Harness.ActOkAsync(seller, orderId, OrderAction.Accept);
        await Harness.ActOkAsync(seller, orderId, OrderAction.Prepare);
        await Harness.ActOkAsync(seller, orderId, OrderAction.MarkReady);
        await Harness.ActOkAsync(seller, orderId, OrderAction.HandOver);
        Assert.Equal(1, (await buyer.S.Notifications.GetAsync()).Items.Single(i => i.Key == "orders-confirm").Count);
        Assert.Empty((await seller.S.Notifications.GetAsync()).Items.Where(i => i.Key == "orders-pending"));
    }
}
