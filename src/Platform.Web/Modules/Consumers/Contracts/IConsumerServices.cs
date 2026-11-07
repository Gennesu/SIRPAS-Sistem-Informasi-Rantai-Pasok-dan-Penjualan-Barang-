using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Contracts;

// ------------------------------------------------------------------ akun pembeli

public sealed record RegisterConsumerRequest(
    string FullName, string Email, string Password, string Phone, string Address, double? Latitude, double? Longitude);

public sealed record ConsumerProfileDto(
    int UserId, string FullName, string Email, string Phone, string Address, double? Latitude, double? Longitude);

public sealed record UpdateConsumerProfileRequest(string FullName, string Phone, string Address, double? Latitude, double? Longitude);

/// <summary>Akun pembeli akhir: pengguna individu, tanpa BusinessId.</summary>
public interface IConsumerService
{
    /// <summary>Pendaftaran publik. Selalu membuat role CONSUMER (tidak pernah ADMIN) dan profil individu.</summary>
    Task<Result<AuthenticatedUser>> RegisterAsync(RegisterConsumerRequest request, CancellationToken ct = default);

    Task<Result<ConsumerProfileDto>> GetProfileAsync(CancellationToken ct = default);

    Task<Result> UpdateProfileAsync(UpdateConsumerProfileRequest request, CancellationToken ct = default);
}

// ------------------------------------------------------------------ katalog eceran warung

public sealed record ShopCardDto(
    int BusinessId,
    string Name,
    string Address,
    double? DistanceKm,
    bool OffersDelivery,
    bool OffersPickup,
    decimal DeliveryFee,
    bool DeliveryAvailable,
    string? DeliveryNote,
    int ProductCount,
    int PromoCount,
    IReadOnlyList<string> MatchingProducts,
    double? Latitude = null,
    double? Longitude = null);

/// <summary>
/// Produk yang ditawarkan warung kepada pembeli akhir. AvailableQty = stok yang boleh dijual
/// (fisik - dicadangkan - sudah kedaluwarsa). PromoQty/PromoUnitPrice terisi hanya bila warung membuat promo untuk produk ini.
/// </summary>
public sealed record ShopProductDto(
    int ProductId,
    string Name,
    string? Sku,
    string CategoryName,
    string BaseUnitName,
    decimal UnitPrice,
    int AvailableQty,
    DateOnly? NearestExpiry,
    int? DaysToExpiry,
    int PromoPercent,
    int PromoQty,
    decimal PromoUnitPrice,
    string? ImagePath = null);

public sealed record ShopDetailDto(
    ShopCardDto Card,
    string? DeliveryInfo,
    string? PickupInfo,
    string ConsumerAddress,
    IReadOnlyList<ShopProductDto> Products);

public sealed record PromoDealDto(
    int ShopId,
    string ShopName,
    int ProductId,
    string ProductName,
    string BaseUnitName,
    decimal RegularPrice,
    decimal PromoPrice,
    int Percent,
    int PromoQty,
    DateOnly NearestExpiry,
    int DaysLeft,
    string? ImagePath = null);

public interface IConsumerCatalogService
{
    /// <summary>Warung terverifikasi dan aktif yang punya barang layak jual. Dapat dicari berdasarkan nama warung/produk.</summary>
    Task<IReadOnlyList<ShopCardDto>> SearchShopsAsync(string? query, CancellationToken ct = default);

    Task<Result<ShopDetailDto>> GetShopAsync(int businessId, CancellationToken ct = default);

    /// <summary>Promo produk yang masih layak konsumsi (belum kedaluwarsa), diurutkan dari yang paling dekat kedaluwarsa.</summary>
    Task<IReadOnlyList<PromoDealDto>> ListPromosAsync(CancellationToken ct = default);
}

/// <summary>Promo warung untuk stok yang mendekati kedaluwarsa (dikelola warung).</summary>
public sealed record PromoSettingDto(
    int ProductId, string ProductName, string BaseUnitName, decimal RegularPrice, int AvailableQty,
    int? DiscountPercent, int? WithinDays, bool IsActive, int NearExpiryQty, string? ImagePath = null);

public interface IConsumerPromoService
{
    Task<IReadOnlyList<PromoSettingDto>> ListMineAsync(CancellationToken ct = default);

    /// <summary>discountPercent null = hapus promo.</summary>
    Task<Result> SetAsync(int productId, int? discountPercent, int? withinDays, CancellationToken ct = default);
}

