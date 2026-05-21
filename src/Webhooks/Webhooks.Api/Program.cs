using Haworks.BuildingBlocks.Messaging;
using Haworks.BuildingBlocks.Authentication;
using Haworks.BuildingBlocks.Extensions;
using Haworks.BuildingBlocks.Startup;
using Haworks.Webhooks.Application;
using Haworks.Webhooks.Infrastructure;
using Haworks.Webhooks.Infrastructure.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Haworks.Webhooks.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.AddServiceDefaults();

builder.Services.AddApplication();
builder.Services.AddWebhooksInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddStartupTaskRunner();

// MassTransit for Domain Events — fan-out to Svix
builder.Services.AddMassTransitDiagnostics();
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<Haworks.BuildingBlocks.Messaging.GlobalFaultConsumer>();
    x.AddConsumer<EventFanOutConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.ConfigureStandardRabbitMq(context);
    });
});

builder.Services.AddPlatformAuthentication(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddDbHealthCheck<WebhooksDbContext>();

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsEnvironment("Test"))
{
    var startupRunner = app.Services.GetRequiredService<StartupTaskRunner>();
    startupRunner.AddTask(async (sp, ct) =>
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<WebhooksDbContext>();
        await db.Database.MigrateAsync(ct);
    });
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
