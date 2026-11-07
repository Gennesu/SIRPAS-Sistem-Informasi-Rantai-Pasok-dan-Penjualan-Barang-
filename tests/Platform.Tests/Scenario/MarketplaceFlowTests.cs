using Platform.Tests.TestSupport;
using Platform.Web.Data;
using Platform.Web.Modules.Admin.Services;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.BusinessProfile.Services;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Scenario;

/// <summary>
/// Alur marketplace end-to-end: grosir mendaftar dan diverifikasi, warung mencari dan memesan,
/// grosir memproses, warung menerima, stok/biaya/laporan ikut berubah.
/// </summary>
public sealed class MarketplaceFlowTests : IDisposable
{
    private sealed record Actor(ModuleServices S, FakeBusinessContext Ctx, int BusinessId, int UserId);

    private readonly TestDb _testDb = new();
    private readonly List<ModuleServices> _opened = new();
    private readonly DateOnly _today = WibTime.TodayWib(TimeProvider.System);

    public void Dispose()
    {
        foreach (var s in _opened)
        {
            s.Dispose();
        }

        _testDb.Dispose();
    }

    private ModuleServices Open(IBusinessContext context)
    {
        var services = new ModuleServices(_testDb.Options, context, TimeProvider.System);
        _opened.Add(services);
        return services;
    }

    private static FakeBusinessContext AdminContext()
    {
        var context = new FakeBusinessContext();
        context.AsAdmin(900);
        return context;
    }

    private async Task<Actor> RegisterAsync(string email, string name, MarketRole role, double lat, double lon)
    {
        var registration = await Open(new FakeBusinessContext()).Businesses.RegisterAsync(new RegisterBusinessRequest(
            "Pemilik " + name, email, "Rahasia123", name, 1, role, "Jl. Uji No. 1, Bandung", lat, lon, "081200000001"));
        Assert.True(registration.Succeeded, registration.Error);

        var businessId = registration.Value!.BusinessId!.Value;
        var context = new FakeBusinessContext();
        if (role == MarketRole.Wholesale)
        {
            context.AsGrossier(businessId, registration.Value.UserId);
        }
        else
        {
            context.AsRetail(businessId, registration.Value.UserId);
        }

        return new Actor(Open(context), context, businessId, registration.Value.UserId);
    }

    private async Task VerifyAsync(Actor business)
    {
        var result = await Open(AdminContext()).Businesses.SetVerificationAsync(business.BusinessId, VerificationStatus.Verified, null);
        Assert.True(result.Succeeded, result.Error);
    }

    // Grosir dengan Mi Instan: 1 Box = 40 Item @ Rp120.000; stok 10 Box (modal Rp105.000 per Box).
    private async Task<(Actor Seller, int ProductId)> CreateWholesalerAsync(
        string email = "grosir@uji.test", double lat = -6.9150, double lon = 107.6050,
        bool offersDelivery = true, decimal minOrder = 50_000m, bool verify = true)
    {
        var seller = await RegisterAsync(email, "Grosir Uji " + email, MarketRole.Wholesale, lat, lon);

        var product = await seller.S.Products.CreateAsync(new SaveProductRequest(
            "Mi Instan", "MIE-001", 1, null, "Item", 3_300m, new[] { new UnitInput("Box", 40, 120_000m) }));
        Assert.True(product.Succeeded, product.Error);

        var stock = await seller.S.Purchasing.RecordAsync(new RecordPurchaseRequest(
            "Distributor", null, null, new[] { new PurchaseLineInput(product.Value!.Id, "Box", 10, 105_000m) }));
        Assert.True(stock.Succeeded, stock.Error);

        var delivery = await seller.S.Delivery.SaveMineAsync(new SaveDeliveryRequest(offersDelivery, true, 10_000m, minOrder, null, null));
        Assert.True(delivery.Succeeded, delivery.Error);

        if (verify)
        {
            await VerifyAsync(seller);
        }

        return (seller, product.Value.Id);
    }

    private Task<Actor> CreateRetailerAsync(string email = "warung@uji.test") =>
        RegisterAsync(email, "Warung Uji " + email, MarketRole.Retail, -6.9175, 107.6191);

