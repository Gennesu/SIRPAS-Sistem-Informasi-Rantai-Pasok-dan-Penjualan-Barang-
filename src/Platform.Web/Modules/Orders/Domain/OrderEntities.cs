using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Orders.Domain;

public enum OrderStatus
{
    Pending = 1,
    Accepted = 2,
    Preparing = 3,
    Ready = 4,
    Delivered = 5,
    PickedUp = 6,
    Completed = 7,
    Rejected = 8,
    Cancelled = 9
}

/// <summary>
/// Pesanan warung (pembeli) ke grosir (penjual). Dimiliki dua usaha: hanya keduanya dan admin yang dapat melihatnya
/// (filter ITwoPartyOwned). Nama, harga, isi kemasan, dan data produk disimpan sebagai snapshot.
/// </summary>
public class Order : ITwoPartyOwned, IVersioned
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public int BuyerBusinessId { get; set; }
    public int SellerBusinessId { get; set; }
    public string BuyerName { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public FulfillmentType Fulfillment { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }

    /// <summary>Perkiraan jarak jalan (km) saat pesanan dibuat; dasar perhitungan ongkir. Null bila lokasi belum lengkap.</summary>
    public double? DeliveryDistanceKm { get; set; }
    public decimal TotalAmount { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? Note { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// V3: kunci anti-duplikat pembuatan pesanan (unik per usaha pembeli). Klik ganda / refresh / kirim ulang dengan kunci
    /// sama mengembalikan pesanan yang sudah ada, bukan membuat pesanan kedua.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>V3: concurrency token; transisi status serentak tidak dapat saling menimpa.</summary>
    public long Version { get; set; }

    public List<OrderItem> Items { get; set; } = new();

    /// <summary>Jejak perubahan status (untuk pelacakan per fase seperti di marketplace umum).</summary>
    public List<OrderEvent> Events { get; set; } = new();
}

/// <summary>Satu kejadian pada pesanan: kapan status berubah menjadi apa (dan catatan/alasannya).</summary>
public class OrderEvent : ITwoPartyOwned
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int BuyerBusinessId { get; set; }
    public int SellerBusinessId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime At { get; set; }
    public string? Note { get; set; }
}

public class OrderItem : ITwoPartyOwned
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public int BuyerBusinessId { get; set; }
    public int SellerBusinessId { get; set; }

    /// <summary>Id produk milik GROSIR (tanpa foreign key: produk milik usaha lain).</summary>
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? ProductSku { get; set; }
    public int CategoryId { get; set; }
    public string BaseUnitName { get; set; } = string.Empty;

    /// <summary>Seluruh satuan produk saat dipesan, format "Item:1|Box:40|Dus:160" (untuk membuat produk di katalog warung).</summary>
    public string UnitsSnapshot { get; set; } = string.Empty;

    public string UnitName { get; set; } = string.Empty;
    public int QtyPack { get; set; }
    public int ConversionSnapshot { get; set; }
    public decimal UnitPrice { get; set; }
    public int QtyBase { get; set; }
    public decimal Subtotal { get; set; }

    /// <summary>Average cost grosir per satuan dasar saat pesanan diterima; dipakai untuk mengembalikan stok bila dibatalkan.</summary>
    public decimal SellerUnitCost { get; set; }

    /// <summary>
    /// Barang dikirim dari batch kadaluarsa mana, format "yyyy-MM-dd:jumlah|yyyy-MM-dd:jumlah" (satuan dasar).
    /// Diisi saat grosir menerima pesanan; dipakai untuk mengembalikan stok bila dibatalkan dan untuk
    /// membuat batch yang sama di stok warung saat barang diterima.
    /// </summary>
    public string? ExpirySnapshot { get; set; }

    /// <summary>Baris dari paket anti-mubazir (bundle): id paket, namanya, jumlah paket, dan batch grosir yang harus dipakai.</summary>
    public int? BundleId { get; set; }

    public string? BundleName { get; set; }
    public int? BundleQty { get; set; }
    public int? SourceBatchId { get; set; }

    /// <summary>Harga normal baris ini (bila dibeli di luar paket); selisihnya dengan Subtotal = penghematan warung.</summary>
    public decimal? NormalSubtotal { get; set; }
}
