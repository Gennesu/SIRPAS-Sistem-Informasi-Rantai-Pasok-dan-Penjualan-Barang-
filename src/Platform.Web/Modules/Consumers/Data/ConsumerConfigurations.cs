using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Consumers.Domain;

namespace Platform.Web.Modules.Consumers.Data;

public sealed class ConsumerProfileConfiguration : IEntityTypeConfiguration<ConsumerProfile>
{
    public void Configure(EntityTypeBuilder<ConsumerProfile> b)
    {
        b.ToTable("ConsumerProfiles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.Address).HasMaxLength(300).IsRequired();
        b.HasIndex(x => x.UserId).IsUnique();
    }
}

public sealed class ConsumerOrderConfiguration : IEntityTypeConfiguration<ConsumerOrder>
{
    public void Configure(EntityTypeBuilder<ConsumerOrder> b)
    {
        b.ToTable("ConsumerOrders", t => t.HasCheckConstraint("CK_ConsumerOrders_Total", "\"TotalAmount\" >= 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Number).HasMaxLength(20).IsRequired();
        b.Property(x => x.ConsumerName).HasMaxLength(120).IsRequired();
        b.Property(x => x.ConsumerPhone).HasMaxLength(20);
        b.Property(x => x.SellerName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Fulfillment).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.DeliveryFee).HasPrecision(18, 2);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.DeliveryAddress).HasMaxLength(300);
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.RejectReason).HasMaxLength(200);
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);

        // Anti pesanan ganda: satu kunci operasi hanya boleh membuat satu pesanan per pembeli.
        b.HasIndex(x => new { x.ConsumerUserId, x.IdempotencyKey }).IsUnique();
        b.HasIndex(x => new { x.ConsumerUserId, x.CreatedAt });
        b.HasIndex(x => new { x.SellerBusinessId, x.CreatedAt });

        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.ConsumerOrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Events).WithOne().HasForeignKey(e => e.ConsumerOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ConsumerOrderItemConfiguration : IEntityTypeConfiguration<ConsumerOrderItem>
{
    public void Configure(EntityTypeBuilder<ConsumerOrderItem> b)
    {
        b.ToTable("ConsumerOrderItems", t => t.HasCheckConstraint("CK_ConsumerOrderItems_Qty", "\"QtyBase\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
        b.Property(x => x.BaseUnitName).HasMaxLength(20).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(18, 4);
        b.Property(x => x.RegularUnitPrice).HasPrecision(18, 4);
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.ExpirySnapshot).HasMaxLength(400);
    }
}

public sealed class ConsumerOrderEventConfiguration : IEntityTypeConfiguration<ConsumerOrderEvent>
{
    public void Configure(EntityTypeBuilder<ConsumerOrderEvent> b)
    {
        b.ToTable("ConsumerOrderEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.Note).HasMaxLength(200);
        b.HasIndex(x => new { x.ConsumerOrderId, x.At });
    }
}

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> b)
    {
        b.ToTable("Payments", t => t.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.Reference).HasMaxLength(24).IsRequired();
        b.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);
        b.Property(x => x.FailureReason).HasMaxLength(200);
        b.HasIndex(x => x.Reference).IsUnique();

        // Satu operasi pembayaran (kunci sama) tidak membuat catatan kedua.
        b.HasIndex(x => new { x.ConsumerOrderId, x.IdempotencyKey }).IsUnique();

        // JAMINAN DATABASE: paling banyak satu pembayaran SUKSES per pesanan, apa pun yang dilakukan aplikasi.
        b.HasIndex(x => x.ConsumerOrderId).IsUnique().HasFilter("\"Status\" = 'Success'")
            .HasDatabaseName("UX_Payments_OneSuccessPerOrder");
        b.HasIndex(x => x.ConsumerOrderId);

        b.HasOne<ConsumerOrder>().WithMany().HasForeignKey(x => x.ConsumerOrderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ConsumerPromoConfiguration : IEntityTypeConfiguration<ConsumerPromo>
{
    public void Configure(EntityTypeBuilder<ConsumerPromo> b)
    {
        b.ToTable("ConsumerPromos", t => t.HasCheckConstraint(
            "CK_ConsumerPromos_Range", "\"DiscountPercent\" BETWEEN 1 AND 90 AND \"WithinDays\" BETWEEN 1 AND 90"));
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.BusinessId, x.ProductId }).IsUnique();
    }
}
