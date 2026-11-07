using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Orders.Contracts;

public sealed record OrderLineInput(int ProductId, string UnitName, int QtyPack);

/// <summary>Paket anti-mubazir yang dipesan (jumlah paket).</summary>
public sealed record BundleLineInput(int BundleId, int Qty);

public sealed record CreateOrderRequest(
    int SellerBusinessId,
    FulfillmentType Fulfillment,
    string? Note,
    string? DeliveryAddress,
    IReadOnlyList<OrderLineInput> Lines,
    IReadOnlyList<BundleLineInput>? BundleLines = null,
    int? OfferId = null,
    string? IdempotencyKey = null);

public sealed record LivestockSaleLineInput(int ProductId, int QtyBase);

public sealed record CreateLivestockSaleRequest(
    int AgentBusinessId,
    string? Note,
    IReadOnlyList<LivestockSaleLineInput> Lines,
    string? IdempotencyKey = null);

public sealed record OrderSummaryDto(
    int Id,
    string Number,
    string BuyerName,
    string SellerName,
    OrderStatus Status,
    FulfillmentType Fulfillment,
    decimal TotalAmount,
    string ItemsText,
    DateTime CreatedAt,
    bool HasBundle = false,
    decimal CostOfGoods = 0m,
    bool IsLivestockSale = false,
    DateTime? CompletedAt = null);

public sealed record OrderItemDto(
    string ProductName,
    string UnitName,
    int QtyPack,
    int ConversionToBase,
    decimal UnitPrice,
    int QtyBase,
    decimal Subtotal,
    string? BundleName = null,
    string? ExpiryText = null,
    decimal? NormalSubtotal = null);

/// <summary>Satu kejadian pada pesanan (untuk lini masa pelacakan).</summary>
public sealed record OrderEventDto(OrderStatus Status, DateTime At, string? Note);

public enum TrackingState
{
    Done,
    Current,
    Upcoming,
    Failed
}

/// <summary>Satu fase pada pelacakan pesanan (gaya marketplace: dibuat, diterima, disiapkan, dikirim, selesai).</summary>
public sealed record TrackingStep(string Key, string Title, string Description, TrackingState State, DateTime? At);

/// <summary>
/// Data untuk peta pelacakan: titik grosir dan warung, jarak, dan estimasi waktu. Posisi truk di peta
/// DIPERKIRAKAN dari fase pesanan dan waktu berangkat, bukan GPS (tanpa biaya langganan pelacakan).
/// </summary>
public sealed record OrderTrackingInfo(
    string CounterpartName,
    string? CounterpartPhone,
    string SellerName,
    string SellerAddress,
    string BuyerName,
    string BuyerAddress,
    double? SellerLat,
    double? SellerLon,
    double? BuyerLat,
    double? BuyerLon,
    double? RoadKm,
    int? TravelMinutes,
    DateTime? ShippedAtUtc,
    DateTime? EtaUtc,
    string WaitingFor,
    string? SellerPhone = null,
    string? BuyerPhone = null);

public sealed record OrderDetailDto(
    int Id,
    string Number,
    string BuyerName,
    string SellerName,
    OrderStatus Status,
    FulfillmentType Fulfillment,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal TotalAmount,
    string? DeliveryAddress,
    string? Note,
    string? RejectReason,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<OrderItemDto> Items,
    IReadOnlyList<OrderAction> Actions,
    IReadOnlyList<TrackingStep>? Steps = null,
    IReadOnlyList<OrderEventDto>? Events = null,
    OrderTrackingInfo? Tracking = null,
    decimal Savings = 0m,
    double? DeliveryDistanceKm = null,
    bool IsSeller = false,
    bool IsLivestockSale = false);

public enum OrderAction
{
    Accept,
    Reject,
    Prepare,
    MarkReady,
    HandOver,
    SellerCancel,
    BuyerCancel,
    ConfirmReceived
}

public enum OrderActor
{
    Buyer,
    Seller
}

