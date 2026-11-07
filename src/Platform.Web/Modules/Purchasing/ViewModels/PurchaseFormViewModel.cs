using System.ComponentModel.DataAnnotations;

namespace Platform.Web.Modules.Purchasing.ViewModels;

public class PurchaseLineRow
{
    public int? ProductId { get; set; }
    public string? UnitName { get; set; }
    public int? QtyPack { get; set; }
    public decimal? UnitPricePack { get; set; }
    public DateOnly? ExpiryDate { get; set; }
}

/// <summary>Pilihan produk untuk dropdown; UnitsText berformat "Item:1|Box:40" dibaca oleh purchase.js.</summary>
public sealed record ProductOption(int Id, string Name, string UnitsText);

public class PurchaseFormViewModel
{
    [Required(ErrorMessage = "Nama pemasok wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama pemasok 2–120 karakter.")]
    [Display(Name = "Pemasok / grosir")]
    public string SupplierName { get; set; } = string.Empty;

    public DateOnly? Date { get; set; }

    [StringLength(200, ErrorMessage = "Catatan maksimal 200 karakter.")]
    [Display(Name = "Catatan (opsional)")]
    public string? Note { get; set; }

    public List<PurchaseLineRow> Lines { get; set; } = NewRows();

    // diisi server, tidak diposting
    public List<ProductOption> Products { get; set; } = new();

    public static List<PurchaseLineRow> NewRows(int count = 5)
    {
        var rows = new List<PurchaseLineRow>();
        for (var i = 0; i < count; i++)
        {
            rows.Add(new PurchaseLineRow());
        }

        return rows;
    }
}
