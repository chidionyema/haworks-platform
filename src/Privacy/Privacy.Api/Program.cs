using Haworks.Privacy.Application;
using Haworks.Privacy.Infrastructure;
using Haworks.Privacy.Infrastructure.Persistence;
using Haworks.BuildingBlocks.Authentication;
using Haworks.BuildingBlocks.Extensions;
using Haworks.BuildingBlocks.Persistence;
using Haworks.BuildingBlocks.Startup;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire components
builder.AddServiceDefaults();

// Add Serilog — explicit console sink since no appsettings.json exists
builder.Host.UseSerilog((context, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());

// Add layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddStartupTaskRunner();

builder.Services.AddJwksAuthentication(builder.Configuration);
builder.Services.AddHttpContextAccessor();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("api", context =>
    {
        // Partition by authenticated user ID so limits are per-user, not global.
        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? context.User.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";

        return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks().AddDbHealthCheck<Haworks.Privacy.Infrastructure.Persistence.PrivacyDbContext>();

var app = builder.Build();

// The original EF migration omits schema: on CreateTable, so tables land in public
// while HasDefaultSchema("privacy") makes queries target the privacy schema.
// If the migration was "applied" but the privacy schema has no tables, reset
// migration history so MigrateAsync re-runs and creates them properly.
using (var fixScope = app.Services.CreateScope())
{
    var db = fixScope.ServiceProvider.GetRequiredService<PrivacyDbContext>();
    db.Database.ExecuteSqlRaw("CREATE SCHEMA IF NOT EXISTS privacy");

    // Check if privacy.PrivacyRequests exists
    var exists = db.Database.SqlQueryRaw<int>(
        "SELECT COUNT(*)::int AS \"Value\" FROM pg_tables WHERE schemaname = 'privacy' AND tablename = 'PrivacyRequests'")
        .AsEnumerable().FirstOrDefault();

    if (exists == 0)
    {
        // Tables are in public — move them to privacy
        var tables = new[] { "PrivacyRequests", "PrivacyRequestSteps", "PrivacySagaStates",
            "SagaTransitionAudits", "InboxState", "OutboxState", "OutboxMessage" };
        foreach (var t in tables)
        {
#pragma warning disable HWK027 // Table names are compile-time constants, not user input
            db.Database.ExecuteSqlRaw(
                "DO $$ BEGIN " +
                "IF EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = '" + t + "') " +
                "AND NOT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'privacy' AND tablename = '" + t + "') " +
                "THEN EXECUTE 'ALTER TABLE public.\"" + t + "\" SET SCHEMA privacy'; END IF; END $$");
#pragma warning restore HWK027
        }
    }
}
app.MigrateDatabase<PrivacyDbContext>();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();

app.Run();

public partial class Program { }