/// <summary>
/// Aturan perpindahan status pesanan (murni, tanpa database):
/// PENDING → ACCEPTED → PREPARING → READY → DELIVERED/PICKED_UP → COMPLETED,
/// cabang: REJECTED (grosir, dari PENDING), CANCELLED (pembeli dari PENDING; grosir dari ACCEPTED).
/// </summary>
public static class OrderStateMachine
{
    /// <summary>Status hasil dari sebuah aksi (tanpa melihat status saat ini). Dipakai untuk mengenali pengulangan aksi yang sama.</summary>
    public static OrderStatus TargetOf(OrderAction action, FulfillmentType fulfillment) => action switch
    {
        OrderAction.Accept => OrderStatus.Accepted,
        OrderAction.Reject => OrderStatus.Rejected,
        OrderAction.Prepare => OrderStatus.Preparing,
        OrderAction.MarkReady => OrderStatus.Ready,
        OrderAction.HandOver => fulfillment == FulfillmentType.Delivery ? OrderStatus.Delivered : OrderStatus.PickedUp,
        OrderAction.SellerCancel or OrderAction.BuyerCancel => OrderStatus.Cancelled,
        OrderAction.ConfirmReceived => OrderStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    /// <summary>Pihak yang berwenang atas sebuah aksi.</summary>
    public static OrderActor ActorFor(OrderAction action) =>
        action is OrderAction.BuyerCancel or OrderAction.ConfirmReceived ? OrderActor.Buyer : OrderActor.Seller;

    public static bool TryNext(
        OrderStatus current, OrderAction action, FulfillmentType fulfillment,
        out OrderStatus next, out OrderActor actor)
    {
        next = current;
        actor = OrderActor.Seller;

        switch (action)
        {
            case OrderAction.Accept when current == OrderStatus.Pending:
                next = OrderStatus.Accepted;
                return true;
            case OrderAction.Reject when current == OrderStatus.Pending:
                next = OrderStatus.Rejected;
                return true;
            case OrderAction.BuyerCancel when current == OrderStatus.Pending:
                next = OrderStatus.Cancelled;
                actor = OrderActor.Buyer;
                return true;
            case OrderAction.Prepare when current == OrderStatus.Accepted:
                next = OrderStatus.Preparing;
                return true;
            case OrderAction.SellerCancel when current == OrderStatus.Accepted:
                next = OrderStatus.Cancelled;
                return true;
            case OrderAction.MarkReady when current == OrderStatus.Preparing:
                next = OrderStatus.Ready;
                return true;
            case OrderAction.HandOver when current == OrderStatus.Ready:
                next = fulfillment == FulfillmentType.Delivery ? OrderStatus.Delivered : OrderStatus.PickedUp;
                return true;
            case OrderAction.ConfirmReceived when current is OrderStatus.Delivered or OrderStatus.PickedUp:
                next = OrderStatus.Completed;
                actor = OrderActor.Buyer;
                return true;
            default:
                return false;
        }
    }
}

public static class OrderLabels
{
    public static string Status(OrderStatus status) => status switch
    {
        OrderStatus.Pending => "Menunggu penjual",
        OrderStatus.Accepted => "Diterima",
        OrderStatus.Preparing => "Disiapkan",
        OrderStatus.Ready => "Siap",
        OrderStatus.Delivered => "Dikirim",
        OrderStatus.PickedUp => "Sudah diambil",
        OrderStatus.Completed => "Selesai",
        OrderStatus.Rejected => "Ditolak",
        OrderStatus.Cancelled => "Dibatalkan",
        _ => status.ToString()
    };

    public static string BadgeClass(OrderStatus status) => status switch
    {
        OrderStatus.Completed => "badge badge-status badge-ok",
        OrderStatus.Rejected or OrderStatus.Cancelled => "badge badge-status badge-danger",
        OrderStatus.Pending => "badge badge-status badge-warn",
        OrderStatus.Delivered or OrderStatus.PickedUp => "badge badge-status badge-tag",
        _ => "badge badge-status badge-info"
    };

