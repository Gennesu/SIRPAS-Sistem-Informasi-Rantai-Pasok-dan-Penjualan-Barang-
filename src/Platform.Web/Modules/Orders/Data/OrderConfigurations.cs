using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Orders.Domain;

namespace Platform.Web.Modules.Orders.Data;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("Orders", t => t.HasCheckConstraint("CK_Orders_Total", "\"TotalAmount\" >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);
        b.HasIndex(x => new { x.BuyerBusinessId, x.IdempotencyKey }).IsUnique();
        b.Property(x => x.Number).HasMaxLength(20).IsRequired();
        b.Property(x => x.BuyerName).HasMaxLength(120).IsRequired();
        b.Property(x => x.SellerName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Fulfillment).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.DeliveryFee).HasPrecision(18, 2);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.DeliveryAddress).HasMaxLength(300);
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.RejectReason).HasMaxLength(200);
        b.HasIndex(x => new { x.BuyerBusinessId, x.CreatedAt });
        b.HasIndex(x => new { x.SellerBusinessId, x.CreatedAt });

        b.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        b.HasMany(x => x.Events)
            .WithOne()
            .HasForeignKey(e => e.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class OrderEventConfiguration : IEntityTypeConfiguration<OrderEvent>
{
    public void Configure(EntityTypeBuilder<OrderEvent> b)
    {
        b.ToTable("OrderEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.OrderId, x.At });
    }
}

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("OrderItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
        b.Property(x => x.ProductSku).HasMaxLength(40);
        b.Property(x => x.BaseUnitName).HasMaxLength(20).IsRequired();
        b.Property(x => x.UnitsSnapshot).HasMaxLength(300).IsRequired();
        b.Property(x => x.UnitName).HasMaxLength(20).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(18, 4);
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.SellerUnitCost).HasPrecision(18, 4);
        b.Property(x => x.NormalSubtotal).HasPrecision(18, 2);
        b.Property(x => x.ExpirySnapshot).HasMaxLength(400);
        b.Property(x => x.BundleName).HasMaxLength(100);
    }
}
