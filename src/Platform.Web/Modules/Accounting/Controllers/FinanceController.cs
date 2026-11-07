using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Accounting.ViewModels;
using Platform.Web.Modules.Reporting.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Accounting.Controllers;

[Authorize(Roles = RoleNames.Retail)]
public class FinanceController : Controller
{
    private readonly IReportingService _reporting;
    private readonly IExpenseService _expenses;
    private readonly TimeProvider _time;

    public FinanceController(IReportingService reporting, IExpenseService expenses, TimeProvider time)
    {
        _reporting = reporting;
        _expenses = expenses;
        _time = time;
    }

    public async Task<IActionResult> Index(
        PeriodPreset preset = PeriodPreset.ThisMonth,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        var selection = new PeriodSelection(preset, from, to);
        ViewData["Selection"] = selection;
        ViewData["Today"] = WibTime.TodayWib(_time);
        return View(await _reporting.GetFinancialSummaryAsync(selection, ct));
    }

    [HttpPost]
    public async Task<IActionResult> AddExpense(
        ExpenseInput input,
        PeriodPreset preset = PeriodPreset.ThisMonth,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
        {
            TempData["Error"] = "Lengkapi pengeluaran: pilih kategori dan isi jumlah lebih dari 0.";
        }
        else
        {
            var date = input.Date ?? WibTime.TodayWib(_time);
            var result = await _expenses.AddAsync(new AddExpenseRequest(input.Category, input.Amount, date, input.Note), ct);
            if (result.Succeeded)
            {
                TempData["Success"] = "Pengeluaran dicatat.";
            }
            else
            {
                TempData["Error"] = result.Error ?? "Pengeluaran gagal dicatat.";
            }
        }

        return RedirectToAction(nameof(Index), new { preset, from, to });
    }

    [HttpPost]
    public async Task<IActionResult> DeleteExpense(
        int id,
        PeriodPreset preset = PeriodPreset.ThisMonth,
        DateOnly? from = null,
        DateOnly? to = null,
        CancellationToken ct = default)
    {
        var result = await _expenses.DeleteAsync(id, ct);
        if (result.Succeeded)
        {
            TempData["Success"] = "Pengeluaran dihapus.";
        }
        else
        {
            TempData["Error"] = result.Error ?? "Pengeluaran gagal dihapus.";
        }

        return RedirectToAction(nameof(Index), new { preset, from, to });
    }
}