    public static string Fulfillment(FulfillmentType type) =>
        type == FulfillmentType.Delivery ? "Dikirim" : "Ambil sendiri";

    public static string Action(OrderAction action, FulfillmentType fulfillment) => action switch
    {
        OrderAction.Accept => "Terima pesanan",
        OrderAction.Reject => "Tolak pesanan",
        OrderAction.Prepare => "Mulai siapkan",
        OrderAction.MarkReady => fulfillment == FulfillmentType.Delivery ? "Siap dikirim" : "Siap diambil",
        OrderAction.HandOver => fulfillment == FulfillmentType.Delivery ? "Tandai sudah dikirim" : "Tandai sudah diambil",
        OrderAction.SellerCancel => "Batalkan pesanan",
        OrderAction.BuyerCancel => "Batalkan pesanan",
        OrderAction.ConfirmReceived => "Konfirmasi barang diterima",
        _ => action.ToString()
    };
}

/// <summary>
/// Siapa penjual dan siapa pembeli pada satu pesanan, hanya untuk teks pelacakan. Bawaan: grosir menjual ke warung (alur B2B);
/// pesanan pembeli akhir dijual oleh WARUNG, bukan grosir.
/// </summary>
public sealed record TrackingParty(string Seller, string Buyer)
{
    public static readonly TrackingParty Wholesale = new("grosir", "warung");
    public static readonly TrackingParty Consumer = new("warung", "pembeli");
    public static readonly TrackingParty LivestockSale = new("peternakan", "agen");

    internal string SellerTitle => char.ToUpperInvariant(Seller[0]) + Seller[1..];
    internal string BuyerTitle => char.ToUpperInvariant(Buyer[0]) + Buyer[1..];

    /// <summary>Tempat barang diambil: warung sendiri, atau toko penjual lain.</summary>
    internal string SellerPlace => Seller == "warung" ? "warung" : "toko " + Seller;

    /// <summary>Tujuan barang: alamat pembeli akhir, atau warung pemesan.</summary>
    internal string Destination => Buyer == "pembeli" ? "alamat Anda" : Buyer;
}

/// <summary>Menyusun fase pelacakan dari status dan jejak kejadian pesanan (murni, tanpa database).</summary>
public static class OrderTracker
{
    private static readonly OrderStatus[] DeliveryPath =
    {
        OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing,
        OrderStatus.Ready, OrderStatus.Delivered, OrderStatus.Completed
    };

    private static readonly OrderStatus[] PickupPath =
    {
        OrderStatus.Pending, OrderStatus.Accepted, OrderStatus.Preparing,
        OrderStatus.Ready, OrderStatus.PickedUp, OrderStatus.Completed
    };

    public static IReadOnlyList<OrderStatus> Path(FulfillmentType fulfillment) =>
        fulfillment == FulfillmentType.Delivery ? DeliveryPath : PickupPath;

    public static IReadOnlyList<TrackingStep> Build(
        OrderStatus status, FulfillmentType fulfillment, IReadOnlyList<OrderEventDto> events, TrackingParty? party = null)
    {
        party ??= TrackingParty.Wholesale;
        var path = Path(fulfillment);
        var failed = status is OrderStatus.Rejected or OrderStatus.Cancelled;

        int reached;
        if (failed)
        {
            reached = events
                .Select(e => Array.IndexOf((OrderStatus[])path, e.Status))
                .Where(i => i >= 0)
                .DefaultIfEmpty(0)
                .Max();
        }
        else
        {
            reached = Array.IndexOf((OrderStatus[])path, status);
        }

        var steps = new List<TrackingStep>();
        for (var i = 0; i < path.Count; i++)
        {
            var step = path[i];
            var at = events.Where(e => e.Status == step).Select(e => (DateTime?)e.At).LastOrDefault();

            TrackingState state;
            if (failed)
            {
                if (i > reached)
                {
                    break;
                }

                state = TrackingState.Done;
            }
            else if (status == OrderStatus.Completed || i < reached)
            {
                state = TrackingState.Done;
            }
            else
            {
                state = i == reached ? TrackingState.Current : TrackingState.Upcoming;
            }

            steps.Add(new TrackingStep(step.ToString(), Title(step, fulfillment, party), Describe(step, fulfillment, state, party), state, at));
        }

        if (failed)
        {
            var end = events.LastOrDefault(e => e.Status == status);
            var note = string.IsNullOrWhiteSpace(end?.Note) ? null : $" Alasan: {end!.Note}";
            steps.Add(new TrackingStep(
                status.ToString(),
                status == OrderStatus.Rejected ? "Ditolak " + party.Seller : "Dibatalkan",
                (status == OrderStatus.Rejected ? party.SellerTitle + " tidak dapat memenuhi pesanan ini." : "Pesanan dibatalkan.") + note,
                TrackingState.Failed,
                end?.At));
        }

        return steps;
    }

