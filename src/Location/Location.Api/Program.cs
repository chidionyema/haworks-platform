using System.Security.Claims;
using System.Threading.RateLimiting;
using Haworks.BuildingBlocks.Authentication;
using Haworks.BuildingBlocks.Extensions;
using Haworks.BuildingBlocks.Middleware;
using Haworks.BuildingBlocks.Persistence;
using Haworks.BuildingBlocks.Startup;
using Haworks.Location.Application;
using Haworks.Location.Infrastructure;
using Haworks.Location.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ServiceDefaults: OpenTelemetry + service discovery + HTTP resilience
builder.AddServiceDefaults();

// Infrastructure: DB + MassTransit + Outbox
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// Application: MediatR + Validators
builder.Services.AddApplication();
builder.Services.AddStartupTaskRunner();

builder.Services.AddGrpc();

// Identity: JWKS Auth
builder.Services.AddJwksAuthentication(builder.Configuration);
builder.Services.AddHttpContextAccessor();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("api", context =>
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                     ?? context.User.FindFirst("sub")?.Value
                     ?? context.Connection.RemoteIpAddress?.ToString()
                     ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: userId,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1)
            });
    });
});

// Serilog configuration
builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console()
        .Enrich.FromLogContext();
});

builder.Services.AddHealthChecks()
    .AddDbHealthCheck<Haworks.Location.Infrastructure.Persistence.LocationDbContext>();

var app = builder.Build();

app.MigrateDatabase<LocationDbContext>();

var migrateForced = builder.Configuration.GetValue("MigrateDatabase", false);
if (!app.Environment.IsEnvironment("Test") || migrateForced)
{
    _ = app.Services.GetRequiredService<StartupTaskRunner>();
}

app.MapDefaultEndpoints();

// Standard middleware stack
app.UseInstanceIdHeader();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGrpcService<Haworks.Location.Api.Services.LocationHydrationService>();

app.Run();

public partial class Program { }
