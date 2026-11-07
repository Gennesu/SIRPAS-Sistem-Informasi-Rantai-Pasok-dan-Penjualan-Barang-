using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.BusinessProfile.Contracts;

public sealed record BusinessTypeDto(int Id, string Code, string Name);

public sealed record VerifiedAgentDto(int Id, string Name, string Address, string Phone);

public sealed record BusinessProfileDto(
    int Id,
    string Name,
    int BusinessTypeId,
    string BusinessTypeName,
    string BusinessTypeCode,
    string MarketRole,
    string Address,
    double? Latitude,
    double? Longitude,
    string Phone,
    string VerificationStatus,
    bool IsActive,
    DateTime CreatedAt,
    bool ShareSalesData = true);

public sealed record RegisterBusinessRequest(
    string OwnerName,
    string Email,
    string Password,
    string BusinessName,
    int BusinessTypeId,
    MarketRole MarketRole,
    string Address,
    double? Latitude,
    double? Longitude,
    string Phone);

public sealed record UpdateBusinessRequest(
    string Name,
    string Address,
    double? Latitude,
    double? Longitude,
    string Phone,
    bool? ShareSalesData = null);

public interface IBusinessService
{
    Task<IReadOnlyList<BusinessTypeDto>> GetBusinessTypesAsync(CancellationToken ct = default);

    /// <summary>Direktori agen grosir yang aktif dan sudah terverifikasi untuk transaksi peternakan.</summary>
    Task<IReadOnlyList<VerifiedAgentDto>> ListVerifiedAgentsAsync(CancellationToken ct = default);

    /// <summary>
    /// Membuat Business + akun pemilik dalam SATU transaksi (semua berhasil atau semua dibatalkan).
    /// Role akun ditentukan dari MarketRole: Retail -> RETAIL, Wholesale -> GROSSIER. Tidak pernah ADMIN.
    /// </summary>
    Task<Result<AuthenticatedUser>> RegisterAsync(RegisterBusinessRequest request, CancellationToken ct = default);

    /// <summary>
    /// Pemilik usaha dan admin boleh membaca. Selain itu dijawab NotFound (bukan Forbidden)
    /// agar keberadaan data tidak bocor lewat tebakan ID.
    /// </summary>
    Task<Result<BusinessProfileDto>> GetProfileAsync(int businessId, CancellationToken ct = default);

    /// <summary>Hanya pemilik usaha yang boleh mengubah profil miliknya.</summary>
    Task<Result> UpdateProfileAsync(int businessId, UpdateBusinessRequest request, CancellationToken ct = default);

    /// <summary>Hanya admin: setujui atau tolak usaha.</summary>
    Task<Result> SetVerificationAsync(int businessId, VerificationStatus status, string? note, CancellationToken ct = default);

    /// <summary>Hanya admin: aktifkan/nonaktifkan usaha (pengguna usaha nonaktif tidak bisa masuk).</summary>
    Task<Result> SetActiveAsync(int businessId, bool isActive, CancellationToken ct = default);
}
