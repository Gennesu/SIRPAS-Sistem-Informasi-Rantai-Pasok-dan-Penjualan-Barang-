namespace Platform.Web.Modules.Insights.Contracts;

/// <summary>Penjualan satu produk grosir di satu warung langganan (ringkasan, bukan struk).</summary>
public sealed record WarungDemandDto(
    int BusinessId,
    string Name,
    string Address,
    double? DistanceKm,
    int SoldQtyBase,
    double AvgPerDay,
    int StockAtWarungBase,
    double? DaysOfCover,
    DateTime? LastOrderAt);

public sealed record ProductDemandDto(
    int ProductId,
    string ProductName,
    string BaseUnitName,
    int TotalSoldBase,
    int WarungCount,
    int MyStockBase,
    IReadOnlyList<WarungDemandDto> Warungs);

/// <summary>
/// Data penjualan warung untuk grosir: "produk mana yang paling laku, dan di warung mana".
/// Ini satu-satunya tempat data penjualan warung dibaca lintas usaha (IgnoreQueryFilters), dengan batasan:
///  1. hanya untuk grosir yang sedang login;
///  2. hanya warung yang pernah memesan ke grosir itu (pesanan diterima atau lebih lanjut);
///  3. hanya warung yang mengaktifkan "Bagikan ringkasan penjualan" di profilnya;
///  4. hanya angka agregat jumlah terjual per produk grosir (tanpa struk, harga jual, atau keuntungan).
/// </summary>
public interface ISellThroughService
{
    /// <summary>Penjualan N hari terakhir per produk grosir, dengan rincian per warung (terlaris lebih dulu).</summary>
    Task<IReadOnlyList<ProductDemandDto>> GetDemandAsync(int days = 30, int? productId = null, CancellationToken ct = default);

    /// <summary>Jumlah warung langganan yang tidak membagikan data penjualan (untuk catatan di layar).</summary>
    Task<int> CountHiddenCustomersAsync(CancellationToken ct = default);
}
