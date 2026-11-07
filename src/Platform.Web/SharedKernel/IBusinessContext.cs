namespace Platform.Web.SharedKernel;

/// <summary>
/// Konteks pengguna saat ini. BusinessId selalu berasal dari identitas login (claim),
/// BUKAN dari URL, query string, atau body request.
/// </summary>
public interface IBusinessContext
{
    bool IsAuthenticated { get; }
    int? UserId { get; }
    int? BusinessId { get; }
    string? Role { get; }
    bool IsAdmin { get; }
}
