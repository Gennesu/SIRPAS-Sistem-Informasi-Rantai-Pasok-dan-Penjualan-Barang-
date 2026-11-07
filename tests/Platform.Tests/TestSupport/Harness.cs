using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Tests.TestSupport;

/// <summary>
/// Perlengkapan test skenario: mendaftarkan grosir/warung, memverifikasi, membuat produk dan stok bertanggal kadaluarsa,
/// serta menjalankan pesanan sampai selesai. Setiap pelaku punya ModuleServices (DbContext) sendiri, seperti satu request web.
/// </summary>
public sealed class Harness : IDisposable
{
    public sealed record Actor(ModuleServices S, FakeBusinessContext Ctx, int BusinessId, int UserId);

    private readonly List<ModuleServices> _opened = new();

    public TestDb Db { get; } = new();
    public DateOnly Today { get; } = WibTime.TodayWib(TimeProvider.System);

    public void Dispose()
    {
        foreach (var s in _opened)
        {
            s.Dispose();
        }

        Db.Dispose();
    }

    public ModuleServices Open(IBusinessContext context)
    {
        var services = new ModuleServices(Db.Options, context, TimeProvider.System);
        _opened.Add(services);
        return services;
    }

    public async Task<Actor> RegisterAsync(string email, string name, MarketRole role, double lat, double lon, bool verify = true)
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

        var actor = new Actor(Open(context), context, businessId, registration.Value.UserId);
        if (verify)
        {
            var admin = new FakeBusinessContext();
            admin.AsAdmin(900);
            var result = await Open(admin).Businesses.SetVerificationAsync(businessId, VerificationStatus.Verified, null);
            Assert.True(result.Succeeded, result.Error);
        }

        return actor;
    }

    /// <summary>Grosir di Bandung (-6,9150; 107,6050) dengan pengiriman berdasarkan jarak.</summary>
    public async Task<Actor> NewWholesalerAsync(
        string email = "grosir@uji.test", double lat = -6.9150, double lon = 107.6050, decimal minOrder = 0m,
        decimal baseFee = 8_000m, decimal freeKm = 2m, decimal perKm = 2_500m, decimal? maxKm = 12m)
    {
        var seller = await RegisterAsync(email, "Grosir Uji " + email, MarketRole.Wholesale, lat, lon);
        var delivery = await seller.S.Delivery.SaveMineAsync(
            new SaveDeliveryRequest(true, true, baseFee, minOrder, null, null, perKm, freeKm, maxKm));
        Assert.True(delivery.Succeeded, delivery.Error);
        return seller;
    }

    /// <summary>Warung di Bandung (-6,9175; 107,6191), sekitar 1,6 km dari grosir uji.</summary>
    public Task<Actor> NewRetailerAsync(string email = "warung@uji.test", double lat = -6.9175, double lon = 107.6191, bool verify = true) =>
        RegisterAsync(email, "Warung Uji " + email, MarketRole.Retail, lat, lon, verify);

    /// <summary>Produk grosir dengan satu kemasan (mis. Dus isi 24 seharga Rp96.000).</summary>
    public async Task<int> AddProductAsync(
        Actor seller, string name, string sku, string baseUnit, string packUnit, int conversion, decimal packPrice, decimal basePrice = 0m)
    {
        var result = await seller.S.Products.CreateAsync(new SaveProductRequest(
            name, sku, 1, null, baseUnit, basePrice > 0 ? basePrice : Math.Round(packPrice / conversion * 1.05m), new[] { new UnitInput(packUnit, conversion, packPrice) }));
        Assert.True(result.Succeeded, result.Error);
        return result.Value!.Id;
    }

    /// <summary>Stok masuk lewat pembelian dengan tanggal kadaluarsa (null = tanpa tanggal).</summary>
    public async Task StockAsync(Actor business, int productId, string unit, int packs, decimal costPerPack, int? expiresInDays)
    {
        var result = await business.S.Purchasing.RecordAsync(new RecordPurchaseRequest(
            "Distributor", null, null,
            new[] { new PurchaseLineInput(productId, unit, packs, costPerPack, expiresInDays is int d ? Today.AddDays(d) : null) }));
        Assert.True(result.Succeeded, result.Error);
    }

    public static async Task ActOkAsync(Actor who, int orderId, OrderAction action, string? reason = null)
    {
        var result = await who.S.Orders.ActAsync(orderId, action, reason);
        Assert.True(result.Succeeded, $"{action}: {result.Error}");
    }

    public static async Task<int> PlaceOrderAsync(
        Actor buyer, Actor seller, int productId, string unit, int packs, FulfillmentType type = FulfillmentType.Pickup)
    {
        var result = await buyer.S.Orders.CreateAsync(new CreateOrderRequest(
            seller.BusinessId, type, null, null, new[] { new OrderLineInput(productId, unit, packs) }));
        Assert.True(result.Succeeded, result.Error);
        return result.Value;
    }

    /// <summary>Terima, siapkan, siap, serahkan, lalu warung mengonfirmasi.</summary>
    public static async Task CompleteAsync(Actor seller, Actor buyer, int orderId)
    {
        await ActOkAsync(seller, orderId, OrderAction.Accept);
        await ActOkAsync(seller, orderId, OrderAction.Prepare);
        await ActOkAsync(seller, orderId, OrderAction.MarkReady);
        await ActOkAsync(seller, orderId, OrderAction.HandOver);
        await ActOkAsync(buyer, orderId, OrderAction.ConfirmReceived);
    }
}
