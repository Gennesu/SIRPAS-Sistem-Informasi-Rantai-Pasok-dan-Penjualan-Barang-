using System.Globalization;

namespace Platform.Web.SharedKernel;

/// <summary>Format tanggal dan jam berbahasa Indonesia (bulan "Okt", jam "14.30") tanpa bergantung pada culture komputer.</summary>
public static class IdFormat
{
    private static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "Mei", "Jun", "Jul", "Agu", "Sep", "Okt", "Nov", "Des" };
    private static readonly string[] Days = { "Min", "Sen", "Sel", "Rab", "Kam", "Jum", "Sab" };

    /// <summary>"28 Okt 2026".</summary>
    public static string Date(DateOnly d) => $"{d.Day} {Months[d.Month - 1]} {d.Year}";

    /// <summary>"28 Okt".</summary>
    public static string DateShort(DateOnly d) => $"{d.Day} {Months[d.Month - 1]}";

    /// <summary>"Sel, 28 Okt".</summary>
    public static string DateWithDay(DateOnly d) => $"{Days[(int)d.DayOfWeek]}, {d.Day} {Months[d.Month - 1]}";

    /// <summary>Waktu UTC ditampilkan sebagai WIB: "11 Okt, 14.30".</summary>
    public static string Stamp(DateTime utc)
    {
        var wib = WibTime.ToWib(utc);
        return $"{wib.Day} {Months[wib.Month - 1]}, {wib.ToString("HH.mm", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Waktu UTC ditampilkan sebagai WIB dengan tahun: "11 Okt 2026, 14.30".</summary>
    public static string StampWithYear(DateTime utc)
    {
        var wib = WibTime.ToWib(utc);
        return $"{wib.Day} {Months[wib.Month - 1]} {wib.Year}, {wib.ToString("HH.mm", CultureInfo.InvariantCulture)}";
    }

    /// <summary>Jam WIB saja: "14.30".</summary>
    public static string Clock(DateTime utc) => WibTime.ToWib(utc).ToString("HH.mm", CultureInfo.InvariantCulture);

    /// <summary>"baru saja", "5 menit lalu", "3 jam lalu", "2 hari lalu".</summary>
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var span = nowUtc - utc;
        if (span.TotalMinutes < 1)
        {
            return "baru saja";
        }

        if (span.TotalMinutes < 60)
        {
            return $"{(int)span.TotalMinutes} menit lalu";
        }

        if (span.TotalHours < 24)
        {
            return $"{(int)span.TotalHours} jam lalu";
        }

        return $"{(int)span.TotalDays} hari lalu";
    }
}
