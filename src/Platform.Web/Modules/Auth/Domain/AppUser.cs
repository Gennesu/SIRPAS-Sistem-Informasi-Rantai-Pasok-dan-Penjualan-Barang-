namespace Platform.Web.Modules.Auth.Domain;

public class AppUser
{
    public int Id { get; set; }

    /// <summary>
    /// Null untuk ADMIN. Foreign key ke Businesses dikonfigurasi oleh modul BusinessProfile
    /// (tanpa navigation property) sehingga modul Auth tidak mengenal modul Business.
    /// </summary>
    public int? BusinessId { get; set; }

    public int RoleId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public Role Role { get; set; } = null!;
}
