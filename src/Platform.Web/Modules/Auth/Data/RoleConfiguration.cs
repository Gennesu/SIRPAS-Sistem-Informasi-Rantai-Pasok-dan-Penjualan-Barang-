using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Auth.Data;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.ToTable("Roles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Name).HasMaxLength(32).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();

        b.HasData(
            new Role { Id = 1, Name = RoleNames.Admin },
            new Role { Id = 2, Name = RoleNames.Grossier },
            new Role { Id = 3, Name = RoleNames.Retail },
            new Role { Id = 5, Name = RoleNames.Consumer },
            new Role { Id = 6, Name = RoleNames.Livestock });
    }
}
