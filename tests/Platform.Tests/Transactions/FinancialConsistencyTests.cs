using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Sales.Domain;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Transactions;

/// <summary>Kelompok C: konsistensi finansial (nilai, snapshot harga, modal, omzet, HPP, ongkir, laporan = transaksi sumber).</summary>
public sealed class FinancialConsistencyTests : IDisposable
{
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    private static PlaceConsumerOrderRequest Place(V3World.Actor shop, int productId, int qty, string key, PaymentMethod m = PaymentMethod.CashOnPickup) =>
        new(shop.BusinessId!.Value, FulfillmentType.Pickup, m, null, null, new[] { new ConsumerLineInput(productId, qty) }, key);

    [Fact]
    public async Task C1_Nilai_item_dan_total_pesanan_dihitung_server_termasuk_promo_dan_ongkir_usaha()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Hitung");
        var expiry = _w.Today.AddDays(5);
        var productId = await _w.CreateRetailProductAsync(shop, "Yoghurt", 20, 10_000m, 6_000m, expiry);
        Assert.True((await _w.Open(shop).ConsumerPromos.SetAsync(productId, 20, 10)).Succeeded);
        var buyer = await _w.RegisterConsumerAsync();

        // 5 unit semuanya dari batch yang kedaluwarsa dalam 5 hari: harga promo 8.000 (diskon 20%). Total = 5 x 8.000 = 40.000.
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Place(shop, productId, 5, "c1"))).Value;
        var detail = (await _w.Open(buyer).ConsumerOrders.GetAsync(orderId)).Value!;
        Assert.Equal(40_000m, detail.Subtotal);
        Assert.Equal(40_000m, detail.TotalAmount);
        Assert.Equal(10_000m, detail.Savings); // 5 x (10.000 - 8.000)
        Assert.All(detail.Items, i => Assert.True(i.IsPromo));

        // Pesanan usaha: 3 Dus @ 100.000 + ongkir tetap 15.000.
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Hitung", -6.915, 107.605);
        await _w.SetDeliveryAsync(wholesaler, fee: 15_000m);
        var pid = await _w.CreateStockedProductAsync(wholesaler, "Gula", 20, 100_000m);
        var retailer = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Pesan Hitung");
        var oid = (await _w.Open(retailer).Orders.CreateAsync(new CreateOrderRequest(
            wholesaler.BusinessId!.Value, FulfillmentType.Delivery, null, "Jl. Contoh No. 5, Bandung",
            new[] { new OrderLineInput(pid, "Dus", 3) }, null, null, "c1-b2b"))).Value;
        var order = (await _w.Open(retailer).Orders.GetAsync(oid)).Value!;
        Assert.Equal(300_000m, order.Subtotal);
        Assert.Equal(15_000m, order.DeliveryFee);
        Assert.Equal(315_000m, order.TotalAmount);
    }

    [Fact]
    public async Task C2_Snapshot_harga_tidak_berubah_setelah_katalog_diedit()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Snapshot");
        var productId = await _w.CreateRetailProductAsync(shop, "Biskuit", 20, 5_000m);
        var buyer = await _w.RegisterConsumerAsync();
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Place(shop, productId, 4, "c2"))).Value;

        Assert.True((await _w.Open(shop).Inventory.UpdateSettingsAsync(productId, 9_999m, 0)).Succeeded); // harga naik setelah dipesan

        var detail = (await _w.Open(buyer).ConsumerOrders.GetAsync(orderId)).Value!;
        Assert.Equal(20_000m, detail.TotalAmount);
        Assert.Equal(5_000m, detail.Items.Single().UnitPrice);

        // Hal yang sama untuk pesanan usaha: harga grosir diubah setelah pesanan dibuat.
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Snapshot", -6.915, 107.605);
        var pid = await _w.CreateStockedProductAsync(wholesaler, "Teh", 10, 100_000m);
        var retailer = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Beli Snapshot");
        var oid = (await _w.Open(retailer).Orders.CreateAsync(new CreateOrderRequest(
            wholesaler.BusinessId!.Value, FulfillmentType.Pickup, null, null, new[] { new OrderLineInput(pid, "Dus", 2) }, null, null, "c2-b2b"))).Value;
        var ws = _w.Open(wholesaler);
        var product = (await ws.Products.GetAsync(pid)).Value!;
        var edit = await ws.Products.UpdateAsync(pid, new SaveProductRequest(
            product.Name, product.Sku, product.CategoryId, product.Description, product.BaseUnitName, 30_000m,
            new[] { new Platform.Web.Modules.Catalog.Contracts.UnitInput("Dus", 10, 250_000m) }, null, 1));
        Assert.True(edit.Succeeded, edit.Error);
        var order = (await _w.Open(retailer).Orders.GetAsync(oid)).Value!;
        Assert.Equal(200_000m, order.Subtotal); // tetap harga saat dipesan, bukan 500.000
    }

    [Fact]
    public async Task C3_Pembelian_memperbarui_modal_rata_rata_dan_stok_secara_konsisten()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Modal");
        var s = _w.Open(shop);
        var product = (await s.Products.CreateAsync(new SaveProductRequest("Susu", null, 1, null, "Pcs", null, Array.Empty<Platform.Web.Modules.Catalog.Contracts.UnitInput>()))).Value!;

        Assert.True((await s.Purchasing.RecordAsync(new RecordPurchaseRequest("Pemasok", null, null, new[] { new PurchaseLineInput(product.Id, "Pcs", 10, 1_000m, null) }))).Succeeded);
        Assert.True((await s.Purchasing.RecordAsync(new RecordPurchaseRequest("Pemasok", null, null, new[] { new PurchaseLineInput(product.Id, "Pcs", 30, 2_000m, null) }))).Succeeded);

        var inv = await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync(i => i.ProductId == product.Id);
        Assert.Equal(40, inv.QtyBase);
        Assert.Equal(1_750m, inv.AvgCostBase); // (10 x 1.000 + 30 x 2.000) / 40
        var purchased = await _w.Open(_w.Admin()).Db.Purchases.AsNoTracking().SumAsync(p => p.TotalAmount);
        Assert.Equal(70_000m, purchased);
        Assert.Equal(inv.QtyBase, await _w.Open(_w.Admin()).Db.InventoryMovements.SumAsync(m => m.QtyBaseSigned));
    }

    [Fact]
    public async Task C4_Kasir_menghitung_omzet_dan_HPP_dengan_benar_dan_laporan_sama_dengan_transaksi_sumber()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Laporan");
        var productId = await _w.CreateRetailProductAsync(shop, "Kopi", 50, 3_000m, 2_000m);
        var s = _w.Open(shop);
        var r1 = await s.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 10) }, "CASH", "k1", 50_000m));
        var r2 = await s.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, 4) }, "CASH", "k2", null));
        Assert.True(r1.Succeeded && r2.Succeeded);

        var db = _w.Open(_w.Admin()).Db;
        var items = await db.SaleItems.AsNoTracking().ToListAsync();
        Assert.Equal(14 * 3_000m, items.Sum(i => i.Subtotal));  // omzet 42.000
        Assert.Equal(14 * 2_000m, items.Sum(i => i.CostTotal)); // HPP 28.000
        Assert.Equal(20_000m, (await db.Sales.AsNoTracking().Where(x => x.Status == SaleStatus.Confirmed).SumAsync(x => x.TotalAmount)) - 22_000m); // 42.000 - 22.000

        // Laporan keuangan (omzet - HPP = laba kotor) sama dengan penjumlahan transaksi sumber.
        var from = _w.Today.AddDays(-1);
        var to = _w.Today.AddDays(1);
        var summary = await s.Reporting.GetFinancialSummaryAsync(new PeriodSelection(PeriodPreset.Custom, from, to));
        Assert.Equal(42_000m, summary.Revenue);
        Assert.Equal(28_000m, summary.Cogs);
        Assert.Equal(14_000m, summary.GrossProfit);
        Assert.Equal(summary.Revenue - summary.Cogs, summary.GrossProfit); // laba kotor = omzet - HPP
    }

    [Fact]
    public async Task C5_Ongkir_pesanan_usaha_hanya_dicatat_satu_kali_sebagai_biaya_pembeli()
    {
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Ongkir", -6.915, 107.605);
        await _w.SetDeliveryAsync(wholesaler, fee: 15_000m);
        var pid = await _w.CreateStockedProductAsync(wholesaler, "Beras", 20, 100_000m);
        var retailer = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Ongkir");
        var oid = (await _w.Open(retailer).Orders.CreateAsync(new CreateOrderRequest(
            wholesaler.BusinessId!.Value, FulfillmentType.Delivery, null, "Jl. Contoh No. 5, Bandung",
            new[] { new OrderLineInput(pid, "Dus", 2) }, null, null, "c5"))).Value;
        foreach (var a in new[] { OrderAction.Accept, OrderAction.Prepare, OrderAction.MarkReady, OrderAction.HandOver })
        {
            Assert.True((await _w.Open(wholesaler).Orders.ActAsync(oid, a, null)).Succeeded);
        }

        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _w.Open(retailer).Orders.ActAsync(oid, OrderAction.ConfirmReceived, null)).Succeeded);
        }

        var expenses = await _w.Open(_w.Admin()).Db.Expenses.AsNoTracking().Where(e => e.BusinessId == retailer.BusinessId).ToListAsync();
        Assert.Single(expenses);
        Assert.Equal(15_000m, expenses[0].Amount);
    }

    [Fact]
    public async Task C6_Pembayaran_simulasi_tidak_dihitung_sebagai_pendapatan_ganda_omzet_baru_tercatat_saat_selesai_sekali()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Omzet");
        var productId = await _w.CreateRetailProductAsync(shop, "Roti", 20, 6_000m, 4_000m);
        var buyer = await _w.RegisterConsumerAsync();
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Place(shop, productId, 3, "c6", PaymentMethod.DemoSimulation))).Value;
        var db = () => _w.Open(_w.Admin()).Db;

        // Dibayar (simulasi) tidak menciptakan penjualan: pembayaran bukan pendapatan.
        Assert.True((await _w.Open(buyer).Payments.PayDemoAsync(orderId, false, "p1")).Succeeded);
        Assert.Equal(0, await db().Sales.CountAsync());

        foreach (var a in new[] { OrderAction.Accept, OrderAction.Prepare, OrderAction.MarkReady, OrderAction.HandOver })
        {
            Assert.True((await _w.Open(shop).ConsumerOrders.ActAsync(orderId, a, null)).Succeeded);
        }

        Assert.Equal(0, await db().Sales.CountAsync()); // diserahkan belum selesai
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _w.Open(buyer).ConsumerOrders.ActAsync(orderId, OrderAction.ConfirmReceived, null)).Succeeded);
        }

        var sales = await db().Sales.Include(s => s.Items).AsNoTracking().ToListAsync();
        Assert.Single(sales);
        Assert.Equal(18_000m, sales[0].TotalAmount);
        Assert.Equal(12_000m, sales[0].Items.Sum(i => i.CostTotal)); // HPP 3 x 4.000
        Assert.Equal("CONSUMER_ORDER", sales[0].SourceType);
        Assert.Equal(1, await db().Payments.CountAsync(p => p.Status == PaymentStatus.Success));
    }

    [Fact]
    public async Task C7_Pembelian_yang_gagal_tidak_meninggalkan_stok_masuk_atau_biaya_pembelian()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Gagal Beli");
        var productId = await _w.CreateRetailProductAsync(shop, "Teh", 5, 3_000m);
        var s = _w.Open(shop);

        // Baris kedua menunjuk produk yang tidak ada -> seluruh pembelian gagal dan baris pertama tidak boleh tersimpan.
        var result = await s.Purchasing.RecordAsync(new RecordPurchaseRequest("Pemasok", null, null, new[]
        {
            new PurchaseLineInput(productId, "Pcs", 10, 1_000m, null),
            new PurchaseLineInput(987654, "Pcs", 10, 1_000m, null)
        }));
        Assert.False(result.Succeeded);

        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.Purchases.CountAsync());                  // hanya pembelian awal fixture
        Assert.Equal(5, (await db.Inventories.AsNoTracking().SingleAsync()).QtyBase); // stok tidak bertambah 10
        Assert.Equal(1, await db.InventoryMovements.CountAsync(m => m.Type == MovementType.PurchaseIn));
    }

    [Fact]
    public async Task C8_Pembelian_manual_dikirim_dua_kali_dengan_kunci_sama_menambah_stok_sekali()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Beli Dua");
        var s = _w.Open(shop);
        var product = (await s.Products.CreateAsync(new SaveProductRequest("Mie", null, 1, null, "Pcs", null, Array.Empty<Platform.Web.Modules.Catalog.Contracts.UnitInput>()))).Value!;
        var request = new RecordPurchaseRequest("Pemasok", null, null, new[] { new PurchaseLineInput(product.Id, "Pcs", 12, 1_500m, null) }, "beli-1");

        var a = await _w.Open(shop).Purchasing.RecordAsync(request);
        var b = await _w.Open(shop).Purchasing.RecordAsync(request);
        var c = await V3World.RaceAsync(() => _w.Open(shop).Purchasing.RecordAsync(request), () => _w.Open(shop).Purchasing.RecordAsync(request));

        Assert.True(a.Succeeded && b.Succeeded && c.All(x => x.Succeeded));
        Assert.Equal(a.Value, b.Value);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.Purchases.CountAsync());
        Assert.Equal(12, (await db.Inventories.AsNoTracking().SingleAsync()).QtyBase); // V2: 24+ (setiap klik menambah)
    }
}
