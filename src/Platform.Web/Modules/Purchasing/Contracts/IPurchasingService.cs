using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Purchasing.Contracts;

public sealed record PurchaseLineInput(int ProductId, string UnitName, int QtyPack, decimal UnitPricePack, DateOnly? ExpiryDate = null);

public sealed record RecordPurchaseRequest(
    string SupplierName,
    DateTime? PurchasedAtUtc,
    string? Note,
    IReadOnlyList<PurchaseLineInput> Lines,
    string? IdempotencyKey = null);

/// <summary>Baris pembelian dari pesanan marketplace: isi kemasan dan harga mengikuti snapshot pesanan.</summary>
public sealed record OrderPurchaseLine(
    int ProductId,
    string ProductName,
    string UnitName,
    int QtyPack,
    int ConversionToBase,
    decimal UnitPricePack,
    IReadOnlyList<BatchPortion>? Batches = null);

public sealed record PurchaseSummaryDto(
    int Id,
    DateTime PurchasedAt,
    string SupplierName,
    string ItemsText,
    decimal TotalAmount,
    string Source);

public interface IPurchasingService
{
    /// <summary>
    /// Mencatat pembelian + menambah stok (dalam satuan dasar) + memperbarui average cost,
    /// semuanya dalam satu transaksi: bila satu langkah gagal, tidak ada yang tersimpan.
    /// </summary>
    Task<Result<int>> RecordAsync(RecordPurchaseRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<PurchaseSummaryDto>> ListAsync(int take = 30, CancellationToken ct = default);

    /// <summary>
    /// Pembelian dari pesanan marketplace yang sudah diterima (Source = ORDER). Tidak membuka transaksi sendiri:
    /// pemanggil (OrderService) wajib sudah berada di dalam transaksi agar atomik dengan perubahan status pesanan.
    /// </summary>
    Task<Result<int>> RecordFromOrderAsync(int orderId, string supplierName, IReadOnlyList<OrderPurchaseLine> lines, CancellationToken ct = default);
}
