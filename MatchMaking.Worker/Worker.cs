using MatchMaking.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MatchMaking.Worker
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IMatchmakingService _matchmakingService;

        public Worker(ILogger<Worker> logger, IMatchmakingService matchmakingService)
        {
            _logger = logger;
            _matchmakingService = matchmakingService;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Worker started at: {time}", DateTimeOffset.Now);
            await _matchmakingService.ProcessQueueAsync(stoppingToken);
        }
    }
}
