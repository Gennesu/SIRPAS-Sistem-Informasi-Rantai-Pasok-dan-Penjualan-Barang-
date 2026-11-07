using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Web.Modules.Accounting.Domain;

namespace Platform.Web.Modules.Accounting.Data;

public sealed class ExpenseConfiguration : IEntityTypeConfiguration<Expense>
{
    public void Configure(EntityTypeBuilder<Expense> b)
    {
        b.ToTable("Expenses");
        b.HasKey(x => x.Id);
        b.Property(x => x.Category).HasMaxLength(40).IsRequired();
        b.Property(x => x.Note).HasMaxLength(200);
        b.Property(x => x.Amount).HasPrecision(18, 2);
        b.HasIndex(x => new { x.BusinessId, x.Date });
    }
}
