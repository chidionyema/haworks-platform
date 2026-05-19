using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.CheckoutOrchestrator.Infrastructure;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;

namespace Haworks.CheckoutOrchestrator.Integration;

/// <summary>
/// Factory that uses REAL RabbitMQ (Testcontainers) instead of the
/// in-memory test harness. This is the production-equivalent test setup.
/// The saga, EF outbox, RabbitMQ transport, and consumer scope isolation
/// are all exercised exactly as they are in production.
/// </summary>
public sealed class CheckoutRealTransportFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _pgConn = string.Empty;
    private string _rabbitConn = string.Empty;

    public async Task InitializeAsync()
    {
        _pgConn = await SharedTestPostgres.CreateDatabaseAsync("checkout-rt");
        _rabbitConn = await SharedTestRabbitMq.GetConnectionStringAsync();

        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Integration");
        Environment.SetEnvironmentVariable("ConnectionStrings__checkout", _pgConn);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", _rabbitConn);
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
        Environment.SetEnvironmentVariable("Checkout__SuccessUrl", "http://localhost/success");
        Environment.SetEnvironmentVariable("Checkout__CancelUrl", "http://localhost/cancel");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Integration");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:checkout"] = _pgConn,
                ["ConnectionStrings:rabbitmq"] = _rabbitConn,
                ["Vault:Enabled"] = "false",
                ["Checkout:SuccessUrl"] = "http://localhost/success",
                ["Checkout:CancelUrl"] = "http://localhost/cancel",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // NO AddMassTransitTestHarness — use the REAL MassTransit + RabbitMQ
            // The app's DI already registers MassTransit with UsingRabbitMq.
            // We just need auth for the [Authorize] endpoints.
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();
        });
    }

    public async Task EnsureSchemaAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
        await db.Database.MigrateAsync();
    }
}
