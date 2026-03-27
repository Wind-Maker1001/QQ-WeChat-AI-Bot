using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopHealthReportBuilder
{
    public static DesktopHealthReport Build(
        BotConfig? config,
        BackendRuntimeSnapshotViewState? runtimeSnapshot,
        BackendControlApiFailure? controlApiFailure,
        bool isBackendRootValid,
        bool hasUnsavedChanges,
        bool autoStartEnabled)
    {
        var effectiveConfig = config ?? new BotConfig();
        var effectiveRuntimeSnapshot = runtimeSnapshot ?? new BackendRuntimeSnapshotViewState();
        var effectiveControlApiFailure = controlApiFailure ?? new BackendControlApiFailure();
        var checks = new[]
        {
            BuildControlApiCheck(isBackendRootValid, effectiveRuntimeSnapshot, effectiveControlApiFailure),
            BuildOpenAiCheck(effectiveConfig),
            BuildQqCheck(effectiveConfig, effectiveRuntimeSnapshot, effectiveControlApiFailure),
            BuildWechatCheck(effectiveConfig, effectiveRuntimeSnapshot, effectiveControlApiFailure),
            BuildResidentModeCheck(autoStartEnabled, isBackendRootValid)
        };
        var latestIssue = BuildLatestIssue(
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            checks);

        var overallState = ResolveOverallState(
            effectiveConfig,
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            isBackendRootValid);

        var primaryAction = hasUnsavedChanges && isBackendRootValid
            ? "Save config to apply the edits shown in this window, then follow the runtime guidance below."
            : overallState.PrimaryAction;

        return new DesktopHealthReport
        {
            State = overallState.State,
            StateText = overallState.StateText,
            Summary = overallState.Summary,
            ChecklistStatus = BuildChecklistStatus(checks, isBackendRootValid),
            ReadyNowText = BuildReadyNowText(
                effectiveConfig,
                effectiveRuntimeSnapshot,
                effectiveControlApiFailure,
                isBackendRootValid),
            PrimaryAction = primaryAction,
            PrimaryActionLabel = hasUnsavedChanges && isBackendRootValid ? "Save config" : overallState.PrimaryActionLabel,
            PrimaryActionKey = hasUnsavedChanges && isBackendRootValid ? DesktopHealthActionKeys.SaveConfig : overallState.PrimaryActionKey,
            RuntimeExplanation = BuildRuntimeExplanation(
                effectiveConfig,
                effectiveRuntimeSnapshot,
                effectiveControlApiFailure,
                isBackendRootValid),
            LatestIssue = latestIssue.Text,
            LatestIssueActionLabel = latestIssue.ActionLabel,
            LatestIssueActionKey = latestIssue.ActionKey,
            Checks = checks
        };
    }

    private static string BuildChecklistStatus(
        IReadOnlyList<DesktopHealthCheckItem> checks,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return "Setup checklist: choose the installed backend folder before any other checks can pass.";
        }

        var blockingChecks = checks.Where(static check => check.IsBlocking).ToArray();
        var warningChecks = checks
            .Where(static check => !check.IsBlocking && check.State == DesktopHealthState.Warning)
            .ToArray();
        var infoChecks = checks
            .Where(static check => !check.IsBlocking && check.State == DesktopHealthState.Info)
            .ToArray();

        if (blockingChecks.Length > 0)
        {
            var blockingTitles = string.Join(", ", blockingChecks.Select(static check => check.Title));
            return $"Setup checklist: {blockingChecks.Length} required item{(blockingChecks.Length == 1 ? string.Empty : "s")} still need attention: {blockingTitles}.";
        }

        if (warningChecks.Length == 0 && infoChecks.Length == 0)
        {
            return "Setup checklist: required setup is complete and there are no follow-up warnings.";
        }

        if (warningChecks.Length > 0 && infoChecks.Length > 0)
        {
            return $"Setup checklist: required setup is complete. {warningChecks.Length} runtime warning{(warningChecks.Length == 1 ? string.Empty : "s")} and {infoChecks.Length} optional item{(infoChecks.Length == 1 ? string.Empty : "s")} remain visible.";
        }

        if (warningChecks.Length > 0)
        {
            return $"Setup checklist: required setup is complete. {warningChecks.Length} runtime warning{(warningChecks.Length == 1 ? string.Empty : "s")} still need review.";
        }

        return $"Setup checklist: required setup is complete. {infoChecks.Length} optional item{(infoChecks.Length == 1 ? string.Empty : "s")} can be configured later.";
    }

    private static string BuildReadyNowText(
        BotConfig config,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return "Ready now: not yet. Desktop still needs the backend folder before it can manage this runtime.";
        }

        var hasOpenAiKey = HasText(config.OpenAiApiKey) || HasText(config.OpenAiDefaultApiKey);
        var hasNapCatToken = HasText(config.NapCatToken);
        var napCatUrlValid = LooksLikeWebSocketUrl(config.NapCatWsUrl);
        var wechatConfigured = HasText(config.WechatBridgeUrl);

        if (!hasOpenAiKey || !hasNapCatToken || !napCatUrlValid)
        {
            return "Ready now: not yet. Finish the required OpenAI and NapCat setup first.";
        }

        if (controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return "Ready now: paused. Desktop cannot trust live runtime state until the local control API token matches again.";
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return "Ready now: config is usable, but the backend is offline from this window's point of view. Start or recover the backend to verify live state.";
        }

        if (runtimeSnapshot.RuntimeActive != true)
        {
            return wechatConfigured
                ? "Ready now: QQ and WeChat settings look usable, but the backend is stopped. Start it when you want the channels online."
                : "Ready now: QQ is ready to start. The backend is stopped until you choose to run it.";
        }

        if (runtimeSnapshot.RuntimeReady != true)
        {
            return "Ready now: not fully. The backend is running, but QQ is still waiting for NapCat.";
        }

        if (wechatConfigured && runtimeSnapshot.WechatRuntimeReady != true)
        {
            return "Ready now: QQ is ready for daily use. WeChat is still waiting for its bridge, so only that channel is blocked.";
        }

        return wechatConfigured
            ? "Ready now: QQ and WeChat are both ready for regular use."
            : "Ready now: QQ is ready for regular use. WeChat remains optional and disabled.";
    }

    private static DesktopHealthCheckItem BuildControlApiCheck(
        bool isBackendRootValid,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure)
    {
        if (!isBackendRootValid)
        {
            return CreateCheck(
                key: "control-api",
                title: "Desktop attachment",
                state: DesktopHealthState.Error,
                stateText: "Choose backend folder",
                detail: "Pick the backend root that contains package.json and src\\index.mjs before loading or starting the runtime.",
                isBlocking: true,
                actionLabel: "Choose folder",
                actionKey: DesktopHealthActionKeys.FocusBackendRoot);
        }

        if (runtimeSnapshot.ControlApiReachable == true)
        {
            return CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Good,
                stateText: "Connected",
                detail: "Desktop is attached to the local control API and can save config or control the runtime.");
        }

        return controlApiFailure.Kind switch
        {
            BackendControlApiFailureKind.Unauthorized => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Error,
                stateText: "Token mismatch",
                detail: "Desktop could not authenticate to the local control API. Make QQ_AI_BOT_CONTROL_API_TOKEN match on both sides, then reload or restart.",
                isBlocking: true,
                actionLabel: "Go to local token",
                actionKey: DesktopHealthActionKeys.FocusControlApiToken),
            BackendControlApiFailureKind.Rejected => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Warning,
                stateText: "Request rejected",
                detail: DefaultIfBlank(controlApiFailure.Message, "The local control API rejected the request. Check the saved config and retry."),
                actionLabel: "Show logs",
                actionKey: DesktopHealthActionKeys.ShowLogs),
            BackendControlApiFailureKind.Unknown => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Warning,
                stateText: "Unexpected response",
                detail: DefaultIfBlank(controlApiFailure.Message, "The local control API returned an unexpected result."),
                actionLabel: "Reload",
                actionKey: DesktopHealthActionKeys.ReloadConfig),
            _ => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Warning,
                stateText: "Offline",
                detail: "Desktop can still read local config, but it is not currently attached to a live backend control API.",
                actionLabel: "Start backend",
                actionKey: DesktopHealthActionKeys.StartBackend)
        };
    }

    private static DesktopHealthCheckItem BuildOpenAiCheck(BotConfig config)
    {
        if (HasText(config.OpenAiApiKey) || HasText(config.OpenAiDefaultApiKey))
        {
            return CreateCheck(
                key: "openai",
                title: "LLM credentials",
                state: DesktopHealthState.Good,
                stateText: "Configured",
                detail: "At least one OpenAI-compatible API key is present for the runtime routes.");
        }

        return CreateCheck(
            key: "openai",
            title: "LLM credentials",
            state: DesktopHealthState.Error,
            stateText: "Missing API key",
            detail: "Add OPENAI_API_KEY or OPENAI_DEFAULT_API_KEY before starting the runtime.",
            isBlocking: true,
            actionLabel: "Go to API keys",
            actionKey: DesktopHealthActionKeys.FocusOpenAiDefaultKey);
    }

    private static DesktopHealthCheckItem BuildQqCheck(
        BotConfig config,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure)
    {
        if (!HasText(config.NapCatToken))
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Error,
                stateText: "Missing token",
                detail: "Set NAPCAT_TOKEN so the QQ worker can authenticate to NapCat.",
                isBlocking: true,
                actionLabel: "Go to NapCat token",
                actionKey: DesktopHealthActionKeys.FocusNapCatToken);
        }

        if (!LooksLikeWebSocketUrl(config.NapCatWsUrl))
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Error,
                stateText: "Invalid WS URL",
                detail: "NAPCAT_WS_URL should be a ws:// or wss:// address that the backend can reach.",
                isBlocking: true,
                actionLabel: "Go to NapCat URL",
                actionKey: DesktopHealthActionKeys.FocusNapCatUrl);
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Info,
                stateText: "Waiting for backend",
                detail: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized
                    ? "QQ readiness is unknown until the desktop can reattach to the local control API."
                    : $"QQ will come online after the backend starts and connects to NapCat at {config.NapCatWsUrl}.",
                actionLabel: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? "Show logs" : "Start backend",
                actionKey: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? DesktopHealthActionKeys.ShowLogs : DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.RuntimeReady == true && runtimeSnapshot.RuntimeActive == true)
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Good,
                stateText: "Ready",
                detail: $"QQ worker is connected through NapCat at {config.NapCatWsUrl}.");
        }

        if (runtimeSnapshot.RuntimeActive == true)
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Warning,
                stateText: "Connecting",
                detail: $"Backend is running, but QQ is still waiting for NapCat at {config.NapCatWsUrl}.",
                actionLabel: "Go to NapCat config",
                actionKey: DesktopHealthActionKeys.FocusNapCatUrl);
        }

        return CreateCheck(
            key: "qq",
            title: "QQ / NapCat",
            state: DesktopHealthState.Warning,
            stateText: "Not started",
            detail: $"QQ config looks valid. Start the backend to connect NapCat at {config.NapCatWsUrl}.",
            actionLabel: "Start backend",
            actionKey: DesktopHealthActionKeys.StartBackend);
    }

    private static DesktopHealthCheckItem BuildWechatCheck(
        BotConfig config,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure)
    {
        if (!HasText(config.WechatBridgeUrl))
        {
            return CreateCheck(
                key: "wechat",
                title: "WeChat bridge",
                state: DesktopHealthState.Info,
                stateText: "Optional / off",
                detail: "WeChat is disabled until WECHAT_BRIDGE_URL is configured. QQ can run without it.",
                actionLabel: "Configure WeChat",
                actionKey: DesktopHealthActionKeys.FocusWechatUrl);
        }

        if (!LooksLikeWebSocketUrl(config.WechatBridgeUrl))
        {
            return CreateCheck(
                key: "wechat",
                title: "WeChat bridge",
                state: DesktopHealthState.Warning,
                stateText: "Invalid WS URL",
                detail: "WECHAT_BRIDGE_URL should use ws:// or wss://. Fix it before expecting the WeChat worker to start.",
                actionLabel: "Go to WeChat config",
                actionKey: DesktopHealthActionKeys.FocusWechatUrl);
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return CreateCheck(
                key: "wechat",
                title: "WeChat bridge",
                state: DesktopHealthState.Info,
                stateText: "Configured",
                detail: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized
                    ? "WeChat bridge state is unknown until the desktop can reattach to the control API."
                    : $"WeChat is configured for {config.WechatBridgeUrl}. It will come online after the backend starts.",
                actionLabel: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? "Show logs" : "Start backend",
                actionKey: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? DesktopHealthActionKeys.ShowLogs : DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.WechatBridgeConnected == true && runtimeSnapshot.WechatRuntimeReady == true)
        {
            return CreateCheck(
                key: "wechat",
                title: "WeChat bridge",
                state: DesktopHealthState.Good,
                stateText: "Ready",
                detail: $"WeChat worker is connected to {config.WechatBridgeUrl}.");
        }

        if (runtimeSnapshot.WechatRuntimeActive == true || runtimeSnapshot.WechatConfigured == true)
        {
            return CreateCheck(
                key: "wechat",
                title: "WeChat bridge",
                state: DesktopHealthState.Warning,
                stateText: "Waiting for bridge",
                detail: $"WeChat is configured, but the worker is still waiting for the bridge at {config.WechatBridgeUrl}.",
                actionLabel: "Go to WeChat config",
                actionKey: DesktopHealthActionKeys.FocusWechatUrl);
        }

        return CreateCheck(
            key: "wechat",
            title: "WeChat bridge",
            state: DesktopHealthState.Warning,
            stateText: "Configured but stopped",
            detail: $"WeChat bridge is configured for {config.WechatBridgeUrl}, but the worker is not running yet.",
            actionLabel: "Start backend",
            actionKey: DesktopHealthActionKeys.StartBackend);
    }

    private static DesktopHealthCheckItem BuildResidentModeCheck(
        bool autoStartEnabled,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return CreateCheck(
                key: "resident-mode",
                title: "Resident mode",
                state: DesktopHealthState.Info,
                stateText: "Enable later",
                detail: "Choose the backend folder first. After setup, you can enable startup so the desktop reopens minimized and keeps the runtime easy to reach.",
                actionLabel: string.Empty,
                actionKey: string.Empty);
        }

        if (autoStartEnabled)
        {
            return CreateCheck(
                key: "resident-mode",
                title: "Resident mode",
                state: DesktopHealthState.Good,
                stateText: "Starts with Windows",
                detail: "Desktop startup is enabled. It can reopen minimized, keep the tray entry available, and bring the runtime back after sign-in. Closing the desktop window still hides it to the tray; use Stop backend if you want the runtime offline.");
        }

        return CreateCheck(
            key: "resident-mode",
            title: "Resident mode",
            state: DesktopHealthState.Info,
            stateText: "Manual launch",
            detail: "Desktop startup is off. Minimize or close still hides this window to the tray, and Exit Desktop closes only this window. Enable startup if you want this personal runtime to return automatically after Windows sign-in.",
            actionLabel: "Enable startup",
            actionKey: DesktopHealthActionKeys.ToggleAutoStart);
    }

    private static (DesktopHealthState State, string StateText, string Summary, string PrimaryAction, string PrimaryActionLabel, string PrimaryActionKey) ResolveOverallState(
        BotConfig config,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return (
                DesktopHealthState.Error,
                "Setup needed",
                "Desktop is not attached to a valid backend folder yet.",
                "Point Backend Root at the installed runtime folder before loading or starting anything.",
                "Choose folder",
                DesktopHealthActionKeys.FocusBackendRoot);
        }

        var hasOpenAiKey = HasText(config.OpenAiApiKey) || HasText(config.OpenAiDefaultApiKey);
        var hasNapCatToken = HasText(config.NapCatToken);
        var napCatUrlValid = LooksLikeWebSocketUrl(config.NapCatWsUrl);

        if (!hasOpenAiKey)
        {
            return (
                DesktopHealthState.Error,
                "Setup needed",
                "The runtime still needs an OpenAI-compatible API key before it can answer requests.",
                "Fill in OPENAI_API_KEY or OPENAI_DEFAULT_API_KEY, save config, then start the backend.",
                "Go to API keys",
                DesktopHealthActionKeys.FocusOpenAiDefaultKey);
        }

        if (!hasNapCatToken || !napCatUrlValid)
        {
            return (
                DesktopHealthState.Error,
                "Setup needed",
                "QQ cannot come online until the NapCat connection details are complete.",
                "Set NAPCAT_TOKEN and a valid NAPCAT_WS_URL, then save config and start the backend.",
                "Go to NapCat config",
                hasNapCatToken ? DesktopHealthActionKeys.FocusNapCatUrl : DesktopHealthActionKeys.FocusNapCatToken);
        }

        if (controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return (
                DesktopHealthState.Error,
                "Action needed",
                "Desktop and backend disagree on the local control API token.",
                "Make QQ_AI_BOT_CONTROL_API_TOKEN consistent, then reload this window or restart the backend.",
                "Go to local token",
                DesktopHealthActionKeys.FocusControlApiToken);
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return (
                DesktopHealthState.Warning,
                "Ready to start",
                "Config looks usable, but the desktop is not currently attached to a running backend.",
                "Start the backend from this window to bring the control API and QQ runtime online.",
                "Start backend",
                DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.RuntimeActive != true)
        {
            return (
                DesktopHealthState.Warning,
                "Ready to start",
                "Control API is reachable, but the backend host is currently stopped.",
                "Start the backend to bring QQ online.",
                "Start backend",
                DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.RuntimeReady != true)
        {
            return (
                DesktopHealthState.Warning,
                "Connecting",
                "Backend is running, but QQ is still waiting for NapCat to connect.",
                $"Confirm NapCat is online at {config.NapCatWsUrl} and the token is correct.",
                "Go to NapCat config",
                DesktopHealthActionKeys.FocusNapCatUrl);
        }

        if (HasText(config.WechatBridgeUrl) && runtimeSnapshot.WechatRuntimeReady != true)
        {
            return (
                DesktopHealthState.Warning,
                "Partially ready",
                "QQ is ready. WeChat is configured, but it is still waiting for its bridge.",
                "If you use WeChat, verify the bridge service and token. Otherwise you can ignore this warning.",
                "Go to WeChat config",
                DesktopHealthActionKeys.FocusWechatUrl);
        }

        return (
            DesktopHealthState.Good,
            "Ready",
            "QQ runtime is online and ready for regular use.",
            "No blocking setup issues were detected.",
            "Open backend folder",
            DesktopHealthActionKeys.OpenBackendFolder);
    }

    private static string BuildRuntimeExplanation(
        BotConfig config,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return "This window can only manage a backend folder that contains the runtime entry point and package metadata.";
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized
                ? "Desktop can read local files, but it is currently locked out of the live backend because the local control API token does not match."
                : "Desktop is showing local config, but it is not currently attached to a live backend control API. Runtime state below is unavailable until the backend starts or recovers.";
        }

        var qqExplanation = runtimeSnapshot.RuntimeActive == true
            ? runtimeSnapshot.RuntimeReady == true
                ? "QQ is connected and can process messages."
                : "QQ worker is running, but it has not reached a ready NapCat connection yet."
            : "QQ worker is stopped.";

        var wechatExplanation = !HasText(config.WechatBridgeUrl)
            ? "WeChat is optional and currently disabled."
            : runtimeSnapshot.WechatRuntimeActive == true &&
              runtimeSnapshot.WechatRuntimeReady == true &&
              runtimeSnapshot.WechatBridgeConnected == true
                ? "WeChat bridge is connected."
                : runtimeSnapshot.WechatRuntimeActive == true ||
                  runtimeSnapshot.WechatConfigured == true ||
                  runtimeSnapshot.WechatRuntimeReady == true
                    ? "WeChat is configured, but it is still waiting for the bridge."
                    : "WeChat is configured in the editor, but the worker is not running.";

        return $"{qqExplanation} {wechatExplanation} Recent activity below explains per-turn route decisions and failures.";
    }

    private static LatestIssueSummary BuildLatestIssue(
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure,
        IReadOnlyList<DesktopHealthCheckItem> checks)
    {
        var hasBlockingSetupItems = checks.Any(static check => check.IsBlocking);

        if (runtimeSnapshot.ControlApiReachable != true && controlApiFailure.Kind != BackendControlApiFailureKind.None)
        {
            return controlApiFailure.Kind switch
            {
                BackendControlApiFailureKind.Unauthorized => new LatestIssueSummary(
                    "Latest issue: control API token mismatch blocked the desktop from attaching to the live backend.",
                    "Go to local token",
                    DesktopHealthActionKeys.FocusControlApiToken),
                BackendControlApiFailureKind.Rejected => new LatestIssueSummary(
                    $"Latest issue: control API rejected a request. {DefaultIfBlank(controlApiFailure.Message, "Check the current config and retry.")}",
                    hasBlockingSetupItems ? string.Empty : "Show logs",
                    hasBlockingSetupItems ? string.Empty : DesktopHealthActionKeys.ShowLogs),
                BackendControlApiFailureKind.Unknown => new LatestIssueSummary(
                    $"Latest issue: control API returned an unexpected result. {DefaultIfBlank(controlApiFailure.Message, "Check the backend log for details.")}",
                    hasBlockingSetupItems ? string.Empty : "Reload config",
                    hasBlockingSetupItems ? string.Empty : DesktopHealthActionKeys.ReloadConfig),
                _ => new LatestIssueSummary(
                    "Latest issue: desktop cannot currently reach the local control API.",
                    hasBlockingSetupItems ? string.Empty : "Start backend",
                    hasBlockingSetupItems ? string.Empty : DesktopHealthActionKeys.StartBackend)
            };
        }

        var failures = new[]
        {
            CreateLatestFailure("QQ", runtimeSnapshot.LastQqLlmFailure?.CapturedAt, runtimeSnapshot.LastQqLlmFailure?.Error),
            CreateLatestFailure("WeChat", runtimeSnapshot.LastWechatLlmFailure?.CapturedAt, runtimeSnapshot.LastWechatLlmFailure?.Error)
        }
            .Where(static failure => failure is not null)
            .OrderByDescending(static failure => failure!.CapturedAt)
            .ToArray();

        if (failures.Length == 0)
        {
            return new LatestIssueSummary("Latest issue: no runtime failures have been captured yet.");
        }

        var latestFailure = failures[0]!;
        var action = latestFailure.Channel switch
        {
            "QQ" => new LatestIssueSummary(
                $"Latest issue: {latestFailure.Channel} failed at {FormatCapturedAt(latestFailure.CapturedAtText)}. {DefaultIfBlank(latestFailure.Error, "No error detail was captured.")}",
                "Review QQ failure",
                DesktopHealthActionKeys.FocusQqFailure),
            "WeChat" => new LatestIssueSummary(
                $"Latest issue: {latestFailure.Channel} failed at {FormatCapturedAt(latestFailure.CapturedAtText)}. {DefaultIfBlank(latestFailure.Error, "No error detail was captured.")}",
                "Review WeChat failure",
                DesktopHealthActionKeys.FocusWechatFailure),
            _ => new LatestIssueSummary(
                $"Latest issue: {latestFailure.Channel} failed at {FormatCapturedAt(latestFailure.CapturedAtText)}. {DefaultIfBlank(latestFailure.Error, "No error detail was captured.")}",
                "Show logs",
                DesktopHealthActionKeys.ShowLogs)
        };

        return action;
    }

    private static LatestFailureSnapshot? CreateLatestFailure(string channel, string? capturedAt, string? error)
    {
        if (!DateTimeOffset.TryParse(capturedAt, out var parsedCapturedAt))
        {
            return null;
        }

        return new LatestFailureSnapshot(channel, parsedCapturedAt, capturedAt ?? string.Empty, DefaultIfBlank(error, "unknown failure"));
    }

    private static DesktopHealthCheckItem CreateCheck(
        string key,
        string title,
        DesktopHealthState state,
        string stateText,
        string detail,
        bool isBlocking = false,
        string actionLabel = "",
        string actionKey = "")
    {
        return new DesktopHealthCheckItem
        {
            Key = key,
            Title = title,
            State = state,
            StateText = stateText,
            Detail = detail,
            IsBlocking = isBlocking,
            ActionLabel = actionLabel,
            ActionKey = actionKey
        };
    }

    private static string DefaultIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool HasText(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool LooksLikeWebSocketUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "ws" or "wss";
    }

    private static string FormatCapturedAt(string capturedAt)
    {
        if (DateTimeOffset.TryParse(capturedAt, out var parsed))
        {
            return parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }

        return string.IsNullOrWhiteSpace(capturedAt) ? "unknown" : capturedAt;
    }

    private sealed record LatestFailureSnapshot(
        string Channel,
        DateTimeOffset CapturedAt,
        string CapturedAtText,
        string Error);

    private sealed record LatestIssueSummary(
        string Text,
        string ActionLabel = "",
        string ActionKey = "");
}
