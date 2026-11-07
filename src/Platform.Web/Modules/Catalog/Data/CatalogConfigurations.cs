using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Catalog.Domain;

namespace Platform.Web.Modules.Catalog.Data;

public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("Categories");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(60).IsRequired();

        b.HasData(
            new Category { Id = 1, Name = "Makanan Instan" },
            new Category { Id = 2, Name = "Minyak & Bumbu" },
            new Category { Id = 3, Name = "Minuman" },
            new Category { Id = 4, Name = "Sembako" },
            new Category { Id = 5, Name = "Kebersihan" },
            new Category { Id = 6, Name = "Lainnya" },
            new Category { Id = 7, Name = "Telur & Unggas" },
            new Category { Id = 8, Name = "Hasil Tani" });
    }
}

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("Products", t => t.HasCheckConstraint("CK_Products_Moq", "\"MoqBase\" >= 1"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(120).IsRequired();
        b.Property(x => x.Sku).HasMaxLength(40);
        b.Property(x => x.Description).HasMaxLength(300);
        b.Property(x => x.ImagePath).HasMaxLength(260);
        b.Property(x => x.BaseUnitName).HasMaxLength(20).IsRequired();
        b.HasIndex(x => new { x.BusinessId, x.Sku }).IsUnique();
        b.HasIndex(x => new { x.BusinessId, x.Name });

        b.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasMany(x => x.Units)
            .WithOne()
            .HasForeignKey(u => u.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class ProductUnitConfiguration : IEntityTypeConfiguration<ProductUnit>
{
    public void Configure(EntityTypeBuilder<ProductUnit> b)
    {
        b.ToTable("ProductUnits");
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitName).HasMaxLength(20).IsRequired();
        b.Property(x => x.WholesalePrice).HasPrecision(18, 2);
        b.HasIndex(x => new { x.ProductId, x.UnitName }).IsUnique();
    }
}
