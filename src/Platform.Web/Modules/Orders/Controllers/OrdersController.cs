using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Orders.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Orders.Controllers;

[Authorize(Roles = RoleNames.Retail + "," + RoleNames.Grossier + "," + RoleNames.Livestock)]
public class OrdersController : Controller
{
    private readonly IOrderService _orders;

    public OrdersController(IOrderService orders)
    {
        _orders = orders;
    }

    public async Task<IActionResult> Index(OrderStatus? status, CancellationToken ct)
    {
        var asSeller = User.IsInRole(RoleNames.Grossier) || User.IsInRole(RoleNames.Livestock);
        ViewData["Status"] = status;
        ViewData["IsSeller"] = asSeller;
        ViewData["IsLivestock"] = User.IsInRole(RoleNames.Livestock);
        return View(await _orders.ListAsync(status, 100, ct));
    }

    [Authorize(Roles = RoleNames.Grossier)]
    public async Task<IActionResult> FromLivestock(OrderStatus? status, CancellationToken ct)
    {
        ViewData["Status"] = status;
        var sales = await _orders.ListAsync(status, 100, ct);
        return View(sales.Where(order => order.IsLivestockSale).ToList());
    }

    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var result = await _orders.GetAsync(id, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        return View(result.Value);
    }

    /// <summary>Nota pesanan siap cetak (A5). Boleh dibuka grosir maupun warung pada pesanan miliknya.</summary>
    public async Task<IActionResult> Nota(int id, CancellationToken ct)
    {
        var result = await _orders.GetAsync(id, ct);
        return result.Succeeded ? View(result.Value) : NotFound();
    }

    /// <summary>Dipanggil browser tiap 20 detik di halaman pesanan: bila status berubah, halaman dimuat ulang (tanpa WebSocket).</summary>
    [HttpGet]
    public async Task<IActionResult> Status(int id, CancellationToken ct)
    {
        var result = await _orders.GetAsync(id, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        return Json(new { status = result.Value!.Status.ToString(), updatedAt = result.Value.UpdatedAt });
    }

    [HttpPost]
    [Authorize(Roles = RoleNames.Retail + "," + RoleNames.Grossier)]
    public async Task<IActionResult> Create(CreateOrderInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Data pesanan tidak valid. Periksa alamat dan catatan.";
            return RedirectToAction("Wholesaler", "Marketplace", new { id = input.SellerBusinessId });
        }

        // Baris tanpa jumlah diabaikan (form menampilkan seluruh katalog).
        var lines = input.Lines
            .Where(l => l.ProductId is > 0 && l.QtyPack is > 0)
            .Select(l => new OrderLineInput(l.ProductId!.Value, l.UnitName ?? string.Empty, l.QtyPack!.Value))
            .ToList();

        var result = await _orders.CreateAsync(
            new CreateOrderRequest(
                input.SellerBusinessId, input.Fulfillment, input.Note, input.DeliveryAddress, lines, null, null, input.IdempotencyKey), ct);

        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error ?? "Pesanan gagal dibuat.";
            return RedirectToAction("Wholesaler", "Marketplace", new { id = input.SellerBusinessId });
        }

        TempData["Success"] = "Pesanan dikirim ke penjual dan menunggu persetujuan.";
        return RedirectToAction(nameof(Detail), new { id = result.Value });
    }

    [HttpPost]
    public async Task<IActionResult> Act(int id, OrderAction action, string? reason, CancellationToken ct)
    {
        var result = await _orders.ActAsync(id, action, reason, ct);
        if (result.Succeeded)
        {
            TempData["Success"] = "Status pesanan diperbarui.";
        }
        else if (result.Kind == ErrorKind.NotFound)
        {
            return NotFound();
        }
        else
        {
            TempData["Error"] = result.Error ?? "Aksi gagal diproses.";
        }

        return RedirectToAction(nameof(Detail), new { id });
    }
}
