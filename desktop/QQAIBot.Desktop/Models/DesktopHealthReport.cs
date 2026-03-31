namespace QQAIBot.Desktop.Models;

public enum DesktopHealthState
{
    Info = 0,
    Good = 1,
    Warning = 2,
    Error = 3
}

public sealed record DesktopHealthCheckItem
{
    public string Key { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public DesktopHealthState State { get; init; } = DesktopHealthState.Info;

    public string StateText { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public bool IsBlocking { get; init; }

    public string ActionLabel { get; init; } = string.Empty;

    public string ActionKey { get; init; } = string.Empty;
}

public sealed record DesktopGuideStepItem
{
    public string StepNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string StatusText { get; init; } = string.Empty;

    public bool IsComplete { get; init; }

    public bool IsCurrent { get; init; }

    public string ActionLabel { get; init; } = string.Empty;

    public string ActionKey { get; init; } = string.Empty;
}

public sealed record DesktopNextActionItem
{
    public string StepNumber { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;

    public bool IsPrimary { get; init; }

    public string ActionLabel { get; init; } = string.Empty;

    public string ActionKey { get; init; } = string.Empty;
}

public sealed record DesktopHealthReport
{
    public DesktopHealthState State { get; init; } = DesktopHealthState.Info;

    public string StateText { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string ChecklistStatus { get; init; } = string.Empty;

    public string ReadyNowText { get; init; } = string.Empty;

    public string PrimaryAction { get; init; } = string.Empty;

    public string PrimaryActionLabel { get; init; } = string.Empty;

    public string PrimaryActionKey { get; init; } = string.Empty;

    public string RuntimeExplanation { get; init; } = string.Empty;

    public string LatestIssue { get; init; } = string.Empty;

    public string LatestIssueActionLabel { get; init; } = string.Empty;

    public string LatestIssueActionKey { get; init; } = string.Empty;

    public string ActionSummary { get; init; } = string.Empty;

    public IReadOnlyList<DesktopNextActionItem> NextActions { get; init; } = [];

    public IReadOnlyList<DesktopHealthCheckItem> Checks { get; init; } = [];
}

public static class DesktopHealthActionKeys
{
    public const string FocusBackendRoot = "focus_backend_root";
    public const string FocusControlApiToken = "focus_control_api_token";
    public const string FocusOpenAiDefaultKey = "focus_openai_default_key";
    public const string FocusOpenAiAdvancedKey = "focus_openai_advanced_key";
    public const string FocusDeepSeekApiKey = "focus_deepseek_api_key";
    public const string FocusNapCatUrl = "focus_napcat_url";
    public const string FocusNapCatToken = "focus_napcat_token";
    public const string FocusWechatUrl = "focus_wechat_url";
    public const string FocusQqFailure = "focus_qq_failure";
    public const string FocusWechatFailure = "focus_wechat_failure";
    public const string FocusLatestActivity = "focus_latest_activity";
    public const string ToggleAutoStart = "toggle_auto_start";
    public const string SaveConfig = "save_config";
    public const string StartBackend = "start_backend";
    public const string ReloadConfig = "reload_config";
    public const string OpenBackendFolder = "open_backend_folder";
    public const string ShowLogs = "show_logs";
}
