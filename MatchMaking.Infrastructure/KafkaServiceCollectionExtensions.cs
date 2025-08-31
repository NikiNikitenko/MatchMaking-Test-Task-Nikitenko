using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MatchMaking.Infrastructure;

public static class KafkaServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaProducer(this IServiceCollection services, IConfiguration cfg)
    {
        var bootstrap = cfg["Kafka:BootstrapServers"] ?? "kafka:9092";
        return services.AddSingleton<IProducer<Null, string>>(
            _ => new ProducerBuilder<Null, string>(new ProducerConfig { BootstrapServers = bootstrap }).Build());
    }

    public static IServiceCollection AddKafkaConsumer(this IServiceCollection services, IConfiguration cfg, string groupId)
    {
        var bootstrap = cfg["Kafka:BootstrapServers"] ?? "kafka:9092";
        return services.AddSingleton<IConsumer<Ignore, string>>(
            _ => new ConsumerBuilder<Ignore, string>(new ConsumerConfig
            {
                BootstrapServers = bootstrap,
                GroupId = groupId,
                AutoOffsetReset = AutoOffsetReset.Earliest
            }).Build());
    }
}
