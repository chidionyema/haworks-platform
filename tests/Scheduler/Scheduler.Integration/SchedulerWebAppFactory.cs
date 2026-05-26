using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Scheduler.Api.Infrastructure;
using Haworks.Scheduler.Infrastructure.Persistence;
using Hangfire;

namespace Haworks.Scheduler.Integration;

public class SchedulerWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string ConnString { get; set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnString = await SharedTestPostgres.CreateDatabaseAsync("scheduler");

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__scheduler", ConnString);
        Environment.SetEnvironmentVariable("RabbitMq__Host", "localhost");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");

        JwtTestDefaults.SetTestEnvironmentVariables();

        // Force host build so Services are available, then create schema
        _ = Services;
        await EnsureSchemaAsync();
    }

    public async Task EnsureSchemaAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulerDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS scheduler;");
        await db.Database.MigrateAsync();
    }

    public new Task DisposeAsync() => Task.CompletedTask;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:scheduler"] = ConnString,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace DbContext to suppress PendingModelChangesWarning during currency migration
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<SchedulerDbContext>));
            if (descriptor != null) services.Remove(descriptor);
            services.AddDbContext<SchedulerDbContext>((sp, options) =>
            {
                options.UseNpgsql(ConnString);
                options.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));
            });

            var mockBackgroundJobClient = new Mock<IBackgroundJobClient>();
            services.AddSingleton(mockBackgroundJobClient.Object);
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();

            // Remove the LeaseBootstrapStartupTask — it queries VaultLeases
            // at startup before MigrateAsync can create the schema.
            var leaseDescriptor = services.SingleOrDefault(d =>
                d.ImplementationType == typeof(LeaseBootstrapStartupTask));
            if (leaseDescriptor != null) services.Remove(leaseDescriptor);
        });
    }
}
