using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Security;

/// <summary>Kelompok E: keamanan dan otorisasi (isolasi tenant, manipulasi permintaan, pendaftaran publik, akun nonaktif).</summary>
public sealed class V3AuthorizationTests : IDisposable
{
    private readonly V3World _w = new();

    public void Dispose() => _w.Dispose();

    [Fact]
    public async Task E1_Pengguna_tidak_dapat_membaca_data_privat_usaha_lain_pada_semua_tabel_bertenant()
    {
        var shopA = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung A");
        var shopB = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung B");
        var pA = await _w.CreateRetailProductAsync(shopA, "Produk Milik A", 10, 5_000m);
        await _w.CreateRetailProductAsync(shopB, "Produk Milik B", 10, 5_000m);
        var buyer = await _w.RegisterConsumerAsync();
        var order = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shopA.BusinessId!.Value, Platform.Web.Modules.Delivery.Contracts.FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null,
            new[] { new ConsumerLineInput(pA, 2) }, "e1"))).Value;
        Assert.True((await _w.Open(shopA).Sales.ConfirmAsync(new Platform.Web.Modules.Sales.Contracts.ConfirmSaleRequest(
            new[] { new Platform.Web.Modules.Sales.Contracts.SaleLineInput(pA, 1) }, "CASH", "e1-sale", null))).Succeeded);

        // Warung B hanya melihat data B pada produk, stok, movement, penjualan, pembelian, reservasi, dan pesanan pembeli.
        var b = _w.Open(shopB).Db;
        Assert.Equal(new[] { "Produk Milik B" }, await b.Products.Select(p => p.Name).ToListAsync());
        Assert.Equal(1, await b.Inventories.CountAsync());
        Assert.All(await b.InventoryMovements.ToListAsync(), m => Assert.Equal(shopB.BusinessId, m.BusinessId));
        Assert.Equal(0, await b.Sales.CountAsync());
        Assert.Equal(0, await b.SaleItems.CountAsync());
        Assert.Equal(0, await b.StockReservations.CountAsync());
        Assert.Equal(0, await b.ConsumerOrders.CountAsync());
        Assert.Equal(0, await b.Payments.CountAsync());
        Assert.Equal(0, await b.ConsumerOrderItems.CountAsync());

        // Layanan: pesanan pembeli milik orang lain tidak ditemukan; pembeli lain tidak dapat membacanya.
        Assert.Equal(ErrorKind.NotFound, (await _w.Open(shopB).ConsumerOrders.GetAsync(order)).Kind);
        var other = await _w.RegisterConsumerAsync("Pembeli Lain");
        Assert.Equal(ErrorKind.NotFound, (await _w.Open(other).ConsumerOrders.GetAsync(order)).Kind);
        Assert.Empty(await _w.Open(other).ConsumerOrders.ListAsync(null));
        Assert.Single(await _w.Open(buyer).ConsumerOrders.ListAsync(null));

        // Pembeli akhir tidak dapat membaca stok internal, modal, penjualan, atau laporan usaha.
        var c = _w.Open(buyer).Db;
        Assert.Equal(0, await c.Inventories.CountAsync());
        Assert.Equal(0, await c.Sales.CountAsync());
        Assert.Equal(0, await c.Purchases.CountAsync());
        Assert.Equal(0, await c.Expenses.CountAsync());
        Assert.Equal(0, await c.StockBatches.CountAsync());
        Assert.Equal(1, await c.ConsumerProfiles.CountAsync()); // hanya profil sendiri
    }

    [Fact]
    public async Task E2_Pengguna_tidak_dapat_mengubah_harga_stok_atau_status_pembayaran_lewat_permintaan_yang_dimanipulasi()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung E2");
        var other = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Lain E2");
        var pid = await _w.CreateRetailProductAsync(shop, "Teh", 10, 5_000m);
        var buyer = await _w.RegisterConsumerAsync();
        var intruder = await _w.RegisterConsumerAsync("Penyusup");
        var orderId = (await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.DemoSimulation, null, null, new[] { new ConsumerLineInput(pid, 2) }, "e2"))).Value;

        // Warung lain tidak dapat mengubah harga/stok produk warung ini (produk tidak terlihat di tenant-nya).
        Assert.Equal(ErrorKind.NotFound, (await _w.Open(other).Inventory.UpdateSettingsAsync(pid, 1m, 0)).Kind);
        Assert.Equal(ErrorKind.NotFound, (await _w.Open(other).Inventory.StockOutAsync(new StockOutRequest(pid, 1, MovementType.CorrectionOut))).Kind);
        Assert.Equal(5_000m, (await _w.Open(_w.Admin()).Db.Inventories.AsNoTracking().SingleAsync(i => i.BusinessId == shop.BusinessId)).SellingPriceBase);

        // Pembeli lain tidak dapat membayar, membatalkan, atau menerima pesanan milik orang lain.
        Assert.Equal(ErrorKind.NotFound, (await _w.Open(intruder).Payments.PayDemoAsync(orderId, false, "x")).Kind);
        Assert.Equal(ErrorKind.NotFound, (await _w.Open(intruder).ConsumerOrders.ActAsync(orderId, OrderAction.BuyerCancel, null)).Kind);

        // Status pesanan tidak dapat diloncati: pembeli tidak dapat menerima, menyerahkan, atau mengubah status secara bebas.
        var asBuyer = _w.Open(buyer).ConsumerOrders;
        Assert.Equal(ErrorKind.Conflict, (await asBuyer.ActAsync(orderId, OrderAction.ConfirmReceived, null)).Kind);  // belum diserahkan
        Assert.Equal(ErrorKind.Conflict, (await asBuyer.ActAsync(orderId, OrderAction.HandOver, null)).Kind);
        Assert.Equal(ErrorKind.Forbidden, (await asBuyer.ActAsync(orderId, OrderAction.Accept, null)).Kind);   // aksi penjual, ditolak otorisasi
        Assert.Equal(ErrorKind.Forbidden, (await _w.Open(shop).ConsumerOrders.ActAsync(orderId, OrderAction.BuyerCancel, null)).Kind); // penjual bukan pembeli
        Assert.Equal(OrderStatus.Pending, (await _w.Open(_w.Admin()).Db.ConsumerOrders.SingleAsync()).Status);

        // Penjual tidak dapat menyerahkan pesanan simulasi yang belum dibayar; status pembayaran tidak bisa dipalsukan.
        var seller = _w.Open(shop).ConsumerOrders;
        foreach (var a in new[] { OrderAction.Accept, OrderAction.Prepare, OrderAction.MarkReady })
        {
            Assert.True((await seller.ActAsync(orderId, a, null)).Succeeded);
        }

        Assert.Equal(ErrorKind.Conflict, (await seller.ActAsync(orderId, OrderAction.HandOver, null)).Kind);
        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(0, await db.Payments.CountAsync(p => p.Status == PaymentStatus.Success));
        Assert.Equal(OrderPaymentStatus.Pending, (await db.ConsumerOrders.SingleAsync()).PaymentStatus);

        // Input tidak masuk akal ditolak server: jumlah nol/negatif/terlalu besar dan produk bukan milik warung.
        var bad = new[] { 0, -5, 1_000_000_000 };
        foreach (var qty in bad)
        {
            var r = await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
                shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(pid, qty) }, "e2-bad" + qty));
            Assert.False(r.Succeeded);
        }

        var foreign = await _w.CreateRetailProductAsync(other, "Barang Warung Lain", 10, 1_000m);
        Assert.False((await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(foreign, 1) }, "e2-foreign"))).Succeeded);
    }

    [Fact]
    public async Task E3_Konsumen_tidak_dapat_memakai_layanan_internal_penjual_dan_usaha_tidak_dapat_memakai_layanan_pembeli()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung E3");
        var pid = await _w.CreateRetailProductAsync(shop, "Kopi", 5, 3_000m);
        var buyer = await _w.RegisterConsumerAsync();
        var c = _w.Open(buyer);

        Assert.Equal(ErrorKind.Forbidden, (await c.Inventory.StockInAsync(new StockInRequest(pid, 5, 1m, MovementType.ManualIn))).Kind);
        Assert.Equal(ErrorKind.Forbidden, (await c.Inventory.UpdateSettingsAsync(pid, 1m, 0)).Kind);
        Assert.False((await c.Sales.ConfirmAsync(new Platform.Web.Modules.Sales.Contracts.ConfirmSaleRequest(
            new[] { new Platform.Web.Modules.Sales.Contracts.SaleLineInput(pid, 1) }, "CASH", "e3", null))).Succeeded);
        Assert.False((await c.Orders.CreateAsync(new CreateOrderRequest(shop.BusinessId!.Value, FulfillmentType.Pickup, null, null,
            new[] { new OrderLineInput(pid, "Pcs", 1) }, null, null, "e3-o"))).Succeeded);
        Assert.Equal(ErrorKind.Forbidden, (await c.Delivery.SaveMineAsync(new SaveDeliveryRequest(true, true, 0, 0, null, null))).Kind);

        // Warung tidak dapat membuat pesanan sebagai pembeli akhir.
        Assert.Equal(ErrorKind.Forbidden, (await _w.Open(shop).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(pid, 1) }, "e3-x"))).Kind);
        Assert.Empty(await _w.Open(shop).ConsumerCatalog.SearchShopsAsync(null));
    }

    [Fact]
    public async Task E4_Pendaftaran_publik_tidak_dapat_menghasilkan_admin_dan_akun_admin_tidak_boleh_terikat_usaha()
    {
        foreach (var role in new[] { MarketRole.Retail, MarketRole.Wholesale, MarketRole.Livestock })
        {
            var r = await _w.RegisterBusinessAsync(role, "Usaha " + role, verify: false);
            Assert.NotNull(r);
        }

        await _w.RegisterConsumerAsync();
        var bogus = await _w.OpenAnonymous().Businesses.RegisterAsync(new RegisterBusinessRequest(
            "Peretas", "peretas@uji.test", "Rahasia123", "Usaha Palsu", 1, (MarketRole)99, "Jl. Palsu No. 1", null, null, "081200000099"));
        Assert.False(bogus.Succeeded);

        var db = _w.Open(_w.Admin()).Db;
        Assert.Equal(0, await db.Users.CountAsync(u => u.Role.Name == RoleNames.Admin));
        var roles = await db.Users.Select(u => u.Role.Name).Distinct().ToListAsync();
        Assert.All(roles, r => Assert.Contains(r, new[] { RoleNames.Retail, RoleNames.Grossier, RoleNames.Livestock, RoleNames.Consumer }));

        // Aturan pengikatan: pembeli/admin tanpa usaha; usaha wajib terikat.
        var auth = _w.OpenAnonymous().Auth;
        Assert.False((await auth.CreateUserAsync(new CreateUserRequest("X", "x1@uji.test", "Rahasia123", RoleNames.Consumer, 1))).Succeeded);
        Assert.False((await auth.CreateUserAsync(new CreateUserRequest("X", "x2@uji.test", "Rahasia123", RoleNames.Admin, 1))).Succeeded);
        Assert.False((await auth.CreateUserAsync(new CreateUserRequest("X", "x3@uji.test", "Rahasia123", RoleNames.Grossier, null))).Succeeded);
        Assert.False((await auth.CreateUserAsync(new CreateUserRequest("X", "x4@uji.test", "Rahasia123", "SUPERUSER", null))).Succeeded);
    }

    [Fact]
    public async Task E5_Akun_dan_usaha_nonaktif_tidak_dapat_login_dan_tidak_dapat_dipesan()
    {
        var shop = await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung E5");
        var pid = await _w.CreateRetailProductAsync(shop, "Gula", 10, 14_000m);
        var buyer = await _w.RegisterConsumerAsync();
        var admin = _w.Open(_w.Admin());

        // Usaha dinonaktifkan: pengguna usahanya ditolak masuk dan warung hilang dari katalog/tidak dapat dipesan.
        Assert.True((await admin.Businesses.SetActiveAsync(shop.BusinessId!.Value, false)).Succeeded);
        var credentials = await _w.OpenAnonymous().Auth.ValidateCredentialsAsync(shop.Email, "Rahasia123");
        Assert.True(credentials.Succeeded);
        Assert.NotNull(await _w.OpenAnonymous().BusinessGate.CheckAsync(credentials.Value!)); // gerbang login menolak (pesan alasan)
        Assert.Empty(await _w.Open(buyer).ConsumerCatalog.SearchShopsAsync(null));
        var order = await _w.Open(buyer).ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
            shop.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, null, new[] { new ConsumerLineInput(pid, 1) }, "e5"));
        Assert.False(order.Succeeded);

        // Akun pembeli dinonaktifkan: tidak aktif menurut validator sesi.
        Assert.True(await _w.OpenAnonymous().Auth.IsUserActiveAsync(buyer.UserId));
        Assert.True((await admin.Auth.SetUserActiveAsync(buyer.UserId, false)).Succeeded);
        Assert.False(await _w.OpenAnonymous().Auth.IsUserActiveAsync(buyer.UserId));
    }

    [Fact]
    public void E7_Antiforgery_global_terdaftar_dan_tidak_ada_aksi_yang_mematikannya()
    {
        // Aplikasi memakai filter GLOBAL AutoValidateAntiforgeryToken (Program.cs): seluruh POST/PUT/DELETE divalidasi.
        // Yang harus dijaga: tidak ada controller/aksi yang memakai [IgnoreAntiforgeryToken] untuk mematikannya.
        var web = Quality.OfflineAssetsTests.FindWebRootForTests();
        Assert.NotNull(web);
        Assert.Contains("new AutoValidateAntiforgeryTokenAttribute()", File.ReadAllText(Path.Combine(web!, "Program.cs")));

        var optOut = new List<string>();
        foreach (var controller in typeof(Platform.Web.Data.AppDbContext).Assembly.GetTypes().Where(t => typeof(Microsoft.AspNetCore.Mvc.Controller).IsAssignableFrom(t)))
        {
            if (controller.GetCustomAttributes(true).Any(a => a is Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute))
            {
                optOut.Add(controller.Name);
            }

            foreach (var m in controller.GetMethods().Where(m => m.GetCustomAttributes(true).Any(a => a is Microsoft.AspNetCore.Mvc.IgnoreAntiforgeryTokenAttribute)))
            {
                optOut.Add($"{controller.Name}.{m.Name}");
            }
        }

        // Satu-satunya pengecualian sah (warisan V2): halaman status/galat, yang hanya menampilkan pesan dan harus dapat dieksekusi ulang
        // untuk metode HTTP apa pun setelah galat; tidak mengubah data.
        Assert.True(optOut.Distinct().SequenceEqual(new[] { "HomeController.Status" }), "Aksi yang mematikan antiforgery: " + string.Join(", ", optOut));
    }

    [Fact]
    public async Task E8_Kata_sandi_disimpan_sebagai_hash_dan_tidak_pernah_teks_asli()
    {
        await _w.RegisterBusinessAsync(MarketRole.Retail, "Warung Hash");
        await _w.RegisterConsumerAsync();
        var users = await _w.Open(_w.Admin()).Db.Users.AsNoTracking().ToListAsync();
        Assert.NotEmpty(users);
        Assert.All(users, u =>
        {
            Assert.NotEqual("Rahasia123", u.PasswordHash);
            Assert.DoesNotContain("Rahasia123", u.PasswordHash);
            Assert.True(u.PasswordHash.Length > 40, "hash terlalu pendek");
        });
        Assert.Equal(users.Count, users.Select(u => u.PasswordHash).Distinct().Count()); // salt unik: hash sama-sandi pun berbeda
    }
}
