namespace MatchMaking.Worker.Options;

public class MatchmakingOptions
{
    public int PlayersPerMatch { get; set; } = 3;
    public string? LockKey { get; set; }
    public int LockTtlSeconds { get; set; } = 5;

}
