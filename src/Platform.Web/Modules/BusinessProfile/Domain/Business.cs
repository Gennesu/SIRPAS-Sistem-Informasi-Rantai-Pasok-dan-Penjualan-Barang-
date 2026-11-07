namespace Platform.Web.Modules.BusinessProfile.Domain;

/// <summary>
/// Peran USAHA di rantai pasok: warung, grosir, atau peternakan. Berbeda dari role akun (hak akses) dan dari
/// jenis bisnis/template (GROCERY, CLOTHING, ...): ketiganya tidak boleh saling menggantikan.
/// </summary>
public enum MarketRole
{
    Retail = 1,
    Wholesale = 2,
    /// <summary>Nilai lama untuk membaca data tersimpan; tidak didukung untuk usaha baru.</summary>
    Retired = 3,
    /// <summary>Peternakan: pemasok telur dan ayam.</summary>
    Livestock = 4
}

public enum VerificationStatus
{
    Pending = 1,
    Verified = 2,
    Rejected = 3
}

/// <summary>
/// Satu Business = satu tenant. Business.Id adalah BusinessId yang dipakai seluruh entity bisnis lain.
/// Business sendiri TIDAK dikenai tenant filter karena marketplace perlu membaca grosir lintas tenant;
/// akses dikendalikan di BusinessService.
/// </summary>
public class Business
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int BusinessTypeId { get; set; }
    public MarketRole MarketRole { get; set; }
    public string Address { get; set; } = string.Empty;

    /// <summary>Koordinat opsional (Fase 7 menambahkan pemilihan lewat peta).</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string Phone { get; set; } = string.Empty;
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Pending;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Warung: izinkan grosir langganan melihat RINGKASAN penjualan barang dari grosir itu (jumlah terjual per produk,
    /// bukan struk, harga jual, atau keuntungan). Dipakai grosir untuk menawarkan barang yang pasti laku.
    /// </summary>
    public bool ShareSalesData { get; set; } = true;

    public BusinessType BusinessType { get; set; } = null!;
}
