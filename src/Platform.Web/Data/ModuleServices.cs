using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Accounting.Services;
using Platform.Web.Modules.Audit.Services;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.Modules.Auth.Services;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Services;
using Platform.Web.Modules.Admin.Contracts;
using Platform.Web.Modules.Admin.Services;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Services;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Catalog.Services;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Delivery.Services;
using Platform.Web.Modules.Insights.Contracts;
using Platform.Web.Modules.Insights.Services;
using Platform.Web.Modules.Map.Contracts;
using Platform.Web.Modules.Map.Services;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Marketplace.Services;
using Platform.Web.Modules.Notifications.Contracts;
using Platform.Web.Modules.Notifications.Services;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Services;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Purchasing.Services;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Rescue.Services;
using Platform.Web.Modules.Reporting.Contracts;
using Platform.Web.Modules.Reporting.Services;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Sales.Services;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Services;
using Platform.Web.SharedKernel;

namespace Platform.Web.Data;

/// <summary>
/// Komposisi manual seluruh layanan untuk proses di luar HTTP (seeder data demo dan test):
/// satu DbContext, satu konteks pengguna ("berperan sebagai siapa"), satu jam.
/// Aplikasi web sendiri memakai dependency injection (lihat Program.cs).
/// </summary>
public sealed class ModuleServices : IDisposable
{
    public ModuleServices(DbContextOptions<AppDbContext> options, IBusinessContext context, TimeProvider time)
    {
        Db = new AppDbContext(options, context);

        var audit = new AuditLogService(Db, context, time);
        Auth = new AuthService(Db, context, new PasswordHasher<AppUser>(), audit, time, NullLogger<AuthService>.Instance);
        Businesses = new BusinessService(Db, context, Auth, audit, time, NullLogger<BusinessService>.Instance);
        Products = new ProductService(Db, context, audit, time, NullLogger<ProductService>.Instance);
        Uow = new EfUnitOfWork(Db, NullLogger<EfUnitOfWork>.Instance);
        Inventory = new InventoryService(Db, context, audit, time, NullLogger<InventoryService>.Instance, Uow);
        Reservations = new StockReservationService(Db, context, time, Uow);
        Integrity = new StockIntegrityService(Db);
        Purchasing = new PurchasingService(Db, context, Products, Inventory, audit, time, NullLogger<PurchasingService>.Instance, Uow);
        Sales = new SalesService(Db, context, Inventory, audit, time, NullLogger<SalesService>.Instance, Uow);
        Expenses = new ExpenseService(Db, context, audit, time);
        Reporting = new ReportingService(Db, time);
        Delivery = new DeliveryService(Db, context, audit);
        Expiry = new ExpiryService(Db, context, Inventory, audit, time, Uow);
        SellThrough = new SellThroughService(Db, context, time);
        Rescue = new RescueService(Db, context, Expiry, SellThrough, Delivery, audit, time);
        Marketplace = new MarketplaceService(Db, context, Businesses, Delivery, Rescue, time);
        Orders = new OrderService(
            Db, context, Marketplace, Delivery, Rescue, Businesses, Products, Inventory, Purchasing, Expenses, audit, time,
            NullLogger<OrderService>.Instance, Uow, Reservations);
        Consumers = new ConsumerService(Db, context, Auth, audit, time, Uow);
        ConsumerCatalog = new ConsumerCatalogService(Db, context, Delivery, time);
        ConsumerPromos = new ConsumerPromoService(Db, context, audit, time, Uow);
        ConsumerOrders = new ConsumerOrderService(Db, context, Uow, Reservations, Inventory, Sales, Delivery, audit, time);
        Payments = new PaymentService(Db, context, audit, time, Uow);
        FoodWaste = new FoodWasteService(Db, context, time);
        Admin = new AdminService(Db, context, Integrity);
        BusinessGate = new BusinessLoginGate(Db);
        WarungMap = new WarungMapService(Db, context, Delivery, SellThrough);
        Notifications = new NotificationService(Db, context, time);
    }

    public AppDbContext Db { get; }
    public IAuthService Auth { get; }
    public IBusinessService Businesses { get; }
    public IProductService Products { get; }
    public IUnitOfWork Uow { get; }
    public IConsumerService Consumers { get; }
    public IConsumerCatalogService ConsumerCatalog { get; }
    public IConsumerPromoService ConsumerPromos { get; }
    public IConsumerOrderService ConsumerOrders { get; }
    public IPaymentService Payments { get; }
    public IFoodWasteService FoodWaste { get; }
    public IAdminService Admin { get; }
    public ILoginGate BusinessGate { get; }
    public IInventoryService Inventory { get; }
    public IStockReservationService Reservations { get; }
    public IStockIntegrityService Integrity { get; }
    public IPurchasingService Purchasing { get; }
    public ISalesService Sales { get; }
    public IExpenseService Expenses { get; }
    public IReportingService Reporting { get; }
    public IDeliveryService Delivery { get; }
    public IExpiryService Expiry { get; }
    public ISellThroughService SellThrough { get; }
    public IRescueService Rescue { get; }
    public IMarketplaceService Marketplace { get; }
    public IOrderService Orders { get; }
    public IWarungMapService WarungMap { get; }
    public INotificationService Notifications { get; }

    public void Dispose() => Db.Dispose();
}
