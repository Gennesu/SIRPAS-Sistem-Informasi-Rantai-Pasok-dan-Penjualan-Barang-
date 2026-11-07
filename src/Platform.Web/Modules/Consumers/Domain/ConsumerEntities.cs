using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Domain;

/// <summary>Cara bayar pesanan pembeli akhir. SEMUANYA demo lokal: tidak ada uang nyata, tidak ada penyedia pembayaran.</summary>
public enum PaymentMethod
{
    /// <summary>Bayar tunai ketika mengambil barang di warung.</summary>
    CashOnPickup = 1,

    /// <summary>Bayar tunai ketika barang diterima (diantar).</summary>
    CashOnDelivery = 2,

    /// <summary>Simulasi pembayaran lokal dengan tombol konfirmasi demo (bukan transaksi bank).</summary>
    DemoSimulation = 3
}

/// <summary>Status satu percobaan pembayaran.</summary>
public enum PaymentStatus
{
    Pending = 1,
    Success = 2,
    Failed = 3,
    Cancelled = 4,

    /// <summary>Pembayaran sukses lalu pesanan dibatalkan/ditolak: dikembalikan (simulasi, tidak ada uang nyata).</summary>
    Refunded = 5
}

/// <summary>Status pembayaran pada tingkat pesanan (terpisah dari status pesanan).</summary>
public enum OrderPaymentStatus
{
    /// <summary>Metode tunai: belum dibayar, akan lunas saat barang diambil/diterima.</summary>
    Unpaid = 1,

    /// <summary>Simulasi: menunggu pembeli menekan tombol konfirmasi demo.</summary>
    Pending = 2,

    Paid = 3,

    /// <summary>Percobaan simulasi terakhir gagal; pembeli boleh mencoba lagi.</summary>
    Failed = 4,

    /// <summary>Pesanan dibatalkan/ditolak sebelum dibayar.</summary>
    Cancelled = 5,

    /// <summary>Sudah dibayar (simulasi) lalu pesanan dibatalkan/ditolak: dikembalikan.</summary>
    Refunded = 6
}

/// <summary>Profil pembeli akhir (pengguna individu). Tidak terikat BusinessId.</summary>
public class ConsumerProfile : IUserOwned
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Pesanan pembeli akhir ke satu warung. Dimiliki dua pihak: pembeli (ConsumerUserId) dan warung penjual
/// (filter IConsumerOwned). Harga, nama produk, dan modal disimpan sebagai snapshot pada item.
/// </summary>
public class ConsumerOrder : IConsumerOwned, IVersioned
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public int ConsumerUserId { get; set; }
    public string ConsumerName { get; set; } = string.Empty;
    public string? ConsumerPhone { get; set; }
    public int SellerBusinessId { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public FulfillmentType Fulfillment { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DeliveryFee { get; set; }
    public double? DeliveryDistanceKm { get; set; }
    public decimal TotalAmount { get; set; }
    public string? DeliveryAddress { get; set; }
    public string? Note { get; set; }
    public string? RejectReason { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public OrderPaymentStatus PaymentStatus { get; set; } = OrderPaymentStatus.Unpaid;

    /// <summary>Kunci anti-duplikat checkout (unik per pembeli). Pengiriman ulang dengan kunci sama mengembalikan pesanan yang sama.</summary>
    public string? IdempotencyKey { get; set; }

    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public List<ConsumerOrderItem> Items { get; set; } = new();
    public List<ConsumerOrderEvent> Events { get; set; } = new();
}

public class ConsumerOrderItem : IConsumerOwned
{
    public int Id { get; set; }
    public int ConsumerOrderId { get; set; }
    public int ConsumerUserId { get; set; }
    public int SellerBusinessId { get; set; }

    /// <summary>Id produk milik warung penjual.</summary>
    public int ProductId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string BaseUnitName { get; set; } = string.Empty;
    public int QtyBase { get; set; }

    /// <summary>Harga per satuan dasar yang dibayar (sudah termasuk promo bila ada), snapshot.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Harga normal per satuan dasar saat dipesan (snapshot) untuk menghitung penghematan.</summary>
    public decimal RegularUnitPrice { get; set; }

    public bool IsPromo { get; set; }
    public int PromoPercent { get; set; }

    /// <summary>Tanggal kadaluarsa batch tempat barang ini berasal (bila ada); ditampilkan untuk produk promo.</summary>
    public DateOnly? ExpiryDate { get; set; }

    public decimal Subtotal { get; set; }

    /// <summary>Modal (average cost) warung per satuan dasar saat barang dikeluarkan; dasar HPP.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>"yyyy-MM-dd:jumlah|..." batch asal barang saat dikeluarkan (untuk mengembalikan stok bila penjual membatalkan).</summary>
    public string? ExpirySnapshot { get; set; }
}

public class ConsumerOrderEvent : IConsumerOwned
{
    public int Id { get; set; }
    public int ConsumerOrderId { get; set; }
    public int ConsumerUserId { get; set; }
    public int SellerBusinessId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime At { get; set; }
    public string? Note { get; set; }
}

/// <summary>
/// Satu percobaan pembayaran demo. Database menjamin paling banyak SATU percobaan berstatus Success per pesanan
/// (unique index berfilter), sehingga konfirmasi ganda tidak dapat mencatat pembayaran sukses dua kali.
/// </summary>
public class Payment : IConsumerOwned, IVersioned
{
    public int Id { get; set; }

    /// <summary>Nomor referensi demo, mis. PAY-000012 (untuk diperiksa saat presentasi).</summary>
    public string Reference { get; set; } = string.Empty;

    public int ConsumerOrderId { get; set; }
    public int ConsumerUserId { get; set; }
    public int SellerBusinessId { get; set; }
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? FailureReason { get; set; }
    public long Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Promo warung untuk stok yang mendekati kadaluarsa: unit dari batch yang kadaluarsa dalam WithinDays hari
/// dijual dengan diskon DiscountPercent kepada pembeli akhir (dan terjual lebih dulu lewat FEFO).
/// </summary>
public class ConsumerPromo : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int ProductId { get; set; }
    public int DiscountPercent { get; set; }
    public int WithinDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
