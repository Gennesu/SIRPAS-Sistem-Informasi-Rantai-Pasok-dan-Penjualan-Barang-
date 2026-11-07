using Platform.Web.Modules.Template.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Template.Services;

public sealed class MenuService : IMenuService
{
    private static readonly string[] RetailOnly = { RoleNames.Retail };
    private static readonly string[] GrossierOnly = { RoleNames.Grossier };
    private static readonly string[] LivestockOnly = { RoleNames.Livestock };

    // Satu baris per menu: modul pemilik, teks, controller, action, role, kelompok sidebar, ikon, dan kunci lencana.
    // Urutan baris = urutan tampil; kelompok mengikuti kemunculan pertama.
    private static readonly (ModuleKey Module, string Text, string Controller, string Action, string[] Roles, string Group, string Icon, string? Badge)[] Items =
    {
        // ---- Warung: layani pembeli, kelola barang, belanja ke grosir, lalu tangani stok berisiko.
        (ModuleKey.Pos, "Kasir", "Pos", "Index", RetailOnly, "Jualan", "calculator", null),
        (ModuleKey.Pos, "Pesanan pembeli", "ConsumerOrders", "Index", RetailOnly, "Jualan", "shopping-basket", "consumer-orders"),
        (ModuleKey.Delivery, "Pengantaran ke pembeli", "Delivery", "Index", RetailOnly, "Jualan", "truck", null),
        (ModuleKey.Pos, "Riwayat penjualan", "Sales", "Index", RetailOnly, "Jualan", "receipt", null),
        (ModuleKey.Accounting, "Keuangan", "Finance", "Index", RetailOnly, "Jualan", "wallet", null),
        (ModuleKey.Catalog, "Produk", "Products", "Index", RetailOnly, "Barang", "package", null),
        (ModuleKey.Inventory, "Stok", "Stock", "Index", RetailOnly, "Barang", "boxes", "stock-low"),
        (ModuleKey.Purchasing, "Pembelian", "Purchases", "Index", RetailOnly, "Barang", "package-open", null),
        (ModuleKey.Marketplace, "Cari agen", "Marketplace", "Index", RetailOnly, "Belanja ke grosir", "store", null),
        (ModuleKey.Orders, "Pesanan saya", "Orders", "Index", RetailOnly, "Belanja ke grosir", "truck", "orders-confirm"),
        (ModuleKey.Map, "Peta agen", "Map", "Index", RetailOnly, "Belanja ke grosir", "map", null),
        (ModuleKey.Rescue, "Kadaluarsa", "Rescue", "Index", RetailOnly, "Anti-mubazir", "leaf", "expiring"),
        (ModuleKey.Rescue, "Promo & penawaran", "Deals", "Index", RetailOnly, "Anti-mubazir", "badge-percent", "offers"),
        (ModuleKey.Rescue, "Promo untuk pembeli", "ConsumerPromos", "Index", RetailOnly, "Anti-mubazir", "badge-percent", null),
        (ModuleKey.Rescue, "Dampak food waste", "Rescue", "Impact", RetailOnly, "Anti-mubazir", "leaf", null),

        // ---- Grosir: proses pesanan, kelola toko, tangani stok berisiko, lalu lihat mitra warung.
        (ModuleKey.Orders, "Pesanan & pasokan", "Orders", "Index", GrossierOnly, "Pesanan", "inbox", "orders-pending"),
        (ModuleKey.Orders, "Pembelian dari peternak", "Orders", "FromLivestock", GrossierOnly, "Pesanan", "package-open", "agent-sales-confirm"),
        (ModuleKey.Delivery, "Pengiriman & ongkir", "Delivery", "Index", GrossierOnly, "Pesanan", "truck", null),
        (ModuleKey.Catalog, "Produk & harga", "Products", "Index", GrossierOnly, "Toko", "package", null),
        (ModuleKey.Inventory, "Stok & kadaluarsa", "Stock", "Index", GrossierOnly, "Toko", "boxes", null),
        (ModuleKey.Purchasing, "Stok masuk", "Purchases", "Index", GrossierOnly, "Toko", "package-open", null),
        (ModuleKey.Rescue, "Dashboard food waste", "Rescue", "Index", GrossierOnly, "Anti-mubazir", "leaf", "expiring"),
        (ModuleKey.Rescue, "Paket & penawaran", "Rescue", "Bundles", GrossierOnly, "Anti-mubazir", "gift", null),
        (ModuleKey.Rescue, "Dampak food waste", "Rescue", "Impact", GrossierOnly, "Anti-mubazir", "leaf", null),
        (ModuleKey.Insights, "Penjualan warung", "Insights", "Index", GrossierOnly, "Mitra", "chart-column", null),
        (ModuleKey.Map, "Peta warung & peternakan", "Map", "Index", GrossierOnly, "Mitra", "map-pin", null),

        // ---- Peternakan: siapkan produk, catat hasil, kelola stok, lalu tinjau ringkasan.
        (ModuleKey.Catalog, "Produk & foto produk", "Products", "Index", LivestockOnly, "Operasional", "gift", null),
        (ModuleKey.Catalog, "Produksi & panen", "Chain", "Produksi", LivestockOnly, "Operasional", "package", null),
        (ModuleKey.Inventory, "Manajemen stok", "Chain", "Stok", LivestockOnly, "Stok & penjualan", "boxes", null),
        (ModuleKey.Catalog, "Penjualan ke agen", "Chain", "Sales", LivestockOnly, "Stok & penjualan", "hand-coins", null),
        (ModuleKey.Delivery, "Pengiriman & ongkir", "Delivery", "Index", LivestockOnly, "Stok & penjualan", "truck", null),
        (ModuleKey.Map, "Peta agen", "Map", "Index", LivestockOnly, "Stok & penjualan", "map-pin", null),
        (ModuleKey.Accounting, "Keuangan & laporan", "Chain", "Keuangan", LivestockOnly, "Ringkasan", "wallet", null)
    };

