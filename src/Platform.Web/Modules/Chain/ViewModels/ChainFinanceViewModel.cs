using Platform.Web.Modules.Accounting.Contracts;

namespace Platform.Web.Modules.Chain.ViewModels;

public sealed class ChainFinanceViewModel
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public decimal Revenue { get; init; }
    public decimal CostOfGoods { get; init; }
    public decimal GrossProfit => Revenue - CostOfGoods;
    public decimal Expenses { get; init; }
    public decimal NetProfit => GrossProfit - Expenses;
    public decimal PendingSales { get; init; }
    public decimal InventoryValue { get; init; }
    public IReadOnlyList<AgentSalesReportRow> SalesByAgent { get; init; } = Array.Empty<AgentSalesReportRow>();
    public IReadOnlyList<ProductionReportRow> Production { get; init; } = Array.Empty<ProductionReportRow>();
    public IReadOnlyList<ExpenseDto> ExpenseRows { get; init; } = Array.Empty<ExpenseDto>();
    public IReadOnlyList<string> ExpenseCategories { get; init; } = Array.Empty<string>();
    public bool HasProductionCosts { get; init; }
}

public sealed record AgentSalesReportRow(string AgentName, int CompletedCount, decimal Revenue, decimal PendingAmount);
public sealed record ProductionReportRow(string ProductName, string UnitName, int ProducedBase, int DamagedBase);

public sealed class LivestockSalesViewModel
{
    public IReadOnlyList<VerifiedAgentOption> Agents { get; init; } = Array.Empty<VerifiedAgentOption>();
    public IReadOnlyList<LivestockSaleProductOption> Products { get; init; } = Array.Empty<LivestockSaleProductOption>();
    public IReadOnlyList<Platform.Web.Modules.Orders.Contracts.OrderSummaryDto> Sales { get; init; }
        = Array.Empty<Platform.Web.Modules.Orders.Contracts.OrderSummaryDto>();
}

public sealed record VerifiedAgentOption(int Id, string Name);
public sealed record LivestockSaleProductOption(int Id, string Name, string UnitName, int AvailableBase, decimal? UnitPrice);

public sealed class LivestockSaleInput
{
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue, ErrorMessage = "Pilih agen.")]
    public int AgentBusinessId { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(200, ErrorMessage = "Catatan maksimal 200 karakter.")]
    public string? Note { get; set; }

    public string? IdempotencyKey { get; set; }
    public List<LivestockSaleLineFormInput> Lines { get; set; } = new();
}

public sealed class LivestockSaleLineFormInput
{
    public int ProductId { get; set; }

    [System.ComponentModel.DataAnnotations.Range(0, 1_000_000, ErrorMessage = "Jumlah maksimal 1.000.000.")]
    public int QtyBase { get; set; }
}
