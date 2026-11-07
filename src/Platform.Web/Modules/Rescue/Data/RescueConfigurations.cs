using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Rescue.Domain;

namespace Platform.Web.Modules.Rescue.Data;

public sealed class RescueBundleConfiguration : IEntityTypeConfiguration<RescueBundle>
{
    public void Configure(EntityTypeBuilder<RescueBundle> b)
    {
        b.ToTable("RescueBundles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Title).HasMaxLength(100).IsRequired();
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.Price).HasPrecision(18, 2);
        b.Property(x => x.NormalPrice).HasPrecision(18, 2);
        b.HasIndex(x => new { x.BusinessId, x.IsEnded, x.ValidUntil });

        b.HasMany(x => x.Items)
            .WithOne()
            .HasForeignKey(i => i.BundleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class RescueBundleItemConfiguration : IEntityTypeConfiguration<RescueBundleItem>
{
    public void Configure(EntityTypeBuilder<RescueBundleItem> b)
    {
        b.ToTable("RescueBundleItems");
        b.HasKey(x => x.Id);
        b.Property(x => x.ProductName).HasMaxLength(120).IsRequired();
        b.Property(x => x.BaseUnitName).HasMaxLength(20).IsRequired();
        b.Property(x => x.NormalUnitPriceBase).HasPrecision(18, 4);
        b.HasIndex(x => x.BatchId);
    }
}

public sealed class RescueOfferConfiguration : IEntityTypeConfiguration<RescueOffer>
{
    public void Configure(EntityTypeBuilder<RescueOffer> b)
    {
        b.ToTable("RescueOffers");
        b.HasKey(x => x.Id);
        b.Property(x => x.SellerName).HasMaxLength(120).IsRequired();
        b.Property(x => x.BuyerName).HasMaxLength(120).IsRequired();
        b.Property(x => x.BundleTitle).HasMaxLength(100).IsRequired();
        b.Property(x => x.Message).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(x => new { x.BuyerBusinessId, x.Status, x.ExpiresAt });
        b.HasIndex(x => new { x.SellerBusinessId, x.CreatedAt });
    }
}
