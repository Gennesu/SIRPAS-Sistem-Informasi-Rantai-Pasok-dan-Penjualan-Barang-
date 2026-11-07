using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Reporting.Contracts;

public sealed record SaleListItemDto(int Id, string Number, DateTime SoldAtUtc, string PaymentMethod, int Items, decimal Total);

public sealed record SalesReportLine(string ProductName, int Qty, decimal AvgPrice, decimal Revenue, decimal Cogs, decimal Profit);

public sealed record SalesReportDto(
    DateRangeWib Range,
    int Transactions,
    int ItemsSold,
    decimal Revenue,
    decimal Cogs,
    decimal GrossProfit,
    IReadOnlyList<SalesReportLine> Lines,
    IReadOnlyList<SaleListItemDto> Sales);

public sealed record ExpenseCategoryTotal(string Category, decimal Amount);

public sealed record FinancialSummaryDto(
    DateRangeWib Range,
    decimal Revenue,
    decimal Cogs,
    decimal GrossProfit,
    decimal OperatingExpense,
    decimal NetResult,
    IReadOnlyList<ExpenseDto> Expenses,
    IReadOnlyList<ExpenseCategoryTotal> ExpenseByCategory,
    decimal InventoryValue);

public sealed record DailyPoint(DateOnly Date, decimal Revenue, decimal Profit, int Transactions);

public sealed record TopProduct(string Name, int Qty, decimal Revenue);

public sealed record LowStockItem(string Name, int QtyBase, int MinStockBase, string BaseUnit);

public sealed record DashboardDto(
    decimal RevenueToday,
    int TransactionsToday,
    decimal GrossProfitToday,
    int ItemsSoldToday,
    long TotalStockBase,
    int ProductCount,
    int LowStockCount,
    decimal InventoryValue,
    IReadOnlyList<DailyPoint> Last7Days,
    IReadOnlyList<TopProduct> TopProducts,
    IReadOnlyList<LowStockItem> LowStock);

/// <summary>
/// Query baca lintas modul (Sales, Expense, Inventory). Tidak mengubah data apa pun;
/// POS sendiri tidak menghitung laporan.
/// </summary>
public interface IReportingService
{
    Task<SalesReportDto> GetSalesReportAsync(PeriodSelection period, CancellationToken ct = default);
    Task<FinancialSummaryDto> GetFinancialSummaryAsync(PeriodSelection period, CancellationToken ct = default);
    Task<DashboardDto> GetDashboardAsync(CancellationToken ct = default);
}
