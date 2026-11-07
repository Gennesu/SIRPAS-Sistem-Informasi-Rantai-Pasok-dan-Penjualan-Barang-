namespace Platform.Web.SharedKernel;

public sealed record DemoAccount(string Role, string Name, string Email);

/// <summary>Akun dummy untuk mode demo (dibuat oleh DemoDataSeeder). Hanya untuk demo, bukan untuk produksi.</summary>
public static class DemoAccounts
{
    public const string Password = "Demo12345";

    public static readonly DemoAccount[] All =
    {
        new("Warung", "Warung Maju", "warung@demo.test"),
        new("Warung", "Toko Sejahtera", "warung3@demo.test"),
        new("Warung", "Warung Pak Dedi", "warung5@demo.test"),
        new("Warung", "Kedai Bu Rina (belum pernah memesan)", "warung4@demo.test"),
        new("Warung", "Toko Ujung Berung (di luar jangkauan)", "warung6@demo.test"),
        new("Warung", "Warung Berkah (menunggu verifikasi)", "warung2@demo.test"),
        new("Agen", "Grosir Sumber Rejeki", "grosir@demo.test"),
        new("Agen", "Grosir Makmur Jaya", "grosir2@demo.test"),
        new("Peternakan", "Ayam Petelur Cisarua", "ternak@demo.test"),
        new("Peternakan", "Kandang Sejahtera Cicalengka", "ternak2@demo.test"),
        new("Peternakan", "Peternakan Ciparay", "ternak3@demo.test"),
        new("Pembeli", "Budi Pembeli", "pembeli@demo.test"),
        new("Pembeli", "Siti Konsumen", "pembeli2@demo.test"),
        new("Admin", "Administrator Demo", "admin@demo.test")
    };
}
