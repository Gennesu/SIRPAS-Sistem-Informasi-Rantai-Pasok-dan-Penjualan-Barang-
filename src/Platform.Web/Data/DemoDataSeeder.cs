using Microsoft.EntityFrameworkCore;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Data;

/// <summary>
/// Data DUMMY untuk demo kuliah. Semua dibuat lewat layanan modul yang sama dengan aplikasi
/// (bukan insert langsung), sehingga stok, modal, pesanan, dan laporan konsisten. Termasuk stok awal warung:
/// diperoleh dari PESANAN marketplace sungguhan ke grosir (dibuat, diterima, disiapkan, diambil, dikonfirmasi).
/// Riwayat penjualan dibuat untuk 7 hari lalu; HARI INI sengaja kosong agar skenario demo langsung
/// (beli 2 Box Mi Instan dari grosir, jual 5 Item) menghasilkan angka bersih: omzet Rp17.500, untung Rp2.500.
/// </summary>
public static partial class DemoDataSeeder
{
    private const int GroceryTypeId = 1;

    private sealed record Pack(string Unit, int Conversion, decimal WholesalePrice);

    private sealed record CatalogItem(
        string Name,
        int CategoryId,
        string Sku,
        decimal BaseWholesalePrice,
        Pack[] Packs);

    private sealed record RetailItem(
        string Name,
        int CategoryId,
        string Sku,
        string PackUnit,
        int PackConversion,
        decimal SellingPrice,
        int MinStock,
        int PurchasePacks,
        decimal PurchasePricePerPack);

    private static readonly CatalogItem[] WholesaleCatalog =
    {
        new("Mi Instan Goreng", 1, "MIE-001", 3_300m,
            new[] { new Pack("Box", 40, 120_000m), new Pack("Dus", 160, 460_000m) }),

        new("Minyak Goreng 1L", 2, "MYK-001", 15_500m,
            new[] { new Pack("Dus", 12, 180_000m) }),

        new("Gula Pasir 1kg", 4, "GLA-001", 14_500m,
            new[] { new Pack("Pak", 10, 140_000m) }),

        new("Beras Premium 5kg", 4, "BRS-001", 72_000m,
            new[] { new Pack("Dus", 4, 280_000m) }),

        new("Kopi Sachet", 3, "KPI-001", 1_600m,
            new[] { new Pack("Renceng", 10, 15_000m), new Pack("Box", 100, 145_000m) }),

        new("Teh Botol 350ml", 3, "TEH-001", 4_500m,
            new[] { new Pack("Krat", 24, 105_000m) }),

        new("Sabun Cuci Piring 800ml", 5, "SBN-001", 17_000m,
            new[] { new Pack("Dus", 12, 195_000m) })
    };

    // Stok awal grosir dibeli dari distributor pusat dengan harga lebih murah dari harga grosir.
    private static readonly (string Name, string Unit, int Qty, decimal Price)[] WholesaleStock =
    {
        ("Mi Instan Goreng", "Box", 10, 105_000m),
        ("Minyak Goreng 1L", "Dus", 20, 168_000m),
        ("Gula Pasir 1kg", "Pak", 15, 130_000m),
        ("Beras Premium 5kg", "Dus", 10, 265_000m),
        ("Kopi Sachet", "Box", 20, 135_000m),
        ("Teh Botol 350ml", "Krat", 15, 96_000m),
        ("Sabun Cuci Piring 800ml", "Dus", 10, 180_000m)
    };

    private static readonly RetailItem[] RetailCatalog =
    {
        // Mi instan sengaja tanpa stok untuk skenario pembelian langsung dari grosir.
        new("Mi Instan Goreng", 1, "MIE-001", "Box", 40, 3_500m, 20, 0, 0m),
        new("Minyak Goreng 1L", 2, "MYK-001", "Dus", 12, 18_000m, 6, 6, 180_000m),
        new("Gula Pasir 1kg", 4, "GLA-001", "Pak", 10, 16_000m, 8, 8, 140_000m),
        new("Beras Premium 5kg", 4, "BRS-001", "Dus", 4, 78_000m, 4, 5, 280_000m),
        new("Kopi Sachet", 3, "KPI-001", "Renceng", 10, 2_000m, 60, 12, 15_000m),
        new("Teh Botol 350ml", 3, "TEH-001", "Krat", 24, 5_500m, 12, 6, 105_000m),
        new("Sabun Cuci Piring 800ml", 5, "SBN-001", "Dus", 12, 20_000m, 6, 4, 195_000m)
    };

    private static readonly OrderAction[] FullFlow =
    {
        OrderAction.Accept,
        OrderAction.Prepare,
        OrderAction.MarkReady,
        OrderAction.HandOver,
        OrderAction.ConfirmReceived
    };

