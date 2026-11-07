using Microsoft.EntityFrameworkCore;
using Platform.Tests.TestSupport;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Scenario;

/// <summary>
/// Skenario end-to-end dari spesifikasi: grosir menjual Mi Instan 1 Box = 40 Item @ Rp120.000;
/// warung membeli 2 Box (stok 80 Item, modal Rp3.000), menjual 5 Item @ Rp3.500
/// (omzet Rp17.500, modal Rp15.000, untung kotor Rp2.500, stok 75).
/// </summary>
public sealed class ScenarioTests : IDisposable
{
    private sealed record Actor(ModuleServices Services, FakeBusinessContext Context);

    private readonly TestDb _testDb = new();
    private readonly List<ModuleServices> _opened = new();

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

    // Mendaftarkan warung baru lalu berperan sebagai pemiliknya.
    private async Task<Actor> RegisterRetailerAsync(string email = "siti@warung.test", string name = "Warung Maju")
    {
        var registration = await Open(new FakeBusinessContext()).Businesses.RegisterAsync(
            new RegisterBusinessRequest(
                "Bu Siti", email, "Rahasia123", name, 1, MarketRole.Retail,
                "Jl. Mawar No. 5, Bandung", null, null, "081234567890"));
        Assert.True(registration.Succeeded, registration.Error);

        var context = new FakeBusinessContext();
        context.AsRetail(registration.Value!.BusinessId!.Value, registration.Value.UserId);
        return new Actor(Open(context), context);
    }

    private static SaveProductRequest MiInstan() =>
        new("Mi Instan", null, 1, null, "Item", null, new[] { new UnitInput("Box", 40, null) });

    private static async Task<ProductDto> CreateMiInstanAsync(ModuleServices s)
    {
        var created = await s.Products.CreateAsync(MiInstan());
        Assert.True(created.Succeeded, created.Error);
        return created.Value!;
    }

    private static async Task BuyAsync(ModuleServices s, int productId, int boxes, decimal pricePerBox)
    {
        var purchase = await s.Purchasing.RecordAsync(new RecordPurchaseRequest(
            "Grosir Sumber Rejeki", null, null, new[] { new PurchaseLineInput(productId, "Box", boxes, pricePerBox) }));
        Assert.True(purchase.Succeeded, purchase.Error);
    }

    [Fact]
    public async Task Skenario_beli_2_Box_lalu_jual_5_Item_angkanya_konsisten()
    {
        var actor = await RegisterRetailerAsync();
        var s = actor.Services;
        var product = await CreateMiInstanAsync(s);

        // 2 Box x 40 = 80 Item; Rp120.000 / 40 = Rp3.000 per Item.
        await BuyAsync(s, product.Id, 2, 120_000m);
        var afterPurchase = (await s.Inventory.GetStockAsync()).Single();
        Assert.Equal(80, afterPurchase.QtyBase);
        Assert.Equal(3_000m, afterPurchase.AvgCostBase);
        Assert.Equal("2 Box", afterPurchase.QtyBreakdown);

        // Warung menentukan harga jual sendiri.
        Assert.True((await s.Inventory.UpdateSettingsAsync(product.Id, 3_500m, null)).Succeeded);

        // POS: jual 5 Item.
        var sale = await s.Sales.ConfirmAsync(
            new ConfirmSaleRequest(new[] { new SaleLineInput(product.Id, 5) }, "CASH", null));
        Assert.True(sale.Succeeded, sale.Error);
        Assert.Equal(17_500m, sale.Value!.Total);
        Assert.Equal(75, (await s.Inventory.GetStockAsync()).Single().QtyBase);

        // Penjualan Hari Ini: omzet, modal, untung kotor.
        var report = await s.Reporting.GetSalesReportAsync(new PeriodSelection(PeriodPreset.Today, null, null));
        Assert.Equal(1, report.Transactions);
        Assert.Equal(5, report.ItemsSold);
        Assert.Equal(17_500m, report.Revenue);
        Assert.Equal(15_000m, report.Cogs);
        Assert.Equal(2_500m, report.GrossProfit);

        // Dashboard ikut berubah.
        var dashboard = await s.Reporting.GetDashboardAsync();
        Assert.Equal(17_500m, dashboard.RevenueToday);
        Assert.Equal(2_500m, dashboard.GrossProfitToday);
        Assert.Equal(5, dashboard.ItemsSoldToday);
        Assert.Equal(75, dashboard.TotalStockBase);

        // Setiap perubahan stok tercatat sebagai movement: +80 (pembelian) lalu -5 (penjualan).
        var movements = await s.Inventory.GetMovementsAsync(product.Id);
        Assert.Equal(2, movements.Count);
        Assert.Equal(new[] { -5, 80 }, movements.Select(m => m.QtyBaseSigned).ToArray());
    }

