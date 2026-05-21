using System.Security.Claims;
using System.Text.Encodings.Web;
using Haworks.Webhooks.Application.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Haworks.BuildingBlocks.Testing;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Webhooks.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.Webhooks.Integration;

/// <summary>
/// Webhooks-specific test auth handler. Extends the standard test principal
/// with a <c>partner_id</c> claim (GUID) so that <c>SubscriptionsController.GetPartnerId()</c>
/// resolves to a non-empty GUID and passes FluentValidation's NotEmpty rule.
/// </summary>
internal sealed class WebhooksTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string TestPartnerIdString = "00000000-0000-0000-0000-000000000123";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, TestAuthenticationHandler.TestUserId),
            new Claim("sub", TestAuthenticationHandler.TestUserId),
            new Claim("partner_id", TestPartnerIdString),
            new Claim("email", "test@test.invalid"),
            new Claim(ClaimTypes.Name, TestAuthenticationHandler.TestUserId),
            new Claim(ClaimTypes.Role, "User"),
            new Claim(ClaimTypes.Role, "Admin"),
        };

        var identity = new ClaimsIdentity(claims, TestAuthenticationHandler.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, TestAuthenticationHandler.SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

public class WebhooksWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private DatabaseResetter? _resetter;
    public string ConnectionString { get; private set; } = string.Empty;
    public string RabbitMqConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = await SharedTestPostgres.CreateDatabaseAsync("webhooks");
        RabbitMqConnectionString = "amqp://guest:guest@localhost:5672/";
        _resetter = new DatabaseResetter(ConnectionString, "webhooks");

        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__webhooks", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", RabbitMqConnectionString);
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");
        Environment.SetEnvironmentVariable("Svix__ServerUrl", "http://localhost:8071");
        Environment.SetEnvironmentVariable("Svix__AuthToken", "test-token");

        // Force host build so Services are available, then apply schema
        _ = Services;
        await EnsureSchemaAsync();
    }

    public async Task EnsureSchemaAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS webhooks;");

        var creator = db.Database.GetService<IRelationalDatabaseCreator>();
        try { await creator.CreateTablesAsync(); }
        catch (Npgsql.PostgresException ex) when (string.Equals(ex.SqlState, "42P07", StringComparison.Ordinal)) { /* tables already exist */ }
    }

    public Task ResetDatabaseAsync() => _resetter!.ResetAsync();

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
                ["ConnectionStrings:webhooks"] = ConnectionString,
                ["ConnectionStrings:rabbitmq"] = RabbitMqConnectionString,
                ["Vault:Enabled"] = "false",
                ["Svix:ServerUrl"] = "http://localhost:8071",
                ["Svix:AuthToken"] = "test-token",
                ["JwksOptions:Issuer"] = "https://test-issuer.invalid",
                ["JwksOptions:Audience"] = "test-audience",
                ["JwksOptions:JwksUri"] = "https://test-issuer.invalid/.well-known/jwks",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Replace ALL named HttpClient handlers with AlwaysOkHandler so
            // ValidateWebhookUrlAsync never makes real HTTP calls.
            services.AddHttpClient("WebhookValidator")
                .ConfigurePrimaryHttpMessageHandler(() => new AlwaysOkHandler());

            // Mock Svix dispatcher — integration tests validate subscription CRUD,
            // not actual Svix API calls.
            services.AddScoped<IWebhookDispatcher>(_ => new Mock<IWebhookDispatcher>().Object);

            services.AddAuthentication(TestAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, WebhooksTestAuthHandler>(
                    TestAuthenticationHandler.SchemeName, _ => { });
        });
    }

    private sealed class AlwaysOkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