    public static async Task SeedAsync(IServiceProvider sp, ILogger logger)
    {
        var options = sp.GetRequiredService<DbContextOptions<AppDbContext>>();
        var clock = new SettableTimeProvider(DateTimeOffset.UtcNow);
        var today = WibTime.TodayWib(TimeProvider.System);

        AuthenticatedUser grosir1;
        AuthenticatedUser grosir2;
        AuthenticatedUser warung1;
        AuthenticatedUser warung2;

        using (var root = new ModuleServices(
            options,
            FixedBusinessContext.Anonymous(),
            clock))
        {
            if (await root.Db.Users.AnyAsync(u => u.Email == "warung@demo.test"))
            {
                logger.LogInformation("Data demo sudah ada; seeder dilewati.");
                return;
            }

            logger.LogInformation("Mengisi data dummy untuk demo...");

            await OkAsync(root.Auth.CreateUserAsync(
                new CreateUserRequest(
                    "Administrator Demo",
                    "admin@demo.test",
                    DemoAccounts.Password,
                    RoleNames.Admin,
                    null)));

            grosir1 = await RegisterAsync(
                root,
                "Pak Hendra",
                "grosir@demo.test",
                "Grosir Sumber Rejeki",
                MarketRole.Wholesale,
                "Jl. Pasar Baru No. 12, Bandung",
                -6.9150,
                107.6050,
                "0222001001");

            grosir2 = await RegisterAsync(
                root,
                "Bu Dewi",
                "grosir2@demo.test",
                "Grosir Makmur Jaya",
                MarketRole.Wholesale,
                "Jl. Soekarno-Hatta No. 88, Bandung",
                -6.9500,
                107.6400,
                "0222002002");

            warung1 = await RegisterAsync(
                root,
                "Bu Siti",
                "warung@demo.test",
                "Warung Maju",
                MarketRole.Retail,
                "Jl. Mawar No. 5, Bandung",
                -6.9175,
                107.6191,
                "081234567890");

            warung2 = await RegisterAsync(
                root,
                "Pak Joko",
                "warung2@demo.test",
                "Warung Berkah",
                MarketRole.Retail,
                "Jl. Melati No. 7, Bandung",
                -6.9040,
                107.6100,
                "081298765432");

            // Diverifikasi admin, kecuali Warung Berkah yang dibiarkan menunggu.
            var verified = new[]
            {
                grosir1.BusinessId!.Value,
                grosir2.BusinessId!.Value,
                warung1.BusinessId!.Value
            };

            foreach (var business in await root.Db.Businesses
                         .Where(b => verified.Contains(b.Id))
                         .ToListAsync())
            {
                business.VerificationStatus = VerificationStatus.Verified;
            }

            await root.Db.SaveChangesAsync();
        }

        await SeedWholesalerAsync(
            options,
            clock,
            grosir1,
            WholesaleCatalog,
            WholesaleStock,
            new SaveDeliveryRequest(
                true,
                true,
                15_000m,
                100_000m,
                "Pengiriman area Bandung, 1 hari kerja",
                "Ambil di toko, 08.00–17.00"),
            today);

        var catalog2 = WholesaleCatalog
            .Where(c => c.Name is
                "Mi Instan Goreng" or "Minyak Goreng 1L" or "Gula Pasir 1kg")
            .Select(c => c with
            {
                Packs = c.Packs.Select(p => p with
                {
                    WholesalePrice = p.WholesalePrice - 2_000m
                }).ToArray()
            })
            .ToArray();

        var stock2 = WholesaleStock
            .Where(s => s.Name is
                "Mi Instan Goreng" or "Minyak Goreng 1L" or "Gula Pasir 1kg")
            .ToArray();

        await SeedWholesalerAsync(
            options,
            clock,
            grosir2,
            catalog2,
            stock2,
            new SaveDeliveryRequest(
                false,
                true,
                0m,
                50_000m,
                null,
                "Ambil di gudang, 09.00–16.00"),
            today);

        await SeedRetailerAsync(
            options,
            clock,
            warung1,
            RetailCatalog,
            today,
            grosir1,
            grosir2);

        await SeedRetailerAsync(
            options,
            clock,
            warung2,
            RetailCatalog
                .Where(r => r.Name is "Minyak Goreng 1L" or "Gula Pasir 1kg")
                .Select(r => r with { PurchasePacks = 1 })
                .ToArray(),
            today,
            null,
            null);

        await SeedFoodWasteAsync(options, clock, today, grosir1, warung1);

        clock.Set(DateTimeOffset.UtcNow);

        logger.LogInformation(
            "Data dummy selesai. Login: warung@demo.test / grosir@demo.test / admin@demo.test (password {Password}).",
            DemoAccounts.Password);
    }

    // ---------------------------------------------------------------
    // Data grosir
    // ---------------------------------------------------------------

