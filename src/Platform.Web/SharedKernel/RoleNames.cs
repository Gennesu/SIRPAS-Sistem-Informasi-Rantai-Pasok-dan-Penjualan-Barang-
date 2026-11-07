namespace Platform.Web.SharedKernel;

/// <summary>
/// Nama role akun (disimpan di tabel Roles). Role = HAK AKSES akun, bukan jenis bisnis dan bukan peran usaha di rantai pasok.
/// Nama V2 (ADMIN, GROSSIER, RETAIL) tidak diubah agar login, filter tenant, dan data demo lama tetap berlaku.
/// </summary>
public static class RoleNames
{
    public const string Admin = "ADMIN";
    public const string Grossier = "GROSSIER";
    public const string Retail = "RETAIL";

    /// <summary>V3: pembeli akhir (pengguna individu, TANPA BusinessId).</summary>
    public const string Consumer = "CONSUMER";

    /// <summary>V3.2: peternakan (pemasok telur/ayam), terikat pada satu usaha.</summary>
    public const string Livestock = "LIVESTOCK";

    /// <summary>Role yang wajib terikat pada satu usaha (BusinessId wajib terisi).</summary>
    public static bool IsBusinessRole(string? role) => role is Grossier or Retail or Livestock;

    public static bool IsSupportedRole(string? role) => role is Admin or Grossier or Retail or Consumer or Livestock;
}
