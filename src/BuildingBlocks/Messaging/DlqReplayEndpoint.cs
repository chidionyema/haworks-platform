using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Haworks.BuildingBlocks.Messaging;

/// <summary>
/// DLQ replay service that uses the RabbitMQ Management HTTP API to move
/// messages from error queues back to their original queues.
///
/// Register: services.AddHttpClient("RabbitMqManagement");
///           services.AddScoped&lt;DlqReplayService&gt;();
///
/// Use from any controller or minimal API endpoint.
/// </summary>
public sealed class DlqReplayService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<DlqReplayService> _logger;

    public DlqReplayService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<DlqReplayService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Replays up to <paramref name="count"/> messages from the error queue
    /// back to the original queue for reprocessing.
    /// </summary>
    public async Task<ReplayResult> ReplayAsync(string queue, int count = 10, CancellationToken ct = default)
    {
        var client = CreateManagementClient();
        var errorQueue = queue.EndsWith("_error", StringComparison.Ordinal) ? queue : $"{queue}_error";
        var targetQueue = errorQueue.Replace("_error", "", StringComparison.Ordinal);
        count = Math.Min(count, 100);

        var vhost = Uri.EscapeDataString(_config["RabbitMQ:VHost"] ?? "/");

        // Fetch messages from error queue (ack_requeue_false = consume them)
        var fetchUrl = $"/api/queues/{vhost}/{Uri.EscapeDataString(errorQueue)}/get";
        var fetchPayload = JsonSerializer.Serialize(new { count, ackmode = "ack_requeue_false", encoding = "auto" });
        var fetchContent = new StringContent(fetchPayload, System.Text.Encoding.UTF8, "application/json");
        var fetchResp = await client.PostAsync(fetchUrl, fetchContent, ct);

        if (!fetchResp.IsSuccessStatusCode)
        {
            _logger.LogWarning("DLQ replay: failed to fetch from {Queue}: {Status}", errorQueue, fetchResp.StatusCode);
            return new ReplayResult(0, 0, errorQueue, targetQueue, $"Fetch failed: {fetchResp.StatusCode}");
        }

        var body = await fetchResp.Content.ReadAsStringAsync(ct);
        var messages = JsonSerializer.Deserialize<JsonElement[]>(body) ?? [];

        // Republish each message to the default exchange with the original queue as routing key
        var replayed = 0;
        foreach (var msg in messages)
        {
            var payload = msg.TryGetProperty("payload", out var p) ? p.GetString() ?? "" : "";
            var publishUrl = $"/api/exchanges/{vhost}/{Uri.EscapeDataString("")}/publish";
            var publishPayload = JsonSerializer.Serialize(new
            {
                routing_key = targetQueue,
                payload,
                payload_encoding = "string",
                properties = new { }
            });
            var pubContent = new StringContent(publishPayload, System.Text.Encoding.UTF8, "application/json");
            var pubResp = await client.PostAsync(publishUrl, pubContent, ct);
            if (pubResp.IsSuccessStatusCode) replayed++;
        }

        _logger.LogInformation("DLQ replay: {Replayed}/{Total} messages from {From} → {To}",
            replayed, messages.Length, errorQueue, targetQueue);

        return new ReplayResult(replayed, messages.Length, errorQueue, targetQueue, null);
    }

    /// <summary>Lists error queues that have messages.</summary>
    public async Task<IReadOnlyList<ErrorQueueInfo>> ListErrorQueuesAsync(CancellationToken ct = default)
    {
        var client = CreateManagementClient();
        var vhost = Uri.EscapeDataString(_config["RabbitMQ:VHost"] ?? "/");

        var resp = await client.GetAsync($"/api/queues/{vhost}", ct);
        if (!resp.IsSuccessStatusCode) return [];

        var body = await resp.Content.ReadAsStringAsync(ct);
        var queues = JsonSerializer.Deserialize<JsonElement[]>(body) ?? [];

        return queues
            .Where(q => q.TryGetProperty("name", out var n) &&
                        (n.GetString()?.EndsWith("_error", StringComparison.Ordinal) ?? false))
            .Select(q => new ErrorQueueInfo(
                q.GetProperty("name").GetString() ?? "",
                q.TryGetProperty("messages", out var m) ? m.GetInt32() : 0))
            .Where(q => q.MessageCount > 0)
            .ToList();
    }

    private HttpClient CreateManagementClient()
    {
        var client = _httpClientFactory.CreateClient("RabbitMqManagement");
        var url = _config["RabbitMQ:ManagementUrl"]
            ?? throw new InvalidOperationException("RabbitMQ:ManagementUrl configuration is required for DLQ replay");
        var user = _config["RabbitMQ:ManagementUser"] ?? "guest";
        var pass = _config["RabbitMQ:ManagementPassword"] ?? "guest";

        client.BaseAddress = new Uri(url);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{user}:{pass}")));

        return client;
    }
}

public sealed record ReplayResult(int Replayed, int Total, string FromQueue, string ToQueue, string? Error);
public sealed record ErrorQueueInfo(string QueueName, int MessageCount);
