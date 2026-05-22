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

app.MigrateDatabase<PrivacyDbContext>();

// Enable detailed errors so we can see what's crashing
app.UseDeveloperExceptionPage();
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
