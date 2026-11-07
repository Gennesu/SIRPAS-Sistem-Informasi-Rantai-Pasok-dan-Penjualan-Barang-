using Platform.Web.Modules.Catalog.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Domain;

public enum MovementType
{
    PurchaseIn = 1,
    ManualIn = 2,
    SaleOut = 3,
    CorrectionOut = 4,
    DamagedOut = 5,
    ExpiredOut = 6,
    OtherOut = 7,

    /// <summary>Stok grosir dikurangi saat pesanan DITERIMA (reservasi).</summary>
    OrderOut = 8,

    /// <summary>Stok grosir dikembalikan saat pesanan yang sudah diterima dibatalkan penjual.</summary>
    OrderReturnIn = 9,

    /// <summary>Hasil produksi atau panen peternakan (dicatat dari halaman Produksi).</summary>
    ProductionIn = 10,

    /// <summary>Hasil ternak dijual dan stok peternakan keluar ke agen.</summary>
    LivestockSaleOut = 11
}

/// <summary>Siklus reservasi stok: ditahan (Active) -> dilepas (Released) ATAU dipakai/dikeluarkan (Consumed). Tepat satu kali.</summary>
public enum ReservationStatus
{
    Active = 1,
    Released = 2,
    Consumed = 3
}

/// <summary>
/// Posisi stok satu produk di satu usaha, selalu dalam satuan dasar.
/// QtyBase adalah ringkasan dari InventoryMovement; jangan diubah tanpa membuat movement.
/// </summary>
public class Inventory : IBusinessOwned, IVersioned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int ProductId { get; set; }
    public int QtyBase { get; set; }

    /// <summary>
    /// V3: jumlah yang sedang DICADANGKAN pesanan yang belum diterima penjual (satuan dasar).
    /// Stok tersedia untuk dijual/dipesan = QtyBase - ReservedBase. Aturan database: 0 &lt;= ReservedBase &lt;= QtyBase.
    /// </summary>
    public int ReservedBase { get; set; }

    /// <summary>Concurrency token (dinaikkan otomatis oleh AppDbContext pada setiap UPDATE).</summary>
    public long Version { get; set; }

    /// <summary>Biaya rata-rata (moving average) per satuan dasar, 4 desimal.</summary>
    public decimal AvgCostBase { get; set; }

    /// <summary>Harga jual eceran per satuan dasar (ditentukan warung sendiri).</summary>
    public decimal SellingPriceBase { get; set; }

    public int MinStockBase { get; set; }
    public DateTime? LastPurchaseAt { get; set; }

    public Product Product { get; set; } = null!;
}

/// <summary>Buku besar stok: setiap perubahan jumlah tercatat di sini beserta alasannya.</summary>
public class InventoryMovement : IBusinessOwned
{
    public long Id { get; set; }
    public int BusinessId { get; set; }
    public int InventoryId { get; set; }
    public int ProductId { get; set; }
    public MovementType Type { get; set; }

    /// <summary>Positif = masuk, negatif = keluar (dalam satuan dasar).</summary>
    public int QtyBaseSigned { get; set; }

    /// <summary>Biaya per satuan dasar saat kejadian (masuk: harga beli; keluar: average cost saat itu).</summary>
    public decimal UnitCost { get; set; }

    public int BalanceAfter { get; set; }
    public string? RefType { get; set; }
    public int? RefId { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? CreatedByUserId { get; set; }

    /// <summary>
    /// V3: kunci operasi (idempotensi). Penyesuaian stok manual yang dikirim ulang (klik ganda, refresh, retry)
    /// dengan kunci sama tidak membuat movement kedua; dijaga unique index (BusinessId, OperationKey).
    /// </summary>
    public string? OperationKey { get; set; }

    public Inventory Inventory { get; set; } = null!;
    public Product Product { get; set; } = null!;
}

/// <summary>
/// Satu batch (lot) stok yang punya tanggal kadaluarsa. Stok tanpa tanggal kadaluarsa tidak punya batch:
/// jumlah semua batch selalu &lt;= Inventory.QtyBase, selisihnya adalah stok "tanpa tanggal kadaluarsa".
/// Barang keluar memakai aturan FEFO (First Expired, First Out): batch yang paling dekat kadaluarsa keluar lebih dulu.
/// </summary>
public class StockBatch : IBusinessOwned, IVersioned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int ProductId { get; set; }

    /// <summary>Tanggal kadaluarsa (tanggal kalender WIB).</summary>
    public DateOnly ExpiryDate { get; set; }

    /// <summary>Sisa barang di batch ini (satuan dasar).</summary>
    public int QtyRemainingBase { get; set; }

    /// <summary>V3: bagian batch ini yang sedang dicadangkan pesanan. Aturan database: 0 &lt;= ReservedBase &lt;= QtyRemainingBase.</summary>
    public int ReservedBase { get; set; }

    /// <summary>V3: nomor batch/lot dari produsen (opsional) dan tanggal produksinya, untuk penelusuran asal batch.</summary>
    public string? LotNumber { get; set; }

    public DateOnly? ProductionDate { get; set; }

    public long Version { get; set; }

    /// <summary>Total yang pernah masuk ke batch ini (satuan dasar), hanya untuk informasi.</summary>
    public int QtyReceivedBase { get; set; }

    /// <summary>Modal per satuan dasar saat batch masuk (rata-rata berbobot bila digabung).</summary>
    public decimal UnitCost { get; set; }

    public DateTime ReceivedAt { get; set; }
    public string? SourceRef { get; set; }

    public Product Product { get; set; } = null!;
}

/// <summary>
/// V3: satu baris reservasi stok untuk satu pesanan (pesanan usaha: RefType "ORDER"; pesanan pembeli akhir: "CORDER").
/// Mencatat dari batch mana barang ditahan. Perubahan Inventory.ReservedBase / StockBatch.ReservedBase SELALU punya baris di sini,
/// dan setiap baris hanya bisa berpindah dari Active ke Released atau Consumed satu kali.
/// </summary>
public class StockReservation : IBusinessOwned
{
    public long Id { get; set; }

    /// <summary>Pemilik stok = usaha PENJUAL (bukan pembeli yang membuat pesanan).</summary>
    public int BusinessId { get; set; }

    public int InventoryId { get; set; }
    public int ProductId { get; set; }

    /// <summary>Null = ditahan dari stok tanpa tanggal kadaluarsa.</summary>
    public int? BatchId { get; set; }

    public DateOnly? ExpiryDate { get; set; }
    public int QtyBase { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Active;
    public string RefType { get; set; } = string.Empty;
    public int RefId { get; set; }
    public int? RefItemId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}
