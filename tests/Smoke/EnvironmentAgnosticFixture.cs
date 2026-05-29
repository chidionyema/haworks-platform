using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Haworks.Tests.Smoke;

public sealed class EnvironmentAgnosticFixture : IAsyncLifetime
{
    private IDistributedApplicationTestingBuilder? _appBuilder;
    private DistributedApplication? _app;
    private HttpClient? _httpClient;

    public HttpClient HttpClient => _httpClient ?? throw new InvalidOperationException("Fixture not initialized");

    public async Task InitializeAsync()
    {
        var targetUrl = Environment.GetEnvironmentVariable("SMOKE_TARGET_URL");

        if (!string.IsNullOrEmpty(targetUrl))
        {
            _httpClient = new HttpClient { BaseAddress = new Uri(targetUrl) };
        }
        else
        {
            _appBuilder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.HaworksPlatform_AppHost>();
            _app = await _appBuilder.BuildAsync();
            await _app.StartAsync();

            _httpClient = _app.CreateHttpClient("bff-web");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Wait for BFF to become healthy before running tests.
            // The full service graph (Postgres, RabbitMQ, 14 services)
            // can take 2-3 minutes to bootstrap on CI runners.
            await WaitForHealthyAsync(_httpClient, timeout: TimeSpan.FromMinutes(5));
        }
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

    public async Task DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        _httpClient?.Dispose();
    }
}

[CollectionDefinition("Smoke Tests")]
public class SmokeTestCollection : ICollectionFixture<EnvironmentAgnosticFixture> { }
