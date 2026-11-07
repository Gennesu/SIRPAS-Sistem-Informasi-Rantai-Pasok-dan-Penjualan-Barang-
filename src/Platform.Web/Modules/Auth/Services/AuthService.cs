using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Auth.Services;

public sealed class AuthService : IAuthService
{
    private const int MinPasswordLength = 8;
    private const int MaxPasswordLength = 100;
    private const string InvalidLoginMessage = "Email atau password salah.";

    private static readonly Regex EmailPattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext db,
        IBusinessContext context,
        IPasswordHasher<AppUser> hasher,
        IAuditLogService audit,
        TimeProvider time,
        ILogger<AuthService> logger)
    {
        _db = db;
        _context = context;
        _hasher = hasher;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    public async Task<Result<AuthenticatedUser>> ValidateCredentialsAsync(
        string email, string password, CancellationToken ct = default)
    {
        var normalized = NormalizeEmail(email);
        var user = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email == normalized, ct);

        // Password diperiksa lebih dulu agar pesan error tidak membocorkan apakah email terdaftar.
        var verification = user is null
            ? PasswordVerificationResult.Failed
            : _hasher.VerifyHashedPassword(user, user.PasswordHash, password ?? string.Empty);

        if (user is null || verification == PasswordVerificationResult.Failed)
        {
            _audit.Record("AUTH_LOGIN_FAILED", "AppUser", user?.Id.ToString(), $"email={normalized}", user?.Id, user?.BusinessId);
            await _db.SaveChangesAsync(ct);
            return Result<AuthenticatedUser>.Fail(ErrorKind.Validation, InvalidLoginMessage);
        }

        if (!user.IsActive)
        {
            _audit.Record("AUTH_LOGIN_BLOCKED", "AppUser", user.Id.ToString(), "akun nonaktif", user.Id, user.BusinessId);
            await _db.SaveChangesAsync(ct);
            return Result<AuthenticatedUser>.Fail(ErrorKind.Forbidden, "Akun Anda dinonaktifkan. Hubungi admin platform.");
        }

        if (!RoleNames.IsSupportedRole(user.Role.Name))
        {
            _audit.Record("AUTH_LOGIN_BLOCKED", "AppUser", user.Id.ToString(), "role tidak didukung", user.Id, user.BusinessId);
            await _db.SaveChangesAsync(ct);
            return Result<AuthenticatedUser>.Fail(ErrorKind.Forbidden, "Peran akun ini tidak lagi tersedia.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password!);
        }

        user.LastLoginAt = _time.GetUtcNow().UtcDateTime;
        _audit.Record("AUTH_LOGIN", "AppUser", user.Id.ToString(), null, user.Id, user.BusinessId);
        await _db.SaveChangesAsync(ct);

        return Result<AuthenticatedUser>.Ok(
            new AuthenticatedUser(user.Id, user.FullName, user.Email, user.Role.Name, user.BusinessId));
    }

    public async Task<Result<AuthenticatedUser>> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var email = NormalizeEmail(request.Email);
        var fullName = (request.FullName ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (fullName.Length is < 2 or > 120)
        {
            return Invalid("Nama lengkap harus 2–120 karakter.");
        }

        if (email.Length > 256 || !EmailPattern.IsMatch(email))
        {
            return Invalid("Format email tidak valid.");
        }

        if (password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            return Invalid($"Password harus {MinPasswordLength}–{MaxPasswordLength} karakter.");
        }

        if (!RoleNames.IsSupportedRole(request.RoleName))
        {
            return Invalid("Role tidak dikenal.");
        }

        // Admin dan pembeli akhir adalah pengguna tanpa usaha; akun usaha wajib terikat pada satu usaha.
        var isUnboundRole = request.RoleName is RoleNames.Admin or RoleNames.Consumer;
        if (isUnboundRole && request.BusinessId is not null)
        {
            return Invalid(request.RoleName == RoleNames.Admin
                ? "Akun admin tidak boleh terikat pada usaha."
                : "Akun pembeli tidak boleh terikat pada usaha.");
        }

        if (!isUnboundRole && request.BusinessId is null)
        {
            return Invalid("Akun usaha harus terikat pada usaha.");
        }

        var role = await _db.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Name == request.RoleName, ct);
        if (role is null)
        {
            return Invalid("Role tidak dikenal.");
        }

        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
        {
            return Result<AuthenticatedUser>.Fail(ErrorKind.Conflict, "Email sudah terdaftar.");
        }

        var user = new AppUser
        {
            BusinessId = request.BusinessId,
            RoleId = role.Id,
            Email = email,
            FullName = fullName,
            IsActive = true,
            CreatedAt = _time.GetUtcNow().UtcDateTime
        };
        user.PasswordHash = _hasher.HashPassword(user, password);
        _db.Users.Add(user);

        try
        {
            await _db.SaveChangesAsync(ct);

            // Id baru tersedia setelah insert; catat audit lalu simpan.
            _audit.Record("AUTH_USER_CREATED", "AppUser", user.Id.ToString(), $"role={role.Name}", user.Id, user.BusinessId);
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Gagal menyimpan akun baru.");
            return Result<AuthenticatedUser>.Fail(ErrorKind.Unexpected, "Akun tidak dapat disimpan saat ini. Silakan coba lagi.");
        }

        return Result<AuthenticatedUser>.Ok(
            new AuthenticatedUser(user.Id, user.FullName, user.Email, role.Name, user.BusinessId));
    }

    public async Task<bool> IsUserActiveAsync(int userId, CancellationToken ct = default) =>
        await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, ct);

    public async Task<Result> SetUserActiveAsync(int userId, bool isActive, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || !_context.IsAdmin)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya admin yang dapat mengubah status akun.");
        }

        if (userId == _context.UserId)
        {
            return Result.Fail(ErrorKind.Validation, "Admin tidak dapat menonaktifkan akunnya sendiri.");
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return Result.Fail(ErrorKind.NotFound, "Akun tidak ditemukan.");
        }

        user.IsActive = isActive;
        _audit.Record(isActive ? "USER_ACTIVATED" : "USER_DEACTIVATED", "AppUser", user.Id.ToString(), user.Email, null, user.BusinessId);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    private static Result<AuthenticatedUser> Invalid(string message) =>
        Result<AuthenticatedUser>.Fail(ErrorKind.Validation, message);
}
