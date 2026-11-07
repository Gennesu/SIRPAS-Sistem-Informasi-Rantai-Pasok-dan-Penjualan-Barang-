using System.ComponentModel.DataAnnotations;
using Platform.Web.Modules.BusinessProfile.Contracts;
using Platform.Web.Modules.BusinessProfile.Domain;

namespace Platform.Web.Modules.BusinessProfile.ViewModels;

/// <summary>Hanya field yang boleh diubah pemilik; dipisah agar bagian read-only tidak ikut divalidasi saat POST.</summary>
public class EditBusinessInput
{
    [Required(ErrorMessage = "Nama usaha wajib diisi.")]
    [StringLength(120, MinimumLength = 3, ErrorMessage = "Nama usaha 3–120 karakter.")]
    [Display(Name = "Nama usaha")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Alamat wajib diisi.")]
    [StringLength(300, MinimumLength = 5, ErrorMessage = "Alamat 5–300 karakter.")]
    [Display(Name = "Alamat")]
    public string Address { get; set; } = string.Empty;

    [Required(ErrorMessage = "Nomor kontak wajib diisi.")]
    [StringLength(30, ErrorMessage = "Nomor kontak maksimal 30 karakter.")]
    [Display(Name = "Nomor kontak")]
    public string Phone { get; set; } = string.Empty;

    [Range(-90.0, 90.0, ErrorMessage = "Latitude harus di antara -90 dan 90.")]
    [Display(Name = "Latitude")]
    public double? Latitude { get; set; }

    [Range(-180.0, 180.0, ErrorMessage = "Longitude harus di antara -180 dan 180.")]
    [Display(Name = "Longitude")]
    public double? Longitude { get; set; }

    [Display(Name = "Bagikan ringkasan penjualan ke grosir langganan")]
    public bool ShareSalesData { get; set; } = true;
}

public class BusinessProfileViewModel
{
    public int Id { get; set; }
    public string BusinessTypeName { get; set; } = string.Empty;
    public string MarketRoleLabel { get; set; } = string.Empty;
    public string VerificationLabel { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool CanEdit { get; set; }
    public EditBusinessInput Form { get; set; } = new();

    public static BusinessProfileViewModel From(BusinessProfileDto dto, bool canEdit) => new()
    {
        Id = dto.Id,
        BusinessTypeName = dto.BusinessTypeName,
        MarketRoleLabel = dto.MarketRole == nameof(MarketRole.Wholesale) ? "Toko grosir" : "Warung / retail",
        VerificationLabel = dto.VerificationStatus switch
        {
            nameof(VerificationStatus.Verified) => "Terverifikasi",
            nameof(VerificationStatus.Rejected) => "Ditolak",
            _ => "Menunggu verifikasi"
        },
        IsActive = dto.IsActive,
        CreatedAt = dto.CreatedAt,
        CanEdit = canEdit,
        Form = new EditBusinessInput
        {
            Name = dto.Name,
            Address = dto.Address,
            Phone = dto.Phone,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            ShareSalesData = dto.ShareSalesData
        }
    };
}
