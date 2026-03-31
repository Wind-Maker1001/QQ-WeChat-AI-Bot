using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopHealthGuidanceBuilder
{
    public static DesktopHealthGuidance Build(
        BotConfig? config,
        BackendRuntimeSnapshotViewState? runtimeSnapshot,
        BackendControlApiFailure? controlApiFailure,
        IReadOnlyList<DesktopHealthCheckItem>? checks,
        bool isBackendRootValid,
        bool hasUnsavedChanges,
        bool autoStartEnabled)
    {
        var effectiveConfig = config ?? new BotConfig();
        var effectiveRuntimeSnapshot = runtimeSnapshot ?? new BackendRuntimeSnapshotViewState();
        var effectiveControlApiFailure = controlApiFailure ?? new BackendControlApiFailure();
        var effectiveChecks = checks ?? [];
        var latestIssue = BuildLatestIssue(
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            effectiveChecks);
        var nextActions = BuildNextActions(
            effectiveConfig,
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            effectiveChecks,
            latestIssue,
            isBackendRootValid,
            hasUnsavedChanges,
            autoStartEnabled);

        return new DesktopHealthGuidance
        {
            LatestIssueText = latestIssue.Text,
            LatestIssueActionLabel = latestIssue.ActionLabel,
            LatestIssueActionKey = latestIssue.ActionKey,
            ActionSummary = BuildActionSummary(nextActions, isBackendRootValid),
            NextActions = nextActions
        };
    }

    private static IReadOnlyList<DesktopNextActionItem> BuildNextActions(
        BotConfig config,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure,
        IReadOnlyList<DesktopHealthCheckItem> checks,
        LatestIssueSummary latestIssue,
        bool isBackendRootValid,
        bool hasUnsavedChanges,
        bool autoStartEnabled)
    {
        var actions = new List<DesktopNextActionItem>();

        void addAction(
            string title,
            string detail,
            string outcome,
            string actionLabel,
            string actionKey)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(actionKey) &&
                actions.Any((action) => string.Equals(action.ActionKey, actionKey, StringComparison.Ordinal)))
            {
                return;
            }

            if (actions.Any((action) => string.Equals(action.Title, title, StringComparison.Ordinal)))
            {
                return;
            }

            actions.Add(
                new DesktopNextActionItem
                {
                    Title = title,
                    Detail = detail,
                    Outcome = outcome,
                    ActionLabel = actionLabel,
                    ActionKey = actionKey
                });
        }

        if (!isBackendRootValid)
        {
            addAction(
                "选择已安装的 runtime 目录",
                "Desktop 需要先连到包含 package.json 和 src\\index.mjs 的本地 runtime 目录，才能加载配置和显示实时状态。",
                "完成后，其余设置项会在同一个界面里继续展开。",
                "选择目录",
                DesktopHealthActionKeys.FocusBackendRoot);
            return FinalizeNextActions(actions);
        }

        var blockingChecks = checks.Where(static check => check.IsBlocking).ToArray();

        foreach (var blockingCheck in blockingChecks)
        {
            var blockingAction = BuildBlockingAction(blockingCheck, config);
            addAction(
                blockingAction.Title,
                blockingAction.Detail,
                blockingAction.Outcome,
                blockingAction.ActionLabel,
                blockingAction.ActionKey);
        }

        if (blockingChecks.Length > 0)
        {
            return FinalizeNextActions(actions);
        }

        if (hasUnsavedChanges)
        {
            addAction(
                "保存当前修改",
                "这个窗口里显示的值在通过 control API 保存之前，仍然只是本地编辑状态。",
                "保存后，后端就会使用你在这里看到的同一组设置。",
                "保存配置",
                DesktopHealthActionKeys.SaveConfig);
        }

        if (controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            addAction(
                "令牌一致后重新加载",
                "本地 control token 修正后，这个窗口还需要重新加载一次，才能重新信任实时 runtime 状态。",
                "完成后，desktop 才能重新加载配置、启动后端并验证 runtime 健康状态。",
                "重新加载配置",
                DesktopHealthActionKeys.ReloadConfig);
            return FinalizeNextActions(actions);
        }

        var hasOpenAiKey = HasText(config.OpenAiApiKey) || HasText(config.OpenAiDefaultApiKey);
        var hasQqConfig = HasText(config.NapCatToken) && LooksLikeWebSocketUrl(config.NapCatWsUrl);
        var canStartRuntime = hasOpenAiKey && hasQqConfig;

        if (canStartRuntime)
        {
            if (runtimeSnapshot.ControlApiReachable != true || runtimeSnapshot.RuntimeActive != true)
            {
                addAction(
                    "启动后端",
                    runtimeSnapshot.ControlApiReachable == true
                        ? "配置看起来已经可用，但 backend 宿主当前仍然停止。"
                        : "配置看起来已经可用，但这个窗口当前还没有附着到实时 backend。",
                    "完成后，desktop 就能验证实时状态，QQ 也会开始尝试上线。",
                    "启动后端",
                    DesktopHealthActionKeys.StartBackend);
            }
            else if (runtimeSnapshot.RuntimeReady != true)
            {
                addAction(
                    "确认 NapCat 已在线",
                    $"QQ 仍在等待连接 {config.NapCatWsUrl} 的 NapCat。",
                    "完成后，QQ 就能进入正常可用状态。",
                    "检查 NapCat 配置",
                    DesktopHealthActionKeys.FocusNapCatUrl);
            }
        }

        var hasUrgentAction = actions.Count > 0;

        if (!hasUrgentAction && runtimeSnapshot.ControlApiReachable == true && runtimeSnapshot.RuntimeActive == true)
        {
            if (HasText(config.WechatBridgeUrl) && runtimeSnapshot.WechatRuntimeReady != true)
            {
                addAction(
                    "检查微信桥接",
                    $"QQ 已经上线，但微信仍在等待连接 {config.WechatBridgeUrl} 的桥接服务。",
                    "完成后，微信 worker 就能加入同一个 runtime。",
                    "查看微信配置",
                    DesktopHealthActionKeys.FocusWechatUrl);
            }
            else if (!string.IsNullOrWhiteSpace(latestIssue.ActionLabel))
            {
                addAction(
                    "查看最新问题",
                    latestIssue.Text,
                    "完成后，你就能判断这个 runtime 是否已经完全健康，还是仍需要继续跟进。",
                    latestIssue.ActionLabel,
                    latestIssue.ActionKey);
            }
        }

        if (!autoStartEnabled && canStartRuntime)
        {
            addAction(
                "稍后启用开机启动（可选）",
                "当前仍是手动启动，所以 Windows 登录后需要你自己重新打开 Local AI Runtime。",
                "完成后，desktop 可以以最小化方式自动恢复，更适合作为日常控制台。",
                "启用开机启动",
                DesktopHealthActionKeys.ToggleAutoStart);
        }

        return FinalizeNextActions(actions);
    }

    private static (string Title, string Detail, string Outcome, string ActionLabel, string ActionKey) BuildBlockingAction(
        DesktopHealthCheckItem check,
        BotConfig config)
    {
        return check.Key switch
        {
            "control-api" => (
                "同步 desktop control token",
                "在 QQ_AI_BOT_CONTROL_API_TOKEN 和后端 .env 一致之前，这个 desktop 窗口无法保存配置或验证实时状态。",
                "完成后，desktop 才能重新连上实时 runtime。",
                DefaultIfBlank(check.ActionLabel, "查看本机令牌"),
                DefaultIfBlank(check.ActionKey, DesktopHealthActionKeys.FocusControlApiToken)),
            "openai" => (
                "添加 OpenAI 兼容密钥",
                "在至少保存一个 API key 之前，runtime 还不能回答请求。",
                "完成后，runtime 才能调用配置好的模型路由。",
                DefaultIfBlank(check.ActionLabel, "查看 API 密钥"),
                DefaultIfBlank(check.ActionKey, DesktopHealthActionKeys.FocusOpenAiDefaultKey)),
            "qq" when !HasText(config.NapCatToken) => (
                "补上 NapCat Token",
                "在 Token 保存前，QQ 还无法向 NapCat 完成鉴权。",
                "完成后，QQ 就具备启动前所需的最后一个必要凭据。",
                DefaultIfBlank(check.ActionLabel, "查看 NapCat Token"),
                DefaultIfBlank(check.ActionKey, DesktopHealthActionKeys.FocusNapCatToken)),
            "qq" => (
                "修正 NapCat 地址",
                "在 QQ 连接前，仍然需要一个有效的 ws:// 或 wss:// NapCat 地址。",
                "完成后，后端启动时 QQ 才能尝试连接 NapCat。",
                DefaultIfBlank(check.ActionLabel, "查看 NapCat 地址"),
                DefaultIfBlank(check.ActionKey, DesktopHealthActionKeys.FocusNapCatUrl)),
            _ => (
                DefaultIfBlank(check.Title, "完成必填设置"),
                DefaultIfBlank(check.Detail, "仍有一个必填设置项需要处理。"),
                "完成后，desktop 才能进入下一个引导步骤。",
                check.ActionLabel,
                check.ActionKey)
        };
    }

    private static IReadOnlyList<DesktopNextActionItem> FinalizeNextActions(IReadOnlyList<DesktopNextActionItem> actions)
    {
        return actions
            .Take(3)
            .Select((action, index) => action with
            {
                StepNumber = (index + 1).ToString(),
                IsPrimary = index == 0
            })
            .ToArray();
    }

    private static string BuildActionSummary(
        IReadOnlyList<DesktopNextActionItem> actions,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return "先从这里开始：选择已安装的 runtime 目录。之后 desktop 会把剩余设置项自动聚焦给你。";
        }

        if (actions.Count == 0)
        {
            return "当前重点：现在没有阻塞这个 runtime 的紧急问题。若要检查最新一轮执行，请查看最近活动。";
        }

        var primaryAction = actions[0];
        return $"当前重点：{primaryAction.Title}。{primaryAction.Outcome}";
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
                    "最新问题：control API 令牌不一致，导致 desktop 无法附着到实时后端。",
                    "查看本机令牌",
                    DesktopHealthActionKeys.FocusControlApiToken),
                BackendControlApiFailureKind.Rejected => new LatestIssueSummary(
                    $"最新问题：control API 拒绝了一个请求。{DefaultIfBlank(controlApiFailure.Message, "请检查当前配置后再重试。")}",
                    hasBlockingSetupItems ? string.Empty : "查看日志",
                    hasBlockingSetupItems ? string.Empty : DesktopHealthActionKeys.ShowLogs),
                BackendControlApiFailureKind.Unknown => new LatestIssueSummary(
                    $"最新问题：control API 返回了意外结果。{DefaultIfBlank(controlApiFailure.Message, "请查看后端日志了解详情。")}",
                    hasBlockingSetupItems ? string.Empty : "重新加载配置",
                    hasBlockingSetupItems ? string.Empty : DesktopHealthActionKeys.ReloadConfig),
                BackendControlApiFailureKind.Incompatible => new LatestIssueSummary(
                    $"最新问题：当前连接到的本地 control API 版本过旧。{DefaultIfBlank(controlApiFailure.Message, "请重启当前工作区或最新安装版本的 backend。")}",
                    hasBlockingSetupItems ? string.Empty : "打开 backend 目录",
                    hasBlockingSetupItems ? string.Empty : DesktopHealthActionKeys.OpenBackendFolder),
                _ => new LatestIssueSummary(
                    "最新问题：desktop 当前无法连接本地 control API。",
                    hasBlockingSetupItems ? string.Empty : "启动后端",
                    hasBlockingSetupItems ? string.Empty : DesktopHealthActionKeys.StartBackend)
            };
        }

        var failures = new[]
        {
            CreateLatestFailure("QQ", runtimeSnapshot.LastQqLlmFailure?.CapturedAt, runtimeSnapshot.LastQqLlmFailure?.Error),
            CreateLatestFailure("微信", runtimeSnapshot.LastWechatLlmFailure?.CapturedAt, runtimeSnapshot.LastWechatLlmFailure?.Error)
        }
            .Where(static failure => failure is not null)
            .OrderByDescending(static failure => failure!.CapturedAt)
            .ToArray();

        if (failures.Length == 0)
        {
            return new LatestIssueSummary("最新问题：当前还没有捕获到 runtime 失败记录。");
        }

        var latestFailure = failures[0]!;
        return latestFailure.Channel switch
        {
            "QQ" => new LatestIssueSummary(
                $"最新问题：{latestFailure.Channel} 在 {FormatCapturedAt(latestFailure.CapturedAtText)} 失败。{DefaultIfBlank(latestFailure.Error, "没有捕获到更详细的错误信息。")}",
                "查看 QQ 失败",
                DesktopHealthActionKeys.FocusQqFailure),
            "微信" => new LatestIssueSummary(
                $"最新问题：{latestFailure.Channel} 在 {FormatCapturedAt(latestFailure.CapturedAtText)} 失败。{DefaultIfBlank(latestFailure.Error, "没有捕获到更详细的错误信息。")}",
                "查看微信失败",
                DesktopHealthActionKeys.FocusWechatFailure),
            _ => new LatestIssueSummary(
                $"最新问题：{latestFailure.Channel} 在 {FormatCapturedAt(latestFailure.CapturedAtText)} 失败。{DefaultIfBlank(latestFailure.Error, "没有捕获到更详细的错误信息。")}",
                "查看日志",
                DesktopHealthActionKeys.ShowLogs)
        };
    }

    private static LatestFailureSnapshot? CreateLatestFailure(string channel, string? capturedAt, string? error)
    {
        if (!DateTimeOffset.TryParse(capturedAt, out var parsedCapturedAt))
        {
            return null;
        }

        return new LatestFailureSnapshot(channel, parsedCapturedAt, capturedAt ?? string.Empty, DefaultIfBlank(error, "未知失败"));
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

public sealed record DesktopHealthGuidance
{
    public string LatestIssueText { get; init; } = string.Empty;

    public string LatestIssueActionLabel { get; init; } = string.Empty;

    public string LatestIssueActionKey { get; init; } = string.Empty;

    public string ActionSummary { get; init; } = string.Empty;

    public IReadOnlyList<DesktopNextActionItem> NextActions { get; init; } = [];
}
