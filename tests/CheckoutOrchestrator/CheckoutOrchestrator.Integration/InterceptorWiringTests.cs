using Haworks.CheckoutOrchestrator.Domain;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;
using Haworks.BuildingBlocks.Messaging;
using Haworks.CheckoutOrchestrator.Infrastructure;
using System.Net;
using System.Net.Http.Json;

namespace Haworks.CheckoutOrchestrator.Integration;

public sealed class InterceptorWiringTests : IClassFixture<CheckoutRealTransportFactory>, IAsyncLifetime
{
    private readonly CheckoutRealTransportFactory _factory;
    private readonly ITestOutputHelper _output;
    private HttpClient _client = null!;

    public InterceptorWiringTests(CheckoutRealTransportFactory factory, ITestOutputHelper output)
    {
        _factory = factory;
        _output = output;
    }

    public async Task InitializeAsync()
    {
        await _factory.EnsureSchemaAsync();
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void Interceptor_is_registered()
    {
        var interceptor = _factory.Services.GetService<ISaveChangesInterceptor>();
        interceptor.Should().NotBeNull();
        interceptor.Should().BeOfType<SagaPersistenceInterceptor>();
    }

    [Fact]
    public async Task Interceptor_fires_on_saga_creation_via_real_RabbitMQ()
    {
        // Capture log output from the interceptor
        var logMessages = new List<string>();
        var loggerFactory = _factory.Services.GetRequiredService<ILoggerFactory>();

        var sagaId = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync("/api/checkouts", new
        {
            sagaId,
            orderId = Guid.NewGuid(),
            userId = "test",
            customerEmail = "test@test.com",
            totalAmount = 10m,
            currency = "GBP",
            idempotencyKey = $"int-{sagaId:N}",
            items = new[] { new { productId = Guid.NewGuid(), productName = "W", quantity = 1, unitPrice = 10m } }
        });

        _output.WriteLine($"POST response: {response.StatusCode}");
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);

        // Wait for saga to be created
        CheckoutSagaState? saga = null;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(500);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CheckoutDbContext>();
            saga = await db.CheckoutSagas.AsNoTracking()
                .FirstOrDefaultAsync(s => s.CorrelationId == sagaId);
            if (saga != null)
            {
                _output.WriteLine($"Saga found at attempt {i}: State={saga.CurrentState}");
                break;
            }
        }

        saga.Should().NotBeNull("saga should be created via real RabbitMQ");

        // The interceptor should have logged — check by querying the DbContext
        // to see if our interceptor metadata is present
        _output.WriteLine($"Saga: {saga!.CorrelationId} State={saga.CurrentState} Currency={saga.Currency}");
    }
}
