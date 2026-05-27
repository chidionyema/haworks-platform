using System.Text.Json.Serialization;
using Haworks.BuildingBlocks.Common;
using Haworks.BuildingBlocks.Idempotency;
using Haworks.RulesEngine.Api.Domain;
using MediatR;

namespace Haworks.RulesEngine.Api.Application;

public record CreateRuleCommand(
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Expression,
    string IdempotencyKey = default!) : IIdempotentCommand, IRequest<Result<Rule>>
{
    public string IdempotencyKey { get; init; } = IdempotencyKey ?? Guid.NewGuid().ToString();
}

public record UpdateRuleCommand(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] string Expression,
    [property: JsonRequired] bool IsActive,
    string IdempotencyKey = default!) : IIdempotentCommand, IRequest<Result<Rule>>
{
    public string IdempotencyKey { get; init; } = IdempotencyKey ?? Guid.NewGuid().ToString();
}

public record DeleteRuleCommand(Guid Id, string IdempotencyKey = default!) : IIdempotentCommand, IRequest<Result<bool>>
{
    public string IdempotencyKey { get; init; } = IdempotencyKey ?? Guid.NewGuid().ToString();
}

public record GetRuleQuery(Guid Id) : IRequest<Result<Rule>>;

public record ListRulesQuery(bool? ActiveOnly, int Skip = 0, int Take = 50) : IRequest<Result<IReadOnlyList<Rule>>>;
