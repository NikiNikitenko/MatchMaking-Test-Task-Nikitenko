using Confluent.Kafka;
using MatchMaking.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace MatchMaking.Worker.Consumers
{
    public class RequestConsumerHostedService : BackgroundService
    {
        private readonly IConsumer<Ignore, string> _consumer;
        private readonly IDatabase _db;
        private readonly string _requestTopic;
        private readonly ILogger<RequestConsumerHostedService> _logger;

        public RequestConsumerHostedService(
            IConsumer<Ignore, string> consumer,
            IConnectionMultiplexer redis,
            IConfiguration cfg,
            ILogger<RequestConsumerHostedService> logger)
        {
            _consumer = consumer;
            _db = redis.GetDatabase();
            _requestTopic = cfg["Kafka:RequestTopic"] ?? "matchmaking.request";
            _logger = logger;
        }

        protected override Task ExecuteAsync(CancellationToken stoppingToken)
        {
            return Task.Run(() =>
            {
                _consumer.Subscribe(_requestTopic);
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        var cr = _consumer.Consume(stoppingToken);
                        if (cr == null || cr.Message == null || string.IsNullOrWhiteSpace(cr.Message.Value))
                            continue;

                        using var doc = JsonDocument.Parse(cr.Message.Value);
                        if (!doc.RootElement.TryGetProperty("userId", out var idProp))
                            continue;
                        var userId = idProp.GetString();
                        if (string.IsNullOrWhiteSpace(userId))
                            continue;

                        var entry = JsonSerializer.Serialize(new { PlayerId = userId });
                        _db.ListLeftPush(AppConstants.QueueKey, entry);
                        _logger.LogInformation("Accepted matchmaking request for user {UserId}", userId);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error consuming topic {Topic}", _requestTopic);
                    }
                }
            }, stoppingToken);
        }
    }
}
