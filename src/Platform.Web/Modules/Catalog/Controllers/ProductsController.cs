using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Catalog.ViewModels;
using Platform.Web.Modules.Template.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Catalog.Controllers;

[Authorize(Roles = RoleNames.Retail + "," + RoleNames.Grossier + "," + RoleNames.Livestock)]
public class ProductsController : Controller
{
    private readonly IProductService _products;
    private readonly IBusinessTemplateService _templates;
    private readonly IProductImageStore _images;
    private readonly IBusinessContext _context;

    public ProductsController(
        IProductService products, IBusinessTemplateService templates, IProductImageStore images, IBusinessContext context)
    {
        _products = products;
        _templates = templates;
        _images = images;
        _context = context;
    }

    public async Task<IActionResult> Index(string? q, bool inactive, CancellationToken ct)
    {
        ViewData["Query"] = q;
        ViewData["ShowInactive"] = inactive;
        ViewData["Wholesale"] = User.IsInRole(RoleNames.Grossier) || User.IsInRole(RoleNames.Livestock);

        return View(await _products.ListAsync(q, inactive, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var template = await _templates.GetCurrentAsync(ct);
        var model = new ProductFormViewModel { BaseUnitName = template.DefaultBaseUnit };
        return View("Form", await PrepareAsync(model, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create(ProductFormViewModel model, CancellationToken ct)
    {
        var prepared = await PrepareAsync(model, ct);
        if (!ModelState.IsValid)
        {
            return View("Form", prepared);
        }

        var uploaded = await TryUploadAsync(prepared, ct);
        if (!ModelState.IsValid)
        {
            return View("Form", prepared);
        }

        var result = await _products.CreateAsync(ToRequest(prepared, uploaded, false), ct);
        if (!result.Succeeded)
        {
            _images.Delete(uploaded);
            ModelState.AddModelError(string.Empty, result.Error ?? "Produk gagal disimpan.");
            return View("Form", prepared);
        }

        TempData["Success"] = $"Produk \"{result.Value!.Name}\" ditambahkan.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var result = await _products.GetAsync(id, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        var p = result.Value!;
        var model = new ProductFormViewModel
        {
            Id = p.Id,
            Name = p.Name,
            Sku = p.Sku,
            CategoryId = p.CategoryId,
            Description = p.Description,
            BaseUnitName = p.BaseUnitName,
            BaseWholesalePrice = p.Units.FirstOrDefault(u => u.IsBase)?.WholesalePrice,
            MoqBase = p.MoqBase,
            ImagePath = p.ImagePath,
            Units = ProductFormViewModel.NewRows(3)
        };

        var extras = p.Units.Where(u => !u.IsBase).Take(model.Units.Count).ToList();
        for (var i = 0; i < extras.Count; i++)
        {
            model.Units[i].UnitName = extras[i].UnitName;
            model.Units[i].ConversionToBase = extras[i].ConversionToBase;
            model.Units[i].WholesalePrice = extras[i].WholesalePrice;
        }

        return View("Form", await PrepareAsync(model, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model, CancellationToken ct)
    {
        model.Id = id;
        var existing = await _products.GetAsync(id, ct);
        if (!existing.Succeeded)
        {
            return NotFound();
        }

        var prepared = await PrepareAsync(model, ct);
        prepared.ImagePath = existing.Value!.ImagePath;
        if (!ModelState.IsValid)
        {
            return View("Form", prepared);
        }

        var uploaded = await TryUploadAsync(prepared, ct);
        if (!ModelState.IsValid)
        {
            return View("Form", prepared);
        }

        var result = await _products.UpdateAsync(id, ToRequest(prepared, uploaded, prepared.RemoveImage && uploaded is null), ct);
        if (!result.Succeeded)
        {
            _images.Delete(uploaded);
            if (result.Kind == ErrorKind.NotFound)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, result.Error ?? "Produk gagal disimpan.");
            return View("Form", prepared);
        }

        // Gambar lama baru dihapus dari disk setelah produk berhasil disimpan.
        if (uploaded is not null || prepared.RemoveImage)
        {
            _images.Delete(existing.Value.ImagePath);
        }

        TempData["Success"] = $"Produk \"{result.Value!.Name}\" diperbarui.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Toggle(int id, bool active, CancellationToken ct)
    {
        var result = await _products.SetActiveAsync(id, active, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        TempData["Success"] = active ? "Produk diaktifkan." : "Produk dinonaktifkan.";
        return RedirectToAction(nameof(Index), new { inactive = !active });
    }

    // Menyimpan gambar unggahan (bila ada) atas nama usaha yang sedang login. Penolakan dicatat sebagai kesalahan pada field Image.
    private async Task<string?> TryUploadAsync(ProductFormViewModel model, CancellationToken ct)
    {
        if (model.Image is null || model.Image.Length == 0)
        {
            return null;
        }

        if (_context.BusinessId is not int businessId)
        {
            ModelState.AddModelError(nameof(ProductFormViewModel.Image), "Hanya akun usaha yang dapat mengunggah gambar produk.");
            return null;
        }

        var saved = await _images.SaveAsync(model.Image, businessId, ct);
        if (!saved.Succeeded)
        {
            ModelState.AddModelError(nameof(ProductFormViewModel.Image), saved.Error ?? "Gambar gagal diunggah.");
            return null;
        }

        return saved.Value;
    }

    private async Task<ProductFormViewModel> PrepareAsync(ProductFormViewModel model, CancellationToken ct)
    {
        var template = await _templates.GetCurrentAsync(ct);
        var categories = await _products.GetCategoriesAsync(ct);

        model.ShowPackaging = template.Has(ModuleKey.Packaging);
        model.ShowWholesale = User.IsInRole(RoleNames.Grossier) || User.IsInRole(RoleNames.Livestock);

        model.Categories = categories
            .Select(c => new SelectListItem(c.Name, c.Id.ToString(), c.Id == model.CategoryId))
            .ToList();

        while (model.Units.Count < 3)
        {
            model.Units.Add(new UnitRow());
        }

        return model;
    }

    private static SaveProductRequest ToRequest(ProductFormViewModel m, string? imagePath, bool clearImage)
    {
        var extras = m.ShowPackaging
            ? m.Units
                .Where(r => !string.IsNullOrWhiteSpace(r.UnitName))
                .Select(r => new UnitInput(r.UnitName!.Trim(), r.ConversionToBase ?? 0, m.ShowWholesale ? r.WholesalePrice : null))
                .ToList()
            : new List<UnitInput>();

        return new SaveProductRequest(
            m.Name,
            m.Sku,
            m.CategoryId,
            m.Description,
            m.BaseUnitName,
            m.ShowWholesale ? m.BaseWholesalePrice : null,
            extras,
            null,
            m.ShowWholesale ? Math.Max(1, m.MoqBase) : 1,
            imagePath,
            clearImage);
    }
}
