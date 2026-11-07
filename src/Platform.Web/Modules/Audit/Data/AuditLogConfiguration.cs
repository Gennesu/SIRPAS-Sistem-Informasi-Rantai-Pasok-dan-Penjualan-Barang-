using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Audit.Domain;

namespace Platform.Web.Modules.Audit.Data;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.ToTable("AuditLogs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasMaxLength(64).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(64);
        b.Property(x => x.EntityId).HasMaxLength(64);
        b.Property(x => x.Detail).HasMaxLength(1000);
        b.HasIndex(x => new { x.BusinessId, x.CreatedAt });
        b.HasIndex(x => x.UserId);
    }
}
