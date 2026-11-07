namespace Platform.Web.Modules.BusinessProfile.Domain;

/// <summary>Jenis bisnis (GROCERY, CLOTHING, ELECTRONICS, OTHER). Di-seed lewat migration.</summary>
public class BusinessType
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
