using FluentAssertions;
using Haworks.Webhooks.Application.Interfaces;
using Haworks.Webhooks.Application.Subscriptions;
using Haworks.Webhooks.Domain;
using Haworks.Webhooks.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Haworks.Webhooks.Unit.Application;

public class SubscriptionHandlersTests
{
    private readonly WebhooksDbContext _db;
    private readonly SubscriptionHandlers _handlers;

    public SubscriptionHandlersTests()
    {
        var options = new DbContextOptionsBuilder<WebhooksDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        _db = new WebhooksDbContext(options);
        _handlers = new SubscriptionHandlers(
            _db,
            Mock.Of<IHttpClientFactory>(),
            NullLogger<SubscriptionHandlers>.Instance);
    }

    private WebhookSubscription AddSubscription(Guid partnerId)
    {
        var sub = new WebhookSubscription(partnerId, "https://example.com/hook", "secret", "hash", "cret", ["order.created"]);
        _db.Subscriptions.Add(sub);
        _db.SaveChanges();
        return sub;
    }

    [Fact]
    public async Task Update_With_Correct_Partner_Should_Succeed()
    {
        var partnerId = Guid.NewGuid();
        var sub = AddSubscription(partnerId);

        var result = await _handlers.Handle(
            new UpdateWebhookSubscriptionCommand(sub.Id, "https://example.com/new", ["order.completed"], false, partnerId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Url.Should().Be("https://example.com/new");
        result.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Update_With_Wrong_Partner_Should_Return_NotFound()
    {
        var sub = AddSubscription(Guid.NewGuid());

        var result = await _handlers.Handle(
            new UpdateWebhookSubscriptionCommand(sub.Id, "https://example.com/new", ["order.completed"], false, Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_With_Correct_Partner_Should_SoftDelete()
    {
        var partnerId = Guid.NewGuid();
        var sub = AddSubscription(partnerId);

        var result = await _handlers.Handle(new DeleteWebhookSubscriptionCommand(sub.Id, partnerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        (await _db.Subscriptions.FindAsync(sub.Id))!.DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_With_Wrong_Partner_Should_Return_NotFound()
    {
        var sub = AddSubscription(Guid.NewGuid());

        var result = await _handlers.Handle(new DeleteWebhookSubscriptionCommand(sub.Id, Guid.NewGuid()), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task RotateSecret_With_Correct_Partner_Should_Return_Full_Secret()
    {
        var partnerId = Guid.NewGuid();
        var sub = AddSubscription(partnerId);

        var result = await _handlers.Handle(
            new RotateWebhookSubscriptionSecretCommand(sub.Id, "a-brand-new-rotated-secret", partnerId),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("a-brand-new-rotated-secret");
    }

    [Fact]
    public async Task RotateSecret_With_Wrong_Partner_Should_Return_NotFound()
    {
        var sub = AddSubscription(Guid.NewGuid());

        var result = await _handlers.Handle(
            new RotateWebhookSubscriptionSecretCommand(sub.Id, "new-secret", Guid.NewGuid()),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task List_Should_Only_Return_Callers_Own_Subscriptions()
    {
        var partnerId = Guid.NewGuid();
        AddSubscription(partnerId);
        AddSubscription(partnerId);
        AddSubscription(Guid.NewGuid());

        var result = await _handlers.Handle(new ListWebhookSubscriptionsQuery(partnerId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Total.Should().Be(2);
        result.Value.Items.Should().OnlyContain(x => x.PartnerId == partnerId);
    }
}
