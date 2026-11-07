using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Razor;

namespace Platform.Web.SharedKernel;

/// <summary>
/// Mencari view di Modules/{Modul}/Views/{Controller}/{Action}.cshtml.
/// Nama modul diambil dari namespace controller: Platform.Web.Modules.{Modul}.Controllers.
/// Controller di luar Modules (mis. HomeController) tetap memakai /Views/ biasa.
/// </summary>
public sealed class ModuleViewLocationExpander : IViewLocationExpander
{
    private const string ModuleKey = "module";

    public void PopulateValues(ViewLocationExpanderContext context)
    {
        if (context.ActionContext.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            return;
        }

        var parts = (descriptor.ControllerTypeInfo.Namespace ?? string.Empty).Split('.');
        var index = Array.IndexOf(parts, "Modules");
        if (index >= 0 && index + 1 < parts.Length)
        {
            context.Values[ModuleKey] = parts[index + 1];
        }
    }

    public IEnumerable<string> ExpandViewLocations(ViewLocationExpanderContext context, IEnumerable<string> viewLocations)
    {
        if (!context.Values.TryGetValue(ModuleKey, out var module) || string.IsNullOrEmpty(module))
        {
            return viewLocations;
        }

        var moduleLocations = new[]
        {
            $"/Modules/{module}/Views/{{1}}/{{0}}.cshtml",
            $"/Modules/{module}/Views/Shared/{{0}}.cshtml"
        };

        return moduleLocations.Concat(viewLocations);
    }
}
