using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Catalog.Contracts;

public sealed record CategoryDto(int Id, string Name);

public sealed record ProductUnitDto(string UnitName, int ConversionToBase, bool IsBase, decimal? WholesalePrice);

public sealed record ProductDto(
    int Id,
    string Name,
    string? Sku,
    int CategoryId,
    string CategoryName,
    string? Description,
    string BaseUnitName,
    bool IsActive,
    IReadOnlyList<ProductUnitDto> Units,
    int? SourceProductId = null,
    int MoqBase = 1,
    string? ImagePath = null);

/// <summary>Satuan kemasan tambahan (selain satuan dasar). ConversionToBase minimal 2.</summary>
public sealed record UnitInput(string UnitName, int ConversionToBase, decimal? WholesalePrice);

public sealed record SaveProductRequest(
    string Name,
    string? Sku,
    int CategoryId,
    string? Description,
    string BaseUnitName,
    decimal? BaseWholesalePrice,
    IReadOnlyList<UnitInput> ExtraUnits,
    int? SourceProductId = null,
    int MoqBase = 1,
    string? ImagePath = null,
    bool ClearImage = false);

/// <summary>Data produk grosir (dari snapshot pesanan) untuk membuat/menghubungkan produk di katalog warung.</summary>
public sealed record SourceProductSnapshot(
    int SourceProductId,
    string Name,
    string? Sku,
    int CategoryId,
    string BaseUnitName,
    IReadOnlyList<UnitInput> Units);

public interface IProductService
{
    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductDto>> ListAsync(string? search, bool includeInactive, CancellationToken ct = default);
    Task<Result<ProductDto>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<ProductDto>> CreateAsync(SaveProductRequest request, CancellationToken ct = default);
    Task<Result<ProductDto>> UpdateAsync(int id, SaveProductRequest request, CancellationToken ct = default);

    /// <summary>Produk tidak pernah dihapus (riwayat transaksi merujuk ke produk); cukup dinonaktifkan.</summary>
    Task<Result> SetActiveAsync(int id, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Mencari produk milik usaha ini yang sesuai dengan produk grosir (SourceProductId, lalu SKU, lalu nama);
    /// bila belum ada, produk baru dibuat dari snapshot. Mengembalikan id produk.
    /// </summary>
    Task<Result<int>> FindOrCreateFromSourceAsync(SourceProductSnapshot source, CancellationToken ct = default);

    // Kategori global: hanya admin.
    Task<Result> AddCategoryAsync(string name, CancellationToken ct = default);
    Task<Result> RenameCategoryAsync(int id, string name, CancellationToken ct = default);
}

/// <summary>Hitungan satuan murni (tanpa database) agar mudah diuji dan dijelaskan.</summary>
public static class PackagingMath
{
    /// <summary>2 Box x 40 = 80 Item.</summary>
    public static int ToBase(int qtyPack, int conversionToBase) => checked(qtyPack * conversionToBase);

    /// <summary>Rp120.000 per Box isi 40 = Rp3.000 per Item.</summary>
    public static decimal CostPerBase(decimal pricePerPack, int conversionToBase) =>
        Money.RoundCost(pricePerPack / conversionToBase);

    /// <summary>85 Item (Box = 40) menjadi "2 Box + 5 Item".</summary>
    public static string Breakdown(int qtyBase, IEnumerable<ProductUnitDto> units)
    {
        var list = units.ToList();
        var baseName = list.Where(u => u.ConversionToBase == 1).Select(u => u.UnitName).FirstOrDefault() ?? "Item";
        if (qtyBase <= 0)
        {
            return $"0 {baseName}";
        }

        var parts = new List<string>();
        var remaining = qtyBase;
        foreach (var unit in list.Where(u => u.ConversionToBase > 1).OrderByDescending(u => u.ConversionToBase))
        {
            if (remaining >= unit.ConversionToBase)
            {
                parts.Add($"{remaining / unit.ConversionToBase} {unit.UnitName}");
                remaining %= unit.ConversionToBase;
            }
        }

        if (remaining > 0 || parts.Count == 0)
        {
            parts.Add($"{remaining} {baseName}");
        }

        return string.Join(" + ", parts);
    }
}
