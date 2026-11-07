using Platform.Web.SharedKernel;

namespace Platform.Tests.SharedKernel;

public class GeoTests
{
    [Fact]
    public void Titik_yang_sama_berjarak_nol()
    {
        Assert.Equal(0.0, Geo.DistanceKm(-6.9175, 107.6191, -6.9175, 107.6191), 6);
    }

    [Fact]
    public void Dua_titik_di_Bandung_berjarak_sekitar_1_6_km()
    {
        var km = Geo.DistanceKm(-6.9175, 107.6191, -6.9150, 107.6050);
        Assert.True(km > 1.5 && km < 1.7, km.ToString());
    }

    [Fact]
    public void Jakarta_ke_Bandung_sekitar_116_km()
    {
        var km = Geo.DistanceKm(-6.2088, 106.8456, -6.9175, 107.6191);
        Assert.True(km > 112 && km < 121, km.ToString());
    }
}
