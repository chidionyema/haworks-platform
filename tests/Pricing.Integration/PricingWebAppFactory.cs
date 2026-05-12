using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Haworks.Pricing.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haworks.Pricing.Integration;

public sealed class PricingWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly IContainer _db = new ContainerBuilder()
        .WithImage("postgres:16-alpine")
        .WithEnvironment("POSTGRES_USER", "postgres")
        .WithEnvironment("POSTGRES_PASSWORD", "postgres")
        .WithEnvironment("POSTGRES_DB", "pricing")
        .WithPortBinding(5432, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(5432))
        .Build();

    public async Task InitializeAsync() => await _db.StartAsync();

    public new async Task DisposeAsync() => await _db.StopAsync();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureTestServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<PricingDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            var connString = $"Host={_db.Hostname};Port={_db.GetMappedPublicPort(5432)};Database=pricing;Username=postgres;Password=postgres";
            services.AddDbContext<PricingDbContext>(options => options.UseNpgsql(connString));
        });
    }
}
