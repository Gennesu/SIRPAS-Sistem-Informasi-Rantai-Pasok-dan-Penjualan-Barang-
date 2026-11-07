using System.ComponentModel.DataAnnotations;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;

namespace Platform.Web.Modules.Consumers.ViewModels;

public sealed class RegisterConsumerViewModel
{
    [Required(ErrorMessage = "Nama lengkap wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama 2–120 karakter.")]
    [Display(Name = "Nama lengkap")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email wajib diisi.")]
    [EmailAddress(ErrorMessage = "Format email tidak valid.")]
    [StringLength(160)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Kata sandi wajib diisi.")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Kata sandi minimal 8 karakter.")]
    [DataType(DataType.Password)]
    [Display(Name = "Kata sandi")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nomor telepon wajib diisi.")]
    [StringLength(20, ErrorMessage = "Maksimal 20 karakter.")]
    [Display(Name = "Nomor telepon")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Alamat wajib diisi.")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Alamat 5–300 karakter.")]
    [Display(Name = "Alamat")]
    public string Address { get; set; } = string.Empty;

    [Range(-90, 90, ErrorMessage = "Garis lintang -90 sampai 90.")]
    [Display(Name = "Garis lintang (opsional)")]
    public double? Latitude { get; set; }

    [Range(-180, 180, ErrorMessage = "Garis bujur -180 sampai 180.")]
    [Display(Name = "Garis bujur (opsional)")]
    public double? Longitude { get; set; }
}

public sealed class ConsumerProfileViewModel
{
    [Required(ErrorMessage = "Nama lengkap wajib diisi.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "Nama 2–120 karakter.")]
    [Display(Name = "Nama lengkap")]
    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nomor telepon wajib diisi.")]
    [StringLength(20)]
    [Display(Name = "Nomor telepon")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Alamat wajib diisi.")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Alamat 5–300 karakter.")]
    [Display(Name = "Alamat")]
    public string Address { get; set; } = string.Empty;

    [Range(-90, 90)]
    [Display(Name = "Garis lintang")]
    public double? Latitude { get; set; }

    [Range(-180, 180)]
    [Display(Name = "Garis bujur")]
    public double? Longitude { get; set; }
}

/// <summary>Isi form belanja di satu warung. Harga dan promo TIDAK ikut dikirim: server menghitung ulang semuanya.</summary>
public sealed class ShopOrderInput
{
    public int SellerBusinessId { get; set; }
    public FulfillmentType Fulfillment { get; set; } = FulfillmentType.Pickup;
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.CashOnPickup;

    [StringLength(300)]
    public string? DeliveryAddress { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }

    /// <summary>ProductId -> jumlah (satuan dasar). Baris berjumlah 0 diabaikan.</summary>
    public Dictionary<int, int> Qty { get; set; } = new();

    /// <summary>Dibuat saat halaman konfirmasi ditampilkan; pengiriman ulang dengan kunci sama tidak membuat pesanan kedua.</summary>
    [StringLength(64)]
    public string? IdempotencyKey { get; set; }
}
