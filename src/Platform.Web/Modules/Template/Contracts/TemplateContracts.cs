namespace Platform.Web.Modules.Template.Contracts;

/// <summary>Modul yang dapat diaktifkan/dinonaktifkan per jenis bisnis.</summary>
public enum ModuleKey
{
    Catalog,
    Packaging,
    Variants,
    Inventory,
    Purchasing,
    Pos,
    Accounting,
    Marketplace,
    Orders,
    Map,
    Delivery,

    /// <summary>Anti-mubazir: batch kadaluarsa, paket diskon, dan penawaran.</summary>
    Rescue,

    /// <summary>Data penjualan warung untuk grosir.</summary>
    Insights
}

/// <summary>
/// Template bukan sekadar tema: menentukan modul aktif, satuan dasar, dan field produk tambahan.
/// Menu dan form dirender dari template, bukan dari if (jenisBisnis == "warung").
/// </summary>
public sealed record BusinessTemplate(
    string TypeCode,
    string DisplayName,
    string DefaultBaseUnit,
    IReadOnlyList<ModuleKey> Modules,
    IReadOnlyList<string> ExtraProductFields)
{
    public bool Has(ModuleKey module) => Modules.Contains(module);
}

/// <summary>Satu menu navigasi. Group = judul kelompok di sidebar, Icon = nama ikon UiIcons, BadgeKey = kunci hitungan notifikasi.</summary>
public sealed record MenuItem(string Text, string Controller, string Action, string Group = "", string Icon = "circle-check", string? BadgeKey = null);

public interface IBusinessTemplateService
{
    /// <summary>Template untuk usaha pengguna yang sedang login (default: template OTHER).</summary>
    Task<BusinessTemplate> GetCurrentAsync(CancellationToken ct = default);
}

public interface IMenuService
{
    /// <summary>Menu navigasi sesuai role dan modul aktif pada template usaha.</summary>
    Task<IReadOnlyList<MenuItem>> GetMenuAsync(CancellationToken ct = default);
}
