using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Scenario;

/// <summary>Kelompok D: alur bisnis dari grosir ke warung dan pembeli akhir.</summary>
public sealed class V3SupplyChainFlowTests : IDisposable
{
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    private async Task RunAsync(V3World.Actor seller, int orderId, params OrderAction[] actions)
    {
        foreach (var a in actions)
        {
            var r = await _w.Open(seller).Orders.ActAsync(orderId, a, null);
            Assert.True(r.Succeeded, $"{a}: {r.Error}");
        }
    }

    private static readonly OrderAction[] ToDelivered = { OrderAction.Accept, OrderAction.Prepare, OrderAction.MarkReady, OrderAction.HandOver };

    [Fact]
    public async Task D3_Warung_memesan_dari_grosir_dengan_stok_dari_pembelian()
    {
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Rantai", -6.915, 107.605);
        await _w.SetDeliveryAsync(wholesaler, fee: 10_000m);
        var productId = await _w.CreateStockedProductAsync(wholesaler, "Sirup", 40, 80_000m, 60_000m, moqBase: 10, expiry: _w.Today.AddDays(200), sku: "SRP-1");
        var retailer = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Rantai");

        var order = await _w.Open(retailer).Orders.CreateAsync(new CreateOrderRequest(
            wholesaler.BusinessId!.Value, FulfillmentType.Pickup, null, null, new[] { new OrderLineInput(productId, "Dus", 3) }, null, null, "d3-b"));
        Assert.True(order.Succeeded, order.Error);
        await RunAsync(wholesaler, order.Value, OrderAction.Accept, OrderAction.Prepare, OrderAction.MarkReady, OrderAction.HandOver);
        Assert.True((await _w.Open(retailer).Orders.ActAsync(order.Value, OrderAction.ConfirmReceived, null)).Succeeded);
        var inv = await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync(i => i.BusinessId == retailer.BusinessId);
        Assert.Equal(30, inv.QtyBase);
        Assert.Empty(await _w.Open(_w.Admin()).Integrity.CheckAsync());
    }
    [Fact]
    public async Task D5_Konsumen_membuat_pesanan_dari_warung_dan_seluruh_alur_stok_pembayaran_omzet_konsisten()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Konsumen");
        await _w.SetDeliveryAsync(shop, delivery: true, pickup: true, fee: 8_000m);
        var productId = await _w.CreateRetailProductAsync(shop, "Air Mineral", 30, 4_000m, 2_500m, _w.Today.AddDays(200));
        var buyer = await _w.RegisterConsumerAsync();

        var catalog = (await _w.Open(buyer).ConsumerCatalog.GetShopAsync(shop.BusinessId!.Value)).Value!;
        Assert.Equal(30, catalog.Products.Single().AvailableQty);

