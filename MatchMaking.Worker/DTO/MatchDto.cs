namespace MatchMaking.Worker.Dto;

public record MatchDto
{
    public Guid MatchId { get; init; } = default!;
    public List<string> UserIds { get; init; } = new();
}
