using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace Haworks.BuildingBlocks.Testing.Containers;

/// <summary>
/// Lazy-singleton Svix container shared across webhook integration tests.
/// Requires a Postgres connection string (from SharedTestPostgres) for its backing store.
/// </summary>
public static class SharedTestSvix
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static IContainer? _container;
    private static int _mappedPort;

    public static async Task<(string BaseUrl, int Port)> GetAsync(string postgresConnStr, string jwtSecret = "test-svix-jwt-secret")
    {
        if (_container is { State: TestcontainersStates.Running })
            return ($"http://localhost:{_mappedPort}", _mappedPort);

        if (!await _gate.WaitAsync(TimeSpan.FromMinutes(3)))
            throw new TimeoutException("SharedTestSvix container gate timed out");

        try
        {
            if (_container is null)
            {
                _container = new ContainerBuilder()
                    .WithImage("svix/svix-server:v1.62")
                    .WithPortBinding(8071, true)
                    .WithEnvironment("SVIX_DB_DSN", postgresConnStr)
                    .WithEnvironment("SVIX_JWT_SECRET", jwtSecret)
                    .WithEnvironment("SVIX_QUEUE_TYPE", "memory")
                    .WithEnvironment("SVIX_CACHE_TYPE", "memory")
                    .WithReuse(true)
                    .WithLabel("haworks.reuse-id", "svix-test")
                    .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                        r.ForPath("/api/v1/health/").ForPort(8071)))
                    .Build();

                await _container.StartAsync();
            }

            _mappedPort = _container.GetMappedPublicPort(8071);
            return ($"http://localhost:{_mappedPort}", _mappedPort);
        }
        finally
        {
            _gate.Release();
        }
    }
}
