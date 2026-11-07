namespace Platform.Web.Modules.Audit.Domain;

/// <summary>
/// Jejak aktivitas penting. Sengaja tanpa foreign key agar catatan tetap ada
/// walaupun data yang dirujuk berubah.
/// </summary>
public class AuditLog
{
    public long Id { get; set; }
    public int? UserId { get; set; }
    public int? BusinessId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; }
}
