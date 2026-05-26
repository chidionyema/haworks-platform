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
    // Cache key rounds lat/lon to ~110m grid cells to group nearby queries
    private static string CacheKey(double lat, double lon, double radius, int limit) =>
        $"nearby:{Math.Round(lat, 3)}:{Math.Round(lon, 3)}:{radius}:{limit}";

    public async Task<Result<IReadOnlyList<NearbyAddressDto>>> Handle(GetNearbyAddressesQuery request, CancellationToken ct)
    {
        if (request.Lat < -90 || request.Lat > 90)
            return Result.Failure<IReadOnlyList<NearbyAddressDto>>(Error.Validation("Address.InvalidLatitude", "Latitude must be between -90 and 90."));

        if (request.Lon < -180 || request.Lon > 180)
            return Result.Failure<IReadOnlyList<NearbyAddressDto>>(Error.Validation("Address.InvalidLongitude", "Longitude must be between -180 and 180."));

        if (request.RadiusMeters > 50000)
            return Result.Failure<IReadOnlyList<NearbyAddressDto>>(Error.Validation("Address.InvalidRadius", "RadiusMeters must not exceed 50000."));

        var limit = Math.Clamp(request.Limit, 1, 100);
        var key = CacheKey(request.Lat, request.Lon, request.RadiusMeters, limit);

        if (cache.TryGetValue<IReadOnlyList<NearbyAddressDto>>(key, out var cached))
            return Result.Success(cached!);

        var point = new Point(request.Lon, request.Lat) { SRID = 4326 };

        var results = await dbContext.Addresses
            .Select(a => new { Address = a, Distance = a.Coordinates.Distance(point) })
            .Where(x => x.Distance <= request.RadiusMeters)
            .OrderBy(x => x.Distance)
            .Take(limit)
            .Select(x => new NearbyAddressDto(
                x.Address.Id,
                x.Address.Street,
                x.Address.Postcode,
                x.Distance
            ))
            .ToListAsync(ct);

        cache.Set(key, (IReadOnlyList<NearbyAddressDto>)results, TimeSpan.FromMinutes(5));

        return Result.Success<IReadOnlyList<NearbyAddressDto>>(results);
    }
}
