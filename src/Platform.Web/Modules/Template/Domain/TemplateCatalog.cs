using Platform.Web.Modules.Template.Contracts;

namespace Platform.Web.Modules.Template.Domain;

/// <summary>
/// Definisi template per jenis bisnis. Menambah jenis bisnis baru = menambah satu entri di sini
/// (dan satu baris BusinessType), tanpa mengubah kode modul lain.
/// </summary>
public static class TemplateCatalog
{
    public static readonly BusinessTemplate Grocery = new(
        "GROCERY", "Toko Kelontong / Sembako", "Item",
        new[]
        {
            ModuleKey.Catalog, ModuleKey.Packaging, ModuleKey.Inventory, ModuleKey.Purchasing,
            ModuleKey.Pos, ModuleKey.Accounting, ModuleKey.Marketplace, ModuleKey.Orders, ModuleKey.Map,
            ModuleKey.Rescue, ModuleKey.Insights, ModuleKey.Delivery
        },
        new[] { "Kemasan dan konversi satuan" });

    public static readonly BusinessTemplate Clothing = new(
        "CLOTHING", "Toko Pakaian", "Piece",
        new[]
        {
            ModuleKey.Catalog, ModuleKey.Variants, ModuleKey.Inventory, ModuleKey.Purchasing,
            ModuleKey.Pos, ModuleKey.Accounting, ModuleKey.Marketplace, ModuleKey.Orders, ModuleKey.Map,
            ModuleKey.Delivery
        },
        new[] { "Varian ukuran", "Varian warna" });

    public static readonly BusinessTemplate Electronics = new(
        "ELECTRONICS", "Toko Elektronik", "Unit",
        new[]
        {
            ModuleKey.Catalog, ModuleKey.Inventory, ModuleKey.Purchasing,
            ModuleKey.Pos, ModuleKey.Accounting, ModuleKey.Marketplace, ModuleKey.Orders, ModuleKey.Map,
            ModuleKey.Delivery
        },
        new[] { "Merek", "Model", "Nomor seri (opsional)", "Garansi (opsional)" });

    public static readonly BusinessTemplate Other = new(
        "OTHER", "Usaha Lainnya", "Item",
        new[]
        {
            ModuleKey.Catalog, ModuleKey.Inventory, ModuleKey.Purchasing,
            ModuleKey.Pos, ModuleKey.Accounting, ModuleKey.Marketplace, ModuleKey.Orders, ModuleKey.Map,
            ModuleKey.Rescue, ModuleKey.Insights, ModuleKey.Delivery
        },
        Array.Empty<string>());

    public static BusinessTemplate For(string? typeCode) => typeCode?.Trim().ToUpperInvariant() switch
    {
        "GROCERY" => Grocery,
        "CLOTHING" => Clothing,
        "ELECTRONICS" => Electronics,
        _ => Other
    };
}
