using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;

namespace Haworks.Audit.Integration;

/// <summary>
/// WebApplicationFactory for audit-svc integration tests.
///
/// L0 skeleton: boots the host against a per-fixture Postgres database
/// (created via <c>SharedTestPostgres</c>, the platform-shared container)
/// with Vault disabled. L1.B refines this — the L1.B brief explicitly
/// allows refinement (e.g., adding Testcontainers RabbitMQ for the
/// end-to-end capture tests). L1.A / L1.C / L1.D do NOT modify this file.
/// </summary>
public class AuditWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = await SharedTestPostgres.CreateDatabaseAsync("audit");
        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__audit", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", "amqp://guest:guest@localhost:5672/");
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
                ["ConnectionStrings:audit"] = ConnectionString,
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
                ["Vault:Enabled"] = "false",
            });
        });
    }
}