    private static Task<Result<int>> OrderAsync(Actor buyer, Actor seller, int productId, int boxes, FulfillmentType type) =>
        buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, type, null, null, new[] { new OrderLineInput(productId, "Box", boxes) }));

    private static async Task<int> PlaceAsync(Actor buyer, Actor seller, int productId, int boxes, FulfillmentType type = FulfillmentType.Pickup)
    {
        var result = await OrderAsync(buyer, seller, productId, boxes, type);
        Assert.True(result.Succeeded, result.Error);
        return result.Value;
    }

    private static async Task ActOkAsync(Actor who, int orderId, OrderAction action, string? reason = null)
    {
        var result = await who.S.Orders.ActAsync(orderId, action, reason);
        Assert.True(result.Succeeded, $"{action}: {result.Error}");
    }

    private static async Task<int> StockOfAsync(Actor business, int productId) =>
        (await business.S.Inventory.GetStockAsync()).Single(s => s.ProductId == productId).QtyBase;

    private static async Task DeliverAsync(Actor seller, Actor buyer, int orderId)
    {
        await ActOkAsync(seller, orderId, OrderAction.Accept);
        await ActOkAsync(seller, orderId, OrderAction.Prepare);
        await ActOkAsync(seller, orderId, OrderAction.MarkReady);
        await ActOkAsync(seller, orderId, OrderAction.HandOver);
    }

    // ------------------------------------------------------------------ skenario penuh

    [Fact]
    public async Task Skenario_spesifikasi_pesan_2_Box_ke_grosir_lalu_jual_5_Item()
    {
        var (seller, sellerProductId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();

        // Warung menemukan grosir terverifikasi beserta jaraknya.
        var card = Assert.Single(await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, null)));
        Assert.True(card.DistanceKm is > 1.5 and < 1.7, card.DistanceKm?.ToString());

        var orderId = await PlaceAsync(buyer, seller, sellerProductId, 2);

        // Grosir melihat pesanan masuk; tindakan yang tersedia mengikuti peran.
        Assert.Equal(1, (await seller.S.Orders.ListAsync(null)).Count);
        var sellerView = (await seller.S.Orders.GetAsync(orderId)).Value!;
        Assert.Equal(OrderStatus.Pending, sellerView.Status);
        Assert.Equal(new[] { OrderAction.Accept, OrderAction.Reject }, sellerView.Actions.ToArray());
        Assert.Equal(new[] { OrderAction.BuyerCancel }, (await buyer.S.Orders.GetAsync(orderId)).Value!.Actions.ToArray());

        // Menerima pesanan = stok grosir berkurang 2 x 40 = 80 Item (dipesan).
        Assert.Equal(400, await StockOfAsync(seller, sellerProductId));
        await DeliverAsync(seller, buyer, orderId);
        Assert.Equal(320, await StockOfAsync(seller, sellerProductId));
        Assert.Equal(OrderStatus.PickedUp, (await buyer.S.Orders.GetAsync(orderId)).Value!.Status);
        Assert.Empty(await buyer.S.Products.ListAsync(null, true)); // belum ada stok di warung sebelum konfirmasi

        // Warung mengonfirmasi: produk dibuat otomatis, stok masuk 80 Item, modal Rp3.000.
        await ActOkAsync(buyer, orderId, OrderAction.ConfirmReceived);
        var product = Assert.Single(await buyer.S.Products.ListAsync(null, true));
        Assert.Equal("Mi Instan", product.Name);
        Assert.Equal(sellerProductId, product.SourceProductId);
        Assert.Contains(product.Units, u => u.UnitName == "Box" && u.ConversionToBase == 40);

        var stock = Assert.Single(await buyer.S.Inventory.GetStockAsync());
        Assert.Equal(80, stock.QtyBase);
        Assert.Equal(3_000m, stock.AvgCostBase);

        var purchase = Assert.Single(await buyer.S.Purchasing.ListAsync());
        Assert.Equal("ORDER", purchase.Source);
        Assert.Equal(240_000m, purchase.TotalAmount);
        Assert.Equal(OrderStatus.Completed, (await seller.S.Orders.GetAsync(orderId)).Value!.Status);

        // Warung menentukan harga jual sendiri lalu menjual 5 Item lewat POS.
        Assert.True((await buyer.S.Inventory.UpdateSettingsAsync(product.Id, 3_500m, null)).Succeeded);
        var sale = await buyer.S.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(product.Id, 5) }, "CASH", null));
        Assert.True(sale.Succeeded, sale.Error);
        Assert.Equal(75, (await buyer.S.Inventory.GetStockAsync()).Single().QtyBase);

        var report = await buyer.S.Reporting.GetSalesReportAsync(new PeriodSelection(PeriodPreset.Today, null, null));
        Assert.Equal(17_500m, report.Revenue);
        Assert.Equal(15_000m, report.Cogs);
        Assert.Equal(2_500m, report.GrossProfit);

        // Jejak stok grosir tercatat.
        var movements = await seller.S.Inventory.GetMovementsAsync(sellerProductId);
        Assert.True(movements.Any(m => m.QtyBaseSigned == -80 && m.TypeLabel.Contains("Pesanan")));
    }

    // ------------------------------------------------------------------ katalog publik

    [Fact]
    public async Task Grosir_hanya_tampil_bila_terverifikasi_dan_aktif()
    {
        var (seller, sellerProductId) = await CreateWholesalerAsync(verify: false);
        var buyer = await CreateRetailerAsync();
        var all = new MarketplaceFilter(null, null, null, null);

        Assert.Empty(await buyer.S.Marketplace.SearchAsync(all));
        Assert.Equal(ErrorKind.NotFound, (await buyer.S.Marketplace.GetWholesalerAsync(seller.BusinessId)).Kind);
        Assert.Equal(ErrorKind.NotFound, (await OrderAsync(buyer, seller, sellerProductId, 1, FulfillmentType.Pickup)).Kind);

        await VerifyAsync(seller);
        Assert.Single(await buyer.S.Marketplace.SearchAsync(all));

        var admin = Open(AdminContext());
        Assert.True((await admin.Businesses.SetActiveAsync(seller.BusinessId, false)).Succeeded);
        Assert.Empty(await buyer.S.Marketplace.SearchAsync(all));
    }

    [Fact]
    public async Task Filter_pencarian_jarak_pengiriman_dan_kata_kunci()
    {
        var (near, _) = await CreateWholesalerAsync("dekat@uji.test", -6.9150, 107.6050, offersDelivery: true);
        var (far, _) = await CreateWholesalerAsync("jauh@uji.test", -6.9500, 107.6400, offersDelivery: false);
        var buyer = await CreateRetailerAsync();

        var all = await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, null));
        Assert.Equal(2, all.Count);
        Assert.Equal(near.BusinessId, all[0].BusinessId); // diurutkan dari yang terdekat

        var within2 = await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, 2.0, null));
        Assert.Equal(near.BusinessId, Assert.Single(within2).BusinessId);

        var delivery = await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter(null, null, null, FulfillmentType.Delivery));
        Assert.Equal(near.BusinessId, Assert.Single(delivery).BusinessId);

        Assert.Equal(2, (await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter("mi inst", null, null, null))).Count);
        Assert.Empty(await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter("sepatu", null, null, null)));
        Assert.Equal(2, (await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter(null, 1, null, null))).Count);
        Assert.Empty(await buyer.S.Marketplace.SearchAsync(new MarketplaceFilter(null, 4, null, null)));
        Assert.Equal(2, (await buyer.S.Marketplace.GetMapPointsAsync()).Count);
        Assert.NotEqual(far.BusinessId, near.BusinessId);
    }

    // ------------------------------------------------------------------ validasi pesanan

    [Fact]
    public async Task Pesanan_di_bawah_minimum_ditolak()
    {
        var (seller, productId) = await CreateWholesalerAsync(minOrder: 150_000m);
        var buyer = await CreateRetailerAsync();

        var result = await OrderAsync(buyer, seller, productId, 1, FulfillmentType.Pickup); // Rp120.000
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
        Assert.Contains("Minimal pesanan", result.Error);
        Assert.Empty(await buyer.S.Orders.ListAsync(null));
    }

    [Fact]
    public async Task Pesanan_melebihi_stok_grosir_ditolak()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();

        var result = await OrderAsync(buyer, seller, productId, 11, FulfillmentType.Pickup); // stok hanya 10 Box
        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Conflict, result.Kind);
        Assert.Contains("tidak cukup", result.Error);
    }

    [Fact]
    public async Task Cara_pengambilan_yang_tidak_disediakan_grosir_ditolak()
    {
        var (seller, productId) = await CreateWholesalerAsync(offersDelivery: false);
        var buyer = await CreateRetailerAsync();

        var delivery = await OrderAsync(buyer, seller, productId, 2, FulfillmentType.Delivery);
        Assert.False(delivery.Succeeded);
        Assert.Equal(ErrorKind.Validation, delivery.Kind);
        Assert.True((await OrderAsync(buyer, seller, productId, 2, FulfillmentType.Pickup)).Succeeded);
    }

    // Pesanan ke usaha sendiri ditolak; grosir tidak dapat memakai katalog pesanan yang tersedia untuk warung.
    [Fact]
    public async Task Tidak_ada_usaha_yang_memesan_ke_dirinya_sendiri_dan_grosir_tidak_memesan_ke_grosir()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var (otherWholesaler, _) = await CreateWholesalerAsync("grosir2@uji.test", -6.95, 107.64);
        var retailer = await CreateRetailerAsync();

        Assert.Equal(ErrorKind.Validation, (await OrderAsync(seller, seller, productId, 1, FulfillmentType.Pickup)).Kind);
        Assert.Equal(ErrorKind.Validation, (await OrderAsync(retailer, retailer, productId, 1, FulfillmentType.Pickup)).Kind);
        Assert.Equal(ErrorKind.NotFound, (await OrderAsync(otherWholesaler, seller, productId, 1, FulfillmentType.Pickup)).Kind);
    }

    // ------------------------------------------------------------------ status dan stok

    [Fact]
    public async Task Tolak_pesanan_wajib_beralasan_dan_stok_tidak_berubah()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();
        var orderId = await PlaceAsync(buyer, seller, productId, 2);

        var withoutReason = await seller.S.Orders.ActAsync(orderId, OrderAction.Reject, null);
        Assert.Equal(ErrorKind.Validation, withoutReason.Kind);

        await ActOkAsync(seller, orderId, OrderAction.Reject, "Stok sedang opname");
        var detail = (await buyer.S.Orders.GetAsync(orderId)).Value!;
        Assert.Equal(OrderStatus.Rejected, detail.Status);
        Assert.Equal("Stok sedang opname", detail.RejectReason);
        Assert.Equal(400, await StockOfAsync(seller, productId));
        Assert.Equal(ErrorKind.Conflict, (await seller.S.Orders.ActAsync(orderId, OrderAction.Accept, null)).Kind);
    }

    [Fact]
    public async Task Grosir_membatalkan_setelah_diterima_mengembalikan_stok_dan_modal()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();
        var orderId = await PlaceAsync(buyer, seller, productId, 2);
        var costBefore = (await seller.S.Inventory.GetStockAsync()).Single().AvgCostBase;

        await ActOkAsync(seller, orderId, OrderAction.Accept);
        Assert.Equal(320, await StockOfAsync(seller, productId));

        await ActOkAsync(seller, orderId, OrderAction.SellerCancel, "Barang rusak di gudang");
        Assert.Equal(400, await StockOfAsync(seller, productId));
        Assert.Equal(costBefore, (await seller.S.Inventory.GetStockAsync()).Single().AvgCostBase);
        Assert.Equal(OrderStatus.Cancelled, (await buyer.S.Orders.GetAsync(orderId)).Value!.Status);
    }

    [Fact]
    public async Task Pembeli_hanya_boleh_batal_saat_menunggu_dan_aksi_oleh_pihak_yang_salah_ditolak()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();
        var orderId = await PlaceAsync(buyer, seller, productId, 2);

        Assert.Equal(ErrorKind.Forbidden, (await buyer.S.Orders.ActAsync(orderId, OrderAction.Accept, null)).Kind);
        Assert.Equal(ErrorKind.Conflict, (await seller.S.Orders.ActAsync(orderId, OrderAction.ConfirmReceived, null)).Kind);

        await DeliverAsync(seller, buyer, orderId);
        Assert.Equal(ErrorKind.Forbidden, (await seller.S.Orders.ActAsync(orderId, OrderAction.ConfirmReceived, null)).Kind);
        Assert.Equal(ErrorKind.Conflict, (await buyer.S.Orders.ActAsync(orderId, OrderAction.BuyerCancel, null)).Kind);
    }

    // PERUBAHAN DESAIN V3 (lihat docs/V3-PENGUJIAN.md): stok DICADANGKAN saat pesanan dibuat, bukan baru saat diterima.
    // Di V2 kedua pesanan 8 Box (stok 10 Box) sama-sama bisa dibuat dan baru yang kedua gagal saat diterima. Di V3 pesanan
    // kedua sudah ditolak saat dibuat karena 8 Box pertama ditahan. Tujuan test yang sama (gabungan melebihi stok tidak lolos)
    // tetap diperiksa, dengan asersi yang lebih ketat.
    [Fact]
    public async Task Dua_pesanan_melebihi_stok_gabungan_pesanan_kedua_ditolak_saat_dibuat_dan_bisa_setelah_dilepas()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();
        var first = await PlaceAsync(buyer, seller, productId, 8);

        var secondAttempt = await OrderAsync(buyer, seller, productId, 8, FulfillmentType.Pickup); // 8 Box ditahan -> tersedia 2 Box
        Assert.False(secondAttempt.Succeeded);
        Assert.Equal(ErrorKind.Conflict, secondAttempt.Kind);
        Assert.Contains("tidak cukup", secondAttempt.Error!);
        Assert.Single(await buyer.S.Orders.ListAsync(null)); // pesanan kedua tidak tersimpan sama sekali
        Assert.Equal(400, await StockOfAsync(seller, productId)); // stok fisik (10 Box x 40 Item) belum berubah sebelum pesanan diterima

        await ActOkAsync(seller, first, OrderAction.Accept);
        Assert.Equal(80, await StockOfAsync(seller, productId)); // 10 - 8 = 2 Box (80 Item)

        await ActOkAsync(seller, first, OrderAction.SellerCancel, "Barang rusak");
        Assert.Equal(400, await StockOfAsync(seller, productId));
        Assert.True((await OrderAsync(buyer, seller, productId, 8, FulfillmentType.Pickup)).Succeeded);
    }

    // ------------------------------------------------------------------ isolasi

    [Fact]
    public async Task Usaha_lain_tidak_dapat_melihat_atau_memproses_pesanan()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var (otherSeller, _) = await CreateWholesalerAsync("grosir2@uji.test", -6.95, 107.64);
        var buyer = await CreateRetailerAsync();
        var otherBuyer = await CreateRetailerAsync("warung2@uji.test");
        var orderId = await PlaceAsync(buyer, seller, productId, 2);

        foreach (var outsider in new[] { otherBuyer, otherSeller })
        {
            Assert.Equal(ErrorKind.NotFound, (await outsider.S.Orders.GetAsync(orderId)).Kind);
            Assert.Empty(await outsider.S.Orders.ListAsync(null));
            Assert.Equal(ErrorKind.NotFound, (await outsider.S.Orders.ActAsync(orderId, OrderAction.Accept, null)).Kind);
        }

        // Admin boleh melihat seluruh transaksi.
        Assert.Single(await Open(AdminContext()).Orders.ListAsync(null));
    }

    // ------------------------------------------------------------------ penerimaan: ongkir dan produk

    [Fact]
    public async Task Ongkir_pengiriman_dicatat_sebagai_pengeluaran_transportasi_saat_selesai()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();
        var orderId = await PlaceAsync(buyer, seller, productId, 2, FulfillmentType.Delivery);

        var detail = (await buyer.S.Orders.GetAsync(orderId)).Value!;
        Assert.Equal(240_000m, detail.Subtotal);
        Assert.Equal(10_000m, detail.DeliveryFee);
        Assert.Equal(250_000m, detail.TotalAmount);

        await DeliverAsync(seller, buyer, orderId);
        Assert.Equal(OrderStatus.Delivered, (await buyer.S.Orders.GetAsync(orderId)).Value!.Status);
        await ActOkAsync(buyer, orderId, OrderAction.ConfirmReceived);

        var expense = Assert.Single(await buyer.S.Expenses.ListAsync(_today.AddDays(-1), _today.AddDays(1)));
        Assert.Equal("Transportasi", expense.Category);
        Assert.Equal(10_000m, expense.Amount);
        Assert.Contains(detail.Number, expense.Note);
    }

    [Fact]
    public async Task Produk_warung_yang_sudah_ada_dihubungkan_lewat_SKU_bukan_digandakan()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();
        var existing = await buyer.S.Products.CreateAsync(new SaveProductRequest(
            "Mi Instan Goreng Warung", "MIE-001", 1, null, "Item", null, new[] { new UnitInput("Box", 40, null) }));
        Assert.True(existing.Succeeded, existing.Error);

        var orderId = await PlaceAsync(buyer, seller, productId, 2);
        await DeliverAsync(seller, buyer, orderId);
        await ActOkAsync(buyer, orderId, OrderAction.ConfirmReceived);

        var product = Assert.Single(await buyer.S.Products.ListAsync(null, true));
        Assert.Equal(existing.Value!.Id, product.Id);
        Assert.Equal(productId, product.SourceProductId);
        Assert.Equal(80, await StockOfAsync(buyer, product.Id));
    }

    // ------------------------------------------------------------------ admin

    [Fact]
    public async Task Admin_menonaktifkan_usaha_dan_pengguna_menutup_akses_login()
    {
        var (seller, _) = await CreateWholesalerAsync();
        var admin = Open(AdminContext());
        var gate = new BusinessLoginGate(seller.S.Db);
        var user = new AuthenticatedUser(seller.UserId, "Pemilik", "grosir@uji.test", RoleNames.Grossier, seller.BusinessId);

        Assert.Null(await gate.CheckAsync(user));
        Assert.True(await admin.Auth.IsUserActiveAsync(seller.UserId));

        Assert.True((await admin.Businesses.SetActiveAsync(seller.BusinessId, false)).Succeeded);
        Assert.NotNull(await gate.CheckAsync(user));
        Assert.True((await admin.Businesses.SetActiveAsync(seller.BusinessId, true)).Succeeded);
        Assert.Null(await gate.CheckAsync(user));

        Assert.True((await admin.Auth.SetUserActiveAsync(seller.UserId, false)).Succeeded);
        Assert.False(await admin.Auth.IsUserActiveAsync(seller.UserId));

        // Batasan: admin tidak menonaktifkan dirinya sendiri; non-admin tidak punya akses.
        Assert.Equal(ErrorKind.Validation, (await admin.Auth.SetUserActiveAsync(900, false)).Kind);
        Assert.Equal(ErrorKind.Forbidden, (await seller.S.Auth.SetUserActiveAsync(seller.UserId, true)).Kind);
        Assert.Equal(ErrorKind.Forbidden, (await seller.S.Businesses.SetVerificationAsync(seller.BusinessId, VerificationStatus.Rejected, null)).Kind);
    }

    [Fact]
    public async Task Ringkasan_admin_dan_pembatasan_akses_serta_kategori()
    {
        var (seller, productId) = await CreateWholesalerAsync();
        var buyer = await CreateRetailerAsync();
        await PlaceAsync(buyer, seller, productId, 2);
        var admin = Open(AdminContext());

        var dashboard = await new AdminService(admin.Db, AdminContext()).GetDashboardAsync();
        Assert.Equal(1, dashboard.Wholesalers);
        Assert.Equal(1, dashboard.Retailers);
        Assert.Equal(1, dashboard.PendingVerification); // warung belum diverifikasi
        Assert.Equal(1, dashboard.OrdersTotal);
        Assert.Equal(1, dashboard.OrdersOpen);

        var outsider = new AdminService(seller.S.Db, seller.Ctx);
        Assert.Empty(await outsider.ListUsersAsync());
        Assert.Equal(0, (await outsider.GetDashboardAsync()).Users);

        Assert.True((await admin.Products.AddCategoryAsync("Perlengkapan Bayi")).Succeeded);
        Assert.Equal(ErrorKind.Conflict, (await admin.Products.AddCategoryAsync("perlengkapan bayi")).Kind);
        Assert.Equal(ErrorKind.Forbidden, (await seller.S.Products.AddCategoryAsync("Lain-lain")).Kind);
        Assert.True((await admin.Products.RenameCategoryAsync(6, "Aneka Barang")).Succeeded);
        Assert.Contains("Aneka Barang", (await admin.Products.GetCategoriesAsync()).Select(c => c.Name));
    }
}
