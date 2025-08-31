namespace MatchMaking.Worker.Services;

public interface IMatchmakingService
{
    Task ProcessQueueAsync(CancellationToken stoppingToken);
}
