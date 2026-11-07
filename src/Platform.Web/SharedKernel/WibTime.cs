using System.Globalization;

namespace Platform.Web.SharedKernel;

public enum PeriodPreset
{
    Today = 0,
    Yesterday,
    ThisWeek,
    ThisMonth,
    Custom
}

/// <summary>Pilihan periode dari pengguna (query string). From/To hanya dipakai bila Preset = Custom.</summary>
public sealed record PeriodSelection(PeriodPreset Preset, DateOnly? From, DateOnly? To);

/// <summary>Rentang tanggal WIB (UTC+7) beserta batas UTC-nya. ToUtcExclusive = awal hari setelah To.</summary>
public sealed record DateRangeWib(DateOnly From, DateOnly To, DateTime FromUtc, DateTime ToUtcExclusive, string Label);

/// <summary>
/// Semua waktu disimpan UTC; tampilan dan pelaporan memakai WIB (UTC+7, tanpa DST).
/// Demo memakai offset tetap agar tidak bergantung pada database zona waktu di komputer.
/// </summary>
public static class WibTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTime ToWib(DateTime utc) =>
        DateTime.SpecifyKind(utc.Add(Offset), DateTimeKind.Unspecified);

    public static DateOnly TodayWib(TimeProvider time) =>
        DateOnly.FromDateTime(ToWib(time.GetUtcNow().UtcDateTime));

    /// <summary>Awal hari (00:00 WIB) pada tanggal tsb, dinyatakan dalam UTC.</summary>
    public static DateTime StartOfDayUtc(DateOnly wibDate) =>
        DateTime.SpecifyKind(wibDate.ToDateTime(TimeOnly.MinValue) - Offset, DateTimeKind.Utc);

    public static DateTime WibToUtc(DateOnly date, int hour, int minute) =>
        DateTime.SpecifyKind(date.ToDateTime(new TimeOnly(hour, minute)) - Offset, DateTimeKind.Utc);

    public static string Format(DateOnly d) => d.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    public static string FormatDateTime(DateTime utc) =>
        ToWib(utc).ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);

    public static DateRangeWib Resolve(PeriodPreset preset, DateOnly? from, DateOnly? to, TimeProvider time)
    {
        var today = TodayWib(time);
        DateOnly start;
        DateOnly end;
        string label;

        switch (preset)
        {
            case PeriodPreset.Yesterday:
                start = end = today.AddDays(-1);
                label = "Kemarin";
                break;
            case PeriodPreset.ThisWeek:
                // Pekan dimulai hari Senin.
                start = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                end = today;
                label = "Minggu ini";
                break;
            case PeriodPreset.ThisMonth:
                start = new DateOnly(today.Year, today.Month, 1);
                end = today;
                label = "Bulan ini";
                break;
            case PeriodPreset.Custom:
                start = from ?? today;
                end = to ?? start;
                if (end < start)
                {
                    (start, end) = (end, start);
                }

                label = start == end ? Format(start) : $"{Format(start)} s.d. {Format(end)}";
                break;
            default:
                start = end = today;
                label = "Hari ini";
                break;
        }

        return new DateRangeWib(start, end, StartOfDayUtc(start), StartOfDayUtc(end.AddDays(1)), label);
    }
}
