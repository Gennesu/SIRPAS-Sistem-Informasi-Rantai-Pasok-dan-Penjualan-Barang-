using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Reporting.Contracts;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Sales.Controllers;

[Authorize(Roles = RoleNames.Retail)]
public class SalesController : Controller
{
    private readonly IReportingService _reporting;
    private readonly ISalesService _sales;

    public SalesController(IReportingService reporting, ISalesService sales)
    {
        _reporting = reporting;
        _sales = sales;
    }

    // "Penjualan Hari Ini" + filter: hari ini, kemarin, minggu ini, bulan ini, rentang tanggal sendiri.
    public async Task<IActionResult> Index(
        PeriodPreset preset = PeriodPreset.Today,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        var selection = new PeriodSelection(preset, from, to);
        ViewData["Selection"] = selection;
        return View(await _reporting.GetSalesReportAsync(selection, ct));
    }

    public async Task<IActionResult> Receipt(int id, CancellationToken ct)
    {
        var result = await _sales.GetReceiptAsync(id, ct);
        if (!result.Succeeded)
        {
            return NotFound();
        }

        return View(result.Value);
    }
}