    public static string Title(OrderStatus step, FulfillmentType fulfillment, TrackingParty? party = null) => step switch
    {
        OrderStatus.Pending => "Pesanan dibuat",
        OrderStatus.Accepted => (party ?? TrackingParty.Wholesale).Buyer == "agen"
            ? "Penjualan tercatat"
            : "Diterima " + (party ?? TrackingParty.Wholesale).Seller,
        OrderStatus.Preparing => "Disiapkan",
        OrderStatus.Ready => fulfillment == FulfillmentType.Delivery ? "Siap dikirim" : "Siap diambil",
        OrderStatus.Delivered => "Dalam pengiriman",
        OrderStatus.PickedUp => "Sudah diambil",
        OrderStatus.Completed => "Selesai",
        _ => step.ToString()
    };

    private static string Describe(OrderStatus step, FulfillmentType fulfillment, TrackingState state, TrackingParty party)
    {
        var done = state == TrackingState.Done;
        var current = state == TrackingState.Current;
        var consumer = party.Buyer == "pembeli";
        return step switch
        {
            OrderStatus.Pending => party.Buyer == "agen"
                ? (done ? "Peternakan mencatat penjualan untuk agen." : "Menunggu peternakan mencatat penjualan.")
                : done ? $"Pesanan sudah dikirim ke {party.Seller}." : $"Menunggu {party.Seller} menerima pesanan.",
            OrderStatus.Accepted => party.Buyer == "agen"
                ? (done ? "Stok peternakan dialokasikan untuk penjualan ini." : "Penjualan sedang disiapkan.")
                : done ? $"{party.SellerTitle} menerima pesanan dan mengalokasikan stok."
                : current ? "Pesanan diterima, menunggu disiapkan." : $"{party.SellerTitle} akan memeriksa stok lalu menerima pesanan.",
            OrderStatus.Preparing => done ? (party.Buyer == "agen" ? "Barang disiapkan dan diserahkan kepada agen." : "Barang selesai disiapkan.")
                : current ? $"{party.SellerTitle} sedang menyiapkan dan mengemas barang." : $"Barang akan dikemas oleh {party.Seller}.",
            OrderStatus.Ready => fulfillment == FulfillmentType.Delivery
                ? (done ? "Barang sudah dikemas dan diserahkan ke kurir."
                    : current ? "Barang sudah dikemas, menunggu kurir berangkat." : "Barang akan diserahkan ke kurir.")
                : (done ? $"Barang sudah siap di {party.SellerPlace}."
                    : current ? $"Barang siap diambil di {party.SellerPlace}." : $"{party.BuyerTitle} dapat mengambil barang di {party.SellerPlace}."),
            OrderStatus.Delivered => done ? $"Barang tiba di {party.Destination}."
                : current ? $"Barang dalam perjalanan ke {party.Destination}." : $"Barang akan dikirim ke alamat {(consumer ? "Anda" : party.Buyer)}.",
            OrderStatus.PickedUp => done ? $"Barang sudah diambil {party.Buyer}."
                : current ? $"Barang sudah diambil, menunggu konfirmasi {party.Buyer}." : $"{party.BuyerTitle} mengambil barang di {party.SellerPlace}.",
            OrderStatus.Completed => done
                ? (consumer ? "Pembeli mengonfirmasi barang diterima. Pesanan selesai."
                    : party.Buyer == "agen" ? "Agen mengonfirmasi barang diterima. Stok agen bertambah."
                    : "Warung mengonfirmasi barang diterima. Stok warung bertambah.")
                : $"{party.BuyerTitle} mengonfirmasi barang diterima.",
            _ => string.Empty
        };
    }