    private static async Task SeedWholesalerAsync(
        DbContextOptions<AppDbContext> options,
        SettableTimeProvider clock,
        AuthenticatedUser owner,
        CatalogItem[] catalog,
        (string Name, string Unit, int Qty, decimal Price)[] stock,
        SaveDeliveryRequest delivery,
        DateOnly today)
    {
        using var s = new ModuleServices(
            options,
            FixedBusinessContext.ForGrossier(owner.BusinessId!.Value, owner.UserId),
            clock);

        clock.Set(WibAt(today.AddDays(-10), 9, 0));

        foreach (var item in catalog)
        {
            await OkAsync(s.Products.CreateAsync(
                new SaveProductRequest(
                    item.Name,
                    item.Sku,
                    item.CategoryId,
                    null,
                    "Item",
                    item.BaseWholesalePrice,
                    item.Packs.Select(p =>
                        new UnitInput(p.Unit, p.Conversion, p.WholesalePrice)).ToList())));
        }

        var products = (await s.Products.ListAsync(null, false))
            .ToDictionary(p => p.Name);

        var lines = stock
            .Select(x => new PurchaseLineInput(
                products[x.Name].Id,
                x.Unit,
                x.Qty,
                x.Price))
            .ToList();

        await OkAsync(s.Purchasing.RecordAsync(
            new RecordPurchaseRequest("Distributor Pusat", null, null, lines)));

        await OkAsync(s.Delivery.SaveMineAsync(delivery));
    }

    // ---------------------------------------------------------------
    // Data warung
    // ---------------------------------------------------------------

