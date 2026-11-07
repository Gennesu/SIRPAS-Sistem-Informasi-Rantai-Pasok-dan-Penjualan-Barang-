namespace Platform.Web.SharedKernel;

/// <summary>
/// Penanda entity yang dimiliki satu Business (tenant).
/// Setiap entity yang mengimplementasikan ini otomatis terkena global query filter
/// di AppDbContext (non-admin hanya melihat BusinessId miliknya).
/// </summary>
public interface IBusinessOwned
{
    int BusinessId { get; }
}
