using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Sales.Contracts;

public sealed record SaleLineInput(int ProductId, int QtyBase);

/// <summary>
/// PaidAmount = uang diterima (khusus tunai).
/// Kosong berarti uang pas.
/// </summary>
public sealed record ConfirmSaleRequest(
    IReadOnlyList<SaleLineInput> Lines,
    string PaymentMethod,
    string? IdempotencyKey,
    decimal? PaidAmount = null);

public sealed record SaleLineDto(
    int ProductId,
    string ProductName,
    int QtyBase,
    decimal UnitPrice,
    decimal Subtotal,
    string UnitName = "");

public sealed record SaleReceiptDto(
    int Id,
    string Number,
    DateTime SoldAt,
    string PaymentMethod,
    decimal Total,
    int TotalItems,
    IReadOnlyList<SaleLineDto> Lines,
    decimal Paid = 0m,
    decimal Change = 0m,
    string StoreName = "",
    string StoreAddress = "",
    string? StorePhone = null,
    string? CashierName = null,
    bool IsVoided = false);

public static class PaymentLabels
{
    public static string Method(string method) => method switch
    {
        "CASH" => "Tunai",
        "QRIS" => "QRIS",
        "TRANSFER" => "Transfer",
        _ => method
    };
}

/// <summary>
/// Teks struk polos untuk WhatsApp atau disalin.
/// Khusus QRIS dan Transfer demo, struk diberi label simulasi.
/// </summary>
public static class ReceiptText
{
    public const int Width = 32;

    public static string Build(SaleReceiptDto r)
    {
        var sb = new System.Text.StringBuilder();

        Center(sb, r.StoreName);

        if (!string.IsNullOrWhiteSpace(r.StoreAddress))
        {
            Center(sb, r.StoreAddress);
        }

        if (!string.IsNullOrWhiteSpace(r.StorePhone))
        {
            Center(sb, "Telp. " + r.StorePhone);
        }

        sb.AppendLine(new string('-', Width));

        Field(sb, "No   : ", r.Number);
        Field(
            sb,
            "Waktu: ",
            $"{WibTime.FormatDateTime(r.SoldAt)} WIB");

        if (!string.IsNullOrWhiteSpace(r.CashierName))
        {
            Field(sb, "Kasir: ", r.CashierName);
        }

        sb.AppendLine(new string('-', Width));

        foreach (var l in r.Lines)
        {
            foreach (var part in Wrap(l.ProductName, Width))
            {
                sb.AppendLine(part);
            }

            Row(
                sb,
                $"  {l.QtyBase} x {Rupiah.Format(l.UnitPrice)}",
                Rupiah.Format(l.Subtotal));
        }

        sb.AppendLine(new string('-', Width));

        Row(
            sb,
            $"Total ({r.TotalItems} item)",
            Rupiah.Format(r.Total));

        Row(
            sb,
            PaymentLabels.Method(r.PaymentMethod),
            Rupiah.Format(r.Paid));

        if (r.PaymentMethod == "QRIS" ||
            r.PaymentMethod == "TRANSFER")
        {
            Center(sb, "SIMULASI DUMMY");
            Center(sb, "BUKAN BUKTI PEMBAYARAN NYATA");
        }

        if (r.Change > 0)
        {
            Row(sb, "Kembali", Rupiah.Format(r.Change));
        }

        sb.AppendLine(new string('-', Width));
        Center(sb, "Terima kasih sudah berbelanja!");

        return sb.ToString();
    }

    private static void Center(
        System.Text.StringBuilder sb,
        string text)
    {
        foreach (var line in Wrap(text, Width))
        {
            sb.AppendLine(
                new string(' ', (Width - line.Length) / 2) + line);
        }
    }

    private static void Field(
        System.Text.StringBuilder sb,
        string label,
        string value)
    {
        var lines = Wrap(value, Width - label.Length);

        for (var i = 0; i < lines.Count; i++)
        {
            sb.AppendLine(
                (i == 0 ? label : new string(' ', label.Length))
                + lines[i]);
        }
    }

    // Jika tidak muat, nilai kanan turun ke baris berikutnya.
    private static void Row(
        System.Text.StringBuilder sb,
        string left,
        string right)
    {
        if (left.Length + right.Length + 1 > Width)
        {
            foreach (var part in Wrap(left, Width))
            {
                sb.AppendLine(part);
            }

            sb.AppendLine(right.PadLeft(Width));
            return;
        }

        sb.AppendLine(
            left +
            new string(' ', Width - left.Length - right.Length) +
            right);
    }

    /// <summary>Membungkus teks agar muat pada lebar struk.</summary>
    public static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var raw in (text ?? string.Empty)
                     .Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var word = raw;

            while (word.Length > width)
            {
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = string.Empty;
                }

                lines.Add(word[..width]);
                word = word[width..];
            }

            if (current.Length == 0)
            {
                current = word;
            }
            else if (current.Length + 1 + word.Length <= width)
            {
                current += " " + word;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }
}

/// <summary>Satu baris penjualan dari pesanan pembeli akhir (harga dan modal sudah dibekukan saat pesanan diproses).</summary>
public sealed record ConsumerSaleLine(int ProductId, string ProductName, int QtyBase, decimal UnitPrice, decimal UnitCost);

public interface ISalesService
{
    /// <summary>
    /// Mencatat penjualan dari pesanan pembeli akhir yang SELESAI ke pembukuan warung penjual (omzet, HPP, laba kotor),
    /// tanpa menggerakkan stok lagi (stok sudah keluar saat pesanan diterima). Tepat satu kali per pesanan: dijaga unique index
    /// (BusinessId, SourceType, SourceId) dan pemeriksaan di dalam unit kerja. Dipanggil dari modul Consumers.
    /// </summary>
    Task<Result<int>> RecordConsumerOrderSaleAsync(
        int sellerBusinessId, int consumerOrderId, string orderNumber, DateTime soldAtUtc, string paymentMethod,
        IReadOnlyList<ConsumerSaleLine> lines, CancellationToken ct = default);

    /// <summary>
    /// Mengonfirmasi penjualan secara atomik:
    /// simpan Sale, kurangi stok, dan catat pergerakan stok.
    /// Bila stok tidak cukup atau langkah gagal,
    /// seluruh transaksi dibatalkan.
    /// </summary>
    Task<Result<SaleReceiptDto>> ConfirmAsync(
        ConfirmSaleRequest request,
        CancellationToken ct = default);

    Task<Result<SaleReceiptDto>> GetReceiptAsync(
        int saleId,
        CancellationToken ct = default);
}