    private static async Task SeedRetailerAsync(
        DbContextOptions<AppDbContext> options,
        SettableTimeProvider clock,
        AuthenticatedUser owner,
        RetailItem[] items,
        DateOnly today,
        AuthenticatedUser? supplier,
        AuthenticatedUser? secondSupplier)
    {
        using var s = new ModuleServices(
            options,
            FixedBusinessContext.ForRetail(owner.BusinessId!.Value, owner.UserId),
            clock);

        clock.Set(WibAt(today.AddDays(-8), 8, 0));

        foreach (var item in items)
        {
            await OkAsync(s.Products.CreateAsync(
                new SaveProductRequest(
                    item.Name,
                    item.Sku,
                    item.CategoryId,
                    null,
                    "Item",
                    null,
                    new[]
                    {
                        new UnitInput(item.PackUnit, item.PackConversion, null)
                    })));
        }

        var products = (await s.Products.ListAsync(null, false))
            .ToDictionary(p => p.Name);

        foreach (var item in items)
        {
            await OkAsync(s.Inventory.UpdateSettingsAsync(
                products[item.Name].Id,
                item.SellingPrice,
                item.MinStock));
        }

        var stocked = items
            .Where(i => i.PurchasePacks > 0)
            .ToList();

        if (supplier is null)
        {
            // Warung kecil tanpa riwayat: stok awal dicatat manual.
            var manual = stocked
                .Select(i => new PurchaseLineInput(
                    products[i.Name].Id,
                    i.PackUnit,
                    i.PurchasePacks,
                    i.PurchasePricePerPack))
                .ToList();

            await OkAsync(s.Purchasing.RecordAsync(
                new RecordPurchaseRequest(
                    "Grosir Sumber Rejeki",
                    null,
                    null,
                    manual)));

            return;
        }

        // Stok awal: pesanan marketplace ke Grosir Sumber Rejeki.
        await TradeAsync(
            options,
            clock,
            owner,
            supplier,
            today.AddDays(-8),
            9,
            FulfillmentType.Pickup,
            stocked.Select(i => (i.Name, i.PackUnit, i.PurchasePacks)).ToArray(),
            FullFlow);

        // Grosir mengubah harga minyak dari Rp180.000 menjadi Rp186.000 per dus.
        clock.Set(WibAt(today.AddDays(-4), 10, 0));

        using (var seller = new ModuleServices(
            options,
            FixedBusinessContext.ForGrossier(
                supplier.BusinessId!.Value,
                supplier.UserId),
            clock))
        {
            var minyak = (await seller.Products.ListAsync("Minyak", false))
                .Single();

            await OkAsync(seller.Products.UpdateAsync(
                minyak.Id,
                new SaveProductRequest(
                    minyak.Name,
                    minyak.Sku,
                    minyak.CategoryId,
                    minyak.Description,
                    minyak.BaseUnitName,
                    minyak.Units.First(u => u.IsBase).WholesalePrice,
                    minyak.Units
                        .Where(u => !u.IsBase)
                        .Select(u => new UnitInput(
                            u.UnitName,
                            u.ConversionToBase,
                            u.UnitName == "Dus"
                                ? 186_000m
                                : u.WholesalePrice))
                        .ToList())));
        }

        await TradeAsync(
            options,
            clock,
            owner,
            supplier,
            today.AddDays(-3),
            10,
            FulfillmentType.Delivery,
            new[] { ("Minyak Goreng 1L", "Dus", 3) },
            FullFlow);

        // Riwayat penjualan tujuh hari terakhir.
        // Hari ini sengaja dikosongkan untuk skenario demo langsung.
        var rng = new Random(2026);

        var remaining = items.ToDictionary(
            i => i.Name,
            i => i.PurchasePacks * i.PackConversion);

        remaining["Minyak Goreng 1L"] += 36;

        var sellable = items
            .Where(i => i.PurchasePacks > 0)
            .ToList();

        for (var offset = 7; offset >= 1; offset--)
        {
            var day = today.AddDays(-offset);
            var transactions = 5 + rng.Next(0, 5);

            for (var n = 0; n < transactions; n++)
            {
                clock.Set(WibAt(
                    day,
                    7 + rng.Next(0, 13),
                    rng.Next(0, 60)));

                var lines = new List<SaleLineInput>();

                foreach (var pick in sellable
                             .OrderBy(_ => rng.Next())
                             .Take(1 + rng.Next(0, 3)))
                {
                    var qty = 1 + rng.Next(0, MaxQty(pick.Name));

                    if (remaining[pick.Name] >= qty)
                    {
                        lines.Add(new SaleLineInput(
                            products[pick.Name].Id,
                            qty));

                        remaining[pick.Name] -= qty;
                    }
                }

                if (lines.Count == 0)
                {
                    continue;
                }

                var method = rng.Next(10) < 7
                    ? "CASH"
                    : (rng.Next(2) == 0 ? "QRIS" : "TRANSFER");

                // PERBAIKAN:
                // Gunakan ModuleServices dan DbContext baru setiap transaksi.
                // TradeAsync di atas memakai DbContext lain dan dapat mengubah stok.
                // Dengan konteks baru, transaksi tidak memakai entitas Inventory lama
                // yang masih tersimpan di change tracker konteks sebelumnya.
                using var saleServices = new ModuleServices(
                    options,
                    FixedBusinessContext.ForRetail(
                        owner.BusinessId!.Value,
                        owner.UserId),
                    clock);

                // Pastikan kegagalan penjualan langsung dilaporkan,
                // jangan diteruskan sampai operasi berikutnya.
                await OkAsync(
                    saleServices.Sales.ConfirmAsync(
                        new ConfirmSaleRequest(lines, method, null)));
            }
        }

        // Pengeluaran operasional agar keuntungan setelah biaya terlihat realistis.
        clock.Set(WibAt(today.AddDays(-1), 18, 0));

        await OkAsync(s.Expenses.AddAsync(
            new AddExpenseRequest(
                "Listrik",
                60_000m,
                today.AddDays(-6),
                "Token listrik")));

        await OkAsync(s.Expenses.AddAsync(
            new AddExpenseRequest(
                "Transportasi",
                30_000m,
                today.AddDays(-5),
                "Ongkos angkut belanja grosir")));

        await OkAsync(s.Expenses.AddAsync(
            new AddExpenseRequest(
                "Air",
                25_000m,
                today.AddDays(-3),
                null)));

        await OkAsync(s.Expenses.AddAsync(
            new AddExpenseRequest(
                "Biaya operasional",
                15_000m,
                today.AddDays(-1),
                "Plastik dan kantong belanja")));

        // Pesanan yang masih berjalan:
        // 1. Grosir Sumber Rejeki: menunggu persetujuan (PENDING).
        // 2. Grosir Makmur Jaya: menunggu konfirmasi warung (PICKED_UP).
        await TradeAsync(
            options,
            clock,
            owner,
            supplier,
            today.AddDays(-1),
            17,
            FulfillmentType.Delivery,
            new[] { ("Teh Botol 350ml", "Krat", 2) });

        if (secondSupplier is not null)
        {
            await TradeAsync(
                options,
                clock,
                owner,
                secondSupplier,
                today.AddDays(-1),
                9,
                FulfillmentType.Pickup,
                new[] { ("Gula Pasir 1kg", "Pak", 2) },
                OrderAction.Accept,
                OrderAction.Prepare,
                OrderAction.MarkReady,
                OrderAction.HandOver);
        }
    }


    // ---------------------------------------------------------------
    // Food Waste: batch kadaluarsa, penjualan antar-warung, paket, dan penawaran
    // ---------------------------------------------------------------

    private sealed record FoodItem(
        string Name, int CategoryId, string Sku, string BaseUnit, decimal BaseWholesalePrice,
        string PackUnit, int PackConversion, decimal PackWholesalePrice);

    private static readonly FoodItem[] FoodCatalog =
    {
        new("Susu UHT Cokelat 200ml", 3, "SUS-001", "Kotak", 4_200m, "Dus", 24, 96_000m),
        new("Yogurt Drink 150ml", 3, "YOG-001", "Botol", 2_800m, "Krat", 24, 63_000m),
        new("Biskuit Kelapa 300g", 1, "BSK-001", "Bungkus", 8_000m, "Dus", 20, 150_000m),
        new("Keripik Singkong Pedas", 1, "KRP-001", "Bungkus", 3_300m, "Box", 30, 90_000m),
        new("Wafer Cokelat 150g", 1, "WFR-001", "Bungkus", 7_500m, "Dus", 12, 84_000m)
    };

