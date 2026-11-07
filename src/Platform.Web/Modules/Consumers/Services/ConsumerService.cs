using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Consumers.Contracts;
using Platform.Web.Modules.Consumers.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Consumers.Services;

/// <summary>Akun pembeli akhir: pengguna individu (role CONSUMER) tanpa BusinessId, dengan profil sendiri.</summary>
public sealed class ConsumerService : IConsumerService
{
    private static readonly Regex PhonePattern = new(@"^\+?[0-9]{8,15}$", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IAuthService _auth;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly IUnitOfWork _uow;

    public ConsumerService(
        AppDbContext db, IBusinessContext context, IAuthService auth, IAuditLogService audit, TimeProvider time, IUnitOfWork uow)
    {
        _db = db;
        _context = context;
        _auth = auth;
        _audit = audit;
        _time = time;
        _uow = uow;
    }

    public async Task<Result<AuthenticatedUser>> RegisterAsync(RegisterConsumerRequest request, CancellationToken ct = default)
    {
        var error = Validate(request.FullName, request.Phone, request.Address, request.Latitude, request.Longitude, out var phone);
        if (error is not null)
        {
            return Result<AuthenticatedUser>.Fail(ErrorKind.Validation, error);
        }

        // Akun + profil dibuat dalam satu unit kerja: gagal di salah satunya = tidak ada akun setengah jadi.
        return await _uow.ExecuteAsync<AuthenticatedUser>(async token =>
        {
            // Role SELALU CONSUMER: pendaftaran publik tidak pernah dapat menghasilkan ADMIN atau akun usaha.
            var user = await _auth.CreateUserAsync(
                new CreateUserRequest(request.FullName, request.Email, request.Password, RoleNames.Consumer, null), token);
            if (!user.Succeeded)
            {
                return user;
            }

            _db.ConsumerProfiles.Add(new ConsumerProfile
            {
                UserId = user.Value!.UserId,
                Phone = phone,
                Address = request.Address.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                CreatedAt = _time.GetUtcNow().UtcDateTime
            });
            _audit.Record("CONSUMER_REGISTERED", "AppUser", user.Value.UserId.ToString(), null, user.Value.UserId, null);
            await _db.SaveChangesAsync(token);
            return user;
        }, ct);
    }

    public async Task<Result<ConsumerProfileDto>> GetProfileAsync(CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Consumer || _context.UserId is not int userId)
        {
            return Result<ConsumerProfileDto>.Fail(ErrorKind.Forbidden, "Hanya akun pembeli yang memiliki profil pembeli.");
        }

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        var profile = await _db.ConsumerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (user is null || profile is null)
        {
            return Result<ConsumerProfileDto>.Fail(ErrorKind.NotFound, "Profil pembeli tidak ditemukan.");
        }

        return Result<ConsumerProfileDto>.Ok(new ConsumerProfileDto(
            userId, user.FullName, user.Email, profile.Phone, profile.Address, profile.Latitude, profile.Longitude));
    }

    public async Task<Result> UpdateProfileAsync(UpdateConsumerProfileRequest request, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Consumer || _context.UserId is not int userId)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya akun pembeli yang dapat mengubah profil pembeli.");
        }

        var error = Validate(request.FullName, request.Phone, request.Address, request.Latitude, request.Longitude, out var phone);
        if (error is not null)
        {
            return Result.Fail(ErrorKind.Validation, error);
        }

        return await _uow.ExecuteAsync(async token =>
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, token);
            var profile = await _db.ConsumerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, token);
            if (user is null || profile is null)
            {
                return Result.Fail(ErrorKind.NotFound, "Profil pembeli tidak ditemukan.");
            }

            user.FullName = request.FullName.Trim();
            profile.Phone = phone;
            profile.Address = request.Address.Trim();
            profile.Latitude = request.Latitude;
            profile.Longitude = request.Longitude;
            _audit.Record("CONSUMER_PROFILE_UPDATED", "AppUser", userId.ToString());
            await _db.SaveChangesAsync(token);
            return Result.Ok();
        }, ct);
    }

    private static string? Validate(string fullName, string phone, string address, double? lat, double? lon, out string normalizedPhone)
    {
        normalizedPhone = string.Empty;
        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length is < 2 or > 120)
        {
            return "Nama lengkap harus 2–120 karakter.";
        }

        var digits = new string((phone ?? string.Empty).Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (!PhonePattern.IsMatch(digits))
        {
            return "Nomor telepon harus 8–15 digit.";
        }

        normalizedPhone = digits;
        if (string.IsNullOrWhiteSpace(address) || address.Trim().Length is < 5 or > 300)
        {
            return "Alamat harus 5–300 karakter.";
        }

        if ((lat is null) != (lon is null))
        {
            return "Isi garis lintang dan bujur sekaligus, atau kosongkan keduanya.";
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return "Koordinat tidak valid.";
        }

        return null;
    }
}
