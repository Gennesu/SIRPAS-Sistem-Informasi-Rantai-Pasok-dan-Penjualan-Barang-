using System.ComponentModel.DataAnnotations;
using Platform.Web.Modules.Stock.Domain;

namespace Platform.Web.Modules.Stock.ViewModels;

public class AdjustStockInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Pilih produk.")]
    public int ProductId { get; set; }

    public MovementType Type { get; set; } = MovementType.ManualIn;

    [Range(1, 1_000_000, ErrorMessage = "Jumlah harus 1–1.000.000.")]
    public int Qty { get; set; }

    public decimal? CostPerBase { get; set; }

    [StringLength(200, ErrorMessage = "Alasan maksimal 200 karakter.")]
    public string? Reason { get; set; }

    /// <summary>Tanggal kadaluarsa barang yang ditambahkan (hanya untuk tambah manual, opsional).</summary>
    public DateOnly? ExpiryDate { get; set; }
}

public class TagExpiryInput
{
    [Range(1, int.MaxValue, ErrorMessage = "Pilih produk.")]
    public int ProductId { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "Jumlah harus 1–1.000.000.")]
    public int Qty { get; set; }

    [Required(ErrorMessage = "Tanggal kadaluarsa wajib diisi.")]
    public DateOnly? ExpiryDate { get; set; }
}
