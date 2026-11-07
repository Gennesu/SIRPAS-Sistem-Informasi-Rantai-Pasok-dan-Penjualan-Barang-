using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using Platform.Web.Modules.BusinessProfile.Domain;

namespace Platform.Web.Modules.BusinessProfile.ViewModels;

public class RegisterViewModel
{
    // ---- Akun pemilik ----
    [Required(ErrorMessage = "Nama lengkap wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama lengkap 2–120 karakter.")]
    [Display(Name = "Nama lengkap pemilik")]
    public string OwnerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    [StringLength(256, ErrorMessage = "Email maksimal 256 karakter.")]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password wajib diisi.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password 8–100 karakter.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ulangi password.")]
    [Compare(nameof(Password), ErrorMessage = "Konfirmasi password tidak sama.")]
    [DataType(DataType.Password)]
    [Display(Name = "Ulangi password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    // ---- Usaha ----
    [Required(ErrorMessage = "Nama usaha wajib diisi.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "Nama usaha 3–120 karakter.")]
    [Display(Name = "Nama usaha")]
    public string BusinessName { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Pilih jenis bisnis.")]
    [Display(Name = "Jenis bisnis")]
    public int BusinessTypeId { get; set; }

    [Display(Name = "Daftar sebagai")]
    public MarketRole MarketRole { get; set; } = MarketRole.Retail;

    [Required(ErrorMessage = "Alamat wajib diisi.")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Alamat 5–300 karakter.")]
    [Display(Name = "Alamat usaha")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nomor kontak wajib diisi.")]
    [StringLength(30, ErrorMessage = "Nomor kontak maksimal 30 karakter.")]
    [Display(Name = "Nomor kontak")]
    public string Phone { get; set; } = string.Empty;

    [Range(-90.0, 90.0, ErrorMessage = "Latitude harus di antara -90 dan 90.")]
    [Display(Name = "Latitude (opsional)")]
    public double? Latitude { get; set; }

    [Range(-180.0, 180.0, ErrorMessage = "Longitude harus di antara -180 dan 180.")]
    [Display(Name = "Longitude (opsional)")]
    public double? Longitude { get; set; }

    // Diisi server untuk dropdown; tidak diposting dari form.
    public List<SelectListItem> BusinessTypes { get; set; } = new();
}
