using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Controllers;

[Authorize(Roles = RoleNames.Retail + "," + RoleNames.Grossier)]
public class StockController : Controller
{
    private readonly IInventoryService _inventory;
    private readonly IExpiryService _expiry;

    public StockController(IInventoryService inventory, IExpiryService expiry)
    {
        _inventory = inventory;
        _expiry = expiry;
    }

    public async Task<IActionResult> Index(string? q, CancellationToken ct)
    {
        ViewData["Query"] = q;
        return View(await _inventory.GetStockAsync(q, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Settings(int productId, decimal? sellingPrice, int? minStock, CancellationToken ct)
    {
        var result = await _inventory.UpdateSettingsAsync(productId, sellingPrice, minStock, ct);
        Flash(result, "Pengaturan stok disimpan.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Adjust(AdjustStockInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Lengkapi form koreksi stok dengan benar (produk dan jumlah).";
            return RedirectToAction(nameof(Index));
        }

        var result = await _inventory.AdjustAsync(
            new AdjustStockRequest(input.ProductId, input.Type, input.Qty, input.CostPerBase, input.Reason, input.ExpiryDate), ct);
        Flash(result, "Koreksi stok disimpan.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Menandai stok yang sudah ada (belum bertanggal) dengan tanggal kadaluarsa. Jumlah stok tidak berubah.</summary>
    [HttpPost]
    public async Task<IActionResult> Tag(TagExpiryInput input, CancellationToken ct)
    {
        if (!ModelState.IsValid || input.ExpiryDate is null)
        {
            TempData["Error"] = "Lengkapi produk, jumlah, dan tanggal kadaluarsa.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _expiry.TagStockAsync(new TagStockRequest(input.ProductId, input.Qty, input.ExpiryDate.Value), ct);
        Flash(result, "Tanggal kadaluarsa disimpan. Batch muncul di dashboard food waste.");
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Movements(int? productId, CancellationToken ct)
    {
        return View(await _inventory.GetMovementsAsync(productId, 100, ct));
    }

    private void Flash(Result result, string successMessage)
    {
        if (result.Succeeded)
        {
            TempData["Success"] = successMessage;
        }
        else
        {
            TempData["Error"] = result.Error ?? "Terjadi kesalahan.";
        }
    }
}
