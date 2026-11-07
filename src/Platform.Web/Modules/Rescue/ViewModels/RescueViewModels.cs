using System.ComponentModel.DataAnnotations;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Stock.Contracts;

namespace Platform.Web.Modules.Rescue.ViewModels;

// ------------------------------------------------------------------ grosir: composer

public sealed class ComposeItemInput
{
    public int BatchId { get; set; }

    /// <summary>Jumlah per paket (satuan dasar). 0 = batch ini tidak ikut paket.</summary>
    [Range(0, 100_000)]
    public int QtyBase { get; set; }
}

public sealed class ComposeTargetInput
{
    public int BusinessId { get; set; }
    public bool Selected { get; set; }

    [Range(1, 10_000)]
    public int SuggestedBundles { get; set; } = 1;
}

public sealed class ComposeInput
{
    [StringLength(100, ErrorMessage = "Nama paket maksimal 100 karakter.")]
    public string? Title { get; set; }

    [StringLength(300, ErrorMessage = "Deskripsi maksimal 300 karakter.")]
    public string? Description { get; set; }

    public List<ComposeItemInput> Items { get; set; } = new();

    [Range(1, 90, ErrorMessage = "Diskon harus 1–90%.")]
    public int? DiscountPercent { get; set; }

    /// <summary>Harga paket tetap; bila diisi, mengganti diskon.</summary>
    public decimal? BundlePrice { get; set; }

    public DateOnly? ValidUntil { get; set; }
    public List<ComposeTargetInput> Targets { get; set; } = new();

    [StringLength(200, ErrorMessage = "Pesan maksimal 200 karakter.")]
    public string? Message { get; set; }

    /// <summary>Benar = hanya warung terpilih yang melihat paket; salah = tayang juga di marketplace.</summary>
    public bool PrivateToTargets { get; set; } = true;
}

public sealed record ComposeBatchOption(BatchDto Batch, int DefaultQty, string PackHint, decimal NormalPriceBase);

public sealed record ComposeTargetRow(
    int BusinessId,
    string Name,
    double? DistanceKm,
    int SoldQtyBase,
    double AvgPerDay,
    int StockAtWarungBase,
    int SuggestedQtyBase,
    int SuggestedBundles);

public sealed class ComposeViewModel
{
    public ComposeInput Input { get; set; } = new();
    public IReadOnlyList<ComposeBatchOption> Batches { get; set; } = Array.Empty<ComposeBatchOption>();
    public IReadOnlyList<ComposeTargetRow> Targets { get; set; } = Array.Empty<ComposeTargetRow>();
    public int? FocusBatchId { get; set; }
    public string? FocusProduct { get; set; }
    public int HiddenCustomers { get; set; }
    public DateOnly Today { get; set; }
}

public sealed record BundlesViewModel(
    IReadOnlyList<BundleDto> Bundles,
    IReadOnlyList<OfferDto> Offers,
    string Tab);

// ------------------------------------------------------------------ warung

public sealed record RetailRescueViewModel(
    ExpirySummaryDto Summary,
    IReadOnlyList<BatchDto> Batches,
    IReadOnlyList<OfferDto> OpenOffers,
    IReadOnlyList<BundleDto> Deals);

public sealed record DealsViewModel(
    IReadOnlyList<OfferDto> OpenOffers,
    IReadOnlyList<BundleDto> Bundles,
    string Sort,
    int? SellerId,
    IReadOnlyList<(int Id, string Name)> Sellers);

public sealed class DealDetailViewModel
{
    public BundleDto Bundle { get; set; } = null!;
    public OfferDto? Offer { get; set; }
    public int SuggestedQty { get; set; } = 1;
    public string BuyerAddress { get; set; } = string.Empty;
}

public sealed class DealOrderInput
{
    [Range(1, int.MaxValue)]
    public int BundleId { get; set; }

    [Range(1, 10_000, ErrorMessage = "Jumlah paket harus 1–10.000.")]
    public int Qty { get; set; } = 1;

    public FulfillmentType Fulfillment { get; set; } = FulfillmentType.Pickup;

    [StringLength(300, ErrorMessage = "Alamat maksimal 300 karakter.")]
    public string? DeliveryAddress { get; set; }

    [StringLength(200, ErrorMessage = "Catatan maksimal 200 karakter.")]
    public string? Note { get; set; }

    public int? OfferId { get; set; }
}

/// <summary>Satu kartu tiket paket. ForSeller = tampilan grosir (status, terjual, tombol hentikan); selain itu tampilan warung.</summary>
public sealed record BundleTicketModel(BundleDto Bundle, bool ForSeller, int? OfferId = null);
