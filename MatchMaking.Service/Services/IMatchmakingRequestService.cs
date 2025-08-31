namespace MatchMaking.Service.Services;

public interface IMatchmakingRequestService
{
    Task EnqueueAsync(string userId, CancellationToken ct = default);
    Task<object?> GetMatchForUserAsync(string userId, CancellationToken ct = default);
}
