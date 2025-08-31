using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MatchMaking.Worker.Services;

public class WorkerHeartbeat : BackgroundService
{
    private readonly ILogger<WorkerHeartbeat> _logger;
    private readonly IConnectionMultiplexer _redis;

    public WorkerHeartbeat(ILogger<WorkerHeartbeat> logger, IConnectionMultiplexer redis)
    {
        _logger = logger;
        _redis = redis;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var db = _redis.GetDatabase();
        var host = Environment.MachineName;
        var key = $"worker:{host}:heartbeat";

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await db.StringSetAsync(key, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), expiry: TimeSpan.FromSeconds(45));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat update failed");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (TaskCanceledException) { }
        }
    }
}
