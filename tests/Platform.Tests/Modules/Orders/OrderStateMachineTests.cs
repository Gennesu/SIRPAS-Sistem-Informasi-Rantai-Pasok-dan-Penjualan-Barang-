using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Domain;

namespace Platform.Tests.Modules.Orders;

public class OrderStateMachineTests
{
    private static OrderStatus Step(OrderStatus from, OrderAction action, FulfillmentType type, OrderActor expectedActor)
    {
        Assert.True(OrderStateMachine.TryNext(from, action, type, out var next, out var actor), $"{from} + {action}");
        Assert.Equal(expectedActor, actor);
        return next;
    }

    [Fact]
    public void Alur_pengiriman_dari_pesan_sampai_selesai()
    {
        var s = OrderStatus.Pending;
        s = Step(s, OrderAction.Accept, FulfillmentType.Delivery, OrderActor.Seller);
        Assert.Equal(OrderStatus.Accepted, s);
        s = Step(s, OrderAction.Prepare, FulfillmentType.Delivery, OrderActor.Seller);
        s = Step(s, OrderAction.MarkReady, FulfillmentType.Delivery, OrderActor.Seller);
        s = Step(s, OrderAction.HandOver, FulfillmentType.Delivery, OrderActor.Seller);
        Assert.Equal(OrderStatus.Delivered, s);
        s = Step(s, OrderAction.ConfirmReceived, FulfillmentType.Delivery, OrderActor.Buyer);
        Assert.Equal(OrderStatus.Completed, s);
    }

    [Fact]
    public void Ambil_sendiri_berakhir_sebagai_PickedUp_lalu_Completed()
    {
        var s = Step(OrderStatus.Ready, OrderAction.HandOver, FulfillmentType.Pickup, OrderActor.Seller);
        Assert.Equal(OrderStatus.PickedUp, s);
        Assert.Equal(OrderStatus.Completed, Step(s, OrderAction.ConfirmReceived, FulfillmentType.Pickup, OrderActor.Buyer));
    }

    [Fact]
    public void Cabang_tolak_dan_batal()
    {
        Assert.Equal(OrderStatus.Rejected, Step(OrderStatus.Pending, OrderAction.Reject, FulfillmentType.Pickup, OrderActor.Seller));
        Assert.Equal(OrderStatus.Cancelled, Step(OrderStatus.Pending, OrderAction.BuyerCancel, FulfillmentType.Pickup, OrderActor.Buyer));
        Assert.Equal(OrderStatus.Cancelled, Step(OrderStatus.Accepted, OrderAction.SellerCancel, FulfillmentType.Pickup, OrderActor.Seller));
    }

    [Theory]
    [InlineData(OrderStatus.Pending, OrderAction.Prepare)]
    [InlineData(OrderStatus.Pending, OrderAction.ConfirmReceived)]
    [InlineData(OrderStatus.Accepted, OrderAction.BuyerCancel)]
    [InlineData(OrderStatus.Preparing, OrderAction.SellerCancel)]
    [InlineData(OrderStatus.Ready, OrderAction.ConfirmReceived)]
    [InlineData(OrderStatus.Delivered, OrderAction.HandOver)]
    public void Perpindahan_yang_tidak_diizinkan_ditolak(OrderStatus status, OrderAction action)
    {
        Assert.False(OrderStateMachine.TryNext(status, action, FulfillmentType.Delivery, out _, out _));
    }

    [Fact]
    public void Status_akhir_tidak_dapat_diubah_lagi()
    {
        foreach (var final in new[] { OrderStatus.Completed, OrderStatus.Rejected, OrderStatus.Cancelled })
        {
            foreach (var action in Enum.GetValues<OrderAction>())
            {
                Assert.False(OrderStateMachine.TryNext(final, action, FulfillmentType.Delivery, out _, out _), $"{final} + {action}");
            }
        }
    }
}
