using MatchMaking.Infrastructure;
using MatchMaking.Service.Consumers;
using MatchMaking.Service.Options;
using MatchMaking.Service.Services;
using Microsoft.OpenApi.Models;
using Serilog;

namespace MatchMaking.Service
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                var builder = WebApplication.CreateBuilder(args);

                builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

                var config = builder.Configuration;

                builder.Services.Configure<RateLimitingOptions>(config.GetSection("RateLimiting"));

                builder.Services.AddControllers();
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo { Title = "MatchMaking.Service", Version = "v1" });
                });

                builder.Services.AddRedis(config)
                                .AddKafkaProducer(config)
                                .AddKafkaConsumer(config, groupId: "matchmaking-service");

                builder.Services.AddSingleton<IMatchmakingRequestService, MatchmakingRequestService>();

                builder.Services.AddHostedService<MatchCompleteConsumer>();

                builder.Services.AddHealthChecks();

                var app = builder.Build();

                if (app.Environment.IsDevelopment())
                {
                    app.UseSwagger();
                    app.UseSwaggerUI();
                }

                app.MapControllers();
                app.MapHealthChecks("/health");

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Host terminated unexpectedly");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
