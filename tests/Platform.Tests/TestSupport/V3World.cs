using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Tests.TestSupport;

/// <summary>
/// Dunia uji V3 di atas database SQLite BERBASIS FILE: setiap Open() membuat DbContext (dan koneksi) baru ke file yang sama,
/// sehingga tes konkurensi benar-benar menjalankan beberapa koneksi database secara bersamaan (bukan memanggil berurutan).
/// </summary>
public sealed class V3World : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "platform-v3-" + Guid.NewGuid().ToString("N"));
    private readonly List<ModuleServices> _opened = new();
    private int _seq;

    public V3World(TimeProvider? time = null)
    {
        Directory.CreateDirectory(_dir);
        Time = time ?? TimeProvider.System;
        DbPath = Path.Combine(_dir, "test.db");
        Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={DbPath}").Options;
        using var db = new AppDbContext(Options, new FakeBusinessContext());
        db.Database.EnsureCreated();
    }

    public sealed record Actor(FakeBusinessContext Ctx, int UserId, int? BusinessId, string Name, string Email);

    public TimeProvider Time { get; }
    public string DbPath { get; }
    public DbContextOptions<AppDbContext> Options { get; }
    public DateOnly Today => WibTime.TodayWib(Time);

    /// <summary>Layanan baru (DbContext + koneksi baru) atas nama aktor tertentu.</summary>
    public ModuleServices Open(Actor actor)
    {
        var services = new ModuleServices(Options, actor.Ctx, Time);
        lock (_opened)
        {
            _opened.Add(services);
        }

        return services;
    }

    public ModuleServices OpenAnonymous()
    {
        var services = new ModuleServices(Options, new FakeBusinessContext(), Time);
        lock (_opened)
        {
            _opened.Add(services);
        }

        return services;
    }

    public Actor Admin()
    {
        var ctx = new FakeBusinessContext();
        ctx.AsAdmin(9000);
        return new Actor(ctx, 9000, null, "Admin", "admin@uji.test");
    }

    public async Task<Actor> RegisterBusinessAsync(
        MarketRole role, string name, double lat = -6.9175, double lon = 107.6191, bool verify = true, string? email = null)
    {
        var n = Interlocked.Increment(ref _seq);
        email ??= $"{role.ToString().ToLowerInvariant()}{n}@uji.test";
        var registration = await OpenAnonymous().Businesses.RegisterAsync(new RegisterBusinessRequest(
            "Pemilik " + name, email, "Rahasia123", name, 1, role, "Jl. Uji No. 1, Bandung", lat, lon, "081200000001"));
        if (!registration.Succeeded)
        {
            throw new InvalidOperationException("Registrasi gagal: " + registration.Error);
        }

        var businessId = registration.Value!.BusinessId!.Value;
        var ctx = new FakeBusinessContext();
        switch (role)
        {
            case MarketRole.Wholesale: ctx.AsGrossier(businessId, registration.Value.UserId); break;
            case MarketRole.Livestock: ctx.AsLivestock(businessId, registration.Value.UserId); break;
            default: ctx.AsRetail(businessId, registration.Value.UserId); break;
        }

        var actor = new Actor(ctx, registration.Value.UserId, businessId, name, email);
        if (verify)
        {
            var result = await Open(Admin()).Businesses.SetVerificationAsync(businessId, VerificationStatus.Verified, null);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Verifikasi gagal: " + result.Error);
            }
        }

        return actor;
    }

    public async Task<Actor> RegisterConsumerAsync(string name = "Budi Pembeli", double? lat = -6.9200, double? lon = 107.6200)
    {
        var n = Interlocked.Increment(ref _seq);
        var email = $"pembeli{n}@uji.test";
        var registration = await OpenAnonymous().Consumers.RegisterAsync(new RegisterConsumerRequest(
            name, email, "Rahasia123", "081234567890", "Jl. Pembeli No. 5, Bandung", lat, lon));
        if (!registration.Succeeded)
        {
            throw new InvalidOperationException("Registrasi pembeli gagal: " + registration.Error);
        }

        var ctx = new FakeBusinessContext();
        ctx.AsConsumer(registration.Value!.UserId);
        return new Actor(ctx, registration.Value.UserId, null, name, email);
    }

    /// <summary>Produk dengan satuan dasar "Pcs", kemasan "Dus" (isi 10), harga per Dus, dan stok awal (Dus).</summary>
    public async Task<int> CreateStockedProductAsync(
        Actor owner, string name, int stockPacks, decimal packPrice = 100_000m, decimal packCost = 80_000m,
        int moqBase = 1, DateOnly? expiry = null, string? sku = null)
    {
        var s = Open(owner);
        var product = await s.Products.CreateAsync(new SaveProductRequest(
            name, sku, 1, null, "Pcs", packPrice / 10m, new[] { new UnitInput("Dus", 10, packPrice) }, null, moqBase));
        if (!product.Succeeded)
        {
            throw new InvalidOperationException("Produk gagal: " + product.Error);
        }

        if (stockPacks > 0)
        {
            var purchase = await s.Purchasing.RecordAsync(new RecordPurchaseRequest(
                "Distributor", null, null, new[] { new PurchaseLineInput(product.Value!.Id, "Dus", stockPacks, packCost, expiry) }));
            if (!purchase.Succeeded)
            {
                throw new InvalidOperationException("Stok gagal: " + purchase.Error);
            }
        }

        return product.Value!.Id;
    }

    public async Task SetDeliveryAsync(
        Actor seller, bool delivery = true, bool pickup = true, decimal fee = 10_000m, decimal minOrder = 0m,
        bool? direct = null, decimal feePerKm = 0m, decimal freeKm = 0m, decimal? maxKm = null)
    {
        var result = await Open(seller).Delivery.SaveMineAsync(new SaveDeliveryRequest(
            delivery, pickup, fee, minOrder, null, null, feePerKm, freeKm, maxKm, direct));
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Pengiriman gagal: " + result.Error);
        }
    }

    /// <summary>Stok warung siap dijual ke pembeli akhir: harga jual per Pcs dan jumlah Pcs.</summary>
    public async Task<int> CreateRetailProductAsync(
        Actor shop, string name, int stockPcs, decimal sellPrice, decimal cost = 2_000m, DateOnly? expiry = null)
    {
        var s = Open(shop);
        var product = await s.Products.CreateAsync(new SaveProductRequest(name, null, 1, null, "Pcs", null, Array.Empty<UnitInput>()));
        if (!product.Succeeded)
        {
            throw new InvalidOperationException("Produk gagal: " + product.Error);
        }

        var id = product.Value!.Id;
        var purchase = await s.Purchasing.RecordAsync(new RecordPurchaseRequest(
            "Grosir", null, null, new[] { new PurchaseLineInput(id, "Pcs", stockPcs, cost, expiry) }));
        if (!purchase.Succeeded)
        {
            throw new InvalidOperationException("Stok gagal: " + purchase.Error);
        }

        var price = await s.Inventory.UpdateSettingsAsync(id, sellPrice, 0);
        if (!price.Succeeded)
        {
            throw new InvalidOperationException("Harga gagal: " + price.Error);
        }

        return id;
    }

    /// <summary>Menjalankan beberapa pekerjaan SERENTAK (semua ditahan sampai siap, lalu dilepas bersamaan).</summary>
    public static async Task<T[]> RaceAsync<T>(params Func<Task<T>>[] work)
    {
        // Panggilan SQLite bersifat blocking (menunggu kunci tulis). Tanpa ini thread pool menambah thread hanya ~1/detik sehingga
        // yang terukur adalah kelaparan thread, bukan kontensi database.
        ThreadPool.GetMinThreads(out var minWorker, out var minIo);
        if (minWorker < work.Length * 2 + 8)
        {
            ThreadPool.SetMinThreads(work.Length * 2 + 8, minIo);
        }

        using var gate = new ManualResetEventSlim(false);
        var ready = 0;
        var tasks = work.Select(w => Task.Run(async () =>
        {
            Interlocked.Increment(ref ready);
            gate.Wait();
            return await w();
        })).ToArray();

        var spin = 0;
        while (Volatile.Read(ref ready) < work.Length && spin++ < 2000)
        {
            await Task.Delay(1);
        }

        await Task.Delay(30);
        gate.Set();
        return await Task.WhenAll(tasks);
    }

    public void Dispose()
    {
        foreach (var s in _opened)
        {
            s.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, true);
        }
        catch
        {
            // folder sementara: abaikan bila masih terkunci
        }
    }
}
