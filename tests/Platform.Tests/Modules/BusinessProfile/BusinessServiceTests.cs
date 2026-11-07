using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Tests.TestSupport;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Services;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.Modules.Auth.Services;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.BusinessProfile.Services;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Modules.BusinessProfile;

public sealed class BusinessServiceTests : IDisposable
{
    private readonly TestDb _testDb = new();
    private readonly AppDbContext _db;
    private readonly BusinessService _business;

    public BusinessServiceTests()
    {
        _db = _testDb.CreateDb();
        var audit = new AuditLogService(_db, _testDb.Context, TimeProvider.System);
        var auth = new AuthService(
            _db, _testDb.Context, new PasswordHasher<AppUser>(), audit, TimeProvider.System, NullLogger<AuthService>.Instance);
        _business = new BusinessService(
            _db, _testDb.Context, auth, audit, TimeProvider.System, NullLogger<BusinessService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _testDb.Dispose();
    }

    private static RegisterBusinessRequest Request(
        string email = "budi@warung.test",
        MarketRole role = MarketRole.Retail,
        int businessTypeId = 1,
        string phone = "0812-3456-7890") =>
        new("Budi Santoso", email, "Rahasia123", "Warung Maju", businessTypeId, role,
            "Jl. Mawar No. 1, Bandung", -6.9175, 107.6191, phone);

    private async Task<AuthenticatedUser> RegisterOkAsync(string email)
    {
        var result = await _business.RegisterAsync(Request(email));
        Assert.True(result.Succeeded, result.Error);
        return result.Value!;
    }

    // ---------------------------------------------------------------- Registrasi

    [Fact]
    public async Task Register_warung_membuat_usaha_dan_akun_berperan_RETAIL()
    {
        var result = await _business.RegisterAsync(Request());

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(RoleNames.Retail, result.Value!.Role);

        var business = await _db.Businesses.SingleAsync();
        Assert.Equal(VerificationStatus.Pending, business.VerificationStatus);
        Assert.True(business.IsActive);
        Assert.Equal("081234567890", business.Phone);
        Assert.Equal(business.Id, result.Value.BusinessId);
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == "BUSINESS_REGISTERED" && a.BusinessId == business.Id));
    }

    [Fact]
    public async Task Register_grosir_membuat_akun_berperan_GROSSIER()
    {
        var result = await _business.RegisterAsync(Request(role: MarketRole.Wholesale));

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(RoleNames.Grossier, result.Value!.Role);
        Assert.Equal(MarketRole.Wholesale, (await _db.Businesses.SingleAsync()).MarketRole);
    }

    [Fact]
    public async Task Register_email_duplikat_membatalkan_pembuatan_usaha()
    {
        var first = await _business.RegisterAsync(Request());
        Assert.True(first.Succeeded, first.Error);

        var second = await _business.RegisterAsync(Request());

        Assert.False(second.Succeeded);
        Assert.Equal(ErrorKind.Conflict, second.Kind);

        using var fresh = _testDb.CreateDb();
        Assert.Equal(1, await fresh.Businesses.CountAsync());
        Assert.Equal(1, await fresh.Users.CountAsync());
    }

    [Fact]
    public async Task Register_jenis_bisnis_tidak_valid_ditolak()
    {
        var result = await _business.RegisterAsync(Request(businessTypeId: 999));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
        Assert.Equal(0, await _db.Businesses.CountAsync());
    }

    [Fact]
    public async Task Register_nomor_kontak_tidak_valid_ditolak()
    {
        var result = await _business.RegisterAsync(Request(phone: "abc"));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
        Assert.Equal(0, await _db.Businesses.CountAsync());
    }

    [Fact]
    public async Task Register_peran_tidak_dikenal_ditolak()
    {
        var result = await _business.RegisterAsync(Request(role: (MarketRole)99));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
        Assert.Equal(0, await _db.Users.CountAsync());
    }

    // ---------------------------------------------------------------- Profil & isolasi tenant

    [Fact]
    public async Task GetProfile_milik_sendiri_berhasil()
    {
        var owner = await RegisterOkAsync("a@warung.test");
        var businessId = owner.BusinessId!.Value;
        _testDb.Context.AsRetail(businessId, owner.UserId);

        var profile = await _business.GetProfileAsync(businessId);

        Assert.True(profile.Succeeded, profile.Error);
        Assert.Equal("Warung Maju", profile.Value!.Name);
        Assert.Equal("Toko Kelontong / Sembako", profile.Value.BusinessTypeName);
    }

    [Fact]
    public async Task GetProfile_usaha_lain_dijawab_NotFound()
    {
        var a = await RegisterOkAsync("a@warung.test");
        var b = await RegisterOkAsync("b@warung.test");
        _testDb.Context.AsRetail(a.BusinessId!.Value, a.UserId);

        var result = await _business.GetProfileAsync(b.BusinessId!.Value);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.NotFound, result.Kind);
    }

    [Fact]
    public async Task GetProfile_admin_boleh_membaca_usaha_mana_pun()
    {
        var owner = await RegisterOkAsync("a@warung.test");
        _testDb.Context.AsAdmin();

        var result = await _business.GetProfileAsync(owner.BusinessId!.Value);

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public async Task UpdateProfile_usaha_lain_ditolak_dan_data_tidak_berubah()
    {
        var a = await RegisterOkAsync("a@warung.test");
        var b = await RegisterOkAsync("b@warung.test");
        var targetId = b.BusinessId!.Value;
        _testDb.Context.AsRetail(a.BusinessId!.Value, a.UserId);

        var result = await _business.UpdateProfileAsync(
            targetId, new UpdateBusinessRequest("Dibajak", "Alamat baru 123", null, null, "081200000000"));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.NotFound, result.Kind);

        using var fresh = _testDb.CreateDb();
        Assert.Equal("Warung Maju", (await fresh.Businesses.SingleAsync(x => x.Id == targetId)).Name);
    }

    [Fact]
    public async Task UpdateProfile_valid_menyimpan_perubahan_dan_mencatat_audit()
    {
        var owner = await RegisterOkAsync("a@warung.test");
        var businessId = owner.BusinessId!.Value;
        _testDb.Context.AsRetail(businessId, owner.UserId);

        var result = await _business.UpdateProfileAsync(
            businessId,
            new UpdateBusinessRequest("Warung Maju Jaya", "Jl. Melati No. 9, Bandung", null, null, "0813 9999 8888"));

        Assert.True(result.Succeeded, result.Error);

        using var fresh = _testDb.CreateDb();
        var saved = await fresh.Businesses.SingleAsync(x => x.Id == businessId);
        Assert.Equal("Warung Maju Jaya", saved.Name);
        Assert.Equal("081399998888", saved.Phone);
        Assert.Null(saved.Latitude);
        Assert.True(await fresh.AuditLogs.AnyAsync(a => a.Action == "BUSINESS_UPDATED" && a.BusinessId == businessId));
    }
}
