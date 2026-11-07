using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Purchasing.ViewModels;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Purchasing.Controllers;

[Authorize(Roles = RoleNames.Retail + "," + RoleNames.Grossier)]
public class PurchasesController : Controller
{
    private readonly IPurchasingService _purchasing;
    private readonly IProductService _products;
    private readonly TimeProvider _time;

    public PurchasesController(IPurchasingService purchasing, IProductService products, TimeProvider time)
    {
        _purchasing = purchasing;
        _products = products;
        _time = time;
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        return View(await _purchasing.ListAsync(30, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var model = new PurchaseFormViewModel { Date = WibTime.TodayWib(_time) };
        return View(await PrepareAsync(model, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(PurchaseFormViewModel model, CancellationToken ct)
    {
        var prepared = await PrepareAsync(model, ct);
        if (!ModelState.IsValid)
        {
            return View(prepared);
        }

        var lines = model.Lines
            .Where(l => l.ProductId is > 0)
            .Select(l => new PurchaseLineInput(l.ProductId!.Value, l.UnitName ?? string.Empty, l.QtyPack ?? 0, l.UnitPricePack ?? 0m, l.ExpiryDate))
            .ToList();

        // Tanggal dipilih sebagai tanggal WIB; disimpan sebagai UTC pada pukul 12:00 WIB.
        DateTime? purchasedAt = model.Date is { } date ? WibTime.WibToUtc(date, 12, 0) : null;

        var result = await _purchasing.RecordAsync(
            new RecordPurchaseRequest(model.SupplierName, purchasedAt, model.Note, lines), ct);

        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Pembelian gagal disimpan.");
            return View(prepared);
        }

        TempData["Success"] = "Pembelian dicatat dan stok bertambah.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<PurchaseFormViewModel> PrepareAsync(PurchaseFormViewModel model, CancellationToken ct)
    {
        var products = await _products.ListAsync(null, false, ct);
        model.Products = products
            .Select(p => new ProductOption(
                p.Id,
                p.Name,
                string.Join("|", p.Units.Select(u => $"{u.UnitName}:{u.ConversionToBase}"))))
            .ToList();

        while (model.Lines.Count < 5)
        {
            model.Lines.Add(new PurchaseLineRow());
        }

        return model;
    }
}
