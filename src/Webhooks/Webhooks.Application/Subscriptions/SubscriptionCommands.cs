using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.Webhooks.Application.Common;
using Haworks.Webhooks.Application.Subscriptions;
using MediatR;

namespace Haworks.Webhooks.Application.Subscriptions;

public sealed record CreateWebhookSubscriptionCommand(
    Guid PartnerId,
    string Url,
    string[] Events,
    string? Secret,
    bool IsActive,
    string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result<CreateWebhookSubscriptionResult>>;

public sealed record UpdateWebhookSubscriptionCommand(
    Guid Id,
    string Url,
    string[] Events,
    bool IsActive,
    Guid CallerPartnerId = default,
    string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result<WebhookSubscriptionDto>>;

public sealed record DeleteWebhookSubscriptionCommand(Guid Id, Guid CallerPartnerId = default, string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result>;

public sealed record RotateWebhookSubscriptionSecretCommand(
    Guid Id,
    string? Secret,
    Guid CallerPartnerId = default,
    string IdempotencyKey = "") : IIdempotentCommand, IRequest<Result<string>>;

public sealed record GetWebhookSubscriptionQuery(Guid Id, Guid CallerPartnerId = default) : IRequest<Result<WebhookSubscriptionDto>>;

public sealed record ListWebhookSubscriptionsQuery(
    Guid CallerPartnerId,
    int Skip = 0,
    int Take = 20) : IRequest<Result<PagedResult<WebhookSubscriptionDto>>>;
