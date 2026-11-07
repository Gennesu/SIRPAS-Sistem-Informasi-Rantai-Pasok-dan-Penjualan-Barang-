using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Platform.Tests.TestSupport;
using Platform.Web.Data;
using Platform.Web.Modules.Audit.Services;
using Platform.Web.Modules.Auth.Contracts;
using Platform.Web.Modules.Auth.Domain;
using Platform.Web.Modules.Auth.Services;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.SharedKernel;

namespace Platform.Tests.Modules.Auth;

public sealed class AuthServiceTests : IDisposable
{
    private const string GoodPassword = "Rahasia123";

    private readonly TestDb _testDb = new();
    private readonly AppDbContext _db;
    private readonly PasswordHasher<AppUser> _hasher = new();
    private readonly AuthService _auth;

    public AuthServiceTests()
    {
        _db = _testDb.CreateDb();
        var audit = new AuditLogService(_db, _testDb.Context, TimeProvider.System);
        _auth = new AuthService(_db, _testDb.Context, _hasher, audit, TimeProvider.System, NullLogger<AuthService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _testDb.Dispose();
    }

    // Users.BusinessId punya foreign key, jadi usaha harus ada lebih dulu.
    private async Task<int> AddBusinessAsync()
    {
        var business = new Business
        {
            Name = "Warung Maju",
            BusinessTypeId = 1,
            MarketRole = MarketRole.Retail,
            Address = "Jl. Mawar No. 1",
            Phone = "081234567890",
            CreatedAt = DateTime.UtcNow
        };
        _db.Businesses.Add(business);
        await _db.SaveChangesAsync();
        return business.Id;
    }

    private async Task<AuthenticatedUser> CreateBudiAsync()
    {
        var businessId = await AddBusinessAsync();
        var result = await _auth.CreateUserAsync(
            new CreateUserRequest("Budi Santoso", "budi@warung.test", GoodPassword, RoleNames.Retail, businessId));
        Assert.True(result.Succeeded, result.Error);
        return result.Value!;
    }

    [Fact]
    public async Task CreateUser_menyimpan_hash_bukan_password_asli()
    {
        var businessId = await AddBusinessAsync();

        var result = await _auth.CreateUserAsync(
            new CreateUserRequest("Budi Santoso", "Budi@Warung.Test", GoodPassword, RoleNames.Retail, businessId));

        Assert.True(result.Succeeded, result.Error);
        var user = await _db.Users.SingleAsync();
        Assert.Equal("budi@warung.test", user.Email);
        Assert.NotEqual(GoodPassword, user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            _hasher.VerifyHashedPassword(user, user.PasswordHash, GoodPassword));
    }

    [Fact]
    public async Task CreateUser_email_duplikat_ditolak_walau_beda_huruf_besar()
    {
        var businessId = await AddBusinessAsync();
        await _auth.CreateUserAsync(
            new CreateUserRequest("Budi", "budi@warung.test", GoodPassword, RoleNames.Retail, businessId));

        var second = await _auth.CreateUserAsync(
            new CreateUserRequest("Budi Dua", "BUDI@warung.test", GoodPassword, RoleNames.Retail, businessId));

        Assert.False(second.Succeeded);
        Assert.Equal(ErrorKind.Conflict, second.Kind);
        Assert.Equal(1, await _db.Users.CountAsync());
    }

    [Theory]
    [InlineData("pendek1")]
    [InlineData("")]
    public async Task CreateUser_password_tidak_memenuhi_syarat_ditolak(string password)
    {
        var businessId = await AddBusinessAsync();

        var result = await _auth.CreateUserAsync(
            new CreateUserRequest("Budi", "budi@warung.test", password, RoleNames.Retail, businessId));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
        Assert.Equal(0, await _db.Users.CountAsync());
    }

    [Theory]
    [InlineData("bukan-email")]
    [InlineData("a@b")]
    public async Task CreateUser_email_tidak_valid_ditolak(string email)
    {
        var businessId = await AddBusinessAsync();

        var result = await _auth.CreateUserAsync(
            new CreateUserRequest("Budi", email, GoodPassword, RoleNames.Retail, businessId));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
    }

    [Fact]
    public async Task CreateUser_admin_tidak_boleh_terikat_usaha()
    {
        var businessId = await AddBusinessAsync();

        var result = await _auth.CreateUserAsync(
            new CreateUserRequest("Admin", "admin@platform.test", GoodPassword, RoleNames.Admin, businessId));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
    }

    [Fact]
    public async Task CreateUser_warung_wajib_terikat_usaha()
    {
        var result = await _auth.CreateUserAsync(
            new CreateUserRequest("Budi", "budi@warung.test", GoodPassword, RoleNames.Retail, null));

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Validation, result.Kind);
    }

    [Fact]
    public async Task Login_berhasil_mengisi_LastLoginAt_dan_mencatat_audit()
    {
        var created = await CreateBudiAsync();

        var result = await _auth.ValidateCredentialsAsync("  BUDI@warung.test ", GoodPassword);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(created.UserId, result.Value!.UserId);
        Assert.Equal(RoleNames.Retail, result.Value.Role);
        Assert.Equal(created.BusinessId, result.Value.BusinessId);
        Assert.NotNull((await _db.Users.SingleAsync()).LastLoginAt);
        Assert.True(await _db.AuditLogs.AnyAsync(a => a.Action == "AUTH_LOGIN"));
    }

    [Fact]
    public async Task Login_password_salah_dan_email_tak_dikenal_memberi_pesan_sama()
    {
        await CreateBudiAsync();

        var wrongPassword = await _auth.ValidateCredentialsAsync("budi@warung.test", "salah-total");
        var unknownEmail = await _auth.ValidateCredentialsAsync("tidak-ada@warung.test", GoodPassword);

        Assert.False(wrongPassword.Succeeded);
        Assert.False(unknownEmail.Succeeded);
        Assert.Equal(wrongPassword.Error, unknownEmail.Error);
        Assert.Equal(2, await _db.AuditLogs.CountAsync(a => a.Action == "AUTH_LOGIN_FAILED"));
    }

    [Fact]
    public async Task Login_akun_nonaktif_diblokir()
    {
        await CreateBudiAsync();
        var user = await _db.Users.SingleAsync();
        user.IsActive = false;
        await _db.SaveChangesAsync();

        var result = await _auth.ValidateCredentialsAsync("budi@warung.test", GoodPassword);

        Assert.False(result.Succeeded);
        Assert.Equal(ErrorKind.Forbidden, result.Kind);
    }
}
