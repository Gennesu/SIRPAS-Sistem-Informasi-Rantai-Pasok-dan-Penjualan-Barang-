using System.ComponentModel.DataAnnotations;
using Platform.Web.Modules.Delivery.Contracts;

namespace Platform.Web.Modules.Orders.ViewModels;

public class OrderLineRow
{
    public int? ProductId { get; set; }
    public string? UnitName { get; set; }
    public int? QtyPack { get; set; }
}

public class CreateOrderInput
{
    public int SellerBusinessId { get; set; }

    public FulfillmentType Fulfillment { get; set; } = FulfillmentType.Pickup;

    [StringLength(300, ErrorMessage = "Alamat maksimal 300 karakter.")]
    public string? DeliveryAddress { get; set; }

    [StringLength(200, ErrorMessage = "Catatan maksimal 200 karakter.")]
    public string? Note { get; set; }

    public List<OrderLineRow> Lines { get; set; } = new();

    /// <summary>Kunci operasi dari form (dibuat saat halaman ditampilkan): kirim ulang/klik ganda tidak membuat pesanan kedua.</summary>
    [StringLength(64)]
    public string? IdempotencyKey { get; set; }
}
