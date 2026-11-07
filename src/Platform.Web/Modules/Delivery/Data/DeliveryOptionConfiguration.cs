using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Delivery.Domain;

namespace Platform.Web.Modules.Delivery.Data;

public sealed class DeliveryOptionConfiguration : IEntityTypeConfiguration<DeliveryOption>
{
    public void Configure(EntityTypeBuilder<DeliveryOption> b)
    {
        b.ToTable("DeliveryOptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.DeliveryFee).HasPrecision(18, 2);
        b.Property(x => x.FreeKm).HasPrecision(8, 2);
        b.Property(x => x.FeePerKm).HasPrecision(18, 2);
        b.Property(x => x.MaxDistanceKm).HasPrecision(8, 2);
        b.Property(x => x.MinOrderAmount).HasPrecision(18, 2);
        b.Property(x => x.DeliveryInfo).HasMaxLength(200);
        b.Property(x => x.PickupInfo).HasMaxLength(200);
        b.HasIndex(x => x.BusinessId).IsUnique();
    }
}
