using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Xunit;
using Haworks.BuildingBlocks.Testing.Authentication;
using Haworks.BuildingBlocks.Testing.Containers;
using Microsoft.Extensions.DependencyInjection;
using Haworks.Audit.Application.Capture;
using Haworks.Audit.Application.Extraction;
using Haworks.Audit.Application.Redaction;
using Haworks.Audit.Infrastructure.Persistence;
using MassTransit;
using System.Text.Json;

namespace Haworks.Audit.Integration;

public class AuditWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;
    public string RabbitMqConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        ConnectionString = await SharedTestPostgres.CreateDatabaseAsync("audit");
        RabbitMqConnectionString = await SharedTestRabbitMq.GetConnectionStringAsync();
        JwtTestDefaults.SetTestEnvironmentVariables();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Test");
        Environment.SetEnvironmentVariable("ConnectionStrings__audit", ConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__rabbitmq", RabbitMqConnectionString);
        Environment.SetEnvironmentVariable("ConnectionStrings__s3", "http://localhost:9000");
        Environment.SetEnvironmentVariable("Vault__Enabled", "false");

        // Create schema BEFORE host build — PartitionRolloverService runs at
        // startup and needs the schema to exist before it creates partitions.
        await using var conn = new Npgsql.NpgsqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE SCHEMA IF NOT EXISTS audit;";
        await cmd.ExecuteNonQueryAsync();

        // Force the host to build so Services are available for migration
        _ = Services;
        await EnsureSchemaAsync();
    }

    public async Task EnsureSchemaAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        // Audit uses partitioned tables created by migrations (not EF model).
        // MigrateAsync runs the actual migration SQL including PARTITION BY RANGE.
        // CreateTablesAsync/EnsureCreatedAsync can't handle partitioned tables.
        await db.Database.ExecuteSqlRawAsync("CREATE SCHEMA IF NOT EXISTS audit;");
        await db.Database.MigrateAsync();

        // The migration creates monthly partitions but may not cover the current
        // month in a fresh test database. Add a DEFAULT partition as a catch-all
        // so inserts always succeed.
        await db.Database.ExecuteSqlRawAsync("""
            DO $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM pg_inherits
                    JOIN pg_class c ON c.oid = inhrelid
                    WHERE inhparent = 'audit.audit_events'::regclass
                      AND c.relname = 'audit_events_default'
                ) THEN
                    CREATE TABLE audit.audit_events_default
                    PARTITION OF audit.audit_events DEFAULT;
                END IF;
            END $$;
            """);
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
                ["ConnectionStrings:rabbitmq"] = RabbitMqConnectionString,
                ["ConnectionStrings:s3"] = "http://localhost:9000",
                ["Vault:Enabled"] = "false",
            });
        });

        // Ensure IAuditWriter is always registered: the production path uses a
        // runtime assembly scan which can miss Audit.Infrastructure if it hasn't
        // been JIT-loaded yet when AddAuditCapture() runs.  Registering it
        // explicitly here guarantees the consumer can be resolved in tests.
        // Also register stub extractors/redactor so tests don't need Vault etc.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAuditWriter, AuditWriter>();
            services.AddSingleton(typeof(IAuditExtractor<>), typeof(TestStubExtractor<>));
            services.AddSingleton<ISecretRedactor, TestStubRedactor>();

            services.AddMassTransitTestHarness(mt =>
            {
                AuditMassTransit.RegisterConsumers(mt);
            });

            services.AddAuthentication(TestAuthenticationHandler.SchemeName).AddTestAuth();
        });
    }
}

public class TestStubExtractor<T> : IAuditExtractor<T> where T : class, Haworks.Contracts.IDomainEvent
{
    public AuditRow Extract(T evt, ConsumeContext<T> ctx)
    {
        // Minimal extraction logic for L1.B integration testing
        var json = JsonSerializer.SerializeToElement(evt);
        string entityId = "";
        string entityType = "unknown";

        if (json.TryGetProperty("OrderId", out var orderIdProp))
        {
            entityId = orderIdProp.GetGuid().ToString();
            entityType = "order";
        }
        else if (json.TryGetProperty("PaymentId", out var paymentIdProp))
        {
            entityId = paymentIdProp.GetGuid().ToString();
            entityType = "payment";
        }

        return new AuditRow(
            DateTimeOffset.UtcNow,
            typeof(T).Name,
            entityType,
            entityId,
            "test-actor",
            "user",
            ctx.CorrelationId?.ToString(),
            json,
            JsonSerializer.SerializeToElement(new Dictionary<string, object>())
        );
    }
}

public class TestStubRedactor : ISecretRedactor
{
    public JsonElement Redact(JsonElement input) => input;
}
