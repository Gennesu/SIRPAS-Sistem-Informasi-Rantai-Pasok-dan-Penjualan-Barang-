using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Transactions;

/// <summary>
/// Kelompok A (master prompt bagian 14): idempotensi checkout dan pembayaran demo.
/// Setiap test memeriksa jumlah baris di DATABASE (order, payment, reservasi, movement) dan invarian stok, bukan hanya hasil panggilan.
/// </summary>
public sealed class IdempotencyAndPaymentTests : IDisposable
{
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    private async Task<(V3World.Actor Shop, int ProductId, V3World.Actor Buyer)> SetupAsync(int stock = 10, decimal price = 5_000m)
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Uji");
        var productId = await _w.CreateRetailProductAsync(shop, "Teh Botol", stock, price);
        var buyer = await _w.RegisterConsumerAsync();
        return (shop, productId, buyer);
    }

    private static PlaceConsumerOrderRequest Request(V3World.Actor shop, int productId, int qty, string? key, PaymentMethod method = PaymentMethod.DemoSimulation) =>
        new(shop.BusinessId!.Value, FulfillmentType.Pickup, method, null, null, new[] { new ConsumerLineInput(productId, qty) }, key);

    private async Task AssertConsistentAsync()
    {
        var violations = await _w.Open(_w.Admin()).Integrity.CheckAsync();
        Assert.True(violations.Count == 0, "Pelanggaran integritas: " + string.Join("; ", violations.Select(v => $"{v.Rule} {v.Detail}")));
    }

    [Fact]
    public async Task A1_Checkout_dikirim_dua_kali_dengan_kunci_sama_membuat_satu_pesanan_satu_pembayaran_dan_satu_reservasi()
    {
        var (shop, productId, buyer) = await SetupAsync();
        var key = Guid.NewGuid().ToString("N");

        var first = await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 3, key));
        var second = await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 3, key));

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(first.Value, second.Value);

        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.ConsumerOrders.CountAsync());
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(1, await db.StockReservations.CountAsync(r => r.Status == ReservationStatus.Active));
        var inv = await db.Inventories.SingleAsync();
        Assert.Equal(10, inv.QtyBase);
        Assert.Equal(3, inv.ReservedBase); // 3, bukan 6: tidak ada reservasi ganda
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A1b_Pesanan_usaha_dikirim_dua_kali_dengan_kunci_sama_membuat_satu_pesanan()
    {
        var wholesaler = await _w.RegisterBusinessAsync(MarketRole.Wholesale, "Grosir Uji", -6.915, 107.605);
        await _w.SetDeliveryAsync(wholesaler);
        var productId = await _w.CreateStockedProductAsync(wholesaler, "Minyak", 10);
        var retailer = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Pesan");
        var key = Guid.NewGuid().ToString("N");

        var request = new Platform.Web.Modules.Orders.Contracts.CreateOrderRequest(
            wholesaler.BusinessId!.Value, FulfillmentType.Pickup, null, null,
            new[] { new Platform.Web.Modules.Orders.Contracts.OrderLineInput(productId, "Dus", 2) }, null, null, key);
        var a = await _w.Open(retailer).Orders.CreateAsync(request);
        var b = await _w.Open(retailer).Orders.CreateAsync(request);

        Assert.True(a.Succeeded, a.Error);
        Assert.Equal(a.Value, b.Value);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.Orders.CountAsync());
        Assert.Equal(20, (await db.Inventories.SingleAsync(i => i.BusinessId == wholesaler.BusinessId)).ReservedBase);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A2_Pembayaran_demo_dikonfirmasi_dua_kali_hanya_mencatat_satu_pembayaran_sukses()
    {
        var (shop, productId, buyer) = await SetupAsync();
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 2, "ord-a2"))).Value;

        var p1 = await _w.Open(buyer).Payments.PayDemoAsync(orderId, false, "pay-1");
        var p2 = await _w.Open(buyer).Payments.PayDemoAsync(orderId, false, "pay-2"); // tab lain, kunci berbeda

        Assert.True(p1.Succeeded, p1.Error);
        Assert.True(p2.Succeeded, p2.Error);
        Assert.Equal(p1.Value!.Reference, p2.Value!.Reference); // pembayaran yang sama dikembalikan
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Success));
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(OrderPaymentStatus.Paid, (await db.ConsumerOrders.SingleAsync()).PaymentStatus);
        Assert.StartsWith("PAY-", p1.Value.Reference);
    }

    [Fact]
    public async Task A3_Refresh_halaman_hasil_pembayaran_mengulang_kunci_sama_tidak_mengulang_pembayaran()
    {
        var (shop, productId, buyer) = await SetupAsync();
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 2, "ord-a3"))).Value;

        var first = await _w.Open(buyer).Payments.PayDemoAsync(orderId, false, "pay-refresh");
        for (var i = 0; i < 3; i++)
        {
            var again = await _w.Open(buyer).Payments.PayDemoAsync(orderId, false, "pay-refresh");
            Assert.True(again.Succeeded);
            Assert.Equal(first.Value!.Id, again.Value!.Id);
            Assert.Equal(first.Value.CompletedAt, again.Value.CompletedAt); // waktu tidak berubah: tidak diproses ulang
        }

        Assert.Equal(1, await _w.Open(_w.Admin()).Db.Payments.CountAsync(p => p.Status == PaymentStatus.Success));
    }

    [Fact]
    public async Task A4_Retry_setelah_respons_pertama_tidak_diterima_klien_mengembalikan_hasil_yang_sama()
    {
        var (shop, productId, buyer) = await SetupAsync();
        var key = "ord-a4";
        var placed = await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 4, key));
        // Klien tidak menerima respons dan mengulang persis permintaan yang sama.
        var retried = await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 4, key));
        Assert.Equal(placed.Value, retried.Value);

        var pay = await _w.Open(buyer).Payments.PayDemoAsync(placed.Value, false, "pay-a4");
        var payRetry = await _w.Open(buyer).Payments.PayDemoAsync(placed.Value, false, "pay-a4");
        Assert.Equal(pay.Value!.Id, payRetry.Value!.Id);

        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(1, await db.ConsumerOrders.CountAsync());
        Assert.Equal(1, await db.Payments.CountAsync());
        Assert.Equal(4, (await db.Inventories.SingleAsync()).ReservedBase);
    }

    [Fact]
    public async Task A5_Dua_request_dengan_kunci_sama_pada_waktu_berdekatan_hanya_membuat_satu_pesanan()
    {
        for (var trial = 0; trial < 8; trial++)
        {
            using var w = new V3World();
            var shop = await w.RegisterBusinessAsync(MarketRole.Retail, "Warung Uji");
            var productId = await w.CreateRetailProductAsync(shop, "Teh Botol", 10, 5_000m);
            var buyer = await w.RegisterConsumerAsync();
            var key = Guid.NewGuid().ToString("N");

            var results = await V3World.RaceAsync(
                () => w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 3, key)),
                () => w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 3, key)),
                () => w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 3, key)));

            Assert.All(results, r => Assert.True(r.Succeeded, r.Error)); // semua menerima hasil yang sama, bukan error
            Assert.Single(results.Select(r => r.Value).Distinct());
            var db = w.Open(w.Admin()).Db;
            Assert.Equal(1, await db.ConsumerOrders.CountAsync());
            Assert.Equal(1, await db.Payments.CountAsync());
            Assert.Equal(3, (await db.Inventories.SingleAsync()).ReservedBase);
        }
    }

    [Fact]
    public async Task A5b_Konfirmasi_pembayaran_serentak_hanya_menghasilkan_satu_pembayaran_sukses()
    {
        for (var trial = 0; trial < 6; trial++)
        {
            using var w = new V3World();
            var shop = await w.RegisterBusinessAsync(MarketRole.Retail, "Warung Uji");
            var productId = await w.CreateRetailProductAsync(shop, "Teh Botol", 10, 5_000m);
            var buyer = await w.RegisterConsumerAsync();
            var orderId = (await w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 2, Guid.NewGuid().ToString("N")))).Value;

            var results = await V3World.RaceAsync(
                () => w.Open(buyer).Payments.PayDemoAsync(orderId, false, "k1"),
                () => w.Open(buyer).Payments.PayDemoAsync(orderId, false, "k2"),
                () => w.Open(buyer).Payments.PayDemoAsync(orderId, false, "k3"));

            Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
            Assert.Single(results.Select(r => r.Value!.Id).Distinct());
            var db = w.Open(w.Admin()).Db;
            Assert.Equal(1, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Success));
            Assert.Equal(1, await db.Payments.CountAsync());
        }
    }

    [Fact]
    public async Task A6_Dua_pembelian_baru_yang_berbeda_tetap_dapat_dilakukan()
    {
        var (shop, productId, buyer) = await SetupAsync();

        var first = await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 2, "beli-1"));
        var second = await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 2, "beli-2"));

        Assert.True(first.Succeeded && second.Succeeded);
        Assert.NotEqual(first.Value, second.Value);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(2, await db.ConsumerOrders.CountAsync());
        Assert.Equal(4, (await db.Inventories.SingleAsync()).ReservedBase);
        await AssertConsistentAsync();
    }

    [Fact]
    public async Task A7_Pembayaran_gagal_tidak_meninggalkan_status_lunas_dan_percobaan_ulang_menghasilkan_satu_sukses()
    {
        var (shop, productId, buyer) = await SetupAsync();
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 2, "ord-a7"))).Value;

        var failed = await _w.Open(buyer).Payments.PayDemoAsync(orderId, true, "try-1");
        Assert.True(failed.Succeeded);
        Assert.Equal(PaymentStatus.Failed, failed.Value!.Status);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(OrderPaymentStatus.Failed, (await db.ConsumerOrders.SingleAsync()).PaymentStatus);
        Assert.Equal(0, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Success));

        var ok = await _w.Open(buyer).Payments.PayDemoAsync(orderId, false, "try-2");
        Assert.Equal(PaymentStatus.Success, ok.Value!.Status);
        db = _w.Open(_w.Admin()).Db;
        Assert.Equal(OrderPaymentStatus.Paid, (await db.ConsumerOrders.SingleAsync()).PaymentStatus);
        Assert.Equal(1, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Success));
        Assert.Equal(2, await db.Payments.CountAsync()); // riwayat percobaan: gagal lalu sukses (nomor referensi berbeda)
        Assert.NotEqual(failed.Value.Reference, ok.Value.Reference);
    }

    [Fact]
    public async Task A8_Pesanan_dibatalkan_tidak_dapat_menerima_pembayaran_sukses_dan_database_menolak_dua_sukses()
    {
        var (shop, productId, buyer) = await SetupAsync();
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 2, "ord-a8"))).Value;
        Assert.True((await _w.Open(buyer).ConsumerOrders.ActAsync(orderId, OrderAction.BuyerCancel, null)).Succeeded);

        var late = await _w.Open(buyer).Payments.PayDemoAsync(orderId, false, "late");
        Assert.False(late.Succeeded);
        Assert.Equal(ErrorKind.Conflict, late.Kind);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(0, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Success));
        Assert.Equal(OrderPaymentStatus.Cancelled, (await db.ConsumerOrders.SingleAsync()).PaymentStatus);

        // Pengaman terakhir di database: dua catatan Success untuk satu pesanan DITOLAK oleh unique index berfilter.
        var (shop2, product2, buyer2) = await SetupAsync2();
        var order2 = (await _w.Open(buyer2).ConsumerOrders.PlaceAsync(Request(shop2, product2, 1, "ord-a8b"))).Value;
        Assert.True((await _w.Open(buyer2).Payments.PayDemoAsync(order2, false, "p1")).Succeeded);
        var raw = _w.Open(_w.Admin()).Db;
        raw.Payments.Add(new Payment
        {
            Reference = "PAY-MANUAL-DUP", ConsumerOrderId = order2, ConsumerUserId = buyer2.UserId, SellerBusinessId = shop2.BusinessId!.Value,
            Method = PaymentMethod.DemoSimulation, Status = PaymentStatus.Success, Amount = 5_000m, CreatedAt = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => raw.SaveChangesAsync());
    }

    private async Task<(V3World.Actor Shop, int ProductId, V3World.Actor Buyer)> SetupAsync2()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Dua");
        var productId = await _w.CreateRetailProductAsync(shop, "Air Mineral", 10, 3_000m);
        var buyer = await _w.RegisterConsumerAsync("Siti");
        return (shop, productId, buyer);
    }

    [Fact]
    public async Task Pesanan_tunai_dan_pembayaran_demo_memakai_aturan_status_pembayaran_yang_terpisah_dari_status_pesanan()
    {
        var (shop, productId, buyer) = await SetupAsync();
        var cash = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 1, "cash-1", PaymentMethod.CashOnPickup))).Value;
        var demo = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(Request(shop, productId, 1, "demo-1"))).Value;

        var cashDetail = (await _w.Open(buyer).ConsumerOrders.GetAsync(cash)).Value!;
        var demoDetail = (await _w.Open(buyer).ConsumerOrders.GetAsync(demo)).Value!;
        Assert.Equal(OrderPaymentStatus.Unpaid, cashDetail.PaymentStatus);
        Assert.Equal(OrderPaymentStatus.Pending, demoDetail.PaymentStatus);
        Assert.False(cashDetail.CanPayDemo);
        Assert.True(demoDetail.CanPayDemo);
        Assert.Equal(OrderStatus.Pending, cashDetail.Status);

        // Pembayaran demo untuk pesanan tunai ditolak.
        var wrong = await _w.Open(buyer).Payments.PayDemoAsync(cash, false, "x");
        Assert.Equal(ErrorKind.Validation, wrong.Kind);

        // Pengiriman untuk pesanan simulasi yang belum lunas diblokir.
        var seller = _w.Open(shop).ConsumerOrders;
        Assert.True((await seller.ActAsync(demo, OrderAction.Accept, null)).Succeeded);
        Assert.True((await seller.ActAsync(demo, OrderAction.Prepare, null)).Succeeded);
        Assert.True((await seller.ActAsync(demo, OrderAction.MarkReady, null)).Succeeded);
        var blocked = await seller.ActAsync(demo, OrderAction.HandOver, null);
        Assert.Equal(ErrorKind.Conflict, blocked.Kind);
        Assert.True((await _w.Open(buyer).Payments.PayDemoAsync(demo, false, "ok")).Succeeded);
        Assert.True((await seller.ActAsync(demo, OrderAction.HandOver, null)).Succeeded);
    }
}
