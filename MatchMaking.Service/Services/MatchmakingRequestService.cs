using Confluent.Kafka;
using MatchMaking.Infrastructure;
using StackExchange.Redis;
using System.Text.Json;

namespace MatchMaking.Service.Services;

public sealed class MatchmakingRequestService : IMatchmakingRequestService
{
    private readonly ILogger<MatchmakingRequestService> _logger;
    private readonly IProducer<Null, string> _producer;
    private readonly IDatabase _db;
    private readonly string _requestTopic;

    public MatchmakingRequestService(
        ILogger<MatchmakingRequestService> logger,
        IProducer<Null, string> producer,
        IConnectionMultiplexer redis,
        IConfiguration cfg)
    {
        _logger = logger;
        _producer = producer;
        _db = redis.GetDatabase();
        _requestTopic = cfg["Kafka:RequestTopic"] ?? AppConstants.RequestTopic;
    }

    public async Task EnqueueAsync(string userId, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new { userId });
        await _producer.ProduceAsync(_requestTopic, new Message<Null, string> { Value = payload }, ct);
        _logger.LogInformation("Enqueued matchmaking request for user {UserId}", userId);
    }

    public async Task<object?> GetMatchForUserAsync(string userId, CancellationToken ct = default)
    {
        var matchId = await _db.StringGetAsync($"user:{userId}:match");
        if (matchId.HasValue && !string.IsNullOrWhiteSpace(matchId))
        {
            var matchIdStr = (string)matchId!;

            var usersList = await _db.ListRangeAsync($"match:{matchIdStr}:users", 0, -1);
            if (usersList is { Length: > 0 })
            {
                var userIds = usersList.Select(u => (string)u!).ToArray();
                return new { matchId = matchIdStr, userIds };
            }

            var usersRaw = await _db.StringGetAsync($"match:{matchIdStr}");
            if (usersRaw.HasValue && !string.IsNullOrWhiteSpace(usersRaw))
            {
                try
                {
                    var userIds = JsonSerializer.Deserialize<string[]>(usersRaw!) ?? Array.Empty<string>();
                    if (userIds.Length > 0)
                        return new { matchId = matchIdStr, userIds };
                }
                catch
                {
                }
            }
        }

        return null;
    }
}
