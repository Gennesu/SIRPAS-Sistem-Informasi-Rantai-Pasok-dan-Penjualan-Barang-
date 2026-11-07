using Platform.Web.SharedKernel;

namespace Platform.Tests.SharedKernel;

public class WibTimeTests
{
    // 2026-10-03 17:30 UTC = 2026-10-04 00:30 WIB (hari Minggu): "hari ini" sudah berganti ke tanggal 4.
    private static readonly SettableTimeProvider Clock =
        new(new DateTimeOffset(2026, 10, 3, 17, 30, 0, TimeSpan.Zero));

    [Fact]
    public void Hari_ini_mengikuti_tanggal_WIB_bukan_UTC()
    {
        var range = WibTime.Resolve(PeriodPreset.Today, null, null, Clock);

        Assert.Equal(new DateOnly(2026, 10, 4), range.From);
        Assert.Equal(new DateOnly(2026, 10, 4), range.To);
        Assert.Equal(new DateTime(2026, 10, 3, 17, 0, 0, DateTimeKind.Utc), range.FromUtc);
        Assert.Equal(new DateTime(2026, 10, 4, 17, 0, 0, DateTimeKind.Utc), range.ToUtcExclusive);
    }

    [Fact]
    public void Kemarin_dan_minggu_ini_dan_bulan_ini()
    {
        Assert.Equal(new DateOnly(2026, 10, 3), WibTime.Resolve(PeriodPreset.Yesterday, null, null, Clock).From);

        var week = WibTime.Resolve(PeriodPreset.ThisWeek, null, null, Clock);
        Assert.Equal(new DateOnly(2026, 9, 28), week.From); // Senin
        Assert.Equal(new DateOnly(2026, 10, 4), week.To);   // Minggu (hari ini)

        var month = WibTime.Resolve(PeriodPreset.ThisMonth, null, null, Clock);
        Assert.Equal(new DateOnly(2026, 10, 1), month.From);
    }

    [Fact]
    public void Rentang_custom_yang_terbalik_ditukar()
    {
        var range = WibTime.Resolve(PeriodPreset.Custom, new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 2), Clock);

        Assert.Equal(new DateOnly(2026, 10, 2), range.From);
        Assert.Equal(new DateOnly(2026, 10, 9), range.To);
    }

    [Fact]
    public void Format_Rupiah_gaya_Indonesia()
    {
        Assert.Equal("Rp120.000", Rupiah.Format(120_000m));
        Assert.Equal("Rp3.041,67", Rupiah.Format(3_041.6667m, 2));
        Assert.Equal("Rp0", Rupiah.Format(0m));
    }
}
