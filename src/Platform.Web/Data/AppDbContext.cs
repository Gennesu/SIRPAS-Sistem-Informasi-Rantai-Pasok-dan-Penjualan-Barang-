using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Platform.Web.Modules.Accounting.Domain;
using Platform.Web.Modules.Audit.Domain;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Catalog.Domain;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.Modules.Delivery.Domain;
using Platform.Web.Modules.Orders.Domain;
using Platform.Web.Modules.Purchasing.Domain;
using Platform.Web.Modules.Rescue.Domain;
using Platform.Web.Modules.Sales.Domain;
using Platform.Web.Modules.Stock.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Data;

/// <summary>
/// Satu DbContext untuk seluruh modul (database universal).
/// Konfigurasi entity ditulis per modul (IEntityTypeConfiguration) dan ditemukan otomatis.
/// V3: concurrency token (IVersioned) dinaikkan otomatis, dan filter tenant mencakup pesanan pembeli akhir.
/// </summary>
public class AppDbContext : DbContext
{
    private static readonly MethodInfo ApplyTenantFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ApplyTwoPartyFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyTwoPartyFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ApplyConsumerFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyConsumerFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly MethodInfo ApplyUserFilterMethod =
        typeof(AppDbContext).GetMethod(nameof(ApplyUserFilter), BindingFlags.NonPublic | BindingFlags.Instance)!;

    private readonly IBusinessContext _businessContext;

    public AppDbContext(DbContextOptions<AppDbContext> options, IBusinessContext businessContext)
        : this((DbContextOptions)options, businessContext)
    {
    }

    protected AppDbContext(DbContextOptions options, IBusinessContext businessContext)
        : base(options)
    {
        _businessContext = businessContext;
    }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<BusinessType> BusinessTypes => Set<BusinessType>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductUnit> ProductUnits => Set<ProductUnit>();
    public DbSet<Inventory> Inventories => Set<Inventory>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<StockBatch> StockBatches => Set<StockBatch>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Expense> Expenses => Set<Expense>();

    public DbSet<DeliveryOption> DeliveryOptions => Set<DeliveryOption>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderEvent> OrderEvents => Set<OrderEvent>();

    public DbSet<RescueBundle> RescueBundles => Set<RescueBundle>();
    public DbSet<RescueBundleItem> RescueBundleItems => Set<RescueBundleItem>();
    public DbSet<RescueOffer> RescueOffers => Set<RescueOffer>();

    // ---- V3: pembeli akhir
    public DbSet<ConsumerProfile> ConsumerProfiles => Set<ConsumerProfile>();
    public DbSet<ConsumerOrder> ConsumerOrders => Set<ConsumerOrder>();
    public DbSet<ConsumerOrderItem> ConsumerOrderItems => Set<ConsumerOrderItem>();
    public DbSet<ConsumerOrderEvent> ConsumerOrderEvents => Set<ConsumerOrderEvent>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ConsumerPromo> ConsumerPromos => Set<ConsumerPromo>();

    // Dibaca oleh global query filter; dievaluasi per instance DbContext.
    private bool CurrentIsAdmin => _businessContext.IsAdmin;
    private int? CurrentBusinessId => _businessContext.BusinessId;
    private int? CurrentUserId => _businessContext.UserId;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        if (Database.ProviderName?.Contains("MySql", StringComparison.OrdinalIgnoreCase) == true)
        {
            ConfigureMySqlConstraints(modelBuilder);
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(IBusinessOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTenantFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(ITwoPartyOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyTwoPartyFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(IConsumerOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyConsumerFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
            else if (typeof(IUserOwned).IsAssignableFrom(entityType.ClrType))
            {
                ApplyUserFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private static void ConfigureMySqlConstraints(ModelBuilder modelBuilder)
    {
        // Ekspresi CHECK yang dipakai SQLite/PostgreSQL menggunakan tanda kutip ganda;
        // MySQL memakai backtick untuk nama kolom (tanda kutip ganda default-nya string).
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var constraint in entity.GetCheckConstraints().ToList())
            {
                var sql = constraint.Sql.Replace("\"", "`", StringComparison.Ordinal);
                var name = constraint.Name ?? throw new InvalidOperationException("Constraint database harus memiliki nama.");
                entity.RemoveCheckConstraint(name);
                entity.AddCheckConstraint(name, sql);
            }
        }

        // MySQL tidak menyediakan partial/filtered unique index. Kolom hasil kalkulasi bernilai
        // ConsumerOrderId hanya untuk pembayaran sukses, dan NULL untuk status lain; unique index
        // kemudian tetap menjamin maksimal satu pembayaran sukses per pesanan.
        var paymentEntity = modelBuilder.Model.FindEntityType(typeof(Payment))!;
        var filteredIndex = paymentEntity.GetIndexes().Single(index => index.IsUnique && index.GetFilter() is not null);
        paymentEntity.RemoveIndex(filteredIndex);

        modelBuilder.Entity<Payment>()
            .Property<int?>("SuccessfulConsumerOrderId")
            .HasComputedColumnSql("CASE WHEN `Status` = 'Success' THEN `ConsumerOrderId` ELSE NULL END", stored: true);
        modelBuilder.Entity<Payment>()
            .HasIndex("SuccessfulConsumerOrderId")
            .IsUnique()
            .HasDatabaseName("UX_Payments_OneSuccessPerOrder");
    }

    // Concurrency token: setiap UPDATE menaikkan Version sehingga pembaruan serentak atas baris yang sama terdeteksi.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        BumpVersions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        BumpVersions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void BumpVersions()
    {
        foreach (var entry in ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Modified)
            {
                entry.Entity.Version = entry.OriginalValues.GetValue<long>(nameof(IVersioned.Version)) + 1;
            }
        }
    }

    // Pesanan: hanya pembeli, penjual, atau admin yang dapat melihat.
    private void ApplyTwoPartyFilter<T>(ModelBuilder modelBuilder) where T : class, ITwoPartyOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            CurrentIsAdmin || e.BuyerBusinessId == CurrentBusinessId || e.SellerBusinessId == CurrentBusinessId);
    }

    // Pesanan pembeli akhir: hanya pembeli itu sendiri, warung penjual, atau admin.
    private void ApplyConsumerFilter<T>(ModelBuilder modelBuilder) where T : class, IConsumerOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(e =>
            CurrentIsAdmin || e.ConsumerUserId == CurrentUserId || e.SellerBusinessId == CurrentBusinessId);
    }

    // Data pribadi satu pengguna (profil pembeli): hanya pemilik atau admin.
    private void ApplyUserFilter<T>(ModelBuilder modelBuilder) where T : class, IUserOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => CurrentIsAdmin || e.UserId == CurrentUserId);
    }

    // Admin melihat semua data; pengguna lain hanya data BusinessId miliknya; anonim tidak melihat apa pun.
    private void ApplyTenantFilter<T>(ModelBuilder modelBuilder) where T : class, IBusinessOwned
    {
        modelBuilder.Entity<T>().HasQueryFilter(e => CurrentIsAdmin || e.BusinessId == CurrentBusinessId);
    }
}
