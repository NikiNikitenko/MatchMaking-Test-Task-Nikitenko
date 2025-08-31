using Confluent.Kafka;
using MatchMaking.Infrastructure;
using MatchMaking.Worker.Dto;
using MatchMaking.Worker.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace MatchMaking.Worker.Services
{
    public class MatchmakingService : IMatchmakingService
    {
        private readonly ILogger<MatchmakingService> _logger;
        private readonly IDatabase _redisDb;
        private readonly IProducer<Null, string> _kafkaProducer;
        private readonly int _playersPerMatch;
        private readonly string _lockKey;
        private readonly TimeSpan _lockTtl;

        public MatchmakingService(
            ILogger<MatchmakingService> logger,
            IConnectionMultiplexer redis,
            IProducer<Null, string> kafkaProducer,
            IOptions<MatchmakingOptions> options)
        {
            _logger = logger;
            _redisDb = redis.GetDatabase();
            _kafkaProducer = kafkaProducer;
            _playersPerMatch = options.Value.PlayersPerMatch;
            _lockKey = options.Value.LockKey ?? "matchmaking_lock";
            _lockTtl = TimeSpan.FromSeconds(options.Value.LockTtlSeconds > 0 ? options.Value.LockTtlSeconds : 5);
        }

        public async Task ProcessQueueAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                string lockId = Guid.NewGuid().ToString("N");
                bool acquiredLock = await _redisDb.StringSetAsync(
                    _lockKey,
                    lockId,
                    _lockTtl,
                    when: When.NotExists);

                if (!acquiredLock)
                {
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                try
                {
                    var players = new List<EnqueuePlayerDto>();
                    var rawPlayerData = new List<RedisValue>();

                    for (int i = 0; i < _playersPerMatch; i++)
                    {
                        RedisValue playerData = RedisValue.Null;

                        for (int j = 0; j < 10 && !playerData.HasValue; j++)
                        {
                            playerData = await _redisDb.ListLeftPopAsync(AppConstants.QueueKey);
                            if (!playerData.HasValue)
                                await Task.Delay(500, stoppingToken);
                        }

                        if (!playerData.HasValue)
                            break;

                        rawPlayerData.Add(playerData);

                        try
                        {
                            var json = playerData.ToString();
                            if (!string.IsNullOrWhiteSpace(json))
                            {
                                var player = JsonSerializer.Deserialize<EnqueuePlayerDto>(json);
                                if (player != null)
                                    players.Add(player);
                                else
                                    _logger.LogWarning("Deserialized player is null");
                            }
                            else
                            {
                                _logger.LogWarning("Redis value for player was empty or null");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to deserialize player");
                        }
                    }

                    if (players.Count != _playersPerMatch)
                    {
                        for (int i = rawPlayerData.Count - 1; i >= 0; i--)
                        {
                            await _redisDb.ListLeftPushAsync(AppConstants.QueueKey, rawPlayerData[i]);
                        }

                        _logger.LogWarning("Not enough players in time, returned {Count} to queue", players.Count);
                        continue;
                    }

                    var matchId = Guid.NewGuid().ToString("N");
                    var userIds = players.Select(p => p.PlayerId).ToList();
                    var match = new
                    {
                        MatchId = matchId,
                        UserIds = userIds,
                        CreatedAt = DateTime.UtcNow
                    };

                    var matchJson = JsonSerializer.Serialize(match);

                    await _kafkaProducer.ProduceAsync(
                        AppConstants.CompleteTopic,
                        new Message<Null, string> { Value = matchJson },
                        stoppingToken);

                    foreach (var player in players)
                    {
                        string userMatchKey = $"user:{player.PlayerId}:match";
                        string matchHistoryKey = $"match:history:{player.PlayerId}";

                        await _redisDb.StringSetAsync(userMatchKey, matchId, TimeSpan.FromMinutes(10));
                        await _redisDb.ListRightPushAsync(matchHistoryKey, matchJson);
                    }

                    await _redisDb.StringSetAsync($"match:{matchId}", JsonSerializer.Serialize(userIds));

                    _logger.LogInformation("Match created between: {Players}", string.Join(", ", userIds));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to create and store match");
                }
                finally
                {
                    var currentLockValue = await _redisDb.StringGetAsync(_lockKey);
                    if (currentLockValue == lockId)
                    {
                        await _redisDb.KeyDeleteAsync(_lockKey);
                    }
                }
            }
        }
    }
}
