using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Haworks.BffWeb.Api.Controllers;

/// <summary>
/// BFF passthrough to <c>ai-svc</c> (Python FastAPI). Forwards requests
/// to the internal AI service and returns responses verbatim. The streaming
/// chat endpoint pipes the SSE response directly without buffering.
/// </summary>
[ApiController]
[Route("api/v{version:apiVersion}/ai")]
public sealed class AiController : ControllerBase
{
    private readonly IHttpClientFactory _httpFactory;

    public AiController(IHttpClientFactory httpFactory)
    {
        _httpFactory = httpFactory;
    }

    [HttpPost("search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> SemanticSearch([FromBody] object body, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient(BackendClients.Ai);
        using var upstream = await http.PostAsJsonAsync("/api/v1/ai/search", body, ct);
        var content = await upstream.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
            Content = content,
        };
    }

    [HttpPost("chat/message")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task ChatMessage([FromBody] object body, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient(BackendClients.Ai);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/ai/chat/message")
        {
            Content = JsonContent.Create(body),
        };

        using var upstream = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        Response.StatusCode = (int)upstream.StatusCode;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await using var stream = await upstream.Content.ReadAsStreamAsync(ct);
        await stream.CopyToAsync(Response.Body, ct);
    }

    [HttpGet("recommendations/{userId}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Recommendations(string userId, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient(BackendClients.Ai);
        using var upstream = await http.GetAsync($"/api/v1/ai/recommendations/{userId}", ct);
        var content = await upstream.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
            Content = content,
        };
    }

    [HttpPost("content/generate")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerateContent([FromBody] object body, CancellationToken ct = default)
    {
        var http = _httpFactory.CreateClient(BackendClients.Ai);
        using var upstream = await http.PostAsJsonAsync("/api/v1/ai/content/generate", body, ct);
        var content = await upstream.Content.ReadAsStringAsync(ct);
        return new ContentResult
        {
            StatusCode = (int)upstream.StatusCode,
            ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
            Content = content,
        };
    }
}
