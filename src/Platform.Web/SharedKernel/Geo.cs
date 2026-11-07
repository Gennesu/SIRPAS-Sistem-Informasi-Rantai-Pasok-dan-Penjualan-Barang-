namespace Platform.Web.SharedKernel;

/// <summary>Jarak dua titik di bumi (rumus Haversine), dipakai untuk "grosir terdekat".</summary>
public static class Geo
{
    private const double EarthRadiusKm = 6371.0088;

    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        static double Rad(double degrees) => degrees * Math.PI / 180.0;

        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
    }

    /// <summary>Jarak jalan biasanya lebih panjang dari garis lurus; 1,3 adalah angka umum untuk jalan perkotaan.</summary>
    public const double RoadFactor = 1.3;

    /// <summary>Perkiraan jarak jalan (km) dari jarak garis lurus, dibulatkan 1 desimal.</summary>
    public static double EstimateRoadKm(double straightKm) => Math.Round(straightKm * RoadFactor, 1);

    /// <summary>Perkiraan waktu tempuh (menit): kecepatan rata-rata 25 km/jam di kota ditambah 10 menit berhenti/serah terima.</summary>
    public static int EstimateTravelMinutes(double roadKm) => (int)Math.Round(roadKm / 25.0 * 60.0 + 10);
}
