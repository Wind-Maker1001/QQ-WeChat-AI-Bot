namespace QQAIBot.Desktop.Models;

public sealed class DesktopActivityState
{
    public int Version { get; set; } = 1;

    public List<BackendRecentActivityItem> QqRecentActivities { get; set; } = [];

    public List<BackendRecentActivityItem> WechatRecentActivities { get; set; } = [];

    public string SelectedQqEventKey { get; set; } = string.Empty;

    public string SelectedWechatEventKey { get; set; } = string.Empty;

    public bool PinSelectedQqActivity { get; set; }

    public bool PinSelectedWechatActivity { get; set; }

    public bool ShowOnlyQqFailures { get; set; }

    public bool ShowOnlyWechatFailures { get; set; }
}
