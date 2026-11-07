using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Accounting.Domain;

/// <summary>Biaya operasional usaha (listrik, air, sewa, transportasi, dll). Bukan pembelian barang.</summary>
public class Expense : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public DateOnly Date { get; set; }
    public string Category { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }
}
