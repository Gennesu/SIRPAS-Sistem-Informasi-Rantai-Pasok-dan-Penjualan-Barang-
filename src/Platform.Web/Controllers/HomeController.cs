using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Admin.Contracts;
using Platform.Web.Modules.Reporting.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Controllers;

/// <summary>Halaman depan, pengarah dashboard per role, dan halaman status/error umum.</summary>
public class HomeController : Controller
{
    [AllowAnonymous]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return View("Landing");
        }

        if (User.IsInRole(RoleNames.Admin))
        {
            return RedirectToAction(nameof(AdminDashboard));
        }

        if (User.IsInRole(RoleNames.Grossier))
        {
            return RedirectToAction(nameof(GrossierDashboard));
        }

        if (User.IsInRole(RoleNames.Livestock))
        {
            return RedirectToAction("Dashboard", "Chain");
        }

        if (User.IsInRole(RoleNames.Consumer))
        {
            return RedirectToAction(nameof(ConsumerDashboard));
        }

        return RedirectToAction(nameof(RetailDashboard));
    }

    [Authorize(Roles = RoleNames.Consumer)]
    public async Task<IActionResult> ConsumerDashboard(
        [FromServices] Platform.Web.Modules.Consumers.Contracts.IConsumerOrderService orders,
        [FromServices] Platform.Web.Modules.Consumers.Contracts.IConsumerCatalogService catalog,
        CancellationToken ct)
    {
        ViewData["Orders"] = await orders.ListAsync(null, 5, ct);
        ViewData["Promos"] = await catalog.ListPromosAsync(ct);
        return View();
    }

    [Authorize(Roles = RoleNames.Retail)]
    public async Task<IActionResult> RetailDashboard([FromServices] IReportingService reporting, CancellationToken ct)
    {
        return View(await reporting.GetDashboardAsync(ct));
    }

    [Authorize(Roles = RoleNames.Grossier)]
    public async Task<IActionResult> GrossierDashboard([FromServices] IReportingService reporting, CancellationToken ct)
    {
        return View(await reporting.GetDashboardAsync(ct));
    }

    [Authorize(Roles = RoleNames.Admin)]
    public async Task<IActionResult> AdminDashboard([FromServices] IAdminService admin, CancellationToken ct)
    {
        return View(await admin.GetDashboardAsync(ct));
    }

    // Dipakai UseStatusCodePagesWithReExecute, UseExceptionHandler, dan AccessDeniedPath.
    // Pesan sengaja umum: tidak ada detail teknis yang ditampilkan ke pengguna.
    // Menerima SEMUA metode HTTP: pengeksekusian ulang mempertahankan metode asli (mis. POST yang ditolak
    // antiforgery), dan tanpa [IgnoreAntiforgeryToken] halaman ini akan ikut divalidasi ulang.
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [Route("status/{code:int}")]
    public IActionResult Status(int code)
    {
        var (heading, message) = code switch
        {
            403 => ("Akses ditolak", "Anda tidak memiliki izin untuk membuka halaman ini."),
            404 => ("Halaman tidak ditemukan", "Halaman atau data yang Anda cari tidak tersedia."),
            _ => ("Terjadi kesalahan", "Terjadi kesalahan pada sistem. Silakan coba lagi nanti.")
        };

        ViewData["Heading"] = heading;
        ViewData["Message"] = message;
        return View();
    }
}
