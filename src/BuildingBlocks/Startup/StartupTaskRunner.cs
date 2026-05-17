using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Haworks.BuildingBlocks.Startup;

/// <summary>
/// Runs startup tasks (migrations, Vault init, etc.) in the background.
/// Services start serving immediately; /health/ready reflects completion.
/// </summary>
public sealed class StartupTaskRunner : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupTaskRunner> _logger;
    private readonly List<Func<IServiceProvider, CancellationToken, Task>> _tasks = new();
    private volatile bool _isReady;

    public bool IsReady => _isReady;

    public StartupTaskRunner(IServiceProvider serviceProvider, ILogger<StartupTaskRunner> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public void AddTask(Func<IServiceProvider, CancellationToken, Task> task) => _tasks.Add(task);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var retryPolicy = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                Delay = TimeSpan.FromSeconds(5),
                OnRetry = args =>
                {
                    _logger.LogWarning(args.Outcome.Exception,
                        "Startup task failed — retrying in 5s (attempt {Attempt})",
                        args.AttemptNumber + 1);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();

        foreach (var task in _tasks)
        {
            await retryPolicy.ExecuteAsync(
                async ct => await task(_serviceProvider, ct),
                stoppingToken);
        }
        _isReady = true;
        _logger.LogInformation("All startup tasks completed — service is ready");
    }
}
