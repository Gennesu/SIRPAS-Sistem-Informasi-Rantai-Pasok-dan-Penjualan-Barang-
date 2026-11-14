using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Accounting.Contracts;
using Platform.Web.Modules.Accounting.Services;
using Platform.Web.Modules.Admin.Contracts;
using Platform.Web.Modules.Admin.Services;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Audit.Services;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.Modules.Auth.Services;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Services;
using Platform.Web.Modules.BusinessProfile.Services;
using Platform.Web.Modules.Catalog.Contracts;
using Platform.Web.Modules.Catalog.Services;
using Platform.Web.Modules.Delivery.Contracts;
using Platform.Web.Modules.Delivery.Services;
using Platform.Web.Modules.Rescue.Services;
using Platform.Web.Modules.Rescue.Contracts;
using Platform.Web.Modules.Notifications.Services;
using Platform.Web.Modules.Notifications.Contracts;
using Platform.Web.Modules.Map.Services;
using Platform.Web.Modules.Map.Contracts;
using Platform.Web.Modules.Insights.Services;
using Platform.Web.Modules.Insights.Contracts;
using Platform.Web.Modules.Marketplace.Contracts;
using Platform.Web.Modules.Marketplace.Services;
using Platform.Web.Modules.Orders.Contracts;
using Platform.Web.Modules.Orders.Services;
using Platform.Web.Modules.Purchasing.Contracts;
using Platform.Web.Modules.Purchasing.Services;
using Platform.Web.Modules.Reporting.Contracts;
using Platform.Web.Modules.Reporting.Services;
using Platform.Web.Modules.Sales.Contracts;
using Platform.Web.Modules.Sales.Services;
using Platform.Web.Modules.Stock.Contracts;
using Platform.Web.Modules.Stock.Services;
using Platform.Web.Modules.Template.Contracts;
using Platform.Web.Modules.Template.Services;
using Platform.Web.SharedKernel;

// Angka/koordinat dari form selalu memakai titik desimal, apa pun regional setting komputer
// (tanpa ini "-6.2088" bisa terbaca -62088 di komputer berlokal id-ID).
// Tampilan rupiah diformat eksplisit lewat Rupiah.Format.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

var builder = WebApplication.CreateBuilder(args);

// MySQL/MariaDB menjadi database utama. SQLite dan PostgreSQL tetap bisa dipilih lewat konfigurasi.
var provider = builder.Configuration["Database:Provider"] ?? "MySql";
var useMySql = provider.StartsWith("MySql", StringComparison.OrdinalIgnoreCase);
var usePostgres = provider.StartsWith("Postgres", StringComparison.OrdinalIgnoreCase);
var useSqlite = provider.StartsWith("Sqlite", StringComparison.OrdinalIgnoreCase);
if (!useMySql && !usePostgres && !useSqlite)
{
    throw new InvalidOperationException($"Database:Provider '{provider}' tidak didukung. Gunakan MySql, Postgres, atau Sqlite.");
}

var connectionString = builder.Configuration.GetConnectionString("Default");

// SQLite bersifat sinkron di bawah API async, sehingga konfigurasi thread minimum ini hanya diperlukan pada mode SQLite.
if (useSqlite)
{
    ThreadPool.GetMinThreads(out var minWorkerThreads, out var minIoThreads);
    ThreadPool.SetMinThreads(Math.Max(minWorkerThreads, 64), minIoThreads);
}

// SQLite: jalur file relatif dihitung dari content root agar database yang sama dipakai saat restart dari folder mana pun.
if (!string.IsNullOrWhiteSpace(connectionString) && useSqlite)
{
    var sqlite = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
    if (!string.IsNullOrWhiteSpace(sqlite.DataSource) && sqlite.DataSource != ":memory:" && !sqlite.DataSource.StartsWith("file:", StringComparison.Ordinal))
    {
        sqlite.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, sqlite.DataSource));
        Directory.CreateDirectory(Path.GetDirectoryName(sqlite.DataSource)!);
        connectionString = sqlite.ToString();
    }
}
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:Default belum diatur. Atur connection string MySQL melalui user-secrets atau variabel lingkungan (lihat README).");
}

// ---- Infrastruktur bersama ----
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(builder.Environment.ContentRootPath, "App_Data", "keys")));
builder.Services.Configure<DemoOptions>(builder.Configuration.GetSection("Demo"));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IBusinessContext, HttpBusinessContext>();
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (usePostgres)
    {
        options.UseNpgsql(connectionString);
    }
    else if (useMySql)
    {
        options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// ---- MVC: view modul ada di Modules/{Modul}/Views; semua POST wajib antiforgery token ----
builder.Services
    .AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddRazorOptions(options => options.ViewLocationExpanders.Add(new ModuleViewLocationExpander()));

// ---- Authentication & Authorization ----
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "platform.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/status/403";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;

        // Setiap request: akun/usaha yang dinonaktifkan admin langsung dikeluarkan.
        options.Events.OnValidatePrincipal = CookieSessionValidator.ValidateAsync;
    });

// Aman secara default: semua halaman butuh login kecuali yang diberi [AllowAnonymous].
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// ---- Modul (satu baris per layanan: antarmodul hanya lewat interface) ----
builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ISignInService, CookieSignInService>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<IBusinessTemplateService, BusinessTemplateService>();
builder.Services.AddScoped<IMenuService, MenuService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IProductImageStore, ProductImageStore>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddScoped<IFoodWasteService, FoodWasteService>();
builder.Services.AddScoped<IConsumerService, ConsumerService>();
builder.Services.AddScoped<IConsumerCatalogService, ConsumerCatalogService>();
builder.Services.AddScoped<IConsumerPromoService, ConsumerPromoService>();
builder.Services.AddScoped<IConsumerOrderService, ConsumerOrderService>();
builder.Services.AddScoped<IPaymentService, PaymentService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IStockReservationService, StockReservationService>();
builder.Services.AddScoped<IStockIntegrityService, StockIntegrityService>();
builder.Services.AddScoped<IPurchasingService, PurchasingService>();
builder.Services.AddScoped<ISalesService, SalesService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IReportingService, ReportingService>();
builder.Services.AddScoped<ILoginGate, BusinessLoginGate>();
builder.Services.AddScoped<IDeliveryService, DeliveryService>();
builder.Services.AddScoped<IExpiryService, ExpiryService>();
builder.Services.AddScoped<ISellThroughService, SellThroughService>();
builder.Services.AddScoped<IRescueService, RescueService>();
builder.Services.AddScoped<IMarketplaceService, MarketplaceService>();
builder.Services.AddScoped<IWarungMapService, WarungMapService>();
builder.Services.AddScoped<ILivestockMapService, LivestockMapService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IAdminService, AdminService>();

var app = builder.Build();

// NFR-16: pengguna tidak pernah melihat detail internal server atau stack trace, termasuk saat dijalankan lewat `dotnet run`
// (mode Development). Halaman pengembang hanya muncul bila Demo:ShowDeveloperErrors = true secara eksplisit.
if (app.Configuration.GetValue<bool>("Demo:ShowDeveloperErrors") && app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/status/500");
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/status/{0}");
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

await DbSeeder.InitializeAsync(app.Services, app.Configuration);

app.Run();
