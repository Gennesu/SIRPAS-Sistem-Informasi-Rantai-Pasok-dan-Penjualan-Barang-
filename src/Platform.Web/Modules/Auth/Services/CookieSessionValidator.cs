using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Auth.Services;

/// <summary>
/// Dijalankan pada setiap request: sesi (cookie) yang sah tetapi akunnya sudah dinonaktifkan,
/// atau ditolak oleh ILoginGate (mis. usaha dinonaktifkan), langsung dikeluarkan.
/// Menutup celah "cookie tetap berlaku sampai kedaluwarsa".
/// </summary>
public static class CookieSessionValidator
{
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal is null || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            await RejectAsync(context);
            return;
        }

        var services = context.HttpContext.RequestServices;
        var ct = context.HttpContext.RequestAborted;

        if (!RoleNames.IsSupportedRole(principal.FindFirstValue(ClaimTypes.Role)))
        {
            await RejectAsync(context);
            return;
        }

        var auth = services.GetRequiredService<IAuthService>();
        if (!await auth.IsUserActiveAsync(userId, ct))
        {
            await RejectAsync(context);
            return;
        }

        int? businessId = int.TryParse(principal.FindFirstValue(HttpBusinessContext.BusinessIdClaim), out var bid) ? bid : null;
        var user = new AuthenticatedUser(
            userId,
            principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Email) ?? string.Empty,
            principal.FindFirstValue(ClaimTypes.Role) ?? string.Empty,
            businessId);

        foreach (var gate in services.GetServices<ILoginGate>())
        {
            if (await gate.CheckAsync(user, ct) is not null)
            {
                await RejectAsync(context);
                return;
            }
        }
    }

    private static async Task RejectAsync(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
