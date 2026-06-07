using StackExchange.Redis;
using Testcontainers.Redis;

namespace Haworks.BuildingBlocks.Testing.Containers;

/// <summary>
/// Lazy-singleton Redis container shared across every backend
/// integration assembly. <c>WithReuse(true)</c> means Testcontainers picks
/// the same container across <c>dotnet test</c> invocations as long as the
/// builder config hash is unchanged — keep this builder static and
/// identical, do not parameterize.
/// </summary>
public static class SharedTestRedis
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static RedisContainer? _container;

    private static async Task<RedisContainer> GetAsync()
    {
        if (_container is { State: DotNet.Testcontainers.Containers.TestcontainersStates.Running })
            return _container;
        if (!await _gate.WaitAsync(TimeSpan.FromMinutes(5)))
            throw new TimeoutException("SharedTestRedis container gate timed out after 5 minutes");
        try
        {
            if (_container is null)
            {
                _container = new RedisBuilder()
                    .WithImage("redis:7-alpine")
                    .WithReuse(true)
                    .WithCommand("redis-server", "--protected-mode", "no")
                    .Build();
            }
            if (_container.State != DotNet.Testcontainers.Containers.TestcontainersStates.Running)
                await _container.StartAsync();
            return _container;
        }
        finally { _gate.Release(); }
    }

    /// <summary>
    /// Creates a fresh Redis database index for the service and returns its
    /// connection string. Uses a different database index per service to
    /// provide isolation without needing separate Redis instances.
    /// </summary>
    public static async Task<string> CreateConnectionStringAsync(string serviceName)
    {
        var container = await GetAsync();

        // Use different database indexes for different services
        // Database 0: reserved for general use
        // Database 1+: per-service isolation
        var dbIndex = serviceName switch
        {
            "realtime" => 1,
            "sessions" => 2,
            "cache" => 3,
            _ => throw new ArgumentException($"No Redis database index mapped for service: {serviceName}")
        };

        var connectionString = container.GetConnectionString();

        // Clear keys for this service using pattern deletion (no admin mode required)
        await using var redis = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var db = redis.GetDatabase(dbIndex);
        var server = redis.GetServer(redis.GetEndPoints()[0]);

        // Get all keys in this database and delete them
        // Since we're using separate databases per service, this clears only this service's data
        var keys = server.Keys(dbIndex, pattern: "*").ToArray();
        if (keys.Length > 0)
        {
            await db.KeyDeleteAsync(keys);
        }

        return $"{connectionString},defaultDatabase={dbIndex}";
    }

    /// <summary>
    /// Gets a configured IConnectionMultiplexer for the specified service.
    /// </summary>
    public static async Task<IConnectionMultiplexer> GetConnectionAsync(string serviceName)
    {
        var connectionString = await CreateConnectionStringAsync(serviceName);
        return await ConnectionMultiplexer.ConnectAsync(connectionString);
    }
}