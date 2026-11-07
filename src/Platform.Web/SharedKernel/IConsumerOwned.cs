namespace Platform.Web.SharedKernel;

/// <summary>
/// Entity milik seorang pembeli akhir (pengguna individu) sekaligus satu usaha penjual (warung).
/// Terkena global query filter: hanya pembeli tersebut, usaha penjual, atau admin yang dapat melihatnya.
/// </summary>
public interface IConsumerOwned
{
    int ConsumerUserId { get; }
    int SellerBusinessId { get; }
}

/// <summary>Entity milik satu pengguna individu (mis. profil pembeli). Hanya pemilik atau admin yang dapat melihat.</summary>
public interface IUserOwned
{
    int UserId { get; }
}
