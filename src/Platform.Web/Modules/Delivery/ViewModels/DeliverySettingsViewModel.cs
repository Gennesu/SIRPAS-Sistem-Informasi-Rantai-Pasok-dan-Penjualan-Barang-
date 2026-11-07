using System.ComponentModel.DataAnnotations;

namespace Platform.Web.Modules.Delivery.ViewModels;

public class DeliverySettingsViewModel
{
    [Display(Name = "Sediakan pengiriman")]
    public bool OffersDelivery { get; set; }

    [Display(Name = "Sediakan ambil sendiri (pickup)")]
    public bool OffersPickup { get; set; } = true;

    [Range(0, 1_000_000_000, ErrorMessage = "Biaya pengiriman tidak valid.")]
    [Display(Name = "Tarif dasar pengiriman (Rp)")]
    public decimal DeliveryFee { get; set; }

    [Range(0, 1_000, ErrorMessage = "Jarak tarif dasar tidak valid.")]
    [Display(Name = "Jarak yang sudah termasuk tarif dasar (km)")]
    public decimal FreeKm { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Tarif per km tidak valid.")]
    [Display(Name = "Tambahan per km (Rp)")]
    public decimal FeePerKm { get; set; }

    [Range(0.1, 5_000, ErrorMessage = "Jangkauan harus lebih dari 0 km.")]
    [Display(Name = "Jangkauan maksimal pengiriman (km)")]
    public decimal? MaxDistanceKm { get; set; }

    [Range(0, 1_000_000_000, ErrorMessage = "Minimum pesanan tidak valid.")]
    [Display(Name = "Minimum pesanan (Rp)")]
    public decimal MinOrderAmount { get; set; }

    [StringLength(200, ErrorMessage = "Maksimal 200 karakter.")]
    [Display(Name = "Keterangan pengiriman")]
    public string? DeliveryInfo { get; set; }

    [StringLength(200, ErrorMessage = "Maksimal 200 karakter.")]
    [Display(Name = "Keterangan ambil sendiri (alamat/jam)")]
    public string? PickupInfo { get; set; }

}
