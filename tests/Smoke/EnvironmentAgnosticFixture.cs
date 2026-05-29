using Xunit;

namespace Haworks.Tests.Smoke;

public sealed class EnvironmentAgnosticFixture : IAsyncLifetime
{
    private HttpClient? _httpClient;

    public HttpClient HttpClient => _httpClient ?? throw new InvalidOperationException("Fixture not initialized");

    public async Task InitializeAsync()
    {
        var targetUrl = Environment.GetEnvironmentVariable("SMOKE_TARGET_URL")
            ?? "https://haworks-bffweb.fly.dev";

        _httpClient = new HttpClient { BaseAddress = new Uri(targetUrl) };
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        await WaitForHealthyAsync(_httpClient, timeout: TimeSpan.FromMinutes(2));
    }

    private static async Task WaitForHealthyAsync(HttpClient client, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.IsCancellationRequested)
        {
            try
            {
                var resp = await client.GetAsync("/health", cts.Token);
                if (resp.IsSuccessStatusCode)
                    return;
            }
            catch (Exception) when (!cts.IsCancellationRequested)
            {
                // Service not ready yet
            }

            await Task.Delay(3000, cts.Token);
        }

        throw new TimeoutException($"BFF did not become healthy within {timeout.TotalSeconds}s");
    }

    public Task DisposeAsync()
    {
        _httpClient?.Dispose();
        return Task.CompletedTask;
    }
}

[CollectionDefinition("Smoke Tests")]
public class SmokeTestCollection : ICollectionFixture<EnvironmentAgnosticFixture> { }
