using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Sales.Domain;

public enum SaleStatus
{
    Confirmed = 1,
    Voided = 2
}

/// <summary>Satu transaksi POS. Harga jual dan modal disimpan sebagai snapshot di SaleItem.</summary>
public class Sale : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public string Number { get; set; } = string.Empty;
    public DateTime SoldAt { get; set; }
    public decimal TotalAmount { get; set; }

    /// <summary>Uang yang diterima kasir (tunai). Selisihnya dengan total adalah kembalian. Null pada transaksi lama = pas.</summary>
    public decimal? PaidAmount { get; set; }

    public string PaymentMethod { get; set; } = "CASH";
    public SaleStatus Status { get; set; } = SaleStatus.Confirmed;

    /// <summary>Kunci anti-duplikat: klik ganda / kirim ulang dengan kunci sama tidak membuat transaksi kedua.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>V3: asal transaksi ("POS" bila null, atau "CONSUMER_ORDER" untuk pesanan pembeli akhir yang selesai) dan id asalnya.</summary>
    public string? SourceType { get; set; }

    public int? SourceId { get; set; }

    public int? CreatedByUserId { get; set; }
    public List<SaleItem> Items { get; set; } = new();
}

public class SaleItem : IBusinessOwned
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public int SaleId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int QtyBase { get; set; }

    /// <summary>Harga jual per satuan dasar saat transaksi.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Average cost per satuan dasar saat transaksi (dasar perhitungan COGS).</summary>
    public decimal UnitCost { get; set; }

    public decimal Subtotal { get; set; }
    public decimal CostTotal { get; set; }
}