        var order = await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Delivery, PaymentMethod.CashOnDelivery, "Jl. Pembeli No. 5, Bandung", null,
            new[] { new ConsumerLineInput(productId, 6) }, "d5"));
        Assert.True(order.Succeeded, order.Error);

        var detail = (await _w.Open(buyer).ConsumerOrders.GetAsync(order.Value)).Value!;
        Assert.Equal(24_000m, detail.Subtotal);
        Assert.Equal(8_000m, detail.DeliveryFee);
        Assert.Equal(32_000m, detail.TotalAmount);
        Assert.Equal(OrderPaymentStatus.Unpaid, detail.PaymentStatus);

        var s = _w.Open(shop).ConsumerOrders;
        foreach (var a in ToDelivered) Assert.True((await s.ActAsync(order.Value, a, null)).Succeeded);
        Assert.True((await _w.Open(buyer).ConsumerOrders.ActAsync(order.Value, OrderAction.ConfirmReceived, null)).Succeeded);

        var final = (await _w.Open(buyer).ConsumerOrders.GetAsync(order.Value)).Value!;
        Assert.Equal(OrderStatus.Completed, final.Status);
        Assert.Equal(OrderPaymentStatus.Paid, final.PaymentStatus); // tunai terkumpul saat diterima
        Assert.Equal(24, (await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync()).QtyBase);
        Assert.Empty(await _w.Open(_w.Admin()).Integrity.CheckAsync());
    }

    [Fact]
    public async Task D7_Produk_berisiko_kedaluwarsa_mendapat_peringatan_dan_promo_menjualnya_lebih_dulu()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Risiko");
        var s = _w.Open(shop);
        var product = (await s.Products.CreateAsync(new SaveProductRequest("Susu UHT", null, 3, null, "Pcs", null, Array.Empty<Platform.Web.Modules.Catalog.Contracts.UnitInput>()))).Value!;
        // dua batch: dekat kedaluwarsa (4 hari) dan jauh (90 hari)
        Assert.True((await s.Inventory.AdjustAsync(new AdjustStockRequest(product.Id, MovementType.ManualIn, 10, 5_000m, null, _w.Today.AddDays(4)))).Succeeded);
        Assert.True((await s.Inventory.AdjustAsync(new AdjustStockRequest(product.Id, MovementType.ManualIn, 10, 5_000m, null, _w.Today.AddDays(90)))).Succeeded);
        Assert.True((await s.Inventory.UpdateSettingsAsync(product.Id, 10_000m, 0)).Succeeded);

        var batches = await s.Expiry.GetBatchesAsync();
        Assert.Contains(batches, b => b.DaysLeft <= 7 && b.QtyBase == 10); // peringatan mendekati kedaluwarsa

        Assert.True((await s.ConsumerPromos.SetAsync(product.Id, 30, 7)).Succeeded);
        var buyer = await _w.RegisterConsumerAsync();
        var shown = (await _w.Open(buyer).ConsumerCatalog.GetShopAsync(shop.BusinessId!.Value)).Value!.Products.Single();
        Assert.Equal(10, shown.PromoQty);
        Assert.Equal(7_000m, shown.PromoUnitPrice);
        Assert.Equal(_w.Today.AddDays(4), shown.NearestExpiry);

        // Memesan 12: 10 dari batch dekat (promo 7.000) + 2 dari batch jauh (10.000).
        var id = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(product.Id, 12) }, "d7"))).Value;
        var d = (await _w.Open(buyer).ConsumerOrders.GetAsync(id)).Value!;
        Assert.Equal(10 * 7_000m + 2 * 10_000m, d.Subtotal);
        Assert.Equal(2, d.Items.Count);
        Assert.Equal(30_000m, d.Savings);
    }

    [Fact]
    public async Task D8_Batch_kedaluwarsa_tidak_dapat_dijual_di_kasir_pesanan_pembeli_maupun_pesanan_usaha()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Kedaluwarsa");
        var s = _w.Open(shop);
        var product = (await s.Products.CreateAsync(new SaveProductRequest("Roti Tawar", null, 1, null, "Pcs", null, Array.Empty<Platform.Web.Modules.Catalog.Contracts.UnitInput>()))).Value!;
        Assert.True((await s.Inventory.AdjustAsync(new AdjustStockRequest(product.Id, MovementType.ManualIn, 10, 3_000m, null, _w.Today.AddDays(-2)))).Succeeded); // sudah kedaluwarsa
        Assert.True((await s.Inventory.UpdateSettingsAsync(product.Id, 6_000m, 0)).Succeeded);

        // Kasir: tidak tersedia untuk dijual.
        var sellable = await s.Inventory.SearchSellableAsync(null);
        Assert.True(sellable.Count == 0 || sellable.Single().QtyBase == 0);
        var pos = await s.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(product.Id, 1) }, "CASH", "d8-pos", null));
        Assert.False(pos.Succeeded);

        // Pembeli akhir: tidak tampil dan tidak dapat dipesan.
        var buyer = await _w.RegisterConsumerAsync();
        Assert.False((await _w.Open(buyer).ConsumerCatalog.GetShopAsync(shop.BusinessId!.Value)).Succeeded); // tidak ada barang layak jual
        var order = await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(product.Id, 1) }, "d8-c"));
        Assert.False(order.Succeeded);

        // Pesanan usaha: grosir dengan batch kedaluwarsa tidak dapat memenuhi pesanan.
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Kedaluwarsa", -6.915, 107.605);
        var pid = await _w.CreateStockedProductAsync(wholesaler, "Mentega", 10, 50_000m, expiry: _w.Today.AddDays(-1));
        var retailer = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Pesan Expired");
        var b2b = await _w.Open(retailer).Orders.CreateAsync(new CreateOrderRequest(
            wholesaler.BusinessId!.Value, FulfillmentType.Pickup, null, null, new[] { new OrderLineInput(pid, "Dus", 1) }, null, null, "d8-b"));
        Assert.False(b2b.Succeeded);
        Assert.Contains("tidak cukup", b2b.Error!);
    }

    [Fact]
    public async Task D8b_Batch_yang_kedaluwarsa_setelah_dipesan_ditolak_saat_diterima_dan_dapat_ditolak_penjual()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 19, 3, 0, 0, TimeSpan.Zero));
        using var w = new V3World(clock);
        var shop = await w.RegisterBusinessAsync(MarketRole.Retail, "Warung Waktu");
        var productId = await w.CreateRetailProductAsync(shop, "Keju", 10, 20_000m, 12_000m, w.Today.AddDays(2));
        var buyer = await w.RegisterConsumerAsync();
        var id = (await w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(productId, 4) }, "d8b"))).Value;

        clock.Advance(TimeSpan.FromDays(3)); // batch kedaluwarsa sebelum penjual menerima
        var accept = await w.Open(shop).ConsumerOrders.ActAsync(id, OrderAction.Accept, null);
        Assert.False(accept.Succeeded);
        Assert.Contains("kedaluwarsa", accept.Error!);
        Assert.True((await w.Open(shop).ConsumerOrders.ActAsync(id, OrderAction.Reject, "Barang sudah kedaluwarsa")).Succeeded);
        Assert.Empty(await w.Open(w.Admin()).Integrity.CheckAsync());
    }

    [Fact]
    public async Task D10_Laporan_food_waste_mencerminkan_catatan_stok_dan_transaksi()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Waste");
        var s = _w.Open(shop);
        var product = (await s.Products.CreateAsync(new SaveProductRequest("Donat", null, 1, null, "Pcs", null, Array.Empty<Platform.Web.Modules.Catalog.Contracts.UnitInput>()))).Value!;
        Assert.True((await s.Inventory.AdjustAsync(new AdjustStockRequest(product.Id, MovementType.ManualIn, 20, 2_000m, null, _w.Today.AddDays(-1)))).Succeeded); // kedaluwarsa
        Assert.True((await s.Inventory.AdjustAsync(new AdjustStockRequest(product.Id, MovementType.ManualIn, 30, 2_000m, null, _w.Today.AddDays(5)))).Succeeded);
        Assert.True((await s.Inventory.UpdateSettingsAsync(product.Id, 5_000m, 0)).Succeeded);
        Assert.True((await s.ConsumerPromos.SetAsync(product.Id, 40, 7)).Succeeded);

        // dibuang: batch kedaluwarsa 20 unit (modal 2.000) -> kerugian 40.000
        var expired = (await s.Expiry.GetBatchesAsync()).Single(b => b.DaysLeft < 0);
        Assert.True((await s.Expiry.WriteOffAsync(expired.BatchId, null)).Succeeded);

        // tersalurkan: pembeli membeli 10 promo (3.000) lalu selesai -> terjual 10 + tersalurkan 10
        var buyer = await _w.RegisterConsumerAsync();
        var id = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(product.Id, 10) }, "d10"))).Value;
        foreach (var a in ToDelivered) Assert.True((await s.ConsumerOrders.ActAsync(id, a, null)).Succeeded);
        Assert.True((await _w.Open(buyer).ConsumerOrders.ActAsync(id, OrderAction.ConfirmReceived, null)).Succeeded);

        var summary = await s.FoodWaste.GetSummaryAsync(_w.Today.AddDays(-1), _w.Today.AddDays(1));
        Assert.Equal(20, summary.WastedQtyBase);
        Assert.Equal(40_000m, summary.WastedCostValue);
        Assert.Equal(10, summary.RescuedQtyBase);
        Assert.Equal(30_000m, summary.RescuedValue);
        Assert.Equal(10, summary.SoldQtyBase);
        Assert.Equal(66.7m, summary.WasteRatePercent); // 20 / (10 + 20)
        Assert.Equal(20, summary.AtRiskQtyBase);       // 30 - 10 sisa batch 5 hari
    }

    [Fact]
    public async Task Admin_melihat_ringkasan_pembeli_dan_integritas_stok_nol_pelanggaran()
    {
        await _w.RegisterConsumerAsync();
        var dash = await _w.Open(_w.Admin()).Admin.GetDashboardAsync();
        Assert.Equal(1, dash.Consumers);
        Assert.Equal(0, dash.StockIntegrityViolations);
    }
}
