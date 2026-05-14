using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Hangfire;

namespace Haworks.Scheduler.Integration;

public class SchedulerWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string ConnString { get; set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnString = await SharedTestPostgres.CreateDatabaseAsync("scheduler");

        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__scheduler", ConnString);
        Environment.SetEnvironmentVariable("RabbitMq__Host", "localhost");
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
                ["Authentication:Jwks:JwksUri"] = "http://test-identity.invalid/.well-known/jwks.json",
                ["Authentication:Jwks:Issuer"] = JwtTestDefaults.Issuer,
                ["Authentication:Jwks:Audience"] = JwtTestDefaults.Audience,
                ["Authentication:Jwks:AutomaticRefresh"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            var mockBackgroundJobClient = new Mock<IBackgroundJobClient>();
            services.AddSingleton(mockBackgroundJobClient.Object);
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();
        });
    }
}
