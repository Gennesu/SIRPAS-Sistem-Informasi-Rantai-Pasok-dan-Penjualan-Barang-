using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Controllers;

/// <summary>Katalog eceran warung untuk pembeli akhir: cari warung, lihat produk/promo, konfirmasi, dan buat pesanan.</summary>
[Authorize(Roles = RoleNames.Consumer)]
public class ShopController : Controller
{
    private readonly IConsumerCatalogService _catalog;
    private readonly IConsumerOrderService _orders;

    public ShopController(IConsumerCatalogService catalog, IConsumerOrderService orders)
    {
        _catalog = catalog;
        _orders = orders;
    }

    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        ViewData["Query"] = q;
        ViewData["Promos"] = (await _catalog.ListPromosAsync(ct)).Take(6).ToList();
        return View(await _catalog.SearchShopsAsync(q, ct));
    }

    public async Task<IActionResult> Promos(CancellationToken ct) => View(await _catalog.ListPromosAsync(ct));

    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var shop = await _catalog.GetShopAsync(id, ct);
        if (!shop.Succeeded)
        {
            TempData["Error"] = shop.Error;
            return RedirectToAction(nameof(Index));
        }

        return View(shop.Value);
    }

    // Halaman konfirmasi: server menghitung ulang harga, promo, dan ongkir; kunci operasi dibuat di sini.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(ShopOrderInput input, CancellationToken ct)
    {
        var request = ToRequest(input, null);
        var preview = await _orders.PreviewAsync(request, ct);
        if (!preview.Succeeded)
        {
            TempData["Error"] = preview.Error;
            return RedirectToAction(nameof(Detail), new { id = input.SellerBusinessId });
        }

        input.IdempotencyKey = Guid.NewGuid().ToString("N");
        ViewData["Preview"] = preview.Value;
        return View(input);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Place(ShopOrderInput input, CancellationToken ct)
    {
        var result = await _orders.PlaceAsync(ToRequest(input, input.IdempotencyKey), ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
            return RedirectToAction(nameof(Detail), new { id = input.SellerBusinessId });
        }

        // PRG: refresh/back tidak mengirim ulang form; kunci operasi menjamin pengiriman ulang tidak membuat pesanan kedua.
        TempData["Success"] = "Pesanan dibuat dan stok sudah dicadangkan untuk Anda.";
        return RedirectToAction("Details", "ConsumerOrders", new { id = result.Value });
    }

    private static PlaceConsumerOrderRequest ToRequest(ShopOrderInput input, string? key) =>
        new(input.SellerBusinessId, input.Fulfillment, input.PaymentMethod, input.DeliveryAddress, input.Note,
            input.Qty.Where(kv => kv.Value > 0).Select(kv => new ConsumerLineInput(kv.Key, kv.Value)).ToList(), key);
}
