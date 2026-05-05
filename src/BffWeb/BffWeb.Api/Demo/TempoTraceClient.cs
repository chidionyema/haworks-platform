using System.Text.Json;
using System.Text.Json.Serialization;
using Haworks.BffWeb.Application.Interfaces;

namespace Haworks.BffWeb.Api.Demo;

public class TempoTraceClient(HttpClient httpClient, ILogger<TempoTraceClient> logger)
{
    public async Task<DemoTrace?> GetTraceAsync(string traceId, CancellationToken ct)
    {
        try
        {
            var response = await httpClient.GetAsync($"/api/traces/{traceId}", ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(content);
            
            if (!doc.RootElement.TryGetProperty("batches", out var batches) || batches.GetArrayLength() == 0)
                return null;

            var spans = new List<DemoSpan>();
            string? rootSpanId = null;
            long rootDuration = 0;

            foreach (var batch in batches.EnumerateArray())
            {
                if (!batch.TryGetProperty("scopeSpans", out var scopeSpans)) continue;
                
                foreach (var scope in scopeSpans.EnumerateArray())
                {
                    if (!scope.TryGetProperty("spans", out var scopeSpansArray)) continue;
                    
                    foreach (var span in scopeSpansArray.EnumerateArray())
                    {
                        var spanId = span.GetProperty("spanId").GetString() ?? "";
                        var parentId = span.TryGetProperty("parentSpanId", out var pid) ? pid.GetString() : null;
                        var name = span.GetProperty("name").GetString() ?? "unknown";
                        var startTimeUnixNano = span.GetProperty("startTimeUnixNano").GetString();
                        var endTimeUnixNano = span.GetProperty("endTimeUnixNano").GetString();
                        
                        var startMs = long.Parse(startTimeUnixNano!) / 1_000_000;
                        var endMs = long.Parse(endTimeUnixNano!) / 1_000_000;
                        var durationMs = endMs - startMs;

                        var serviceName = "unknown";
                        if (batch.TryGetProperty("resource", out var resource) && 
                            resource.TryGetProperty("attributes", out var attrs))
                        {
                            foreach (var attr in attrs.EnumerateArray())
                            {
                                if (attr.GetProperty("key").GetString() == "service.name")
                                {
                                    serviceName = attr.GetProperty("value").GetProperty("stringValue").GetString() ?? "unknown";
                                    break;
                                }
                            }
                        }

                        var status = "OK";
                        if (span.TryGetProperty("status", out var statusObj) && 
                            statusObj.TryGetProperty("code", out var code) && 
                            code.GetInt32() == 2)
                        {
                            status = "Error";
                        }

                        var attributes = new Dictionary<string, object>();
                        if (span.TryGetProperty("attributes", out var spanAttrs))
                        {
                            foreach (var attr in spanAttrs.EnumerateArray())
                            {
                                var key = attr.GetProperty("key").GetString()!;
                                var valObj = attr.GetProperty("value");
                                if (valObj.TryGetProperty("stringValue", out var s)) attributes[key] = s.GetString()!;
                                else if (valObj.TryGetProperty("intValue", out var i)) attributes[key] = i.GetString()!;
                                else if (valObj.TryGetProperty("boolValue", out var b)) attributes[key] = b.GetBoolean();
                                else attributes[key] = valObj.ToString();
                            }
                        }

                        if (string.IsNullOrEmpty(parentId))
                        {
                            rootSpanId = spanId;
                            rootDuration = durationMs;
                        }

                        spans.Add(new DemoSpan(spanId, parentId, serviceName, name, startMs, durationMs, status, attributes));
                    }
                }
            }

            if (rootSpanId == null) return null;

            var rootStart = spans.First(s => s.SpanId == rootSpanId).StartMs;
            var normalizedSpans = spans.Select(s => s with { StartMs = s.StartMs - rootStart }).ToList();

            return new DemoTrace(traceId, rootSpanId, rootDuration, normalizedSpans);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to fetch trace {TraceId} from Tempo", traceId);
            return null;
        }
    }
}
