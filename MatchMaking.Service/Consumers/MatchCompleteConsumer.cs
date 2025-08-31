using Confluent.Kafka;
using MatchMaking.Infrastructure;
using MatchMaking.Worker.Dto;
using StackExchange.Redis;
using System.Text.Json;

namespace MatchMaking.Service.Consumers
{
    public class MatchCompleteConsumer : BackgroundService
    {
        private readonly ILogger<MatchCompleteConsumer> _logger;
        private readonly IConsumer<Ignore, string> _consumer;
        private readonly IDatabase _redisDb;
        private readonly string _completeTopic;

        public MatchCompleteConsumer(
            ILogger<MatchCompleteConsumer> logger,
            IConsumer<Ignore, string> consumer,
            IConnectionMultiplexer redis,
            IConfiguration cfg)
        {
            _logger = logger;
            _consumer = consumer;
            _redisDb = redis.GetDatabase();
            _completeTopic = cfg["Kafka:CompleteTopic"] ?? AppConstants.CompleteTopic;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Run(() => ConsumeMatches(stoppingToken), stoppingToken);
        }

        private void ConsumeMatches(CancellationToken stoppingToken)
        {
            _consumer.Subscribe(_completeTopic);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var result = _consumer.Consume(stoppingToken);
                        var payload = result.Message?.Value;

                        if (string.IsNullOrWhiteSpace(payload))
                            continue;

                        var match = JsonSerializer.Deserialize<MatchDto>(payload);
                        if (match?.UserIds == null || match.UserIds.Count == 0)
                        {
                            _logger.LogWarning("Invalid match payload");
                            continue;
                        }

                        var matchIdStr = match.MatchId.ToString("N");
                        var tasks = new List<Task>();

                        foreach (var uid in match.UserIds.Where(u => !string.IsNullOrWhiteSpace(u)))
                            tasks.Add(_redisDb.StringSetAsync($"user:{uid}:match", matchIdStr, TimeSpan.FromMinutes(10)));

                        var createdAt = DateTimeOffset.UtcNow.ToString("O");
                        tasks.Add(_redisDb.HashSetAsync($"match:{matchIdStr}", new HashEntry[]
                        {
                            new HashEntry("createdAt", createdAt)
                        }));

                        var listKey = $"match:{matchIdStr}:users";
                        tasks.Add(_redisDb.KeyDeleteAsync(listKey));
                        foreach (var uid in match.UserIds)
                            tasks.Add(_redisDb.ListRightPushAsync(listKey, uid));


                        Task.WaitAll(tasks.ToArray(), stoppingToken);
                        _logger.LogInformation("Match {MatchId} stored for {Count} users", matchIdStr, match.UserIds.Count);
                    }
                    catch (ConsumeException ex)
                    {
                        _logger.LogError(ex, "Kafka consume error");
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unexpected error storing match");
                    }
                }
            }
            finally
            {
                _consumer.Close();
            }
        }
    }
}
