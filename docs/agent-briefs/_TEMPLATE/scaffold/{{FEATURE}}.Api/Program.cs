using Haworks.{{FEATURE}}.Application;
using Haworks.{{FEATURE}}.Infrastructure.Persistence;
using Haworks.BuildingBlocks.Extensions;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Postgres connection from Aspire ('{{feature}}' resource) or compose
// (ConnectionStrings__{{feature}} env var).
builder.Services.AddDbContext<{{FEATURE}}DbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("{{feature}}")));

builder.Services.Add{{FEATURE}}Application();

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console()
        .Enrich.FromLogContext();
});

var app = builder.Build();

if (!app.Environment.IsEnvironment("Test"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<{{FEATURE}}DbContext>();
    // No migrations at L0 — L1 tracks add migrations against this context.
    await db.Database.MigrateAsync();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
