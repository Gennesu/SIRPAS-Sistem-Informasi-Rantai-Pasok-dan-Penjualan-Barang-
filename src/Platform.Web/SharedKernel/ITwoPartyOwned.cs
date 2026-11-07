namespace Platform.Web.SharedKernel;

/// <summary>
/// Entity yang dimiliki DUA usaha sekaligus (pesanan: pembeli dan penjual).
/// Terkena global query filter di AppDbContext: hanya pembeli, penjual, atau admin yang dapat melihatnya.
/// </summary>
public interface ITwoPartyOwned
{
    int BuyerBusinessId { get; }
    int SellerBusinessId { get; }
}
