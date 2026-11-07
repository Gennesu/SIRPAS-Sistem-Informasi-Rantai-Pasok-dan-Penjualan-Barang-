using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.Modules.BusinessProfile.Domain;

namespace Platform.Web.Modules.BusinessProfile.Data;

public sealed class BusinessConfiguration : IEntityTypeConfiguration<Business>
{
    public void Configure(EntityTypeBuilder<Business> b)
    {
        b.ToTable("Businesses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Address).HasMaxLength(300).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        b.Property(x => x.MarketRole)
            .HasConversion(
                role => role == MarketRole.Retired ? "Agriculture" : role.ToString(),
                value => value == "Agriculture" ? MarketRole.Retired : Enum.Parse<MarketRole>(value))
            .HasMaxLength(16);
        b.Property(x => x.VerificationStatus).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(x => new { x.MarketRole, x.VerificationStatus, x.IsActive });

        b.HasOne(x => x.BusinessType)
            .WithMany()
            .HasForeignKey(x => x.BusinessTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK Users.BusinessId -> Businesses.Id tanpa navigation property:
        // modul Auth tidak perlu mengenal modul BusinessProfile (arah dependensi: BusinessProfile -> Auth).
        b.HasMany<AppUser>()
            .WithOne()
            .HasForeignKey(u => u.BusinessId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
