using Grpc.Core;
using LocationGrpc;
using Haworks.Location.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Haworks.Location.Api.Services;

/// <summary>
/// gRPC service for "Hydrating" search results.
/// Returns full address details for a given list of Location IDs.
/// </summary>
public class LocationHydrationService(LocationDbContext dbContext, ILogger<LocationHydrationService> logger)
    : LocationHydration.LocationHydrationBase
{
    public override async Task<AddressList> GetAddresses(AddressRequest request, ServerCallContext context)
    {
        var guids = new List<Guid>();
        foreach (var id in request.LocationIds)
        {
            if (Guid.TryParse(id, out var guid))
            {
                guids.Add(guid);
            }
            else
            {
                logger.LogWarning("Invalid GUID provided in address request: {InvalidId}", id);
            }
        }

        if (guids.Count == 0)
        {
            return new AddressList();
        }

        var addresses = await dbContext.Addresses
            .AsNoTracking()
            .Where(a => guids.Contains(a.Id))
            .ToListAsync(context.CancellationToken);

        var response = new AddressList();
        response.Locations.AddRange(addresses.Select(a => new AddressDetail
        {
            Id = a.Id.ToString(),
            Street = a.Street,
            City = a.City,
            Postcode = a.Postcode,
            Country = a.Country,
            Latitude = a.Coordinates.Y,
            Longitude = a.Coordinates.X
        }));

        return response;
    }
}
