using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Stock.Contracts;

/// <summary>Satu baris yang harus ditahan. RefItemId = id baris pesanan (OrderItem / ConsumerOrderItem). BatchId diisi bila batch ditentukan (mis. paket anti-mubazir).</summary>
public sealed record ReserveLine(int RefItemId, int ProductId, int QtyBase, int? BatchId = null);

/// <summary>Hasil penahanan: dari batch mana (null = stok tanpa tanggal kadaluarsa) dan berapa banyak.</summary>
public sealed record ReservedAllocation(int RefItemId, int ProductId, int? BatchId, DateOnly? ExpiryDate, int QtyBase);

/// <summary>Hasil pengeluaran stok dari reservasi: modal (average cost) saat keluar dan batch asal, per baris pesanan.</summary>
public sealed record ConsumedItem(int RefItemId, int ProductId, decimal UnitCost, IReadOnlyList<ReservedAllocation> Allocations);

public static class ReservationRefTypes
{
    /// <summary>Pesanan usaha (Order) antara grosir dan warung.</summary>
    public const string Order = "ORDER";

    /// <summary>Pesanan pembeli akhir (ConsumerOrder).</summary>
    public const string ConsumerOrder = "CORDER";
}

/// <summary>
/// Reservasi stok (SATU mekanisme untuk seluruh aplikasi):
///  1. ReserveAsync  : saat pesanan DIBUAT, stok tersedia (fisik - dicadangkan) ditahan secara atomik; pembeli lain tidak dapat memakainya.
///  2. ConsumeAsync  : saat penjual MENERIMA pesanan, penahanan menjadi pengeluaran fisik (stok berkurang, movement tercatat, modal dibekukan).
///  3. ReleaseAsync  : saat pesanan ditolak/dibatalkan sebelum diterima, penahanan dilepas.
/// Setiap baris reservasi hanya berpindah dari Active ke Released ATAU Consumed, tepat satu kali.
/// Layanan ini sengaja menulis ke stok USAHA PENJUAL walaupun dipanggil oleh pembeli (lintas tenant yang terkontrol);
/// semua akses dibatasi pada sellerBusinessId yang diberikan dan selalu berjalan di dalam unit kerja (transaksi).
/// </summary>
public interface IStockReservationService
{
    Task<Result<IReadOnlyList<ReservedAllocation>>> ReserveAsync(
        int sellerBusinessId, string refType, int refId, IReadOnlyList<ReserveLine> lines, CancellationToken ct = default);

    /// <summary>Melepas semua reservasi Active milik pesanan. Aman dipanggil ulang (tidak ada baris Active = tidak berbuat apa-apa).</summary>
    Task<Result> ReleaseAsync(int sellerBusinessId, string refType, int refId, CancellationToken ct = default);

    /// <summary>Mengubah semua reservasi Active menjadi pengeluaran fisik. Gagal bila tidak ada reservasi aktif atau batch sudah kedaluwarsa.</summary>
    Task<Result<IReadOnlyList<ConsumedItem>>> ConsumeAsync(
        int sellerBusinessId, string refType, int refId, string? reason, CancellationToken ct = default);
}

public sealed record IntegrityViolation(int BusinessId, int? ProductId, string Rule, string Detail);

/// <summary>
/// Rekonsiliasi stok (tidak mengarang nilai): memeriksa invarian antara Inventory, StockBatch, StockMovement, dan StockReservation
/// dan melaporkan pelanggaran. Dipakai tes dan halaman admin; TIDAK memperbaiki data secara otomatis.
/// </summary>
public interface IStockIntegrityService
{
    Task<IReadOnlyList<IntegrityViolation>> CheckAsync(int? businessId = null, CancellationToken ct = default);
}
