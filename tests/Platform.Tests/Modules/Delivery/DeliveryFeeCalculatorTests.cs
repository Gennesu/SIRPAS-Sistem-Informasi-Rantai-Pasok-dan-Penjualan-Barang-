using Platform.Web.Modules.Delivery.Contracts;

namespace Platform.Tests.Modules.Delivery;

public sealed class DeliveryFeeCalculatorTests
{
    // Tarif dasar Rp8.000 untuk 2 km pertama, lalu Rp2.500 per km, maksimal 12 km jalan.
    private static readonly DeliveryOptionDto PerKm = new(true, true, 8_000m, 0m, null, null, 2_500m, 2m, 12m);

    [Fact]
    public void Dalam_jarak_gratis_hanya_membayar_tarif_dasar()
    {
        var quote = DeliveryFeeCalculator.QuoteForRoadKm(PerKm, 1.5);

        Assert.True(quote.Available);
        Assert.Equal(8_000m, quote.Fee);
    }

    [Fact]
    public void Kelebihan_jarak_dikali_tarif_per_km_dan_dibulatkan_ke_atas_per_500()
    {
        // 10,7 km jalan: 8.000 + (10,7 - 2) x 2.500 = 29.750 -> 30.000.
        Assert.Equal(30_000m, DeliveryFeeCalculator.QuoteForRoadKm(PerKm, 10.7).Fee);
        // 2,1 km: 8.000 + 0,1 x 2.500 = 8.250 -> 8.500.
        Assert.Equal(8_500m, DeliveryFeeCalculator.QuoteForRoadKm(PerKm, 2.1).Fee);
    }

    [Fact]
    public void Jarak_garis_lurus_dikali_faktor_jalan_1_3()
    {
        // 5 km garis lurus = 6,5 km jalan -> 8.000 + 4,5 x 2.500 = 19.250 -> 19.500.
        var quote = DeliveryFeeCalculator.Quote(PerKm, 5.0);

        Assert.Equal(6.5, quote.RoadKm);
        Assert.Equal(19_500m, quote.Fee);
        Assert.True(quote.TravelMinutes > 0);
    }

    [Fact]
    public void Di_luar_jangkauan_maksimal_pengiriman_tidak_tersedia_dengan_alasan()
    {
        var quote = DeliveryFeeCalculator.QuoteForRoadKm(PerKm, 13.0);

        Assert.False(quote.Available);
        Assert.Contains("jangkauan", quote.Reason);
    }

    [Fact]
    public void Tarif_per_km_nol_berarti_tarif_tetap()
    {
        var flat = new DeliveryOptionDto(true, true, 15_000m, 0m, null, null);

        Assert.Equal(15_000m, DeliveryFeeCalculator.QuoteForRoadKm(flat, 0.5).Fee);
        Assert.Equal(15_000m, DeliveryFeeCalculator.QuoteForRoadKm(flat, 40).Fee);
    }

    [Fact]
    public void Jarak_belum_diketahui_memakai_tarif_dasar_dan_memberi_petunjuk()
    {
        var quote = DeliveryFeeCalculator.Quote(PerKm, null);

        Assert.True(quote.Available);
        Assert.Equal(8_000m, quote.Fee);
        Assert.Null(quote.RoadKm);
        Assert.Contains("lokasi", quote.Reason);
    }

    [Fact]
    public void Grosir_tanpa_pengiriman_tidak_menyediakan_ongkir()
    {
        var pickupOnly = new DeliveryOptionDto(false, true, 0m, 0m, null, null);

        Assert.False(DeliveryFeeCalculator.Quote(pickupOnly, 1.0).Available);
    }
}
