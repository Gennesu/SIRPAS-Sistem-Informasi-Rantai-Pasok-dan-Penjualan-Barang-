using System.Security.Claims;

namespace Platform.Web.SharedKernel;

/// <summary>Implementasi IBusinessContext yang membaca claim dari HttpContext.</summary>
public sealed class HttpBusinessContext : IBusinessContext
{
    public const string BusinessIdClaim = "business_id";

    private readonly IHttpContextAccessor _accessor;

    public HttpBusinessContext(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public int? UserId => ParseInt(Principal?.FindFirstValue(ClaimTypes.NameIdentifier));
    public int? BusinessId => ParseInt(Principal?.FindFirstValue(BusinessIdClaim));
    public string? Role => Principal?.FindFirstValue(ClaimTypes.Role);
    public bool IsAdmin => Role == RoleNames.Admin;

    private static int? ParseInt(string? value) => int.TryParse(value, out var parsed) ? parsed : (int?)null;
}
