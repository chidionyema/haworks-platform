using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.BuildingBlocks.Testing;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Realtime.Api.Application.Common;
using Haworks.Realtime.Api.Infrastructure.Messaging;
using StackExchange.Redis;

namespace Haworks.Realtime.Integration;

/// <summary>
/// WebApplicationFactory for realtime-svc integration tests.
/// Uses SharedTestRedis for IInboxService storage + in-memory MassTransit harness
/// with OrderStatusChangedConsumer registered so we can publish order events
/// and observe real-time notification delivery end-to-end.
///
/// **Single shared instance** across the whole test assembly via
/// <c>RealtimeIntegrationCollection</c> (one host build per
/// `dotnet test`, not per fixture). Tests that need different
/// configurations call <c>factory.WithWebHostBuilder(...)</c>
/// per-test rather than subclassing the factory — see
/// .claude/rules/testing.md.
/// </summary>
public class RealtimeWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string RedisConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        RedisConnectionString = await SharedTestRedis.CreateConnectionStringAsync("realtime");
        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__Redis", RedisConnectionString);

        // RabbitMQ connection string needed for DI registration even though
        // we replace with test harness below
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMQ", "amqp://guest:guest@localhost:5672/");

        // Initialize services
        _ = Services;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        // SharedTestRedis container outlives the fixture intentionally.
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Redis"] = RedisConnectionString,
                ["ConnectionStrings:RabbitMQ"] = "amqp://guest:guest@localhost:5672/",
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the production MassTransit registration with test harness
            // The production DI skips MassTransit in Test environment, so we add it here
            services.AddMassTransitTestHarness(mt =>
            {
                mt.AddConsumer<OrderStatusChangedConsumer>();
                mt.UsingInMemory((ctx, cfg) =>
                {
                    cfg.ConfigureEndpoints(ctx);
                });
            });

            // [Authorize]-decorated endpoints need an auth scheme. Tests use
            // the no-op TestAuthenticationHandler.
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();
        });
    }

    /// <summary>
    /// Clears all Redis data for this service's database.
    /// </summary>
    public async Task ResetRedisAsync()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(RedisConnectionString);
        var config = ConfigurationOptions.Parse(RedisConnectionString);
        var dbNumber = config.DefaultDatabase ?? 0;

        var db = redis.GetDatabase(dbNumber);
        var server = redis.GetServer(redis.GetEndPoints()[0]);

        // Get all keys in this database and delete them (no admin mode required)
        var keys = server.Keys(dbNumber, pattern: "*").ToArray();
        if (keys.Length > 0)
        {
            await db.KeyDeleteAsync(keys);
        }
    }
}