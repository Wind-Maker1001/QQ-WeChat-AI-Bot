namespace QQAIBot.Desktop.Models;

public sealed record DesktopLatestTurnOverview
{
    public DesktopHealthState State { get; init; } = DesktopHealthState.Info;

    public string Headline { get; init; } = "最新一轮：还没有最近的 QQ 或微信活动。";

    public string Summary { get; init; } = "Summary: this console has not captured a recent turn yet.";

    public string Capabilities { get; init; } = "能力：暂无。";

    public string Reason { get; init; } = "原因：暂无。";

    public string Outcome { get; init; } = "结果：暂无。";

    public string ActionLabel { get; init; } = string.Empty;

    public string ActionKey { get; init; } = string.Empty;
}
