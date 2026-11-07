using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Platform.Web.Modules.Catalog.ViewModels;

/// <summary>Baris kemasan tambahan; baris kosong diabaikan (karena itu semua properti nullable).</summary>
public class UnitRow
{
    public string? UnitName { get; set; }
    public int? ConversionToBase { get; set; }
    public decimal? WholesalePrice { get; set; }
}

public class ProductFormViewModel
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Nama produk wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama produk 2–120 karakter.")]
    [Display(Name = "Nama produk")]
    public string Name { get; set; } = string.Empty;

    [StringLength(40, ErrorMessage = "SKU maksimal 40 karakter.")]
    [Display(Name = "SKU (opsional)")]
    public string? Sku { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Pilih kategori.")]
    [Display(Name = "Kategori")]
    public int CategoryId { get; set; }

    [StringLength(300, ErrorMessage = "Deskripsi maksimal 300 karakter.")]
    [Display(Name = "Deskripsi (opsional)")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Satuan dasar wajib diisi.")]
    [StringLength(20, ErrorMessage = "Satuan dasar maksimal 20 karakter.")]
    [Display(Name = "Satuan dasar")]
    public string BaseUnitName { get; set; } = "Item";

    [Display(Name = "Harga grosir per satuan dasar")]
    public decimal? BaseWholesalePrice { get; set; }

    [Range(1, 1_000_000, ErrorMessage = "MOQ harus 1–1.000.000.")]
    [Display(Name = "Minimum pesanan / MOQ (satuan dasar)")]
    public int MoqBase { get; set; } = 1;

    public List<UnitRow> Units { get; set; } = NewRows();

    /// <summary>Gambar produk yang diunggah pemilik usaha (opsional; JPG/PNG/WebP/GIF, maks. 2 MB).</summary>
    [Display(Name = "Gambar produk (opsional)")]
    public IFormFile? Image { get; set; }

    /// <summary>Centang untuk menghapus gambar yang sedang dipakai.</summary>
    public bool RemoveImage { get; set; }

    // ---- diisi server untuk tampilan (tidak diposting) ----
    public string? ImagePath { get; set; }

    public bool ShowPackaging { get; set; }
    public bool ShowWholesale { get; set; }
    public List<SelectListItem> Categories { get; set; } = new();

    public static List<UnitRow> NewRows(int count = 3)
    {
        var rows = new List<UnitRow>();
        for (var i = 0; i < count; i++)
        {
            rows.Add(new UnitRow());
        }

        return rows;
    }
}
