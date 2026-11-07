using System.ComponentModel.DataAnnotations;

namespace Platform.Web.Modules.Accounting.ViewModels;

public class ExpenseInput
{
    [Required(ErrorMessage = "Pilih kategori.")]
    public string Category { get; set; } = string.Empty;

    [Range(typeof(decimal), "1", "1000000000000", ErrorMessage = "Jumlah harus lebih dari 0.")]
    public decimal Amount { get; set; }

    public DateOnly? Date { get; set; }

    [StringLength(200, ErrorMessage = "Catatan maksimal 200 karakter.")]
    public string? Note { get; set; }
}