    [Fact]
    public async Task Stok_tidak_boleh_negatif_dan_penjualan_ditolak_tanpa_mengubah_data()
    {
        var actor = await RegisterRetailerAsync();
        var product = await CreateMiInstanAsync(actor.Services);
        await BuyAsync(actor.Services, product.Id, 2, 120_000m);
        await actor.Services.Inventory.UpdateSettingsAsync(product.Id, 3_500m, null);

        var tooMany = await actor.Services.Sales.ConfirmAsync(
            new ConfirmSaleRequest(new[] { new SaleLineInput(product.Id, 81) }, "CASH", null));

        Assert.False(tooMany.Succeeded);
        Assert.Equal(ErrorKind.Conflict, tooMany.Kind);

        var fresh = Open(actor.Context);
        Assert.Equal(80, (await fresh.Inventory.GetStockAsync()).Single().QtyBase);
        var report = await fresh.Reporting.GetSalesReportAsync(new PeriodSelection(PeriodPreset.Today, null, null));
        Assert.Equal(0, report.Transactions);
    }

    [Fact]
    public async Task Pembelian_kedua_dengan_harga_baru_memakai_modal_rata_rata_dan_riwayat_tetap()
    {
        var actor = await RegisterRetailerAsync();
        var s = actor.Services;
        var product = await CreateMiInstanAsync(s);
        await s.Inventory.UpdateSettingsAsync(product.Id, 3_500m, null);

        await BuyAsync(s, product.Id, 2, 120_000m); // 80 Item @ 3.000
        await BuyAsync(s, product.Id, 1, 125_000m); // 40 Item @ 3.125

        // (80 x 3.000 + 40 x 3.125) / 120 = 3.041,6667
        var stock = (await s.Inventory.GetStockAsync()).Single();
        Assert.Equal(120, stock.QtyBase);
        Assert.Equal(3_041.6667m, stock.AvgCostBase);

        // Harga pembelian pertama tidak berubah menjadi 125.000.
        var items = await s.Db.PurchaseItems.OrderBy(i => i.Id).ToListAsync();
        Assert.Equal(3_000m, items[0].CostPerBase);
        Assert.Equal(3_125m, items[1].CostPerBase);

        // COGS penjualan memakai modal rata-rata saat transaksi: 10 x 3.041,6667 = 30.416,67.
        await s.Sales.ConfirmAsync(new ConfirmSaleRequest(new[] { new SaleLineInput(product.Id, 10) }, "CASH", null));
        var report = await s.Reporting.GetSalesReportAsync(new PeriodSelection(PeriodPreset.Today, null, null));
        Assert.Equal(30_416.67m, report.Cogs);
    }

    [Fact]
    public async Task Kunci_idempotensi_mencegah_transaksi_ganda()
    {
        var actor = await RegisterRetailerAsync();
        var s = actor.Services;
        var product = await CreateMiInstanAsync(s);
        await BuyAsync(s, product.Id, 2, 120_000m);
        await s.Inventory.UpdateSettingsAsync(product.Id, 3_500m, null);

        var request = new ConfirmSaleRequest(new[] { new SaleLineInput(product.Id, 5) }, "CASH", "kunci-klik-ganda");
        var first = await s.Sales.ConfirmAsync(request);
        var second = await s.Sales.ConfirmAsync(request);

        Assert.True(first.Succeeded, first.Error);
        Assert.True(second.Succeeded, second.Error);
        Assert.Equal(first.Value!.Id, second.Value!.Id);
        Assert.Equal(75, (await s.Inventory.GetStockAsync()).Single().QtyBase);
    }

    [Fact]
    public async Task Warung_tidak_bisa_melihat_atau_memakai_produk_warung_lain()
    {
        var a = await RegisterRetailerAsync("a@warung.test", "Warung A");
        var b = await RegisterRetailerAsync("b@warung.test", "Warung B");
        var product = await CreateMiInstanAsync(a.Services);

        Assert.Empty(await b.Services.Products.ListAsync(null, true));

        var byId = await b.Services.Products.GetAsync(product.Id);
        Assert.False(byId.Succeeded);
        Assert.Equal(ErrorKind.NotFound, byId.Kind);

        var purchase = await b.Services.Purchasing.RecordAsync(new RecordPurchaseRequest(
            "Grosir", null, null, new[] { new PurchaseLineInput(product.Id, "Box", 1, 100_000m) }));
        Assert.False(purchase.Succeeded);
    }
}
