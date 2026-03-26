namespace QQAIBot.Desktop.Models;

public sealed record BackendControlApiPollState
{
    public int ConsecutiveFailures { get; init; }

    public bool OutageNotified { get; init; }

    public bool UnauthorizedNotified { get; init; }
}
