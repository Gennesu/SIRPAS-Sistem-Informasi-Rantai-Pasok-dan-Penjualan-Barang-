using System.Globalization;

namespace Platform.Web.Modules.Stock.Contracts;

/// <summary>
/// Format teks "yyyy-MM-dd:jumlah|..." untuk membekukan batch asal barang pada baris pesanan,
/// sehingga stok yang dikembalikan (pembatalan) masuk ke batch dengan kadaluarsa yang sama.
/// </summary>
public static class ExpirySnapshotFormat
{
    public static string? Format(IEnumerable<ReservedAllocation> allocations)
    {
        var dated = allocations.Where(a => a.ExpiryDate is not null).ToList();
        if (dated.Count == 0)
        {
            return null;
        }

        var text = string.Join("|", dated
            .GroupBy(a => a.ExpiryDate!.Value)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Key:yyyy-MM-dd}:{g.Sum(a => a.QtyBase)}"));
        return text.Length <= 400 ? text : text[..text.LastIndexOf('|', 399)];
    }

    public static IReadOnlyList<BatchPortion>? Parse(string? snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot))
        {
            return null;
        }

        var list = new List<BatchPortion>();
        foreach (var part in snapshot.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 2
                && DateOnly.TryParseExact(pieces[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                && int.TryParse(pieces[1], out var qty) && qty > 0)
            {
                list.Add(new BatchPortion(date, qty));
            }
        }

        return list.Count == 0 ? null : list;
    }
}
