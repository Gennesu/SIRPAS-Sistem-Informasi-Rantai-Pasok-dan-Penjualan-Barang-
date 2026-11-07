using Microsoft.EntityFrameworkCore;
using Platform.Web.Data;
using Platform.Web.Modules.BusinessProfile.Domain;
using Platform.Web.Modules.Map.Contracts;
using Platform.Web.SharedKernel;

namespace Platform.Web.Modules.Map.Services;

public sealed class LivestockMapService : ILivestockMapService
{
    private const int MaxPoints = 120;

    private readonly AppDbContext _db;
    private readonly IBusinessContext _context;

    public LivestockMapService(AppDbContext db, IBusinessContext context)
    {
        _db = db;
        _context = context;
    }

    public async Task<LivestockMapDto> GetForAgentAsync(CancellationToken ct = default)
    {
        if (!_context.IsAuthenticated || _context.Role != RoleNames.Grossier || _context.BusinessId is not int agentId)
        {
            return new LivestockMapDto(null, null, null, 0, Array.Empty<LivestockMapPointDto>());
        }

        var origin = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == agentId, ct);
        if (origin is null)
        {
            return new LivestockMapDto(null, null, null, 0, Array.Empty<LivestockMapPointDto>());
        }

        var farms = await _db.Businesses.AsNoTracking()
            .Where(b => b.MarketRole == MarketRole.Livestock
                && b.VerificationStatus == VerificationStatus.Verified
                && b.IsActive)
            .ToListAsync(ct);

        var located = farms.Where(b => b.Latitude is not null && b.Longitude is not null)
            .Select(b => new LivestockMapPointDto(
                b.Id,
                b.Name,
                b.Address,
                b.Phone,
                b.Latitude!.Value,
                b.Longitude!.Value,
                origin.Latitude is double lat && origin.Longitude is double lon
                    ? Math.Round(Geo.DistanceKm(lat, lon, b.Latitude.Value, b.Longitude.Value), 2)
                    : null))
            .OrderBy(b => b.DistanceKm ?? double.MaxValue)
            .ThenBy(b => b.Name)
            .Take(MaxPoints)
            .ToList();

        return new LivestockMapDto(origin.Name, origin.Latitude, origin.Longitude,
            farms.Count(b => b.Latitude is null || b.Longitude is null), located);
    }
}
