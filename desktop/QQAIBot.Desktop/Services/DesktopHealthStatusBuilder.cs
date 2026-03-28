using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopHealthStatusBuilder
{
    public static DesktopHealthStatusSnapshot Build(
        BotConfig? config,
        BackendRuntimeSnapshotViewState? runtimeSnapshot,
        BackendControlApiFailure? controlApiFailure,
        bool isBackendRootValid)
    {
        var effectiveConfig = config ?? new BotConfig();
        var effectiveRuntimeSnapshot = runtimeSnapshot ?? new BackendRuntimeSnapshotViewState();
        var effectiveControlApiFailure = controlApiFailure ?? new BackendControlApiFailure();
        var overallState = ResolveOverallState(
            effectiveConfig,
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            isBackendRootValid);

        return new DesktopHealthStatusSnapshot
        {
            State = overallState.State,
            StateText = overallState.StateText,
            Summary = overallState.Summary,
            PrimaryAction = overallState.PrimaryAction,
            PrimaryActionLabel = overallState.PrimaryActionLabel,
            PrimaryActionKey = overallState.PrimaryActionKey,
            ReadyNowText = BuildReadyNowText(
                effectiveConfig,
                effectiveRuntimeSnapshot,
                effectiveControlApiFailure,
                isBackendRootValid),
            RuntimeExplanation = BuildRuntimeExplanation(
                effectiveConfig,
                effectiveRuntimeSnapshot,
                effectiveControlApiFailure,
                isBackendRootValid)
        };
    }

    private static string BuildReadyNowText(
        BotConfig config,
        BackendRuntimeSnapshotViewState runtimeSnapshot,
        BackendControlApiFailure controlApiFailure,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return "立即可用：还不行。这个控制台还需要先连接 backend 目录，才能管理这个 runtime。";
        }

        var hasOpenAiKey = HasText(config.OpenAiApiKey) || HasText(config.OpenAiDefaultApiKey);
        var hasNapCatToken = HasText(config.NapCatToken);
        var napCatUrlValid = LooksLikeWebSocketUrl(config.NapCatWsUrl);
        var wechatConfigured = HasText(config.WechatBridgeUrl);

        if (!hasOpenAiKey || !hasNapCatToken || !napCatUrlValid)
        {
            return "立即可用：还不行。请先完成 OpenAI 和 NapCat 的必填设置。";
        }

        if (controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return "立即可用：暂缓。只有本地 control API 令牌重新一致后，这个控制台才能重新信任实时 runtime 状态。";
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return "立即可用：配置本身可用，但从这个窗口看 backend 仍然离线。请先启动或恢复 backend，再验证实时状态。";
        }

        if (runtimeSnapshot.RuntimeActive != true)
        {
            return wechatConfigured
                ? "立即可用：QQ 和微信设置看起来都可用，但 backend 当前已停止。需要通道上线时再启动它。"
                : "立即可用：QQ 已具备启动条件。backend 当前保持停止，等你决定运行时再启动。";
        }

        if (runtimeSnapshot.RuntimeReady != true)
        {
            return "立即可用：还不完全。backend 已运行，但 QQ 仍在等待 NapCat。";
        }

        if (wechatConfigured && runtimeSnapshot.WechatRuntimeReady != true)
        {
            return "立即可用：QQ 已可日常使用。微信仍在等待桥接，所以当前只有那个通道受阻。";
        }

        return wechatConfigured
            ? "立即可用：QQ 和微信都已可正常使用。"
            : "立即可用：QQ 已可正常使用，微信仍保持可选且关闭。";
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
                "需要设置",
                "这个控制台还没有连接到一个有效的 backend 目录。",
                "先把 Backend Root 指向安装后的 runtime 目录，再执行加载或启动。",
                "选择目录",
                DesktopHealthActionKeys.FocusBackendRoot);
        }

        var hasOpenAiKey = HasText(config.OpenAiApiKey) || HasText(config.OpenAiDefaultApiKey);
        var hasNapCatToken = HasText(config.NapCatToken);
        var napCatUrlValid = LooksLikeWebSocketUrl(config.NapCatWsUrl);

        if (!hasOpenAiKey)
        {
            return (
                DesktopHealthState.Error,
                "需要设置",
                "runtime 还缺少一个 OpenAI 兼容 API key，暂时还不能回答请求。",
                "先填写 OPENAI_API_KEY 或 OPENAI_DEFAULT_API_KEY，保存配置后再启动后端。",
                "查看 API 密钥",
                DesktopHealthActionKeys.FocusOpenAiDefaultKey);
        }

        if (!hasNapCatToken || !napCatUrlValid)
        {
            return (
                DesktopHealthState.Error,
                "需要设置",
                "在 NapCat 连接信息补完整之前，QQ 还不能上线。",
                "先设置 NAPCAT_TOKEN 和有效的 NAPCAT_WS_URL，保存配置后再启动后端。",
                "查看 NapCat 配置",
                hasNapCatToken ? DesktopHealthActionKeys.FocusNapCatUrl : DesktopHealthActionKeys.FocusNapCatToken);
        }

        if (controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return (
                DesktopHealthState.Error,
                "需要处理",
                "这个控制台和 backend 使用的本地 control API 令牌不一致。",
                "先让 QQ_AI_BOT_CONTROL_API_TOKEN 保持一致，再重新加载这个窗口或重启 backend。",
                "查看本机令牌",
                DesktopHealthActionKeys.FocusControlApiToken);
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return (
                DesktopHealthState.Warning,
                "可启动",
                "配置看起来已经可用，但 desktop 当前还没有附着到一个正在运行的 backend。",
                "从这个窗口启动后端，让 control API 和 QQ runtime 一起上线。",
                "启动后端",
                DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.RuntimeActive != true)
        {
            return (
                DesktopHealthState.Warning,
                "可启动",
                "Control API 可达，但 backend 宿主当前已停止。",
                "启动后端，让 QQ 上线。",
                "启动后端",
                DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.RuntimeReady != true)
        {
            return (
                DesktopHealthState.Warning,
                "连接中",
                "Backend 已运行，但 QQ 仍在等待 NapCat 连上。",
                $"确认 {config.NapCatWsUrl} 上的 NapCat 已在线，并且 Token 正确。",
                "查看 NapCat 配置",
                DesktopHealthActionKeys.FocusNapCatUrl);
        }

        if (HasText(config.WechatBridgeUrl) && runtimeSnapshot.WechatRuntimeReady != true)
        {
            return (
                DesktopHealthState.Warning,
                "部分就绪",
                "QQ 已就绪。微信已配置，但仍在等待桥接。",
                "如果你要用微信，请检查桥接服务和 Token；否则可以先忽略这个提示。",
                "查看微信配置",
                DesktopHealthActionKeys.FocusWechatUrl);
        }

        return (
            DesktopHealthState.Good,
            "已就绪",
            "QQ runtime 已在线，可正常日常使用。",
            "当前没有发现阻塞性的设置问题。",
            "打开 backend 目录",
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
            return "这个窗口只能管理包含 runtime 入口和包元数据的 backend 目录。";
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized
                ? "这个控制台还能读取本地文件，但因为本地 control API 令牌不匹配，当前无法接入实时 backend。"
                : "这个控制台当前展示的是本地配置，还没有附着到实时 backend control API。在 backend 启动或恢复之前，下方 runtime 状态都不可用。";
        }

        var qqExplanation = runtimeSnapshot.RuntimeActive == true
            ? runtimeSnapshot.RuntimeReady == true
                ? "QQ 已连接，可以开始处理消息。"
                : "QQ worker 已运行，但还没有等到一个可用的 NapCat 连接。"
            : "QQ worker 已停止。";

        var wechatExplanation = !HasText(config.WechatBridgeUrl)
            ? "微信是可选项，当前保持关闭。"
            : runtimeSnapshot.WechatRuntimeActive == true &&
              runtimeSnapshot.WechatRuntimeReady == true &&
              runtimeSnapshot.WechatBridgeConnected == true
                ? "微信桥接已连接。"
                : runtimeSnapshot.WechatRuntimeActive == true ||
                  runtimeSnapshot.WechatConfigured == true ||
                  runtimeSnapshot.WechatRuntimeReady == true
                    ? "微信已配置，但仍在等待桥接。"
                    : "微信已在编辑器里配置，但 worker 还没有运行。";

        return $"{qqExplanation} {wechatExplanation} 下方最近活动会继续解释每一轮的路由决策和失败原因。";
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
}

public sealed record DesktopHealthStatusSnapshot
{
    public DesktopHealthState State { get; init; } = DesktopHealthState.Info;

    public string StateText { get; init; } = string.Empty;

    public string Summary { get; init; } = string.Empty;

    public string ReadyNowText { get; init; } = string.Empty;

    public string PrimaryAction { get; init; } = string.Empty;

    public string PrimaryActionLabel { get; init; } = string.Empty;

    public string PrimaryActionKey { get; init; } = string.Empty;

    public string RuntimeExplanation { get; init; } = string.Empty;
}
