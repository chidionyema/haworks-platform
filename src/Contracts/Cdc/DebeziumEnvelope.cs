using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haworks.Contracts.Cdc;

/// <summary>
/// Debezium change event envelope. Deserialized from Kafka topic values
/// produced by Debezium Connect watching Postgres WAL.
/// </summary>
public sealed record DebeziumEnvelope
{
    [JsonPropertyName("before")]
    public JsonElement? Before { get; init; }

    [JsonPropertyName("after")]
    public JsonElement? After { get; init; }

    [JsonPropertyName("op")]
    public required string Op { get; init; }

    [JsonPropertyName("ts_ms")]
    public long TsMs { get; init; }

    [JsonPropertyName("source")]
    public DebeziumSource? Source { get; init; }
}