    // Skenario demo: susu laris di Warung Maju (80) dan Toko Sejahtera (60). Grosir punya 2 batch susu yang mendekati
    // kadaluarsa (20 hari dan 6 hari) sehingga dashboard Food Waste langsung merekomendasikan penawaran ke dua warung itu.
    private static async Task SeedFoodWasteAsync(
        DbContextOptions<AppDbContext> options,
        SettableTimeProvider clock,
        DateOnly today,
        AuthenticatedUser grosir,
        AuthenticatedUser warungMaju)
    {
        AuthenticatedUser sejahtera;
        AuthenticatedUser bina;
        AuthenticatedUser dedi;
        AuthenticatedUser ujung;

        using (var root = new ModuleServices(options, FixedBusinessContext.Anonymous(), clock))
        {
            sejahtera = await RegisterAsync(root, "Bu Ratna", "warung3@demo.test", "Toko Sejahtera", MarketRole.Retail,
                "Jl. Cihampelas No. 21, Bandung", -6.9300, 107.6250, "081311112222");
            bina = await RegisterAsync(root, "Bu Rina", "warung4@demo.test", "Kedai Bu Rina", MarketRole.Retail,
                "Jl. Setiabudi No. 40, Bandung", -6.8900, 107.5950, "081322223333");
            dedi = await RegisterAsync(root, "Pak Dedi", "warung5@demo.test", "Warung Pak Dedi", MarketRole.Retail,
                "Jl. Kiaracondong No. 15, Bandung", -6.9650, 107.6600, "081333334444");
            ujung = await RegisterAsync(root, "Pak Asep", "warung6@demo.test", "Toko Ujung Berung", MarketRole.Retail,
                "Jl. Raya Ujungberung No. 3, Bandung", -6.9167, 107.7000, "081344445555");

            var ids = new[] { sejahtera, bina, dedi, ujung }.Select(u => u.BusinessId!.Value).ToList();
            foreach (var business in await root.Db.Businesses.Where(b => ids.Contains(b.Id)).ToListAsync())
            {
                business.VerificationStatus = VerificationStatus.Verified;
            }

            await root.Db.SaveChangesAsync();
        }

        // 1) Grosir: katalog baru, stok reguler (kadaluarsa jauh), dan batch yang sudah lewat tanggal.
        using (var g = new ModuleServices(options, FixedBusinessContext.ForGrossier(grosir.BusinessId!.Value, grosir.UserId), clock))
        {
            clock.Set(WibAt(today.AddDays(-28), 9, 0));
            foreach (var item in FoodCatalog)
            {
                await OkAsync(g.Products.CreateAsync(new SaveProductRequest(
                    item.Name, item.Sku, item.CategoryId, null, item.BaseUnit, item.BaseWholesalePrice,
                    new[] { new UnitInput(item.PackUnit, item.PackConversion, item.PackWholesalePrice) })));
            }

            var products = (await g.Products.ListAsync(null, false)).ToDictionary(p => p.Name);
            await OkAsync(g.Purchasing.RecordAsync(new RecordPurchaseRequest("Distributor Pusat", null, null, new[]
            {
                new PurchaseLineInput(products["Susu UHT Cokelat 200ml"].Id, "Dus", 20, 70_000m, today.AddDays(120)),
                new PurchaseLineInput(products["Wafer Cokelat 150g"].Id, "Dus", 2, 62_000m, today.AddDays(-2))
            })));

            // Ongkir mengikuti jarak: Rp8.000 untuk 2 km pertama, lalu Rp2.500 per km, maksimal 12 km jalan.
            await OkAsync(g.Delivery.SaveMineAsync(new SaveDeliveryRequest(
                true, true, 8_000m, 100_000m,
                "Pengiriman area Bandung, tarif menyesuaikan jarak", "Ambil di toko, 08.00–17.00",
                2_500m, 2m, 12m)));
        }

        // 2) Warung membeli susu dari grosir lalu menjualnya: inilah "data penjualan" yang dibaca grosir.
        await TradeAsync(options, clock, warungMaju, grosir, today.AddDays(-26), 9, FulfillmentType.Pickup,
            new[] { ("Susu UHT Cokelat 200ml", "Dus", 4) }, FullFlow);
        await TradeAsync(options, clock, sejahtera, grosir, today.AddDays(-24), 9, FulfillmentType.Pickup,
            new[] { ("Susu UHT Cokelat 200ml", "Dus", 3), ("Teh Botol 350ml", "Krat", 2) }, FullFlow);
        await TradeAsync(options, clock, dedi, grosir, today.AddDays(-21), 9, FulfillmentType.Delivery,
            new[] { ("Susu UHT Cokelat 200ml", "Dus", 2) }, FullFlow);

        await SellDownAsync(options, clock, warungMaju, "Susu UHT Cokelat 200ml", 5_000m, 12, 80, today.AddDays(-25), today.AddDays(-1), 11);
        await SellDownAsync(options, clock, sejahtera, "Susu UHT Cokelat 200ml", 5_000m, 12, 60, today.AddDays(-23), today.AddDays(-1), 12);
        await SellDownAsync(options, clock, sejahtera, "Teh Botol 350ml", 5_500m, 12, 40, today.AddDays(-22), today.AddDays(-1), 13);
        await SellDownAsync(options, clock, dedi, "Susu UHT Cokelat 200ml", 5_000m, 12, 24, today.AddDays(-19), today.AddDays(-1), 14);

        // Warung Pak Dedi memilih tidak membagikan ringkasan penjualan (contoh pengaturan privasi).
        using (var d = new ModuleServices(options, FixedBusinessContext.ForRetail(dedi.BusinessId!.Value, dedi.UserId), clock))
        {
            var profile = await OkAsync(d.Businesses.GetProfileAsync(dedi.BusinessId!.Value));
            await OkAsync(d.Businesses.UpdateProfileAsync(
                dedi.BusinessId!.Value,
                new UpdateBusinessRequest(profile.Name, profile.Address, profile.Latitude, profile.Longitude, profile.Phone, false)));
        }

        // 3) Grosir menerima stok baru yang mendekati kadaluarsa, dan memberi tanggal pada stok lama.
        using (var g = new ModuleServices(options, FixedBusinessContext.ForGrossier(grosir.BusinessId!.Value, grosir.UserId), clock))
        {
            clock.Set(WibAt(today.AddDays(-2), 9, 30));
            var products = (await g.Products.ListAsync(null, false)).ToDictionary(p => p.Name);
            await OkAsync(g.Purchasing.RecordAsync(new RecordPurchaseRequest("Distributor Pusat", null, null, new[]
            {
                new PurchaseLineInput(products["Susu UHT Cokelat 200ml"].Id, "Dus", 10, 70_000m, today.AddDays(20)),
                new PurchaseLineInput(products["Susu UHT Cokelat 200ml"].Id, "Dus", 4, 70_000m, today.AddDays(6)),
                new PurchaseLineInput(products["Yogurt Drink 150ml"].Id, "Krat", 6, 46_000m, today.AddDays(9)),
                new PurchaseLineInput(products["Biskuit Kelapa 300g"].Id, "Dus", 8, 112_000m, today.AddDays(28)),
                new PurchaseLineInput(products["Keripik Singkong Pedas"].Id, "Box", 6, 66_000m, today.AddDays(12))
            })));

            var expiryByProduct = new Dictionary<string, int>
            {
                ["Mi Instan Goreng"] = 160, ["Minyak Goreng 1L"] = 240, ["Gula Pasir 1kg"] = 300, ["Beras Premium 5kg"] = 200,
                ["Kopi Sachet"] = 210, ["Teh Botol 350ml"] = 75, ["Sabun Cuci Piring 800ml"] = 500
            };
            foreach (var item in await g.Inventory.GetStockAsync())
            {
                var untracked = item.QtyBase - item.TrackedQtyBase;
                if (untracked > 0 && expiryByProduct.TryGetValue(item.ProductName, out var days))
                {
                    await OkAsync(g.Expiry.TagStockAsync(new TagStockRequest(item.ProductId, untracked, today.AddDays(days))));
                }
            }
        }

        // 4) Warung Maju juga punya barang mendekati kadaluarsa (dashboard Food Waste sisi warung).
        using (var w = new ModuleServices(options, FixedBusinessContext.ForRetail(warungMaju.BusinessId!.Value, warungMaju.UserId), clock))
        {
            clock.Set(WibAt(today.AddDays(-1), 20, 0));
            var stock = (await w.Inventory.GetStockAsync()).ToDictionary(i => i.ProductName);
            async Task Tag(string name, int qty, int daysLeft)
            {
                if (!stock.TryGetValue(name, out var item))
                {
                    return;
                }

                var take = Math.Min(qty, item.QtyBase - item.TrackedQtyBase);
                if (take > 0)
                {
                    await OkAsync(w.Expiry.TagStockAsync(new TagStockRequest(item.ProductId, take, today.AddDays(daysLeft))));
                }
            }

            await Tag("Teh Botol 350ml", 6, -1);
            await Tag("Teh Botol 350ml", 12, 9);
            await Tag("Kopi Sachet", 30, 24);
        }

        // 5) Sekarang (waktu nyata): paket, penawaran, dan pesanan yang sedang berjalan.
        clock.Set(DateTimeOffset.UtcNow);
        int bundleId;
        using (var g = new ModuleServices(options, FixedBusinessContext.ForGrossier(grosir.BusinessId!.Value, grosir.UserId), clock))
        {
            var batches = await g.Expiry.GetBatchesAsync();
            BatchDto Batch(string name, int daysLeft) => batches.Single(b => b.ProductName == name && b.DaysLeft == daysLeft);

            // Penawaran khusus: susu 20 hari lagi ke dua warung yang paling laku menjualnya.
            bundleId = await OkAsync(g.Rescue.CreateBundleAsync(new CreateBundleRequest(
                null, "Susu cokelat 1 dus, harga khusus karena tinggal 20 hari.",
                new[] { new RescueItemInput(Batch("Susu UHT Cokelat 200ml", 20).BatchId, 24) },
                null, 20, today.AddDays(7),
                new[] { new OfferTarget(warungMaju.BusinessId!.Value, 2), new OfferTarget(sejahtera.BusinessId!.Value, 1) },
                "Susu cokelat paling laku di warung Anda. Kami beri harga khusus supaya tidak terbuang.", true)));

            // Paket publik di marketplace.
            await OkAsync(g.Rescue.CreateBundleAsync(new CreateBundleRequest(
                "Paket Camilan Hemat", "Biskuit kelapa dan keripik pedas untuk rak depan warung.",
                new[]
                {
                    new RescueItemInput(Batch("Biskuit Kelapa 300g", 28).BatchId, 20),
                    new RescueItemInput(Batch("Keripik Singkong Pedas", 12).BatchId, 30)
                },
                null, 30, today.AddDays(7))));
            await OkAsync(g.Rescue.CreateBundleAsync(new CreateBundleRequest(
                null, "Yogurt segar satu krat, cepat habis.",
                new[] { new RescueItemInput(Batch("Yogurt Drink 150ml", 9).BatchId, 24) },
                null, 35, today.AddDays(5))));
        }

        // Toko Sejahtera mengambil penawaran susu: pesanan baru menunggu grosir.
        using (var w = new ModuleServices(options, FixedBusinessContext.ForRetail(sejahtera.BusinessId!.Value, sejahtera.UserId), clock))
        {
            var offer = (await w.Rescue.ListReceivedAsync(false)).Single(o => o.BundleId == bundleId);
            await OkAsync(w.Orders.CreateAsync(new CreateOrderRequest(
                grosir.BusinessId!.Value, FulfillmentType.Pickup, "Ambil sore ini ya.", null,
                Array.Empty<OrderLineInput>(), new[] { new BundleLineInput(bundleId, 1) }, offer.Id)));
        }

        // Pesanan Warung Pak Dedi sedang dalam perjalanan (jarak jauh) supaya peta pelacakan menampilkan truk di tengah rute.
        await TradeLiveAsync(options, clock, dedi, grosir, FulfillmentType.Delivery,
            new[] { ("Mi Instan Goreng", "Dus", 1), ("Kopi Sachet", "Box", 1) });
    }

