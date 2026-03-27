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
        bool localControlSettingsOperation = false,
        bool canStartBackend = false)
    {
        var detail = DefaultIfBlank(controlApiFailure?.Message, technicalMessage);

        if (localControlSettingsOperation)
        {
            return new DesktopUserFacingOperationError
            {
                StatusText = fallbackStatusText,
                DialogTitle = $"{operationLabel} failed",
                DialogMessage = BuildDialogMessage(
                    whatHappened: $"{operationLabel} could not update this machine's local desktop attachment settings.",
                    whyText: "This action only changes how the desktop reattaches to the local control API. It does not change QQ, WeChat, or model settings by itself.",
                    doNowText: "Check whether the backend .env file is writable, then save the local control settings again.",
                    improvementText: "This window can reattach to the local runtime again without changing the channel config.",
                    technicalDetail: detail,
                    additionalContext:
                    [
                        string.IsNullOrWhiteSpace(envPath) ? null : $"Target file: {envPath}"
                    ]),
                SuggestedActionLabel = "Go to local token",
                SuggestedActionKey = DesktopHealthActionKeys.FocusControlApiToken
            };
        }

        var failure = controlApiFailure ?? new BackendControlApiFailure();

        return failure.Kind switch
        {
            BackendControlApiFailureKind.Unauthorized => BuildUnauthorizedError(
                operationLabel,
                fallbackStatusText,
                detail,
                envPath),
            BackendControlApiFailureKind.Rejected => BuildRejectedError(
                operationLabel,
                fallbackStatusText,
                detail),
            BackendControlApiFailureKind.Unknown => BuildUnknownError(
                operationLabel,
                fallbackStatusText,
                detail),
            BackendControlApiFailureKind.Unreachable => BuildUnreachableError(
                operationLabel,
                fallbackStatusText,
                detail,
                canStartBackend),
            _ => new DesktopUserFacingOperationError
            {
                StatusText = fallbackStatusText,
                DialogTitle = $"{operationLabel} failed",
                DialogMessage = BuildDialogMessage(
                    whatHappened: $"{operationLabel} failed.",
                    whyText: "Desktop did not receive enough information to classify the failure cleanly.",
                    doNowText: "Review the log panel, then retry from this window.",
                    improvementText: "The desktop can resume normal control once the failing step succeeds.",
                    technicalDetail: DefaultIfBlank(technicalMessage, "No extra error detail was captured.")),
                SuggestedActionLabel = "Show logs",
                SuggestedActionKey = DesktopHealthActionKeys.ShowLogs
            }
        };
    }

    private static DesktopUserFacingOperationError BuildUnauthorizedError(
        string operationLabel,
        string fallbackStatusText,
        string detail,
        string? envPath)
    {
        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}: local control token mismatch",
            DialogTitle = "Local control token required",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel} could not continue because desktop authentication to the local control API failed.",
                whyText: "The backend .env and this desktop window are using different QQ_AI_BOT_CONTROL_API_TOKEN values.",
                doNowText: "Open the local desktop attachment settings (本机连接设置) in this window, make QQ_AI_BOT_CONTROL_API_TOKEN match the backend .env, then retry.",
                improvementText: "This window can verify live runtime state and send start, stop, load, or save actions again.",
                technicalDetail: detail,
                additionalContext:
                [
                    string.IsNullOrWhiteSpace(envPath) ? null : $"Local config file: {envPath}"
                ]),
            SuggestedActionLabel = "Go to local token",
            SuggestedActionKey = DesktopHealthActionKeys.FocusControlApiToken
        };
    }

    private static DesktopUserFacingOperationError BuildRejectedError(
        string operationLabel,
        string fallbackStatusText,
        string detail)
    {
        var guidance = ResolveRejectedGuidance(detail);

        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}: control API rejected the request",
            DialogTitle = $"{operationLabel} rejected",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel} reached the local control API, but the backend refused the current values.",
                whyText: guidance.WhyText,
                doNowText: $"Check the field mentioned below, then retry. {guidance.NextStepText}",
                improvementText: guidance.ImprovementText,
                technicalDetail: detail),
            SuggestedActionLabel = guidance.Label,
            SuggestedActionKey = guidance.Key
        };
    }

    private static DesktopUserFacingOperationError BuildUnknownError(
        string operationLabel,
        string fallbackStatusText,
        string detail)
    {
        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}: unexpected control API response",
            DialogTitle = $"{operationLabel} returned an unexpected response",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel} reached the local control API, but the response could not be understood.",
                whyText: "Desktop expected a normal control-plane response, but the backend returned something incomplete or unexpected.",
                doNowText: "Reload config from this window, then retry the same action.",
                improvementText: "Desktop can rebuild a clean picture of the runtime before you act again.",
                technicalDetail: detail),
            SuggestedActionLabel = "Reload config",
            SuggestedActionKey = DesktopHealthActionKeys.ReloadConfig
        };
    }

    private static DesktopUserFacingOperationError BuildUnreachableError(
        string operationLabel,
        string fallbackStatusText,
        string detail,
        bool canStartBackend)
    {
        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}: control API not reachable",
            DialogTitle = $"{operationLabel} could not reach the local control API",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel} could not continue because the desktop is not attached to a live control API.",
                whyText: canStartBackend
                    ? "The backend is likely stopped, so this window can only see the last local file state right now."
                    : "The backend may still be recovering or the desktop may be temporarily detached from live runtime state.",
                doNowText: canStartBackend
                    ? "Start the backend from this window, wait for the status to refresh, then retry."
                    : "Reload config after the backend comes back. If it should already be online, inspect the log panel next.",
                improvementText: "Desktop can refresh live state and continue controlling the runtime again.",
                technicalDetail: detail),
            SuggestedActionLabel = canStartBackend ? "Start backend" : "Reload config",
            SuggestedActionKey = canStartBackend ? DesktopHealthActionKeys.StartBackend : DesktopHealthActionKeys.ReloadConfig
        };
    }

    private static RejectedGuidance ResolveRejectedGuidance(string detail)
    {
        if (detail.Contains("WECHAT_BRIDGE_URL", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("WECHAT_BRIDGE_TOKEN", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("WECHAT_BOT_PREFIX", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "Go to WeChat config",
                DesktopHealthActionKeys.FocusWechatUrl,
                "The saved WeChat bridge settings do not pass backend validation yet.",
                "Open WeChat bridge settings in this window, fix the saved value, then save again.",
                "The WeChat worker can start once the saved bridge settings match a valid live bridge service.");
        }

        if (detail.Contains("NAPCAT_WS_URL", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "Go to NapCat URL",
                DesktopHealthActionKeys.FocusNapCatUrl,
                "The saved NapCat websocket address is not valid yet.",
                "Open NapCat settings, fix NAPCAT_WS_URL, then save again.",
                "QQ can try to connect once the saved NapCat address is valid.");
        }

        if (detail.Contains("NAPCAT_TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "Go to NapCat token",
                DesktopHealthActionKeys.FocusNapCatToken,
                "QQ is still missing the credential it needs to authenticate to NapCat.",
                "Open NapCat settings, add NAPCAT_TOKEN, then save again.",
                "QQ has the credential it needs before startup.");
        }

        if (detail.Contains("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_DEFAULT_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_DEFAULT_MODEL", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_ADVANCED_MODEL", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "Go to API keys",
                DesktopHealthActionKeys.FocusOpenAiDefaultKey,
                "The saved model route is missing a required API or model value.",
                "Open the API key section, fill the missing model or key value, then save again.",
                "The runtime can call the configured model route once the required value is present.");
        }

        if (detail.Contains("QQ_AI_BOT_CONTROL_API_TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "Go to local token",
                DesktopHealthActionKeys.FocusControlApiToken,
                "The backend rejected the saved local control token value.",
                "Open the local desktop attachment settings, fix QQ_AI_BOT_CONTROL_API_TOKEN, then retry.",
                "This desktop can reattach to the runtime again.");
        }

        return new RejectedGuidance(
            "Reload config",
            DesktopHealthActionKeys.ReloadConfig,
            "The current values do not pass backend validation yet.",
            "Reload config, inspect the field named in the technical detail, then retry.",
            "Desktop can save a clean config once the invalid value is corrected.");
    }

    private static string BuildDialogMessage(
        string whatHappened,
        string whyText,
        string doNowText,
        string improvementText,
        string technicalDetail,
        params string?[] additionalContext)
    {
        var lines = new List<string>
        {
            "What happened",
            whatHappened
        };

        foreach (var contextLine in additionalContext.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            lines.Add(contextLine!);
        }

        lines.Add(string.Empty);
        lines.Add("Why");
        lines.Add(whyText);
        lines.Add(string.Empty);
        lines.Add("Do this now");
        lines.Add(doNowText);
        lines.Add(string.Empty);
        lines.Add("What improves after this");
        lines.Add(improvementText);
        lines.Add(string.Empty);
        lines.Add("Technical detail");
        lines.Add(DefaultIfBlank(technicalDetail, "No extra error detail was captured."));

        return string.Join(Environment.NewLine, lines);
    }

    private static string DefaultIfBlank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed record RejectedGuidance(
        string Label,
        string Key,
        string WhyText,
        string NextStepText,
        string ImprovementText);
}
