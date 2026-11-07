using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Reporting.Contracts;
using Platform.Web.Modules.Sales.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Reporting.Services;

/// <summary>
/// Semua agregasi dilakukan di memori setelah data periode diambil: sederhana, tidak bergantung pada
/// kemampuan database (mis. SQLite tidak mendukung SUM decimal), dan cukup untuk skala demo.
/// </summary>
public sealed class ReportingService : IReportingService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    public ReportingService(AppDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public async Task<SalesReportDto> GetSalesReportAsync(PeriodSelection period, CancellationToken ct = default)
    {
        var range = WibTime.Resolve(period.Preset, period.From, period.To, _time);
        var sales = await LoadSalesAsync(range.FromUtc, range.ToUtcExclusive, ct);
        return BuildSalesReport(range, sales);
    }

    public async Task<FinancialSummaryDto> GetFinancialSummaryAsync(PeriodSelection period, CancellationToken ct = default)
    {
        var range = WibTime.Resolve(period.Preset, period.From, period.To, _time);
        var report = BuildSalesReport(range, await LoadSalesAsync(range.FromUtc, range.ToUtcExclusive, ct));

        var expenses = (await _db.Expenses.AsNoTracking().ToListAsync(ct))
            .Where(e => e.Date >= range.From && e.Date <= range.To)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .ToList();

        var operatingExpense = expenses.Sum(e => e.Amount);
        var byCategory = expenses
            .GroupBy(e => e.Category)
            .Select(g => new ExpenseCategoryTotal(g.Key, g.Sum(e => e.Amount)))
            .OrderByDescending(c => c.Amount)
            .ToList();

        return new FinancialSummaryDto(
            range,
            report.Revenue,
            report.Cogs,
            report.GrossProfit,
            operatingExpense,
            report.GrossProfit - operatingExpense,
            expenses.Select(e => new ExpenseDto(e.Id, e.Date, e.Category, e.Amount, e.Note)).ToList(),
            byCategory,
            await InventoryValueAsync(ct));
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var today = WibTime.TodayWib(_time);
        var firstDay = today.AddDays(-6);
        var sales = await LoadSalesAsync(WibTime.StartOfDayUtc(firstDay), WibTime.StartOfDayUtc(today.AddDays(1)), ct);

        var byDay = sales.GroupBy(s => DateOnly.FromDateTime(WibTime.ToWib(s.SoldAt))).ToDictionary(g => g.Key, g => g.ToList());
        var points = new List<DailyPoint>();
        for (var i = 0; i < 7; i++)
        {
            var day = firstDay.AddDays(i);
            var daySales = byDay.TryGetValue(day, out var list) ? list : new List<Sale>();
            var items = daySales.SelectMany(s => s.Items).ToList();
            points.Add(new DailyPoint(
                day,
                items.Sum(x => x.Subtotal),
                items.Sum(x => x.Subtotal - x.CostTotal),
                daySales.Count));
        }

        var todayItems = (byDay.TryGetValue(today, out var todaySales) ? todaySales : new List<Sale>())
            .SelectMany(s => s.Items)
            .ToList();
        var todayPoint = points[^1];

        var top = sales
            .SelectMany(s => s.Items)
            .GroupBy(i => i.ProductId)
            .Select(g => new TopProduct(g.First().ProductName, g.Sum(x => x.QtyBase), g.Sum(x => x.Subtotal)))
            .OrderByDescending(t => t.Qty)
            .Take(5)
            .ToList();

        // Stok
        var products = await _db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var inventories = (await _db.Inventories.AsNoTracking().ToListAsync(ct)).ToDictionary(i => i.ProductId);

        var lowStock = new List<LowStockItem>();
        long totalStock = 0;
        foreach (var product in products)
        {
            if (!inventories.TryGetValue(product.Id, out var inv))
            {
                continue;
            }

            totalStock += inv.QtyBase;
            if (inv.QtyBase <= 0 || (inv.MinStockBase > 0 && inv.QtyBase <= inv.MinStockBase))
            {
                lowStock.Add(new LowStockItem(product.Name, inv.QtyBase, inv.MinStockBase, product.BaseUnitName));
            }
        }

        return new DashboardDto(
            todayPoint.Revenue,
            todayPoint.Transactions,
            todayPoint.Profit,
            todayItems.Sum(x => x.QtyBase),
            totalStock,
            products.Count,
            lowStock.Count,
            await InventoryValueAsync(ct),
            points,
            top,
            lowStock.OrderBy(l => l.QtyBase).Take(8).ToList());
    }

    // ------------------------------------------------------------------ helper

    private async Task<List<Sale>> LoadSalesAsync(DateTime fromUtc, DateTime toUtcExclusive, CancellationToken ct)
    {
        return await _db.Sales
            .AsNoTracking()
            .Include(s => s.Items)
            .Where(s => s.Status == SaleStatus.Confirmed && s.SoldAt >= fromUtc && s.SoldAt < toUtcExclusive)
            .OrderByDescending(s => s.SoldAt)
            .ToListAsync(ct);
    }

    private static SalesReportDto BuildSalesReport(DateRangeWib range, List<Sale> sales)
    {
        var items = sales.SelectMany(s => s.Items).ToList();

        var lines = items
            .GroupBy(i => i.ProductId)
            .Select(g =>
            {
                var qty = g.Sum(x => x.QtyBase);
                var revenue = g.Sum(x => x.Subtotal);
                var cogs = g.Sum(x => x.CostTotal);
                return new SalesReportLine(
                    g.First().ProductName, qty, qty > 0 ? Money.Round(revenue / qty) : 0m, revenue, cogs, revenue - cogs);
            })
            .OrderByDescending(l => l.Revenue)
            .ToList();

        var revenueTotal = items.Sum(i => i.Subtotal);
        var cogsTotal = items.Sum(i => i.CostTotal);

        var list = sales
            .Select(s => new SaleListItemDto(s.Id, s.Number, s.SoldAt, s.PaymentMethod, s.Items.Sum(i => i.QtyBase), s.TotalAmount))
            .ToList();

        return new SalesReportDto(
            range, sales.Count, items.Sum(i => i.QtyBase), revenueTotal, cogsTotal, revenueTotal - cogsTotal, lines, list);
    }

    private async Task<decimal> InventoryValueAsync(CancellationToken ct)
    {
        var inventories = await _db.Inventories.AsNoTracking().ToListAsync(ct);
        return Money.Round(inventories.Sum(i => i.QtyBase * i.AvgCostBase));
    }
}
