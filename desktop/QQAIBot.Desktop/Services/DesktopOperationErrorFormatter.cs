using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopOperationErrorFormatter
{
    public static DesktopUserFacingOperationError Build(
        string operationLabel,
        string fallbackStatusText,
        string technicalMessage,
        BackendControlApiFailure? controlApiFailure = null,
        string? envPath = null,
        bool localControlSettingsOperation = false)
    {
        if (localControlSettingsOperation)
        {
            return new DesktopUserFacingOperationError
            {
                StatusText = fallbackStatusText,
                DialogTitle = $"{operationLabel} failed",
                DialogMessage = string.Join(
                    Environment.NewLine,
                    BuildLines(
                        $"{operationLabel} could not update this machine's local desktop attachment settings.",
                        "Check whether the .env file is writable, then retry.",
                        string.IsNullOrWhiteSpace(envPath) ? null : $"Target file: {envPath}",
                        $"Technical detail: {DefaultIfBlank(technicalMessage, "No extra error detail was captured.")}")),
                SuggestedActionLabel = "Go to local token",
                SuggestedActionKey = DesktopHealthActionKeys.FocusControlApiToken
            };
        }

        var failure = controlApiFailure ?? new BackendControlApiFailure();
        var detail = DefaultIfBlank(failure.Message, technicalMessage);

        return failure.Kind switch
        {
            BackendControlApiFailureKind.Unauthorized => new DesktopUserFacingOperationError
            {
                StatusText = $"{fallbackStatusText}: local control token mismatch",
                DialogTitle = "Local control token required",
                DialogMessage = string.Join(
                    Environment.NewLine,
                    BuildLines(
                        $"{operationLabel} could not continue because desktop authentication to the local control API failed.",
                        "Open 本机连接设置 in this window and make QQ_AI_BOT_CONTROL_API_TOKEN match the backend .env, then retry.",
                        string.IsNullOrWhiteSpace(envPath) ? null : $"Local config file: {envPath}",
                        $"Technical detail: {detail}")),
                SuggestedActionLabel = "Go to local token",
                SuggestedActionKey = DesktopHealthActionKeys.FocusControlApiToken
            },
            BackendControlApiFailureKind.Rejected => new DesktopUserFacingOperationError
            {
                StatusText = $"{fallbackStatusText}: control API rejected the request",
                DialogTitle = $"{operationLabel} rejected",
                DialogMessage = string.Join(
                    Environment.NewLine,
                    BuildLines(
                        $"{operationLabel} was rejected by the local control API.",
                        "Check the field mentioned below, then retry. If the reason is still unclear, reload config and inspect the log panel.",
                        $"Technical detail: {detail}")),
                SuggestedActionLabel = ResolveSuggestedAction(detail).Label,
                SuggestedActionKey = ResolveSuggestedAction(detail).Key
            },
            BackendControlApiFailureKind.Unknown => new DesktopUserFacingOperationError
            {
                StatusText = $"{fallbackStatusText}: unexpected control API response",
                DialogTitle = $"{operationLabel} returned an unexpected response",
                DialogMessage = string.Join(
                    Environment.NewLine,
                    BuildLines(
                        $"{operationLabel} reached the local control API, but the response could not be understood.",
                        "Reload config and retry. If this keeps happening, inspect the log panel for backend details.",
                        $"Technical detail: {detail}")),
                SuggestedActionLabel = "Show logs",
                SuggestedActionKey = DesktopHealthActionKeys.ShowLogs
            },
            BackendControlApiFailureKind.Unreachable => new DesktopUserFacingOperationError
            {
                StatusText = $"{fallbackStatusText}: control API not reachable",
                DialogTitle = $"{operationLabel} could not reach the local control API",
                DialogMessage = string.Join(
                    Environment.NewLine,
                    BuildLines(
                        $"{operationLabel} could not continue because the desktop is not attached to a live control API.",
                        "Start or recover the backend first, then retry from this window.",
                        $"Technical detail: {detail}")),
                SuggestedActionLabel = "Show logs",
                SuggestedActionKey = DesktopHealthActionKeys.ShowLogs
            },
            _ => new DesktopUserFacingOperationError
            {
                StatusText = fallbackStatusText,
                DialogTitle = $"{operationLabel} failed",
                DialogMessage = string.Join(
                    Environment.NewLine,
                    BuildLines(
                        $"{operationLabel} failed.",
                        $"Technical detail: {DefaultIfBlank(technicalMessage, "No extra error detail was captured.")}")),
                SuggestedActionLabel = "Show logs",
                SuggestedActionKey = DesktopHealthActionKeys.ShowLogs
            }
        };
    }

    private static (string Label, string Key) ResolveSuggestedAction(string detail)
    {
        if (detail.Contains("WECHAT_BRIDGE_URL", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("WECHAT_BRIDGE_TOKEN", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("WECHAT_BOT_PREFIX", StringComparison.OrdinalIgnoreCase))
        {
            return ("Go to WeChat config", DesktopHealthActionKeys.FocusWechatUrl);
        }

        if (detail.Contains("NAPCAT_WS_URL", StringComparison.OrdinalIgnoreCase))
        {
            return ("Go to NapCat URL", DesktopHealthActionKeys.FocusNapCatUrl);
        }

        if (detail.Contains("NAPCAT_TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return ("Go to NapCat token", DesktopHealthActionKeys.FocusNapCatToken);
        }

        if (detail.Contains("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_DEFAULT_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_DEFAULT_MODEL", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_ADVANCED_MODEL", StringComparison.OrdinalIgnoreCase))
        {
            return ("Go to API keys", DesktopHealthActionKeys.FocusOpenAiDefaultKey);
        }

        if (detail.Contains("QQ_AI_BOT_CONTROL_API_TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return ("Go to local token", DesktopHealthActionKeys.FocusControlApiToken);
        }

        return ("Show logs", DesktopHealthActionKeys.ShowLogs);
    }

    private static IReadOnlyList<string> BuildLines(params string?[] lines) =>
        lines.Where(static line => !string.IsNullOrWhiteSpace(line)).Cast<string>().ToArray();

    private static string DefaultIfBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
