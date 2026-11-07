using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Catalog.Domain;

/// <summary>Kategori global (dikelola Admin). Bukan milik tenant.</summary>
public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Produk milik satu usaha. Stok TIDAK disimpan di sini (lihat modul Stock); produk hanya
/// mendefinisikan satuan dasar dan kemasan.
/// </summary>
public class Product : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public int CategoryId { get; set; }
    public string? Description { get; set; }

    /// <summary>Satuan dasar (Item, Piece, Unit). Seluruh stok dihitung dalam satuan ini.</summary>
    public string BaseUnitName { get; set; } = "Item";

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Minimum order quantity (MOQ) dalam satuan dasar saat produk dijual ke usaha lain. 1 = tanpa minimum.
    /// </summary>
    public int MoqBase { get; set; } = 1;

    /// <summary>Id produk grosir asal (diisi saat produk dibuat/dihubungkan dari pesanan marketplace). Tanpa foreign key.</summary>
    public int? SourceProductId { get; set; }

    /// <summary>
    /// Alamat publik gambar produk: /uploads/products/{usaha}/... (diunggah pemilik usaha) atau /img/products/... (gambar demo bawaan).
    /// Null = belum ada gambar; tampilan memakai kotak inisial.
    /// </summary>
    public string? ImagePath { get; set; }

    public Category Category { get; set; } = null!;
    public List<ProductUnit> Units { get; set; } = new();
}

/// <summary>
/// Satuan produk. Selalu ada tepat satu satuan dasar (ConversionToBase = 1, IsBase = true).
/// Kemasan lain disimpan relatif ke satuan dasar (bukan berantai): 1 Dus = 160 Item langsung.
/// </summary>
public class ProductUnit : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int ProductId { get; set; }
    public string UnitName { get; set; } = string.Empty;
    public int ConversionToBase { get; set; } = 1;
    public bool IsBase { get; set; }

    /// <summary>Harga grosir per satuan ini (dipakai usaha bertipe grosir).</summary>
    public decimal? WholesalePrice { get; set; }
}
