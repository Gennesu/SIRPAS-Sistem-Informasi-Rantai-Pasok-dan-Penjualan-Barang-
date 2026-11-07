using Microsoft.EntityFrameworkCore;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Data;

/// <summary>
/// Data demo tambahan V3: peternakan, pembeli akhir, dan promo konsumen.
/// IDEMPOTEN: dijalankan berkali-kali (restart aplikasi) tidak membuat akun/produk/kelompok ganda dan tidak menyentuh transaksi.
/// Dijalankan setelah seeder V2 karena memakai akun warung demo V2 (dicari lewat email).
/// </summary>
public static partial class DemoDataSeeder
{
    private sealed record LivestockSpec(string Owner, string Email, string Business, string Address, double Latitude, double Longitude);

    // Data demo untuk usaha peternakan.
    private static readonly LivestockSpec[] LivestockBusinesses =
    {
        new("Pak Joko", "ternak@demo.test", "Ayam Petelur Cisarua", "Cisarua, Lembang, Bandung Barat", -6.8300, 107.5750),
        new("Bu Wati", "ternak2@demo.test", "Kandang Sejahtera Cicalengka", "Cicalengka, Kabupaten Bandung", -6.9820, 107.8330),
        new("Pak Ujang", "ternak3@demo.test", "Peternakan Ciparay", "Ciparay, Kabupaten Bandung", -7.0270, 107.7290)
    };

    private sealed record LivestockItem(string Name, string Sku, int CategoryId, string BaseUnit, decimal BasePrice, string PackUnit, int PackConversion, decimal PackPrice, int MoqBase);

    private static LivestockItem[] LivestockCatalog() => new LivestockItem[]
    {
        new("Telur Ayam Segar", "TLR-001", 7, "Kg", 27_500m, "Boks (15 kg)", 15, 412_500m, 300),
        new("Ayam Potong Segar", "AYM-001", 7, "Kg", 36_000m, "Karung (25 kg)", 25, 900_000m, 100)
    };

