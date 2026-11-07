using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Rescue.Domain;

public enum OfferStatus
{
    Sent = 1,
    Accepted = 2,
    Declined = 3,
    Withdrawn = 4
}

/// <summary>
/// Paket anti-mubazir: beberapa barang yang mendekati kadaluarsa dijual bersama dengan harga miring supaya laku
/// sebelum terbuang. Milik grosir (penjual). Setiap isi paket menunjuk ke satu batch stok grosir,
/// sehingga barang yang keluar pasti batch yang dimaksud (bukan batch lain yang masih lama).
/// </summary>
public class RescueBundle : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Harga per paket (yang dibayar warung).</summary>
    public decimal Price { get; set; }

    /// <summary>Harga normal per paket (jumlah harga grosir normal seluruh isinya), snapshot saat paket dibuat.</summary>
    public decimal NormalPrice { get; set; }

    public int DiscountPercent { get; set; }

    /// <summary>Paket bisa dipesan sampai tanggal ini (WIB). Tidak pernah melewati kadaluarsa barang paling dekat.</summary>
    public DateOnly ValidUntil { get; set; }

    /// <summary>Privat = tidak tampil di marketplace; hanya warung yang menerima penawaran yang melihatnya.</summary>
    public bool IsPrivate { get; set; }

    public bool IsEnded { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public List<RescueBundleItem> Items { get; set; } = new();
}

public class RescueBundleItem : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int BundleId { get; set; }

    /// <summary>Produk dan batch milik grosir (tanpa foreign key: dibaca lintas usaha oleh warung pembeli).</summary>
    public int ProductId { get; set; }

    public int BatchId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string BaseUnitName { get; set; } = string.Empty;

    /// <summary>Jumlah per satu paket (satuan dasar).</summary>
    public int QtyBase { get; set; }

    public DateOnly ExpiryDate { get; set; }

    /// <summary>Harga normal per satuan dasar saat paket dibuat.</summary>
    public decimal NormalUnitPriceBase { get; set; }
}

/// <summary>
/// Penawaran grosir ke satu warung tertentu atas satu paket ("Warung Maju paling laku jual susu, tawarkan stok yang
/// 20 hari lagi kadaluarsa"). Dimiliki dua usaha (filter ITwoPartyOwned). Kedaluwarsa penawaran dihitung dari ExpiresAt.
/// </summary>
public class RescueOffer : ITwoPartyOwned
{
    public int Id { get; set; }

    /// <summary>Id paket (tanpa foreign key: paket milik grosir, dibaca pembeli lewat layanan Rescue).</summary>
    public int BundleId { get; set; }

    public int SellerBusinessId { get; set; }
    public int BuyerBusinessId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string BuyerName { get; set; } = string.Empty;
    public string BundleTitle { get; set; } = string.Empty;
    public string? Message { get; set; }

    /// <summary>Jumlah paket yang disarankan grosir untuk warung ini (berdasarkan kecepatan jual warung).</summary>
    public int SuggestedBundles { get; set; } = 1;

    public OfferStatus Status { get; set; } = OfferStatus.Sent;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RespondedAt { get; set; }
    public int? OrderId { get; set; }
}
