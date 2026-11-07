using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Catalog.Domain;
using Platform.Web.Modules.Sales.Domain;

namespace Platform.Web.Modules.Sales.Data;

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> b)
    {
        b.ToTable("Sales");
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(20).IsRequired();
        b.Property(x => x.PaymentMethod).HasMaxLength(16).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.PaidAmount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.BusinessId, x.SoldAt });
        b.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique();
        b.Property(x => x.SourceType).HasMaxLength(20);

        // JAMINAN DATABASE: satu pesanan pembeli akhir hanya menjadi SATU penjualan (omzet tidak tercatat dua kali).
        b.HasIndex(x => new { x.BusinessId, x.SourceType, x.SourceId }).IsUnique();

        b.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> b)
    {
        b.ToTable("SaleItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
        b.Property(x => x.UnitPrice).HasPrecision(18, 2);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.Subtotal).HasPrecision(18, 2);
        b.Property(x => x.CostTotal).HasPrecision(18, 2);

        b.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
