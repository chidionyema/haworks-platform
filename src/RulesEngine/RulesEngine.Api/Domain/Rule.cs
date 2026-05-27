using Haworks.BuildingBlocks.Common;

namespace Haworks.RulesEngine.Api.Domain;

public class Rule
{
    public Guid Id { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Expression { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

public interface IRulesEvaluator
{
    Task<Result<RuleEvaluationResult>> EvaluateAsync(Guid ruleId, Dictionary<string, object> inputs, CancellationToken cancellationToken);
}

public sealed record RuleEvaluationResult(bool Outcome, string Expression, string Trace);
