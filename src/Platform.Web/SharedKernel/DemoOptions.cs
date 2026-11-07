namespace Platform.Web.SharedKernel;

/// <summary>Pengaturan mode demo (section "Demo" di appsettings).</summary>
public sealed class DemoOptions
{
    /// <summary>Isi database dengan data dummy (akun demo, produk, riwayat penjualan).</summary>
    public bool SeedDummyData { get; set; }

    /// <summary>
    /// PERINGATAN: bila true, SELURUH data (akun, transaksi, stok) dihapus lalu dibuat ulang setiap aplikasi dijalankan.
    /// Bawaan false. Aktifkan hanya secara sadar (appsettings atau variabel lingkungan Demo__ResetDatabaseOnStart=true)
    /// dan matikan lagi setelah selesai. Sebelum menghapus, file SQLite dicadangkan ke *.bak-waktu.
    /// </summary>
    public bool ResetDatabaseOnStart { get; set; }
}
