namespace Platform.Web.Modules.Auth.Contracts;

/// <summary>Membuat/menghapus sesi login (cookie) untuk pengguna yang sudah tervalidasi.</summary>
public interface ISignInService
{
    Task SignInAsync(AuthenticatedUser user, bool isPersistent = false);
    Task SignOutAsync();
}
