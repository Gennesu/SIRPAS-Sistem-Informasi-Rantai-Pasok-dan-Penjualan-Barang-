using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Auth.Contracts;

/// <summary>Identitas pengguna terautentikasi; aman dibagikan ke modul lain.</summary>
public sealed record AuthenticatedUser(int UserId, string FullName, string Email, string Role, int? BusinessId);

/// <summary>
/// RoleName harus salah satu dari RoleNames. ADMIN wajib BusinessId = null;
/// GROSSIER/RETAIL wajib memiliki BusinessId.
/// </summary>
public sealed record CreateUserRequest(string FullName, string Email, string Password, string RoleName, int? BusinessId);

public interface IAuthService
{
    /// <summary>Memeriksa email + password. Pesan gagal login sengaja tidak membedakan email/password.</summary>
    Task<Result<AuthenticatedUser>> ValidateCredentialsAsync(string email, string password, CancellationToken ct = default);

    /// <summary>Membuat akun (hash password, validasi, audit). Dipakai registrasi usaha dan seeder admin.</summary>
    Task<Result<AuthenticatedUser>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);

    /// <summary>Dipakai setiap request untuk memastikan akun yang sudah login belum dinonaktifkan admin.</summary>
    Task<bool> IsUserActiveAsync(int userId, CancellationToken ct = default);

    /// <summary>Hanya admin; admin tidak dapat menonaktifkan dirinya sendiri.</summary>
    Task<Result> SetUserActiveAsync(int userId, bool isActive, CancellationToken ct = default);
}

/// <summary>
/// Titik perluasan: modul lain dapat menolak login/sesi tanpa Auth mengenal modul tersebut
/// (mis. BusinessProfile menolak pengguna dari usaha yang dinonaktifkan admin).
/// </summary>
public interface ILoginGate
{
    /// <summary>Mengembalikan pesan penolakan, atau null bila diizinkan.</summary>
    Task<string?> CheckAsync(AuthenticatedUser user, CancellationToken ct = default);
}
