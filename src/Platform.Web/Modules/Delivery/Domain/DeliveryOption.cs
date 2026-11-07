using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Delivery.Domain;

/// <summary>
/// Pengaturan pengiriman/ambil sendiri toko grosir (satu baris per usaha).
/// Ongkir = DeliveryFee (tarif dasar, mencakup FreeKm pertama) + (jarak jalan - FreeKm) x FeePerKm, dibulatkan ke atas per Rp500.
/// FeePerKm = 0 berarti tarif tetap.
/// </summary>
public class DeliveryOption : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public bool OffersDelivery { get; set; }
    public bool OffersPickup { get; set; } = true;
    /// <summary>Tarif dasar pengiriman (Rp). Bila FeePerKm = 0 inilah tarif tetapnya.</summary>
    public decimal DeliveryFee { get; set; }

    /// <summary>Jarak (km) yang sudah termasuk tarif dasar.</summary>
    public decimal FreeKm { get; set; }

    /// <summary>Tambahan per km di atas FreeKm (Rp).</summary>
    public decimal FeePerKm { get; set; }

    /// <summary>Jarak jalan maksimal pengiriman (km). Null = tanpa batas.</summary>
    public decimal? MaxDistanceKm { get; set; }
    public decimal MinOrderAmount { get; set; }
    public string? DeliveryInfo { get; set; }
    public string? PickupInfo { get; set; }

}
