using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.BusinessProfile.Domain;

namespace Platform.Web.Modules.BusinessProfile.Data;

public sealed class BusinessTypeConfiguration : IEntityTypeConfiguration<BusinessType>
{
    public void Configure(EntityTypeBuilder<BusinessType> b)
    {
        b.ToTable("BusinessTypes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(32).IsRequired();
        b.Property(x => x.Name).HasMaxLength(80).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();

        b.HasData(
            new BusinessType { Id = 1, Code = "GROCERY", Name = "Toko Kelontong / Sembako" },
            new BusinessType { Id = 2, Code = "CLOTHING", Name = "Toko Pakaian" },
            new BusinessType { Id = 3, Code = "ELECTRONICS", Name = "Toko Elektronik" },
            new BusinessType { Id = 4, Code = "OTHER", Name = "Lainnya" });
    }
}
