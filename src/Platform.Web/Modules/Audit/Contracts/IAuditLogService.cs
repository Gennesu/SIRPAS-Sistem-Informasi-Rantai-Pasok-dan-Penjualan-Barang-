namespace Platform.Web.Modules.Audit.Contracts;

public interface IAuditLogService
{
    /// <summary>
    /// Menambahkan catatan audit ke DbContext saat ini TANPA menyimpan.
    /// Pemanggil wajib memanggil SaveChanges agar catatan atomik dengan operasi bisnisnya.
    /// UserId/BusinessId diisi dari konteks login bila tidak diberikan.
    /// </summary>
    void Record(
        string action,
        string? entityType = null,
        string? entityId = null,
        string? detail = null,
        int? userId = null,
        int? businessId = null);
}
