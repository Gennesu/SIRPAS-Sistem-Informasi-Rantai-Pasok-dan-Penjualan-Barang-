using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Controllers;

/// <summary>Pesanan pembeli akhir: pembeli melihat/membatalkan/menerima dan membayar (demo); warung memproses pesanan masuk.</summary>
[Authorize(Roles = RoleNames.Consumer + "," + RoleNames.Retail)]
public class ConsumerOrdersController : Controller
{
    private readonly IConsumerOrderService _orders;
    private readonly IPaymentService _payments;

    public ConsumerOrdersController(IConsumerOrderService orders, IPaymentService payments)
    {
        _orders = orders;
        _payments = payments;
    }

    public async Task<IActionResult> Index(OrderStatus? status, CancellationToken ct)
    {
        ViewData["Status"] = status;
        ViewData["IsSeller"] = User.IsInRole(RoleNames.Retail);
        return View(await _orders.ListAsync(status, 100, ct));
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var order = await _orders.GetAsync(id, ct);
        if (!order.Succeeded)
        {
            return NotFound();
        }

        ViewData["IsSeller"] = User.IsInRole(RoleNames.Retail);
        return View(order.Value);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Act(int id, OrderAction action, string? reason, CancellationToken ct)
    {
        var result = await _orders.ActAsync(id, action, reason, ct);
        if (result.Succeeded)
        {
            TempData["Success"] = "Status pesanan diperbarui.";
        }
        else
        {
            TempData["Error"] = result.Error;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Pembayaran SIMULASI. Kunci operasi dari form mencegah pembayaran tercatat dua kali.</summary>
    [HttpPost, Authorize(Roles = RoleNames.Consumer), ValidateAntiForgeryToken]
    public async Task<IActionResult> Pay(int id, bool simulateFailure, string? key, CancellationToken ct)
    {
        var result = await _payments.PayDemoAsync(id, simulateFailure, key, ct);
        if (!result.Succeeded)
        {
            TempData["Error"] = result.Error;
        }
        else if (result.Value!.Status == Domain.PaymentStatus.Success)
        {
            TempData["Success"] = $"Pembayaran simulasi berhasil. Referensi: {result.Value.Reference} (bukan transaksi bank sungguhan).";
        }
        else
        {
            TempData["Error"] = $"Pembayaran simulasi gagal ({result.Value.Reference}). Anda dapat mencoba lagi.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }
}
