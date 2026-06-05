using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Shipping.Api.Infrastructure;

namespace Haworks.Shipping.Integration;

public class ShippingIntegrationTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnString { get; private set; } = string.Empty;
    public Mock<IShippingProvider> MockShippingProvider { get; } = new();

    public async Task InitializeAsync()
    {
        ConnString = await SharedTestPostgres.CreateDatabaseAsync("shipping");
        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__shipping", ConnString);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", "amqp://guest:guest@localhost:5672/");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
        Environment.SetEnvironmentVariable("EasyPost__ApiKey", "test_key");
        Environment.SetEnvironmentVariable("EasyPost__TestMode", "true");

        _ = Services;
        await EnsureSchemaAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
    }

    public async Task EnsureSchemaAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS shipping;");
        await db.Database.MigrateAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:shipping"] = ConnString,
                ["EasyPost:ApiKey"] = "test_key",
                ["EasyPost:TestMode"] = "true",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace IShippingProvider with mock for deterministic tests
            services.Remove(services.Single(s => s.ServiceType == typeof(IShippingProvider)));
            services.AddSingleton(MockShippingProvider.Object);

            // Use in-memory MassTransit for testing
            services.AddMassTransit(x =>
            {
                x.AddConsumers(typeof(Program).Assembly);
                x.SetKebabCaseEndpointNameFormatter();
                x.UsingInMemory((context, cfg) =>
                {
                    cfg.ConfigureEndpoints(context);
                });
            });
        });
    }
}