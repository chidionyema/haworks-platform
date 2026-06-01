using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Haworks.BuildingBlocks.CurrentUser;
using Haworks.Location.Application.Interfaces;
using MassTransit;
using Moq;
using MassTransit.Testing;

namespace Haworks.Location.Integration;

public class LocationWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = await SharedTestPostGis.CreateDatabaseAsync("location");

        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__location", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", "amqp://guest:guest@localhost:5672");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:location"] = ConnectionString,
                ["Vault:Enabled"] = "false",
                ["MigrateDatabase"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            var currentUserMock = new Mock<ICurrentUserService>();
            currentUserMock.Setup(x => x.UserId).Returns("test-user");
            currentUserMock.Setup(x => x.ClientIp).Returns("127.0.0.1");
            services.AddSingleton(currentUserMock.Object);

            // Remove IPublishEndpoint mock to enable MassTransit outbox testing
            // The outbox pattern will be used with real MassTransit test harness

            // Replace geocoding service mock with a test implementation that can be configured
            services.AddSingleton<TestGeocodingService>();
            services.AddScoped<IGeocodingService>(provider => provider.GetRequiredService<TestGeocodingService>());

            // Add MassTransit test harness for integration testing
            services.AddMassTransitTestHarness();
        });
    }
}

/// <summary>
/// Test implementation of IGeocodingService that allows configurable responses for integration testing
/// </summary>
public class TestGeocodingService : IGeocodingService
{
    private readonly Dictionary<string, (double Latitude, double Longitude)?> _responses = new();

    public Task<(double Latitude, double Longitude)?> GeocodeAsync(string address, CancellationToken ct = default)
    {
        if (_responses.TryGetValue(address, out var coordinates))
        {
            return Task.FromResult(coordinates);
        }

        // Default response for unknown addresses (simulating successful geocoding)
        return Task.FromResult<(double Latitude, double Longitude)?>((51.5074, -0.1278));
    }

    /// <summary>
    /// Configure a specific response for a given address
    /// </summary>
    public void SetResponse(string address, (double Latitude, double Longitude)? coordinates)
    {
        _responses[address] = coordinates;
    }

    /// <summary>
    /// Clear all configured responses
    /// </summary>
    public void ClearResponses()
    {
        _responses.Clear();
    }
}
