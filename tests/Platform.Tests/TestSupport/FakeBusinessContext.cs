using Platform.Web.SharedKernel;

namespace Platform.Tests.TestSupport;

/// <summary>Konteks login palsu untuk test: ubah "siapa yang sedang login" lewat As...().</summary>
public sealed class FakeBusinessContext : IBusinessContext
{
    public bool IsAuthenticated { get; private set; }
    public int? UserId { get; private set; }
    public int? BusinessId { get; private set; }
    public string? Role { get; private set; }
    public bool IsAdmin => Role == RoleNames.Admin;

    public void AsAnonymous() => Set(false, null, null, null);
    public void AsRetail(int businessId, int userId = 1) => Set(true, RoleNames.Retail, businessId, userId);
    public void AsGrossier(int businessId, int userId = 1) => Set(true, RoleNames.Grossier, businessId, userId);
    public void AsAdmin(int userId = 1) => Set(true, RoleNames.Admin, null, userId);
    public void AsLivestock(int businessId, int userId = 1) => Set(true, RoleNames.Livestock, businessId, userId);
    public void AsConsumer(int userId) => Set(true, RoleNames.Consumer, null, userId);

    private void Set(bool authenticated, string? role, int? businessId, int? userId)
    {
        IsAuthenticated = authenticated;
        Role = role;
        BusinessId = businessId;
        UserId = userId;
    }
}
