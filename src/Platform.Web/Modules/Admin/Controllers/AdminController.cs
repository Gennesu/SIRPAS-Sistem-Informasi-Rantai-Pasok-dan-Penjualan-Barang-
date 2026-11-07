using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Admin.Contracts;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Admin.Controllers;

[Authorize(Roles = RoleNames.Admin)]
public class AdminController : Controller
{
    private readonly IAdminService _admin;
    private readonly IBusinessService _business;
    private readonly IAuthService _auth;
    private readonly IProductService _products;
    private readonly IOrderService _orders;

    public AdminController(
        IAdminService admin, IBusinessService business, IAuthService auth, IProductService products, IOrderService orders)
    {
        _admin = admin;
        _business = business;
        _auth = auth;
        _products = products;
        _orders = orders;
    }

    // ---------------------------------------------------------------- pengguna
    public async Task<IActionResult> Users(CancellationToken ct) => View(await _admin.ListUsersAsync(ct));

    [HttpPost]
    public async Task<IActionResult> SetUserActive(int id, bool active, CancellationToken ct)
    {
        Flash(await _auth.SetUserActiveAsync(id, active, ct), active ? "Akun diaktifkan." : "Akun dinonaktifkan.");
        return RedirectToAction(nameof(Users));
    }

    // ---------------------------------------------------------------- usaha & verifikasi
    public async Task<IActionResult> Businesses(VerificationStatus? status, CancellationToken ct)
    {
        ViewData["Status"] = status;
        return View(await _admin.ListBusinessesAsync(status, ct));
    }

    [HttpPost]
    public async Task<IActionResult> Verify(int id, CancellationToken ct)
    {
        Flash(await _business.SetVerificationAsync(id, VerificationStatus.Verified, null, ct), "Usaha diverifikasi dan tampil di marketplace.");
        return RedirectToAction(nameof(Businesses));
    }

    [HttpPost]
    public async Task<IActionResult> Reject(int id, string? note, CancellationToken ct)
    {
        Flash(await _business.SetVerificationAsync(id, VerificationStatus.Rejected, note, ct), "Usaha ditolak.");
        return RedirectToAction(nameof(Businesses));
    }

    [HttpPost]
    public async Task<IActionResult> SetBusinessActive(int id, bool active, CancellationToken ct)
    {
        Flash(await _business.SetActiveAsync(id, active, ct), active ? "Usaha diaktifkan." : "Usaha dinonaktifkan; penggunanya tidak dapat masuk.");
        return RedirectToAction(nameof(Businesses));
    }

    // ---------------------------------------------------------------- transaksi, log, kategori
    public async Task<IActionResult> Orders(OrderStatus? status, CancellationToken ct)
    {
        ViewData["Status"] = status;
        return View(await _orders.ListAsync(status, 200, ct));
    }

    public async Task<IActionResult> Audit(string? prefix, CancellationToken ct)
    {
        ViewData["Prefix"] = prefix;
        return View(await _admin.ListAuditAsync(prefix, 200, ct));
    }

    public async Task<IActionResult> Categories(CancellationToken ct) => View(await _products.GetCategoriesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> AddCategory(string name, CancellationToken ct)
    {
        Flash(await _products.AddCategoryAsync(name, ct), "Kategori ditambahkan.");
        return RedirectToAction(nameof(Categories));
    }

    [HttpPost]
    public async Task<IActionResult> RenameCategory(int id, string name, CancellationToken ct)
    {
        Flash(await _products.RenameCategoryAsync(id, name, ct), "Kategori diperbarui.");
        return RedirectToAction(nameof(Categories));
    }

    private void Flash(Result result, string successMessage)
    {
        if (result.Succeeded)
        {
            TempData["Success"] = successMessage;
        }
        else
        {
            TempData["Error"] = result.Error ?? "Aksi gagal diproses.";
        }
    }
}
