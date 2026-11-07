namespace Platform.Web.Modules.Rescue.Contracts;

/// <summary>
/// Ringkasan food waste dari catatan stok dan transaksi NYATA (bukan klaim): terjual, tersalurkan (promo/paket), terbuang, dan kerugiannya.
/// Dihitung untuk usaha yang login; untuk admin = seluruh platform.
/// </summary>
public sealed record FoodWasteSummaryDto(
    DateOnly From,
    DateOnly To,
    int SoldQtyBase,
    decimal SoldValue,
    int RescuedQtyBase,
    decimal RescuedValue,
    decimal RescuedSavings,
    int WastedQtyBase,
    decimal WastedCostValue,
    int OtherDisposedQtyBase,
    decimal OtherDisposedCostValue,
    int AtRiskQtyBase,
    decimal AtRiskCostValue,
    decimal WasteRatePercent,
    IReadOnlyList<FoodWasteProductDto> TopWasted);

public sealed record FoodWasteProductDto(string ProductName, int WastedQtyBase, decimal CostValue);

public interface IFoodWasteService
{
    Task<FoodWasteSummaryDto> GetSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
}
