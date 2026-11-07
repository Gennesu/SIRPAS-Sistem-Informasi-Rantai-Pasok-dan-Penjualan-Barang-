namespace Platform.Web.SharedKernel;

/// <summary>
/// Satu unit kerja = satu transaksi database. SEMUA operasi bisnis yang mengubah stok, pesanan, pembayaran,
/// atau pembelian dijalankan lewat sini sehingga hasilnya "semua berhasil atau semua dibatalkan".
///
/// Aturan:
///  - Pemanggil terluar membuka transaksi; pemanggil bersarang (layanan memanggil layanan) BERGABUNG ke transaksi
///    yang sedang berjalan (tidak ada transaksi bersarang). Commit/rollback hanya diputuskan oleh yang terluar.
///  - Pada SQLite transaksi EF dimulai dengan BEGIN IMMEDIATE: kunci tulis diambil di awal, sehingga pola
///    "baca stok lalu kurangi" tidak dapat disela penulis lain (terbukti lewat PROBE; lihat docs/V3-PENGUJIAN.md).
///  - Hasil gagal (Result.Succeeded == false) atau exception -> rollback penuh dan perubahan yang belum tersimpan dibuang.
///  - Benturan concurrency token atau database terkunci diulang otomatis (maks. 4 kali) dengan data terbaru.
/// </summary>
public interface IUnitOfWork
{
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken ct = default);

    Task<Result> ExecuteAsync(Func<CancellationToken, Task<Result>> work, CancellationToken ct = default);

    /// <summary>True bila saat ini sudah berada di dalam sebuah unit kerja (transaksi terbuka).</summary>
    bool InTransaction { get; }
}
