namespace MatchMaking.Service.Options;

public sealed class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;
    public string KeyPrefix { get; set; } = "ratelimit:";
    public int WindowMilliseconds { get; set; } = 100;
}