    public static async Task SeedV3Async(IServiceProvider sp, ILogger logger)
    {
        var options = sp.GetRequiredService<DbContextOptions<AppDbContext>>();
        var clock = new SettableTimeProvider(DateTimeOffset.UtcNow);

        var livestockUsers = new List<(LivestockSpec Spec, AuthenticatedUser User)>();
        AuthenticatedUser consumer1;
        using (var root = new ModuleServices(options, FixedBusinessContext.Anonymous(), clock))
        {
            if (await root.Db.Users.AnyAsync(u => u.Email == "pembeli@demo.test"))
            {
                logger.LogInformation("Data demo V3 sudah ada; seeder V3 dilewati.");
                return;
            }

            // Seeder V2 harus sudah berjalan karena data demo V3 memakai akun warung.
            if (!await root.Db.Users.AnyAsync(u => u.Email == "warung@demo.test"))
            {
                logger.LogInformation("Data demo V2 belum ada; seeder V3 dilewati.");
                return;
            }

            logger.LogInformation("Mengisi data dummy V3 (peternakan dan pembeli akhir)...");

            var livestockBusinessIds = new List<int>();
            foreach (var livestock in LivestockBusinesses)
            {
                var livestockUser = await RegisterAsync(
                    root, livestock.Owner, livestock.Email, livestock.Business, MarketRole.Livestock, livestock.Address, livestock.Latitude, livestock.Longitude, "0222000000");
                livestockBusinessIds.Add(livestockUser.BusinessId!.Value);
                livestockUsers.Add((livestock, livestockUser));
            }
            foreach (var business in await root.Db.Businesses.Where(b => livestockBusinessIds.Contains(b.Id)).ToListAsync())
            {
                business.VerificationStatus = VerificationStatus.Verified;
            }

            await root.Db.SaveChangesAsync();

            consumer1 = await OkAsync(root.Consumers.RegisterAsync(new RegisterConsumerRequest(
                "Budi Pembeli", "pembeli@demo.test", DemoAccounts.Password, "081355500011",
                "Jl. Cempaka No. 10, Bandung", -6.9190, 107.6200)));
            await OkAsync(root.Consumers.RegisterAsync(new RegisterConsumerRequest(
                "Siti Konsumen", "pembeli2@demo.test", DemoAccounts.Password, "081355500022",
                "Jl. Anggrek No. 3, Bandung", -6.9300, 107.6300)));
        }

        // ---- Peternakan: katalog produk (harga grosir + minimum pembelian). Gambar demo dipasang seeder gambar; pemilik bisa menggantinya dengan foto sendiri.
        foreach (var (_, livestockOwner) in livestockUsers)
        {
            var livestockContext = FixedBusinessContext.ForLivestock(livestockOwner.BusinessId!.Value, livestockOwner.UserId);
            using var livestockServices = new ModuleServices(options, livestockContext, clock);
            foreach (var item in LivestockCatalog())
            {
                await OkAsync(livestockServices.Products.CreateAsync(new SaveProductRequest(
                    item.Name, item.Sku, item.CategoryId, null, item.BaseUnit, item.BasePrice,
                    new[] { new UnitInput(item.PackUnit, item.PackConversion, item.PackPrice) }, null, item.MoqBase)));
            }
        }

        // ---- Promo konsumen untuk Warung Maju; pesanan awal pembeli
        var maju = await FindUserAsync(options, clock, "warung@demo.test");
        using (var w = new ModuleServices(options, FixedBusinessContext.ForRetail(maju.BusinessId!.Value, maju.UserId), clock))
        {
            clock.Set(DateTimeOffset.UtcNow);
            var stock = (await w.Inventory.GetStockAsync()).ToDictionary(i => i.ProductName);
            if (stock.TryGetValue("Teh Botol 350ml", out var teh))
            {
                await OkAsync(w.ConsumerPromos.SetAsync(teh.ProductId, 30, 14));
            }

            if (stock.TryGetValue("Kopi Sachet", out var kopi))
            {
                await OkAsync(w.ConsumerPromos.SetAsync(kopi.ProductId, 15, 30));
            }

            await OkAsync(w.Delivery.SaveMineAsync(new SaveDeliveryRequest(
                true, true, 5_000m, 0m, "Diantar kurir warung, area Bandung kota.", "Ambil di warung.", 1_500m, 2m, 8m)));
        }

        // ---- Satu pesanan pembeli akhir yang menunggu (supaya dashboard warung langsung berisi)
        using (var c = new ModuleServices(options, FixedBusinessContext.ForConsumer(consumer1.UserId), clock))
        {
            var catalog = await c.ConsumerCatalog.GetShopAsync(maju.BusinessId!.Value);
            if (catalog.Succeeded)
            {
                var kopi = catalog.Value!.Products.FirstOrDefault(p => p.Name == "Kopi Sachet");
                if (kopi is not null)
                {
                    await OkAsync(c.ConsumerOrders.PlaceAsync(new PlaceConsumerOrderRequest(
                        maju.BusinessId!.Value, FulfillmentType.Pickup, PaymentMethod.CashOnPickup, null, "Ambil sore hari",
                        new[] { new ConsumerLineInput(kopi.ProductId, 5) }, "seed-pembeli-kopi")));
                }
            }
        }

        clock.Set(DateTimeOffset.UtcNow);
        logger.LogInformation("Data dummy V3 selesai. Login pembeli: pembeli@demo.test (password {Password}).", DemoAccounts.Password);
    }

    private static async Task<AuthenticatedUser> FindUserAsync(DbContextOptions<AppDbContext> options, TimeProvider clock, string email)
    {
        using var root = new ModuleServices(options, FixedBusinessContext.Anonymous(), clock);
        var user = await root.Db.Users.AsNoTracking().FirstAsync(u => u.Email == email);
        return new AuthenticatedUser(user.Id, user.FullName, user.Email, "", user.BusinessId);
    }
}
