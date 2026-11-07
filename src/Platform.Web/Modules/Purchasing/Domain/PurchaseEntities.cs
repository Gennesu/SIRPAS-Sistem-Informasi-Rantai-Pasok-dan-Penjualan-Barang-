using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Purchasing.Domain;

/// <summary>
/// Dokumen pembelian barang. Harga, isi kemasan, dan biaya per satuan dasar disimpan sebagai
/// snapshot di PurchaseItem, sehingga perubahan harga di kemudian hari tidak mengubah riwayat.
/// </summary>
public class Purchase : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public DateTime PurchasedAt { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Note { get; set; }

    /// <summary>MANUAL (dicatat sendiri) atau ORDER (dari pesanan marketplace, tahap berikutnya).</summary>
    public string Source { get; set; } = "MANUAL";

    public int? OrderId { get; set; }

    /// <summary>V3: kunci anti-duplikat pencatatan pembelian manual (unik per usaha).</summary>
    public string? IdempotencyKey { get; set; }

    public List<PurchaseItem> Items { get; set; } = new();
}

public class PurchaseItem : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int PurchaseId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string UnitName { get; set; } = string.Empty;
    public int QtyPack { get; set; }
    public int ConversionSnapshot { get; set; }
    public decimal UnitPricePack { get; set; }
    public int QtyBase { get; set; }

    /// <summary>Harga per kemasan dibagi isi kemasan, 4 desimal. Contoh: 120.000 / 40 = 3.000.</summary>
    public decimal CostPerBase { get; set; }

    public decimal Subtotal { get; set; }

    /// <summary>Tanggal kadaluarsa barang yang dibeli (opsional). Bila beberapa, diisi tanggal yang paling dekat.</summary>
    public DateOnly? ExpiryDate { get; set; }
}
