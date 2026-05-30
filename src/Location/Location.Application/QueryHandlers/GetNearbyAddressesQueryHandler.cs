using Haworks.BuildingBlocks.Common;
using Haworks.Location.Application.Queries;
using Haworks.Location.Application.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries;

namespace Haworks.Location.Application.QueryHandlers;

internal sealed class GetNearbyAddressesQueryHandler(
    ILocationDbContext dbContext,
    IMemoryCache cache) : IRequestHandler<GetNearbyAddressesQuery, Result<IReadOnlyList<NearbyAddressDto>>>
{
    private static string CacheKey(double lat, double lon, double radius, int limit) =>
        $"nearby:{Math.Round(lat, 5)}:{Math.Round(lon, 5)}:{radius}:{limit}";

    public async Task<Result<IReadOnlyList<NearbyAddressDto>>> Handle(GetNearbyAddressesQuery request, CancellationToken ct)
    {
        var limit = Math.Clamp(request.Limit, 1, 100);
        var key = CacheKey(request.Lat, request.Lon, request.RadiusMeters, limit);

        if (cache.TryGetValue<IReadOnlyList<NearbyAddressDto>>(key, out var cached))
            return Result.Success(cached!);

        var point = new Point(request.Lon, request.Lat) { SRID = 4326 };

        var results = await dbContext.Addresses
            .Where(a => a.Coordinates.IsWithinDistance(point, request.RadiusMeters))
            .OrderBy(a => a.Coordinates.Distance(point))
            .Take(limit)
            .Select(a => new NearbyAddressDto(a.Id, a.Street, a.Postcode, a.Coordinates.Distance(point)))
            .ToListAsync(ct);

        cache.Set(key, (IReadOnlyList<NearbyAddressDto>)results, TimeSpan.FromMinutes(5));

        return Result.Success<IReadOnlyList<NearbyAddressDto>>(results);
    }
}