    /// <summary>Siapa yang sedang ditunggu (untuk ditampilkan di bagian atas halaman pesanan).</summary>
    public static string WaitingFor(OrderStatus status, FulfillmentType fulfillment, TrackingParty? party = null)
    {
        var p = party ?? TrackingParty.Wholesale;
        return status switch
        {
            OrderStatus.Pending => $"Menunggu {p.Seller} menerima pesanan",
            OrderStatus.Accepted => $"Menunggu {p.Seller} mulai menyiapkan barang",
            OrderStatus.Preparing => $"{p.SellerTitle} sedang menyiapkan barang",
            OrderStatus.Ready => fulfillment == FulfillmentType.Delivery ? $"Menunggu {p.Seller} mengirim barang" : $"Menunggu {p.Buyer} mengambil barang",
            OrderStatus.Delivered or OrderStatus.PickedUp => $"Menunggu {p.Buyer} mengonfirmasi barang diterima",
            OrderStatus.Completed => "Pesanan selesai",
            _ => "Pesanan dihentikan"
        };
    }

    /// <summary>
    /// Posisi truk di sepanjang rute (0 = di grosir, 1 = di warung), diperkirakan dari fase dan waktu berangkat.
    /// Sebelum berangkat truk di grosir; setelah berangkat bergerak sesuai estimasi waktu tempuh, berhenti di 95% sampai warung konfirmasi.
    /// </summary>
    public static double TruckProgress(OrderStatus status, DateTime? shippedAtUtc, int? travelMinutes, DateTime nowUtc)
    {
        switch (status)
        {
            case OrderStatus.Completed:
            case OrderStatus.PickedUp:
                return 1.0;
            case OrderStatus.Delivered:
                if (shippedAtUtc is null || travelMinutes is null or <= 0)
                {
                    return 0.5;
                }

                var elapsed = (nowUtc - shippedAtUtc.Value).TotalMinutes;
                return Math.Clamp(elapsed / travelMinutes.Value, 0.05, 0.95);
            default:
                return 0.0;
        }
    }
}

public interface IOrderService
{
    /// <summary>
    /// Warung membuat pesanan ke satu grosir. Harga, stok, minimum pesanan, dan ongkir selalu dihitung ulang
    /// dari katalog grosir di server (bukan dari input browser).
    /// </summary>
    Task<Result<int>> CreateAsync(CreateOrderRequest request, CancellationToken ct = default);

    /// <summary>Peternakan mencatat penjualan langsung ke agen aktif terverifikasi; stok keluar saat diserahkan.</summary>
    Task<Result<int>> CreateLivestockSaleAsync(CreateLivestockSaleRequest request, CancellationToken ct = default);

    /// <summary>
    /// Pesanan milik saya. Warung: yang dibuat; grosir: yang masuk.
    /// </summary>
    Task<IReadOnlyList<OrderSummaryDto>> ListAsync(OrderStatus? status, int take = 100, CancellationToken ct = default);

    Task<Result<OrderDetailDto>> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Memproses perpindahan status sesuai OrderStateMachine dan pelakunya (pembeli/penjual).
    /// ACCEPT mengurangi stok grosir; SELLER_CANCEL mengembalikannya; CONFIRM_RECEIVED membuat pembelian dan
    /// menambah stok warung (serta ongkir sebagai pengeluaran). Semua dalam satu transaksi.
    /// </summary>
    Task<Result> ActAsync(int id, OrderAction action, string? reason, CancellationToken ct = default);
}
