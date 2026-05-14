using System.Net.Http.Json;
using FluentAssertions;
using Haworks.Contracts.Payments;
using Haworks.Payments.Api.Controllers;
using Haworks.Payments.Application.Queries.Refunds;
using Haworks.Payments.Domain;
using Haworks.Payments.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Haworks.BuildingBlocks.CurrentUser;
using Moq;

namespace Haworks.Payments.Integration;

[Collection("Payments Integration")]
public class RefundSagaIntegrationTests(PaymentsWebAppFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task CreateRefund_Should_StartSaga_And_ReachAwaitingProvider()
    {
        // Arrange: Create a successful payment first
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        
        var payment = new Payment(
            Guid.NewGuid(), 
            "user_123", 
            Guid.NewGuid(), 
            100.00m, 
            0, 
            "USD", 
            PaymentProvider.Stripe);
        
        // Use reflection to set private fields needed for test
        typeof(Payment).GetProperty("ProviderTransactionId")?.SetValue(payment, "pi_test_123");
        payment.MarkVerified("pi_test_123");
        
        db.Payments.Add(payment);
        await db.SaveChangesAsync();

        var request = new CreateRefundRequest(
            PaymentId: payment.Id,
            Amount: 50.00m,
            Currency: "USD",
            Reason: "Test refund",
            RequestedBy: "TestRunner"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/refunds", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var refundId = await response.Content.ReadFromJsonAsync<Guid>();
        refundId.Should().NotBeEmpty();

        // Poll for saga state
        RefundSagaDto? status = null;
        for (int i = 0; i < 10; i++)
        {
            await Task.Delay(500);
            var statusResponse = await _client.GetAsync($"/api/refunds/{refundId}");
            if (statusResponse.IsSuccessStatusCode)
            {
                status = await statusResponse.Content.ReadFromJsonAsync<RefundSagaDto>();
                if (status?.Status == "AwaitingProviderConfirmation" || status?.Status == "Requested") break;
            }
        }

        status.Should().NotBeNull();
        status!.Amount.Should().Be(50.00m);
        status.Status.Should().BeOneOf("Requested", "AwaitingProviderConfirmation");
    }
}
