namespace QQAIBot.Desktop.Models;

public sealed record DesktopLatestTurnOverview
{
    public DesktopHealthState State { get; init; } = DesktopHealthState.Info;

    public string Headline { get; init; } = "Latest turn: no recent QQ or WeChat activity yet.";

    public string Summary { get; init; } = "Summary: this console has not captured a recent turn yet.";

    public string Capabilities { get; init; } = "Capabilities: n/a.";

    public string Reason { get; init; } = "Why: n/a.";

    public string Outcome { get; init; } = "Outcome: n/a.";

    public string ActionLabel { get; init; } = string.Empty;

    public string ActionKey { get; init; } = string.Empty;
}
