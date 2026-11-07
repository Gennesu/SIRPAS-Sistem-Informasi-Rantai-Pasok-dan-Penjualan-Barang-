using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Stock.Domain;

namespace Platform.Web.Modules.Stock.Data;

public sealed class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> b)
    {
        // Pengaman terakhir di database: walaupun ada bug di aplikasi, stok negatif atau reservasi berlebih tidak dapat tersimpan.
        b.ToTable("Inventories", t => t.HasCheckConstraint(
            "CK_Inventories_Qty", "\"QtyBase\" >= 0 AND \"ReservedBase\" >= 0 AND \"ReservedBase\" <= \"QtyBase\""));
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.AvgCostBase).HasPrecision(18, 4);
        b.Property(x => x.SellingPriceBase).HasPrecision(18, 2);
        b.HasIndex(x => new { x.BusinessId, x.ProductId }).IsUnique();

        b.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        b.ToTable("InventoryMovements");
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(24);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.RefType).HasMaxLength(24);
        b.Property(x => x.Reason).HasMaxLength(200);
        b.Property(x => x.OperationKey).HasMaxLength(64);
        b.HasIndex(x => new { x.BusinessId, x.CreatedAt });
        b.HasIndex(x => x.InventoryId);
        b.HasIndex(x => new { x.BusinessId, x.OperationKey }).IsUnique();

        b.HasOne(x => x.Inventory)
            .WithMany()
            .HasForeignKey(x => x.InventoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockBatchConfiguration : IEntityTypeConfiguration<StockBatch>
{
    public void Configure(EntityTypeBuilder<StockBatch> b)
    {
        b.ToTable("StockBatches", t => t.HasCheckConstraint(
            "CK_StockBatches_Qty", "\"QtyRemainingBase\" >= 0 AND \"ReservedBase\" >= 0 AND \"ReservedBase\" <= \"QtyRemainingBase\""));
        b.HasKey(x => x.Id);
        b.Property(x => x.Version).IsConcurrencyToken();
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.SourceRef).HasMaxLength(40);
        b.Property(x => x.LotNumber).HasMaxLength(40);
        b.HasIndex(x => new { x.BusinessId, x.ProductId, x.ExpiryDate });
        b.HasIndex(x => new { x.BusinessId, x.ExpiryDate });

        b.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> b)
    {
        b.ToTable("StockReservations", t => t.HasCheckConstraint("CK_StockReservations_Qty", "\"QtyBase\" > 0"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.RefType).HasMaxLength(12).IsRequired();
        b.HasIndex(x => new { x.RefType, x.RefId, x.Status });
        b.HasIndex(x => new { x.InventoryId, x.Status });
        b.HasIndex(x => x.BatchId);
    }
}
