using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Contracts;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.BusinessProfile.Services;

public sealed class BusinessService : IBusinessService
{
    private const string NotFoundMessage = "Usaha tidak ditemukan.";

    private static readonly Regex PhonePattern = new(@"^\+?[0-9]{8,15}$", RegexOptions.Compiled);
    private static readonly Regex PhoneSeparators = new(@"[\s\-()]", RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;
    private readonly IAuthService _auth;
    private readonly IAuditLogService _audit;
    private readonly TimeProvider _time;
    private readonly ILogger<BusinessService> _logger;

    public BusinessService(
        AppDbContext db,
        IBusinessContext context,
        IAuthService auth,
        IAuditLogService audit,
        TimeProvider time,
        ILogger<BusinessService> logger)
    {
        _db = db;
        _context = context;
        _auth = auth;
        _audit = audit;
        _time = time;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BusinessTypeDto>> GetBusinessTypesAsync(CancellationToken ct = default)
    {
        return await _db.BusinessTypes
            .AsNoTracking()
            .OrderBy(t => t.Id)
            .Select(t => new BusinessTypeDto(t.Id, t.Code, t.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<VerifiedAgentDto>> ListVerifiedAgentsAsync(CancellationToken ct = default)
    {
        return await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole == MarketRole.Wholesale
                && b.VerificationStatus == VerificationStatus.Verified
                && b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new VerifiedAgentDto(b.Id, b.Name, b.Address, b.Phone))
            .ToListAsync(ct);
    }

    public async Task<Result<AuthenticatedUser>> RegisterAsync(RegisterBusinessRequest request, CancellationToken ct = default)
    {
        var fieldError = ValidateBusinessFields(
            request.BusinessName, request.Address, request.Phone, request.Latitude, request.Longitude);
        if (fieldError is not null)
        {
            return Result<AuthenticatedUser>.Fail(ErrorKind.Validation, fieldError);
        }

        if (request.MarketRole is not (MarketRole.Retail or MarketRole.Wholesale or MarketRole.Livestock))
        {
            return Result<AuthenticatedUser>.Fail(ErrorKind.Validation, "Jenis peran usaha tidak valid.");
        }

        var type = await _db.BusinessTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == request.BusinessTypeId, ct);
        if (type is null)
        {
            return Result<AuthenticatedUser>.Fail(ErrorKind.Validation, "Jenis bisnis tidak valid.");
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var business = new Business
            {
                Name = request.BusinessName.Trim(),
                BusinessTypeId = type.Id,
                MarketRole = request.MarketRole,
                Address = request.Address.Trim(),
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                Phone = NormalizePhone(request.Phone),
                VerificationStatus = VerificationStatus.Pending,
                IsActive = true,
                CreatedAt = _time.GetUtcNow().UtcDateTime
            };
            _db.Businesses.Add(business);
            await _db.SaveChangesAsync(ct);

            var roleName = request.MarketRole switch
            {
                MarketRole.Wholesale => RoleNames.Grossier,
                MarketRole.Livestock => RoleNames.Livestock,
                _ => RoleNames.Retail
            };
            var userResult = await _auth.CreateUserAsync(
                new CreateUserRequest(request.OwnerName, request.Email, request.Password, roleName, business.Id), ct);

            if (!userResult.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return userResult;
            }

            _audit.Record(
                "BUSINESS_REGISTERED",
                "Business",
                business.Id.ToString(),
                $"type={type.Code}; role={request.MarketRole}",
                userResult.Value!.UserId,
                business.Id);
            await _db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);
            return userResult;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Registrasi usaha gagal disimpan.");
            return Result<AuthenticatedUser>.Fail(
                ErrorKind.Unexpected, "Pendaftaran tidak dapat diproses saat ini. Silakan coba lagi.");
        }
    }

    public async Task<Result<BusinessProfileDto>> GetProfileAsync(int businessId, CancellationToken ct = default)
    {
        if (!CanRead(businessId))
        {
            return ProfileNotFound();
        }

        var business = await _db.Businesses
            .AsNoTracking()
            .Include(b => b.BusinessType)
            .FirstOrDefaultAsync(b => b.Id == businessId, ct);

        return business is null
            ? ProfileNotFound()
            : Result<BusinessProfileDto>.Ok(ToDto(business));
    }

    public async Task<Result> UpdateProfileAsync(int businessId, UpdateBusinessRequest request, CancellationToken ct = default)
    {
        if (!CanWrite(businessId))
        {
            return Result.Fail(ErrorKind.NotFound, NotFoundMessage);
        }

        var error = ValidateBusinessFields(
            request.Name, request.Address, request.Phone, request.Latitude, request.Longitude);
        if (error is not null)
        {
            return Result.Fail(ErrorKind.Validation, error);
        }

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct);
        if (business is null)
        {
            return Result.Fail(ErrorKind.NotFound, NotFoundMessage);
        }

        business.Name = request.Name.Trim();
        business.Address = request.Address.Trim();
        business.Phone = NormalizePhone(request.Phone);
        business.Latitude = request.Latitude;
        business.Longitude = request.Longitude;
        if (request.ShareSalesData is bool share)
        {
            business.ShareSalesData = share;
        }

        _audit.Record("BUSINESS_UPDATED", "Business", business.Id.ToString(), null, null, business.Id);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Gagal memperbarui profil usaha {BusinessId}.", businessId);
            return Result.Fail(ErrorKind.Unexpected, "Profil tidak dapat disimpan saat ini. Silakan coba lagi.");
        }

        return Result.Ok();
    }

