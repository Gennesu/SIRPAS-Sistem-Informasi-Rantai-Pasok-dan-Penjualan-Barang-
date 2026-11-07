using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Catalog.Domain;
using Platform.Web.Modules.Purchasing.Domain;

namespace Platform.Web.Modules.Purchasing.Data;

public sealed class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> b)
    {
        b.ToTable("Purchases");
        b.HasKey(x => x.Id);
        b.Property(x => x.SupplierName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.Source).HasMaxLength(16).IsRequired();
        b.Property(x => x.TotalAmount).HasPrecision(18, 2);
        b.Property(x => x.IdempotencyKey).HasMaxLength(64);
        b.HasIndex(x => new { x.BusinessId, x.PurchasedAt });
        b.HasIndex(x => new { x.BusinessId, x.IdempotencyKey }).IsUnique();

        // JAMINAN DATABASE: satu pesanan hanya dapat menjadi SATU pembelian di stok pembeli (penerimaan ulang tidak menambah stok lagi).
        b.HasIndex(x => new { x.BusinessId, x.OrderId }).IsUnique();

        b.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(i => i.PurchaseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PurchaseItemConfiguration : IEntityTypeConfiguration<PurchaseItem>
{
    public void Configure(EntityTypeBuilder<PurchaseItem> b)
    {
        b.ToTable("PurchaseItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
        b.Property(x => x.UnitName).HasMaxLength(20).IsRequired();
        b.Property(x => x.UnitPricePack).HasPrecision(18, 2);
        b.Property(x => x.CostPerBase).HasPrecision(18, 4);
        b.Property(x => x.Subtotal).HasPrecision(18, 2);

        b.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