    // Pembeli akhir tidak punya usaha/template: menunya tetap.
    private static readonly MenuItem[] ConsumerItems =
    {
        new("Belanja di warung", "Shop", "Index", "Belanja", "store"),
        new("Peta warung terdekat", "Map", "Index", "Belanja", "map-pin"),
        new("Promo masih layak konsumsi", "Shop", "Promos", "Belanja", "badge-percent"),
        new("Pesanan saya", "ConsumerOrders", "Index", "Pesanan", "receipt")
    };

    private static readonly MenuItem[] AdminItems =
    {
        new("Usaha", "Admin", "Businesses", "Kelola platform", "building-2", "verify"),
        new("Pengguna", "Admin", "Users", "Kelola platform", "users"),
        new("Kategori", "Admin", "Categories", "Kelola platform", "layers"),
        new("Pesanan", "Admin", "Orders", "Pemantauan", "package"),
        new("Log aktivitas", "Admin", "Audit", "Pemantauan", "history")
    };

    private readonly IBusinessContext _context;
    private readonly IBusinessTemplateService _templates;

    public MenuService(IBusinessContext context, IBusinessTemplateService templates)
    {
        _context = context;
        _templates = templates;
    }

    public async Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken ct = default)
    {
        var role = _context.Role;
        if (_context.IsAuthenticated && _context.IsAdmin)
        {
            return AdminItems;
        }

        if (!_context.IsAuthenticated || role is null)
        {
            return Array.Empty<MenuItem>();
        }

        if (role == RoleNames.Consumer)
        {
            return ConsumerItems;
        }

        var template = await _templates.GetCurrentAsync(ct);
        return Items
            .Where(i => template.Has(i.Module) && i.Roles.Contains(role))
            .Select(i => new MenuItem(i.Text, i.Controller, i.Action, i.Group, i.Icon, i.Badge))
            .ToList();
    }
}
