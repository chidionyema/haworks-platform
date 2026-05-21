using FluentAssertions;
using Haworks.BuildingBlocks.Testing.Containers;
using Haworks.Webhooks.Infrastructure.Svix;
using Microsoft.Extensions.Logging.Abstractions;
using Svix;
using Svix.Models;
using Xunit;

namespace Haworks.Webhooks.Integration;

[Collection("Integration Tests")]
public class SvixForwarderIntegrationTests : IAsyncLifetime
{
    private SvixClient _svixClient = null!;
    private SvixWebhookForwarder _forwarder = null!;

    private const string JwtSecret = "test-secret-that-is-at-least-32-chars-long!!";

    public async Task InitializeAsync()
    {
        var connStr = await SharedTestPostgres.CreateDatabaseAsync("svix_test");
        var (svixUrl, _) = await SharedTestSvix.GetAsync(connStr, JwtSecret);

        var token = GenerateSvixToken(JwtSecret);
        _svixClient = new SvixClient(token, new SvixOptions(svixUrl));
        _forwarder = new SvixWebhookForwarder(_svixClient, NullLogger<SvixWebhookForwarder>.Instance);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ForwardAsync_Creates_App_And_Message()
    {
        var partnerId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString();

        await _forwarder.ForwardAsync(
            partnerId,
            "order.created",
            """{"orderId":"abc-123","amount":9999}""",
            eventId,
            CancellationToken.None);

        var app = await _svixClient.Application.GetAsync(partnerId.ToString());
        app.Should().NotBeNull();
        app.Uid.Should().Be(partnerId.ToString());

        var messages = await _svixClient.Message.ListAsync(partnerId.ToString());
        messages.Data.Should().ContainSingle(m => m.EventId == eventId);
    }

    [Fact]
    public async Task ForwardAsync_Is_Idempotent_On_Same_EventId()
    {
        var partnerId = Guid.NewGuid();
        var eventId = Guid.NewGuid().ToString();
        var payload = """{"orderId":"dup-test","amount":100}""";

        await _forwarder.ForwardAsync(partnerId, "order.created", payload, eventId, CancellationToken.None);
        await _forwarder.ForwardAsync(partnerId, "order.created", payload, eventId, CancellationToken.None);

        var messages = await _svixClient.Message.ListAsync(partnerId.ToString());
        messages.Data.Count(m => m.EventId == eventId).Should().Be(1,
            "Svix deduplicates on EventId within the 5-minute window");
    }

    [Fact]
    public async Task ForwardAsync_GetOrCreate_Is_Idempotent()
    {
        var partnerId = Guid.NewGuid();

        await _forwarder.ForwardAsync(partnerId, "order.created",
            """{"test":1}""", Guid.NewGuid().ToString(), CancellationToken.None);
        await _forwarder.ForwardAsync(partnerId, "payment.completed",
            """{"test":2}""", Guid.NewGuid().ToString(), CancellationToken.None);

        var app = await _svixClient.Application.GetAsync(partnerId.ToString());
        app.Should().NotBeNull();

        var messages = await _svixClient.Message.ListAsync(partnerId.ToString());
        messages.Data.Should().HaveCount(2);
    }

    private static string GenerateSvixToken(string secret)
    {
        var header = Convert.ToBase64String("""{"alg":"HS256","typ":"JWT"}"""u8)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payloadJson = $$"""{"iss":"svix-server","sub":"org_test","iat":{{now}},"exp":{{now + 3600}},"nbf":{{now}}}""";
        var payload = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var input = $"{header}.{payload}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(secret));
        var sig = Convert.ToBase64String(hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        return $"{input}.{sig}";
    }
}
