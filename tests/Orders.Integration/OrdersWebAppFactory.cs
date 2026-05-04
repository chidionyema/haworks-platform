using System.Security.Claims;
using System.Text.Encodings.Web;
using MassTransit;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using Xunit;
using Haworks.BuildingBlocks.Messaging;
using Haworks.Orders.Application.Consumers;

namespace Haworks.Orders.Integration;

/// <summary>
/// WebApplicationFactory for orders-svc integration tests.
/// Same pattern as catalog/payments: Testcontainers postgres + in-memory
/// MassTransit harness with all 3 consumers wired so we can publish upstream
/// events into the harness and assert state + outbound publishes.
/// </summary>
public sealed class OrdersWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("orders")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__orders", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", "amqp://guest:guest@localhost:5672/");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:orders"]   = ConnectionString,
                ["ConnectionStrings:rabbitmq"] = "amqp://guest:guest@localhost:5672/",
                ["Vault:Enabled"]              = "false",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.AddMassTransitTestHarness(mt =>
            {
                mt.AddConsumer<PaymentCompletedConsumer>();
                mt.AddConsumer<PaymentSessionFailedConsumer>();
                mt.AddConsumer<StockReservationFailedConsumer>();
            });
            services.AddDomainEventPublisher();

            // OrdersController endpoints carry [Authorize] but the integration
            // tests don't run a real Identity / JWT issuer. Register a
            // default authentication scheme that auto-authenticates every
            // request as a fixed test user, so [Authorize] is satisfied
            // without having to mint real tokens.
            services.AddAuthentication(TestAuthenticationHandler.Scheme)
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.Scheme, _ => { });
        });
    }

    public async Task EnsureSchemaAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Haworks.Orders.Infrastructure.OrderDbContext>();
        await db.Database.MigrateAsync();
    }
}

/// <summary>
/// No-op authentication handler that always succeeds. Stamps a fixed
/// test principal on every request so [Authorize]-decorated endpoints
/// are usable in integration tests without minting real JWTs. The
/// fixture wires this as the default scheme via AddAuthentication(...).
/// </summary>
internal sealed class TestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";
    public const string TestUserId = "test-user";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestUserId),
            new Claim(ClaimTypes.Name, TestUserId),
            new Claim(ClaimTypes.Role, "User"),
        };
        var identity = new ClaimsIdentity(claims, Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
