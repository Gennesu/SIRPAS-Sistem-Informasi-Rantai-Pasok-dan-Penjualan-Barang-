using System.Globalization;

namespace Platform.Web.SharedKernel;

public static class Money
{
    /// <summary>Pembulatan uang ke 2 desimal (Rupiah), setengah dibulatkan ke atas.</summary>
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Biaya per satuan dasar disimpan 4 desimal agar pembagian harga kemasan tidak kehilangan presisi.</summary>
    public static decimal RoundCost(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
}

/// <summary>Format Rupiah gaya Indonesia (titik ribuan, koma desimal) tanpa bergantung pada culture komputer.</summary>
public static class Rupiah
{
    public static string Format(decimal amount) => Format(amount, 0);

    // Negatif ditulis "-Rp1.234" (bukan "Rp-1.234").
    public static string Format(decimal amount, int decimals) =>
        (amount < 0 ? "-Rp" : "Rp") + Number(Math.Abs(amount), decimals);

    public static string Number(decimal value, int decimals)
    {
        var text = value.ToString("N" + decimals, CultureInfo.InvariantCulture); // 1,234,567.89
        return text.Replace(",", "#").Replace(".", ",").Replace("#", ".");        // 1.234.567,89
    }
}
