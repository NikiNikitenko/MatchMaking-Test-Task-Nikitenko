using Confluent.Kafka;
using MatchMaking.Worker.Consumers;
using MatchMaking.Worker.Options;
using MatchMaking.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace MatchMaking.Worker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((context, services) =>
                {
                    var configuration = context.Configuration;

                    var redisConn = configuration.GetValue<string>("ConnectionStrings:Redis") ?? "localhost:6379";
                    services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConn));

                    var bootstrap = configuration["Kafka:BootstrapServers"] ?? "kafka:9092";
                    services.AddSingleton<IProducer<Null, string>>(
                        _ => new ProducerBuilder<Null, string>(new ProducerConfig { BootstrapServers = bootstrap }).Build());
                    services.AddSingleton<IConsumer<Ignore, string>>(
                        _ => new ConsumerBuilder<Ignore, string>(new ConsumerConfig
                        {
                            BootstrapServers = bootstrap,
                            GroupId = "matchmaking-worker-requests",
                            AutoOffsetReset = AutoOffsetReset.Earliest,
                            EnableAutoCommit = true
                        }).Build());

                    services.Configure<MatchmakingOptions>(configuration.GetSection("Matchmaking"));

                    services.AddSingleton<IMatchmakingService, MatchmakingService>();
                    services.AddHostedService<MatchMaking.Worker.Services.WorkerHeartbeat>();
                    services.AddHostedService<RequestConsumerHostedService>();
                    services.AddHostedService<Worker>();
                })
                .Build();

            host.Run();
        }
    }
}