// ------------------------------------------------------------------ pesanan pembeli

public sealed record ConsumerLineInput(int ProductId, int QtyBase);

public sealed record PlaceConsumerOrderRequest(
    int SellerBusinessId,
    FulfillmentType Fulfillment,
    PaymentMethod PaymentMethod,
    string? DeliveryAddress,
    string? Note,
    IReadOnlyList<ConsumerLineInput> Lines,
    string? IdempotencyKey);

public sealed record ConsumerOrderItemDto(
    string ProductName,
    string BaseUnitName,
    int QtyBase,
    decimal UnitPrice,
    decimal RegularUnitPrice,
    bool IsPromo,
    int PromoPercent,
    DateOnly? ExpiryDate,
    decimal Subtotal,
    string? ImagePath = null);

/// <summary>Rencana pesanan hasil hitung server (harga, promo, ongkir). Dipakai halaman konfirmasi sebelum pesanan dibuat.</summary>
public sealed record ConsumerOrderPreviewDto(
    int SellerBusinessId,
    string SellerName,
    FulfillmentType Fulfillment,
    string? DeliveryAddress,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    decimal Savings,
    IReadOnlyList<ConsumerOrderItemDto> Items);

public sealed record ConsumerOrderSummaryDto(
    int Id,
    string Number,
    string PartyName,
    OrderStatus Status,
    OrderPaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    FulfillmentType Fulfillment,
    decimal TotalAmount,
    string ItemsText,
    DateTime CreatedAt);

public sealed record PaymentDto(
    int Id,
    string Reference,
    PaymentMethod Method,
    PaymentStatus Status,
    decimal Amount,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    string? FailureReason);

public sealed record ConsumerOrderDetailDto(
    int Id,
    string Number,
    string ConsumerName,
    string? ConsumerPhone,
    string SellerName,
    string SellerAddress,
    string? SellerPhone,
    OrderStatus Status,
    OrderPaymentStatus PaymentStatus,
    PaymentMethod PaymentMethod,
    FulfillmentType Fulfillment,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal TotalAmount,
    string? DeliveryAddress,
    string? Note,
    string? RejectReason,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ConsumerOrderItemDto> Items,
    IReadOnlyList<OrderEventDto> Events,
    IReadOnlyList<PaymentDto> Payments,
    IReadOnlyList<OrderAction> Actions,
    IReadOnlyList<TrackingStep> Steps,
    bool CanPayDemo,
    decimal Savings);

public interface IConsumerOrderService
{
    /// <summary>Menghitung ulang harga/promo/ongkir di server tanpa menyimpan apa pun (halaman konfirmasi).</summary>
    Task<Result<ConsumerOrderPreviewDto>> PreviewAsync(PlaceConsumerOrderRequest request, CancellationToken ct = default);

    /// <summary>
    /// Membuat pesanan: satu unit kerja yang mencakup cek kunci operasi, hitung harga di server, reservasi stok atomik,
    /// pesanan + item + event + catatan pembayaran. Kunci yang sama mengembalikan pesanan yang sama (tidak ada pesanan ganda).
    /// </summary>
    Task<Result<int>> PlaceAsync(PlaceConsumerOrderRequest request, CancellationToken ct = default);

    /// <summary>Pembeli: pesanan saya. Warung: pesanan pembeli yang masuk. Admin: semua.</summary>
    Task<IReadOnlyList<ConsumerOrderSummaryDto>> ListAsync(OrderStatus? status, int take = 100, CancellationToken ct = default);

    Task<Result<ConsumerOrderDetailDto>> GetAsync(int id, CancellationToken ct = default);

    /// <summary>Transisi status sesuai OrderStateMachine. Pengulangan aksi yang sama tidak menghasilkan efek ganda.</summary>
    Task<Result> ActAsync(int id, OrderAction action, string? reason, CancellationToken ct = default);
}

public interface IPaymentService
{
    /// <summary>
    /// Pembeli menekan tombol pembayaran SIMULASI (bukan uang nyata). simulateFailure = true mensimulasikan pembayaran gagal.
    /// Kunci operasi yang sama atau pesanan yang sudah lunas mengembalikan catatan pembayaran yang sama: tidak pernah ada
    /// dua pembayaran sukses untuk satu pesanan.
    /// </summary>
    Task<Result<PaymentDto>> PayDemoAsync(int consumerOrderId, bool simulateFailure, string? idempotencyKey, CancellationToken ct = default);
}
