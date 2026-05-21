using System.Text.Json.Serialization;

namespace Haworks.Contracts.Cdc;

public sealed record DebeziumSource
{
    [JsonPropertyName("db")]
    public string? Db { get; init; }

    [JsonPropertyName("schema")]
    public string? Schema { get; init; }

    [JsonPropertyName("table")]
    public string? Table { get; init; }

    [JsonPropertyName("txId")]
    public long? TxId { get; init; }
}