    // Penjualan harian merata: total unit dibagi ke hari-hari antara firstDay dan lastDay.
    private static async Task SellDownAsync(
        DbContextOptions<AppDbContext> options,
        SettableTimeProvider clock,
        AuthenticatedUser owner,
        string product,
        decimal price,
        int minStock,
        int totalUnits,
        DateOnly firstDay,
        DateOnly lastDay,
        int seed)
    {
        int productId;
        using (var s = new ModuleServices(options, FixedBusinessContext.ForRetail(owner.BusinessId!.Value, owner.UserId), clock))
        {
            clock.Set(WibAt(firstDay, 8, 0));
            productId = (await s.Products.ListAsync(product, false)).Single(p => p.Name == product).Id;
            await OkAsync(s.Inventory.UpdateSettingsAsync(productId, price, minStock));
        }

        var rng = new Random(seed);
        var days = lastDay.DayNumber - firstDay.DayNumber + 1;
        var left = totalUnits;
        for (var i = 0; i < days && left > 0; i++)
        {
            var remainingDays = days - i;
            var average = (int)Math.Ceiling(left / (double)remainingDays);
            var qty = remainingDays == 1 ? left : Math.Clamp(average + rng.Next(-1, 2), 1, left);

            clock.Set(WibAt(firstDay.AddDays(i), 9 + rng.Next(0, 11), rng.Next(0, 60)));
            using var services = new ModuleServices(options, FixedBusinessContext.ForRetail(owner.BusinessId!.Value, owner.UserId), clock);
            await OkAsync(services.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(productId, qty) }, "CASH", null)));
            left -= qty;
        }
    }

    // Pesanan yang baru saja dikirim (waktu nyata): grosir sudah menandai "dikirim" beberapa menit lalu.
    private static async Task TradeLiveAsync(
        DbContextOptions<AppDbContext> options,
        SettableTimeProvider clock,
        AuthenticatedUser buyer,
        AuthenticatedUser seller,
        FulfillmentType fulfillment,
        (string Product, string Unit, int Qty)[] lines)
    {
        var now = DateTimeOffset.UtcNow;
        int orderId;
        using (var b = new ModuleServices(options, FixedBusinessContext.ForRetail(buyer.BusinessId!.Value, buyer.UserId), clock))
        {
            clock.Set(now.AddMinutes(-110));
            var wholesaler = await OkAsync(b.Marketplace.GetWholesalerAsync(seller.BusinessId!.Value));
            orderId = await OkAsync(b.Orders.CreateAsync(new CreateOrderRequest(
                seller.BusinessId.Value, fulfillment, "Tolong dikemas rapi.", null,
                lines.Select(l => new OrderLineInput(wholesaler.Products.First(p => p.Name == l.Product).ProductId, l.Unit, l.Qty)).ToList())));
        }

        var steps = new (OrderAction Action, int MinutesAgo)[]
        {
            (OrderAction.Accept, 95), (OrderAction.Prepare, 70), (OrderAction.MarkReady, 40), (OrderAction.HandOver, 6)
        };
        foreach (var (action, minutesAgo) in steps)
        {
            clock.Set(now.AddMinutes(-minutesAgo));
            using var services = new ModuleServices(
                options, FixedBusinessContext.ForGrossier(seller.BusinessId!.Value, seller.UserId), clock);
            await OkAsync(services.Orders.ActAsync(orderId, action, null));
        }
    }

    // ---------------------------------------------------------------
    // Helper pesanan
    // ---------------------------------------------------------------

    // Warung membuat pesanan ke grosir, lalu statusnya dijalankan
    // oleh pihak yang sesuai dengan kewenangannya.
    private static async Task<int> TradeAsync(
        DbContextOptions<AppDbContext> options,
        SettableTimeProvider clock,
        AuthenticatedUser buyer,
        AuthenticatedUser seller,
        DateOnly day,
        int startHour,
        FulfillmentType fulfillment,
        (string Product, string Unit, int Qty)[] lines,
        params OrderAction[] steps)
    {
        int orderId;

        using (var b = new ModuleServices(
            options,
            FixedBusinessContext.ForRetail(
                buyer.BusinessId!.Value,
                buyer.UserId),
            clock))
        {
            clock.Set(WibAt(day, startHour, 0));

            var wholesaler = await OkAsync(
                b.Marketplace.GetWholesalerAsync(
                    seller.BusinessId!.Value));

            var orderLines = lines
                .Select(l => new OrderLineInput(
                    wholesaler.Products.First(p => p.Name == l.Product).ProductId,
                    l.Unit,
                    l.Qty))
                .ToList();

            orderId = await OkAsync(
                b.Orders.CreateAsync(
                    new CreateOrderRequest(
                        seller.BusinessId.Value,
                        fulfillment,
                        "Pesanan demo",
                        null,
                        orderLines)));
        }

        for (var i = 0; i < steps.Length; i++)
        {
            clock.Set(WibAt(day, startHour + 1 + i, 15));

            IBusinessContext actor =
                steps[i] == OrderAction.ConfirmReceived
                    ? FixedBusinessContext.ForRetail(
                        buyer.BusinessId!.Value,
                        buyer.UserId)
                    : FixedBusinessContext.ForGrossier(
                        seller.BusinessId!.Value,
                        seller.UserId);

            using var services = new ModuleServices(
                options,
                actor,
                clock);

            await OkAsync(
                services.Orders.ActAsync(orderId, steps[i], null));
        }

        return orderId;
    }

    private static int MaxQty(string productName) => productName switch
    {
        "Kopi Sachet" => 8,
        "Teh Botol 350ml" => 4,
        "Gula Pasir 1kg" => 3,
        "Minyak Goreng 1L" => 2,
        "Sabun Cuci Piring 800ml" => 2,
        _ => 1
    };

    private static DateTimeOffset WibAt(
        DateOnly day,
        int hour,
        int minute) =>
        new(
            WibTime.WibToUtc(day, hour, minute),
            TimeSpan.Zero);

    private static Task<AuthenticatedUser> RegisterAsync(
        ModuleServices s,
        string owner,
        string email,
        string business,
        MarketRole role,
        string address,
        double latitude,
        double longitude,
        string phone) =>
        OkAsync(
            s.Businesses.RegisterAsync(
                new RegisterBusinessRequest(
                    owner,
                    email,
                    DemoAccounts.Password,
                    business,
                    GroceryTypeId,
                    role,
                    address,
                    latitude,
                    longitude,
                    phone)));

    private static async Task<T> OkAsync<T>(Task<Result<T>> task)
    {
        var result = await task;

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Data demo gagal dibuat: " + result.Error);
        }

        return result.Value!;
    }

    private static async Task OkAsync(Task<Result> task)
    {
        var result = await task;

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Data demo gagal dibuat: " + result.Error);
        }
    }
}