    public async Task<Result> SetVerificationAsync(int businessId, VerificationStatus status, string? note, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || !_context.IsAdmin)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya admin yang dapat memverifikasi usaha.");
        }

        if (!Enum.IsDefined(status))
        {
            return Result.Fail(ErrorKind.Validation, "Status verifikasi tidak valid.");
        }

        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (cleanNote is { Length: > 200 })
        {
            return Result.Fail(ErrorKind.Validation, "Catatan maksimal 200 karakter.");
        }

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct);
        if (business is null)
        {
            return Result.Fail(ErrorKind.NotFound, NotFoundMessage);
        }

        business.VerificationStatus = status;
        var action = status switch
        {
            VerificationStatus.Verified => "BUSINESS_VERIFIED",
            VerificationStatus.Rejected => "BUSINESS_REJECTED",
            _ => "BUSINESS_UNVERIFIED"
        };
        _audit.Record(action, "Business", business.Id.ToString(), cleanNote ?? business.Name, null, business.Id);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetActiveAsync(int businessId, bool isActive, CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || !_context.IsAdmin)
        {
            return Result.Fail(ErrorKind.Forbidden, "Hanya admin yang dapat mengubah status usaha.");
        }

        var business = await _db.Businesses.FirstOrDefaultAsync(b => b.Id == businessId, ct);
        if (business is null)
        {
            return Result.Fail(ErrorKind.NotFound, NotFoundMessage);
        }

        business.IsActive = isActive;
        _audit.Record(isActive ? "BUSINESS_ACTIVATED" : "BUSINESS_DEACTIVATED", "Business", business.Id.ToString(), business.Name, null, business.Id);
        await _db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // Pemilik usaha atau admin.
    private bool CanRead(int businessId) =>
        _context.IsAuthenticated && (_context.IsAdmin || _context.BusinessId == businessId);

    // Hanya pemilik usaha (admin mengelola lewat modul Admin).
    private bool CanWrite(int businessId) =>
        _context.IsAuthenticated && !_context.IsAdmin && _context.BusinessId == businessId;

    private static Result<BusinessProfileDto> ProfileNotFound() =>
        Result<BusinessProfileDto>.Fail(ErrorKind.NotFound, NotFoundMessage);

    private static BusinessProfileDto ToDto(Business b) => new(
        b.Id,
        b.Name,
        b.BusinessTypeId,
        b.BusinessType.Name,
        b.BusinessType.Code,
        b.MarketRole.ToString(),
        b.Address,
        b.Latitude,
        b.Longitude,
        b.Phone,
        b.VerificationStatus.ToString(),
        b.IsActive,
        b.CreatedAt,
        b.ShareSalesData);

    private static string NormalizePhone(string? phone) =>
        PhoneSeparators.Replace(phone ?? string.Empty, string.Empty);

    private static string? ValidateBusinessFields(
        string? name, string? address, string? phone, double? latitude, double? longitude)
    {
        if ((name?.Trim().Length ?? 0) is < 3 or > 120)
        {
            return "Nama usaha harus 3–120 karakter.";
        }

        if ((address?.Trim().Length ?? 0) is < 5 or > 300)
        {
            return "Alamat harus 5–300 karakter.";
        }

        if (!PhonePattern.IsMatch(NormalizePhone(phone)))
        {
            return "Nomor kontak tidak valid (8–15 digit, boleh diawali +).";
        }

        if (latitude.HasValue != longitude.HasValue)
        {
            return "Latitude dan longitude harus diisi bersamaan atau dikosongkan.";
        }

        if (latitude is { } lat && !(lat >= -90 && lat <= 90))
        {
            return "Latitude harus di antara -90 dan 90.";
        }

        if (longitude is { } lon && !(lon >= -180 && lon <= 180))
        {
            return "Longitude harus di antara -180 dan 180.";
        }

        return null;
    }
}
