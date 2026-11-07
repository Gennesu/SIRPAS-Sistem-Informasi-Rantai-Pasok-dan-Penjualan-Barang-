using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Delivery.Contracts;

/// <summary>Cara barang sampai ke warung. Dipakai Marketplace (filter) dan Order (biaya dan alur status).</summary>
public enum FulfillmentType
{
    Delivery = 1,
    Pickup = 2
}

public sealed record DeliveryOptionDto(
    bool OffersDelivery,
    bool OffersPickup,
    decimal DeliveryFee,
    decimal MinOrderAmount,
    string? DeliveryInfo,
    string? PickupInfo,
    decimal FeePerKm = 0m,
    decimal FreeKm = 0m,
    decimal? MaxDistanceKm = null);

public sealed record SaveDeliveryRequest(
    bool OffersDelivery,
    bool OffersPickup,
    decimal DeliveryFee,
    decimal MinOrderAmount,
    string? DeliveryInfo,
    string? PickupInfo,
    decimal FeePerKm = 0m,
    decimal FreeKm = 0m,
    decimal? MaxDistanceKm = null);

/// <summary>
/// Hasil hitung ongkir untuk satu pasang grosir-warung. Available=false bila grosir tidak mengirim
/// atau warung di luar jangkauan; Reason berisi penjelasan yang bisa langsung ditampilkan.
/// </summary>
public sealed record DeliveryQuote(bool Available, decimal Fee, double? StraightKm, double? RoadKm, int? TravelMinutes, string? Reason);

/// <summary>Aturan ongkir (murni, tanpa database) agar mudah diuji dan dipakai di banyak layar.</summary>
public static class DeliveryFeeCalculator
{
    private const decimal Rounding = 500m;

    /// <summary>Ongkir untuk jarak garis lurus tertentu (null = jarak belum diketahui).</summary>
    public static DeliveryQuote Quote(DeliveryOptionDto option, double? straightKm)
    {
        if (!option.OffersDelivery)
        {
            return new DeliveryQuote(false, 0m, straightKm, null, null, "Grosir ini tidak menyediakan pengiriman.");
        }

        if (straightKm is null)
        {
            return new DeliveryQuote(
                true, RoundUp(option.DeliveryFee), null, null, null,
                "Jarak belum diketahui. Lengkapi lokasi usaha agar ongkir sesuai jarak.");
        }

        return QuoteForRoadKm(option, Geo.EstimateRoadKm(straightKm.Value), straightKm);
    }

    /// <summary>Ongkir untuk jarak jalan tertentu (km). Dipakai juga untuk tabel simulasi di pengaturan grosir.</summary>
    public static DeliveryQuote QuoteForRoadKm(DeliveryOptionDto option, double roadKm, double? straightKm = null)
    {
        if (!option.OffersDelivery)
        {
            return new DeliveryQuote(false, 0m, straightKm, roadKm, null, "Grosir ini tidak menyediakan pengiriman.");
        }

        if (option.MaxDistanceKm is decimal max && max > 0 && (decimal)roadKm > max)
        {
            return new DeliveryQuote(
                false, 0m, straightKm, roadKm, null,
                $"Di luar jangkauan pengiriman (maksimal {max:0.#} km, jarak Anda sekitar {roadKm:0.#} km). Pilih ambil sendiri.");
        }

        var extraKm = Math.Max(0m, (decimal)roadKm - option.FreeKm);
        var fee = RoundUp(option.DeliveryFee + extraKm * option.FeePerKm);
        return new DeliveryQuote(true, fee, straightKm, roadKm, Geo.EstimateTravelMinutes(roadKm), null);
    }

    private static decimal RoundUp(decimal value) => Math.Ceiling(value / Rounding) * Rounding;
}

public interface IDeliveryService
{
    /// <summary>Pengaturan milik usaha yang sedang login (default: hanya ambil sendiri).</summary>
    Task<DeliveryOptionDto> GetMineAsync(CancellationToken ct = default);

    /// <summary>Hanya toko grosir. Minimal satu cara (pengiriman atau ambil sendiri) harus aktif.</summary>
    Task<Result> SaveMineAsync(SaveDeliveryRequest request, CancellationToken ct = default);

    /// <summary>
    /// Info pengiriman grosir untuk marketplace (baca lintas usaha, hanya data pengiriman publik).
    /// Grosir yang belum mengatur memakai default.
    /// </summary>
    Task<IReadOnlyDictionary<int, DeliveryOptionDto>> GetPublicAsync(IReadOnlyCollection<int> businessIds, CancellationToken ct = default);

    /// <summary>Ongkir dari tiap grosir ke usaha yang sedang login (warung). Kunci = id grosir.</summary>
    Task<IReadOnlyDictionary<int, DeliveryQuote>> QuoteForBuyerAsync(IReadOnlyCollection<int> sellerIds, CancellationToken ct = default);

    /// <summary>Ongkir dari grosir yang sedang login ke tiap warung. Kunci = id warung.</summary>
    Task<IReadOnlyDictionary<int, DeliveryQuote>> QuoteForSellerAsync(IReadOnlyCollection<int> buyerIds, CancellationToken ct = default);
}
