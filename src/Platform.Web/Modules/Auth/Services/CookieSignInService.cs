using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Auth.Services;

public sealed class CookieSignInService : ISignInService
{
    private readonly IHttpContextAccessor _accessor;

    public CookieSignInService(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public async Task SignInAsync(AuthenticatedUser user, bool isPersistent = false)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, user.Role)
        };

        if (user.BusinessId is int businessId)
        {
            claims.Add(new Claim(HttpBusinessContext.BusinessIdClaim, businessId.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await GetHttpContext().SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = isPersistent });
    }

    public Task SignOutAsync() =>
        GetHttpContext().SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

    private HttpContext GetHttpContext() =>
        _accessor.HttpContext ?? throw new InvalidOperationException("HttpContext tidak tersedia.");
}
