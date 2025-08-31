using MatchMaking.Service.Options;
using MatchMaking.Service.Services;
using MatchMaking.Worker.Dto;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MatchMaking.Service.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MatchmakingController : ControllerBase
    {
        private readonly IMatchmakingRequestService _service;
        private readonly ILogger<MatchmakingController> _logger;
        private readonly bool _rateLimitEnabled;
        private readonly string _rateLimitPrefix;
        private readonly int _rateLimitWindowMs;
        private readonly IDatabase _redisDb;

        public MatchmakingController(
            IMatchmakingRequestService service,
            ILogger<MatchmakingController> logger,
            IOptions<RateLimitingOptions>? rateOptions,
            IConnectionMultiplexer redis)
        {
            _service = service;
            _logger = logger;

            var ro = rateOptions?.Value ?? new RateLimitingOptions();
            _rateLimitEnabled = ro.Enabled;
            _rateLimitPrefix = ro.KeyPrefix ?? "ratelimit:";
            _rateLimitWindowMs = ro.WindowMilliseconds > 0 ? ro.WindowMilliseconds : 100;

            _redisDb = redis.GetDatabase();
        }

        [HttpPost("search")]
        public async Task<IActionResult> Search([FromBody] EnqueuePlayerDto dto)
        {
            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(dto.PlayerId))
                return ValidationProblem(ModelState);

            var id = dto.PlayerId;

            if (_rateLimitEnabled)
            {
                var key = $"{_rateLimitPrefix}{id}";
                var ok = await _redisDb.StringSetAsync(
                    key,
                    "1",
                    expiry: TimeSpan.FromMilliseconds(_rateLimitWindowMs),
                    when: When.NotExists);

                if (!ok)
                {
                    return StatusCode(429);
                }
            }

            await _service.EnqueueAsync(id);
            return NoContent();
        }

        [HttpGet("match/{userId}")]
        public async Task<IActionResult> GetMatch([FromRoute] string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return BadRequest();
            var result = await _service.GetMatchForUserAsync(userId);
            if (result is null) return NotFound();
            return Ok(result);
        }
    }
}
