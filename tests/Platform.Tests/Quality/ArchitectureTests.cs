using System.Reflection;

namespace Platform.Tests.Quality;

/// <summary>
/// Maintainability (ISO 25010): batas antarmodul modular monolith diperiksa otomatis lewat refleksi atas seluruh tipe aplikasi
/// (konstruktor, field, properti, parameter, dan tipe kembalian). Modul POS tidak boleh bergantung pada fitur konsumen.
/// </summary>
public sealed class ArchitectureTests
{
    private const string Root = "Platform.Web.Modules.";
    private static readonly Assembly App = typeof(Platform.Web.Data.AppDbContext).Assembly;

    private static IEnumerable<Type> TypesOf(string module) =>
        App.GetTypes().Where(t => t.Namespace is not null && t.Namespace.StartsWith(Root + module + ".", StringComparison.Ordinal) && !t.IsNested);

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var f in type.GetFields(all)) yield return f.FieldType;
        foreach (var p in type.GetProperties(all)) yield return p.PropertyType;
        foreach (var c in type.GetConstructors(all)) foreach (var p in c.GetParameters()) yield return p.ParameterType;
        foreach (var m in type.GetMethods(all))
        {
            yield return m.ReturnType;
            foreach (var p in m.GetParameters()) yield return p.ParameterType;
        }

        foreach (var i in type.GetInterfaces()) yield return i;
        if (type.BaseType is not null) yield return type.BaseType;
    }

    private static IEnumerable<Type> Flatten(Type t)
    {
        yield return t;
        if (t.IsGenericType) foreach (var a in t.GetGenericArguments()) foreach (var x in Flatten(a)) yield return x;
        if (t.HasElementType && t.GetElementType() is { } e) foreach (var x in Flatten(e)) yield return x;
    }

    private static HashSet<string> DependenciesOf(string module, params string[] layers)
    {
        var result = new HashSet<string>();
        foreach (var type in TypesOf(module).Where(t => layers.Length == 0 || layers.Any(l => t.Namespace!.EndsWith("." + l, StringComparison.Ordinal))))
        {
            foreach (var referenced in ReferencedTypes(type).SelectMany(Flatten))
            {
                var ns = referenced.Namespace;
                if (ns is null || !ns.StartsWith(Root, StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = ns[Root.Length..].Split('.');
                if (parts.Length >= 1 && parts[0] != module)
                {
                    result.Add(parts[0] + (parts.Length > 1 ? "." + parts[1] : string.Empty));
                }
            }
        }

        return result;
    }

    [Fact]
    public void Modul_POS_Sales_tidak_bergantung_pada_modul_pembeli()
    {
        var deps = DependenciesOf("Sales");
        Assert.DoesNotContain(deps, d => d.StartsWith("Consumers", StringComparison.Ordinal));
    }

    [Fact]
    public void Modul_Consumers_hanya_memakai_kontrak_modul_lain_bukan_implementasinya()
    {
        var deps = DependenciesOf("Consumers", "Services", "Controllers");
        Assert.DoesNotContain(deps, d => d.EndsWith(".Services", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_entity_tidak_bergantung_pada_layanan_modul_mana_pun()
    {
        foreach (var module in new[] { "Stock", "Orders", "Sales", "Purchasing", "Consumers", "Catalog" })
        {
            var deps = DependenciesOf(module, "Domain");
            Assert.DoesNotContain(deps, d => d.EndsWith(".Services", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Setiap_layanan_bisnis_yang_mengubah_data_mengimplementasikan_kontrak_publik()
    {
        foreach (var module in new[] { "Consumers", "Stock", "Orders", "Sales", "Purchasing" })
        {
            var services = TypesOf(module).Where(t => t.IsClass && !t.IsAbstract && t.Namespace!.EndsWith(".Services", StringComparison.Ordinal) && t.Name.EndsWith("Service", StringComparison.Ordinal));
            foreach (var service in services)
            {
                Assert.True(service.GetInterfaces().Any(i => i.Namespace is not null && i.Namespace.EndsWith(".Contracts", StringComparison.Ordinal)),
                    $"{service.Name} tidak mengimplementasikan kontrak di namespace .Contracts");
            }
        }
    }
}
