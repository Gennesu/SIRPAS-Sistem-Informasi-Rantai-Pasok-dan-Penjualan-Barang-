namespace Platform.Web.SharedKernel;

/// <summary>
/// Entity dengan concurrency token. AppDbContext menaikkan Version pada setiap UPDATE sehingga dua pembaruan
/// serentak atas baris yang sama tidak saling menimpa diam-diam: yang kedua mendapat DbUpdateConcurrencyException
/// (UPDATE ... WHERE Id = @id AND Version = @versiLama menyentuh 0 baris), lalu UnitOfWork mengulang dengan data terbaru.
/// </summary>
public interface IVersioned
{
    long Version { get; set; }
}
