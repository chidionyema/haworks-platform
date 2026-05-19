using Amazon.S3;
using Amazon.S3.Model;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Contracts.Media;
using Haworks.Media.Api.Infrastructure;
using Haworks.Media.Api.Infrastructure.Workers;
using MassTransit;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Haworks.Media.Integration;

public sealed class MediaWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string BucketName = "media-test";

    private string _connectionString = string.Empty;
    private string _localstackUrl = string.Empty;

    public bool VirusScanShouldFail { get; set; }
    public string LocalstackUrl => _localstackUrl;
    public static string Bucket => BucketName;

    public async Task InitializeAsync()
    {
        _connectionString = await SharedTestPostgres.CreateDatabaseAsync("media");
        _localstackUrl = await SharedTestS3.GetEndpointAsync();

        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _connectionString);
        Environment.SetEnvironmentVariable("Storage__Enabled", "true");
        Environment.SetEnvironmentVariable("Storage__ServiceUrl", _localstackUrl);
        Environment.SetEnvironmentVariable("Storage__AccessKey", "test");
        Environment.SetEnvironmentVariable("Storage__SecretKey", "test");
        Environment.SetEnvironmentVariable("Storage__BucketName", BucketName);
        Environment.SetEnvironmentVariable("Storage__Region", "us-east-1");
        Environment.SetEnvironmentVariable("ClamAV__Enabled", "false");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");

        await EnsureBucketAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((ctx, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connectionString,
                ["Storage:Enabled"] = "true",
                ["Storage:ServiceUrl"] = _localstackUrl,
                ["Storage:AccessKey"] = "test",
                ["Storage:SecretKey"] = "test",
                ["Storage:BucketName"] = BucketName,
                ["Storage:Region"] = "us-east-1",
                ["ClamAV:Enabled"] = "false",
                ["Vault:Enabled"] = "false",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();

            // Replace ClamAV with a fake driven by the VirusScanShouldFail flag
            services.RemoveAll<IVirusScanner>();
            services.AddSingleton<IVirusScanner>(_ => new FakeVirusScanner(this));

            // MassTransit in-memory test harness
            services.AddMassTransitTestHarness(mt =>
            {
                mt.AddConsumer<ProcessMediaConsumer>();
                mt.AddConsumer<MediaUploadCompletedConsumer>();
            });
        });
    }

    private sealed class FakeVirusScanner(MediaWebAppFactory owner) : IVirusScanner
    {
        public Task<bool> ScanAsync(Stream fileStream, CancellationToken ct = default)
            => Task.FromResult(!owner.VirusScanShouldFail);

        public Task<bool> ScanFileAsync(string filePath, CancellationToken ct = default)
            => Task.FromResult(!owner.VirusScanShouldFail);
    }

    public async Task EnsureSchemaAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaDbContext>();
        await db.Database.MigrateAsync();

        // Drop outbox tables — in-memory test harness doesn't use the EF outbox pipeline
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "DROP TABLE IF EXISTS media.\"OutboxMessage\", media.\"InboxState\", media.\"OutboxState\" CASCADE");
        }
        catch { /* tables may not exist */ }
    }

    private async Task EnsureBucketAsync()
    {
        var s3Config = new AmazonS3Config
        {
            ServiceURL = _localstackUrl,
            ForcePathStyle = true,
            AuthenticationRegion = "us-east-1",
        };
        using var s3 = new AmazonS3Client("test", "test", s3Config);
        try
        {
            await s3.PutBucketAsync(new PutBucketRequest { BucketName = BucketName });
        }
        catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
            // Reuse from prior run
        }
    }
}
