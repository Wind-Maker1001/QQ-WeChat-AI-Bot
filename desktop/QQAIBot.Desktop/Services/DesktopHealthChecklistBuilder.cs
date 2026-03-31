using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopHealthChecklistBuilder
{
    public static DesktopHealthChecklistSnapshot Build(
        BotConfig? config,
        BackendRuntimeSnapshotViewState? runtimeSnapshot,
        BackendControlApiFailure? controlApiFailure,
        bool isBackendRootValid,
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

        return new DesktopHealthChecklistSnapshot
        {
            Checks = checks,
            ChecklistStatus = BuildChecklistStatus(checks, isBackendRootValid)
        };
    }

    private static string BuildChecklistStatus(
        IReadOnlyList<DesktopHealthCheckItem> checks,
        bool isBackendRootValid)
    {
        if (!isBackendRootValid)
        {
            return "设置清单：先选择已安装的 backend 目录，其他检查才能继续通过。";
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
            return $"设置清单：还有 {blockingChecks.Length} 个必填项待处理：{blockingTitles}。";
        }

        if (warningChecks.Length == 0 && infoChecks.Length == 0)
        {
            return "设置清单：必填设置已完成，目前没有后续警告。";
        }

        if (warningChecks.Length > 0 && infoChecks.Length > 0)
        {
            return $"设置清单：必填设置已完成。当前还有 {warningChecks.Length} 个 runtime 警告和 {infoChecks.Length} 个可选项可继续处理。";
        }

        if (warningChecks.Length > 0)
        {
            return $"设置清单：必填设置已完成。当前还有 {warningChecks.Length} 个 runtime 警告待查看。";
        }

        return $"设置清单：必填设置已完成。当前还有 {infoChecks.Length} 个可选项可稍后再配。";
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
                title: "Desktop 连接",
                state: DesktopHealthState.Error,
                stateText: "选择 backend 目录",
                detail: "先选中包含 package.json 和 src\\index.mjs 的 backend 根目录，再加载或启动 runtime。",
                isBlocking: true,
                actionLabel: "选择目录",
                actionKey: DesktopHealthActionKeys.FocusBackendRoot);
        }

        if (runtimeSnapshot.ControlApiReachable == true)
        {
            return CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Good,
                stateText: "已连接",
                detail: "Desktop 已连上本地 control API，现在可以保存配置并控制 runtime。");
        }

        return controlApiFailure.Kind switch
        {
            BackendControlApiFailureKind.Unauthorized => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Error,
                stateText: "令牌不一致",
                detail: "Desktop 无法通过本地 control API 鉴权。请让两边的 QQ_AI_BOT_CONTROL_API_TOKEN 保持一致，然后重新加载或重启。",
                isBlocking: true,
                actionLabel: "查看本机令牌",
                actionKey: DesktopHealthActionKeys.FocusControlApiToken),
            BackendControlApiFailureKind.Rejected => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Warning,
                stateText: "请求被拒绝",
                detail: DefaultIfBlank(controlApiFailure.Message, "本地 control API 拒绝了请求。请检查已保存配置后重试。"),
                actionLabel: "查看日志",
                actionKey: DesktopHealthActionKeys.ShowLogs),
            BackendControlApiFailureKind.Unknown => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Warning,
                stateText: "响应异常",
                detail: DefaultIfBlank(controlApiFailure.Message, "本地 control API 返回了意外结果。"),
                actionLabel: "重新加载",
                actionKey: DesktopHealthActionKeys.ReloadConfig),
            BackendControlApiFailureKind.Incompatible => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Error,
                stateText: "版本不兼容",
                detail: DefaultIfBlank(controlApiFailure.Message, "本地 control API 版本过旧，无法与当前 desktop 配置协议兼容。"),
                isBlocking: true,
                actionLabel: "打开 backend 目录",
                actionKey: DesktopHealthActionKeys.OpenBackendFolder),
            _ => CreateCheck(
                key: "control-api",
                title: "Control API",
                state: DesktopHealthState.Warning,
                stateText: "离线",
                detail: "Desktop 仍能读取本地配置，但当前没有连上实时 backend control API。",
                actionLabel: "启动后端",
                actionKey: DesktopHealthActionKeys.StartBackend)
        };
    }

    private static DesktopHealthCheckItem BuildOpenAiCheck(BotConfig config)
    {
        if (HasText(config.OpenAiApiKey) || HasText(config.OpenAiDefaultApiKey))
        {
            return CreateCheck(
                key: "openai",
                title: "模型凭据",
                state: DesktopHealthState.Good,
                stateText: "已配置",
                detail: "当前 runtime 路由至少已经有一个 OpenAI 兼容 API key。");
        }

        return CreateCheck(
            key: "openai",
            title: "模型凭据",
            state: DesktopHealthState.Error,
            stateText: "缺少 API Key",
            detail: "先补 OPENAI_API_KEY 或 OPENAI_DEFAULT_API_KEY，再启动 runtime。",
            isBlocking: true,
            actionLabel: "查看 API 密钥",
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
                stateText: "缺少 Token",
                detail: "先设置 NAPCAT_TOKEN，QQ worker 才能向 NapCat 鉴权。",
                isBlocking: true,
                actionLabel: "查看 NapCat Token",
                actionKey: DesktopHealthActionKeys.FocusNapCatToken);
        }

        if (!LooksLikeWebSocketUrl(config.NapCatWsUrl))
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Error,
                stateText: "WS 地址无效",
                detail: "NAPCAT_WS_URL 应该是 backend 可访问的 ws:// 或 wss:// 地址。",
                isBlocking: true,
                actionLabel: "查看 NapCat 地址",
                actionKey: DesktopHealthActionKeys.FocusNapCatUrl);
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Info,
                stateText: "等待后端",
                detail: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized
                    ? "在 desktop 重新连回本地 control API 前，QQ 就绪状态暂时未知。"
                    : $"后端启动并连上 {config.NapCatWsUrl} 的 NapCat 后，QQ 就会尝试上线。",
                actionLabel: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? "查看日志" : "启动后端",
                actionKey: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? DesktopHealthActionKeys.ShowLogs : DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.RuntimeReady == true && runtimeSnapshot.RuntimeActive == true)
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Good,
                stateText: "已就绪",
                detail: $"QQ worker 已通过 {config.NapCatWsUrl} 上的 NapCat 建立连接。");
        }

        if (runtimeSnapshot.RuntimeActive == true)
        {
            return CreateCheck(
                key: "qq",
                title: "QQ / NapCat",
                state: DesktopHealthState.Warning,
                stateText: "连接中",
                detail: $"Backend 已在运行，但 QQ 仍在等待连接 {config.NapCatWsUrl} 的 NapCat。",
                actionLabel: "检查 NapCat 配置",
                actionKey: DesktopHealthActionKeys.FocusNapCatUrl);
        }

        return CreateCheck(
            key: "qq",
            title: "QQ / NapCat",
            state: DesktopHealthState.Warning,
            stateText: "未启动",
            detail: $"QQ 配置看起来有效。启动后端后，会尝试连接 {config.NapCatWsUrl} 的 NapCat。",
            actionLabel: "启动后端",
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
                title: "微信桥接",
                state: DesktopHealthState.Info,
                stateText: "可选 / 已关闭",
                detail: "在配置 WECHAT_BRIDGE_URL 前，微信会保持关闭。QQ 可独立运行。",
                actionLabel: "配置微信",
                actionKey: DesktopHealthActionKeys.FocusWechatUrl);
        }

        if (!LooksLikeWebSocketUrl(config.WechatBridgeUrl))
        {
            return CreateCheck(
                key: "wechat",
                title: "微信桥接",
                state: DesktopHealthState.Warning,
                stateText: "WS 地址无效",
                detail: "WECHAT_BRIDGE_URL 应使用 ws:// 或 wss://。先修好地址，再期待微信 worker 启动。",
                actionLabel: "查看微信配置",
                actionKey: DesktopHealthActionKeys.FocusWechatUrl);
        }

        if (runtimeSnapshot.ControlApiReachable != true)
        {
            return CreateCheck(
                key: "wechat",
                title: "微信桥接",
                state: DesktopHealthState.Info,
                stateText: "已配置",
                detail: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized
                    ? "在 desktop 重新连回 control API 前，微信桥接状态暂时未知。"
                    : $"微信已配置为 {config.WechatBridgeUrl}，后端启动后会尝试上线。",
                actionLabel: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? "查看日志" : "启动后端",
                actionKey: controlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized ? DesktopHealthActionKeys.ShowLogs : DesktopHealthActionKeys.StartBackend);
        }

        if (runtimeSnapshot.WechatBridgeConnected == true && runtimeSnapshot.WechatRuntimeReady == true)
        {
            return CreateCheck(
                key: "wechat",
                title: "微信桥接",
                state: DesktopHealthState.Good,
                stateText: "已就绪",
                detail: $"微信 worker 已连接到 {config.WechatBridgeUrl}。");
        }

        if (runtimeSnapshot.WechatRuntimeActive == true || runtimeSnapshot.WechatConfigured == true)
        {
            return CreateCheck(
                key: "wechat",
                title: "微信桥接",
                state: DesktopHealthState.Warning,
                stateText: "等待桥接",
                detail: $"微信已配置，但 worker 仍在等待连接 {config.WechatBridgeUrl} 的桥接。",
                actionLabel: "查看微信配置",
                actionKey: DesktopHealthActionKeys.FocusWechatUrl);
        }

        return CreateCheck(
            key: "wechat",
            title: "微信桥接",
            state: DesktopHealthState.Warning,
            stateText: "已配置但未启动",
            detail: $"微信桥接已配置为 {config.WechatBridgeUrl}，但 worker 还没有运行。",
            actionLabel: "启动后端",
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
                title: "常驻模式",
                state: DesktopHealthState.Info,
                stateText: "稍后启用",
                detail: "先选定 backend 目录。设置完成后，再启用开机启动，这样 desktop 会以最小化方式恢复，更方便接回 runtime。",
                actionLabel: string.Empty,
                actionKey: string.Empty);
        }

        if (autoStartEnabled)
        {
            return CreateCheck(
                key: "resident-mode",
                title: "常驻模式",
                state: DesktopHealthState.Good,
                stateText: "随 Windows 启动",
                detail: "Desktop 已开启开机启动。登录后会以最小化方式恢复，并保留托盘入口。关闭窗口仍只会收进托盘；如果要让 runtime 真正离线，请使用“停止后端”。");
        }

        return CreateCheck(
            key: "resident-mode",
            title: "常驻模式",
            state: DesktopHealthState.Info,
            stateText: "手动启动",
            detail: "Desktop 开机启动当前关闭。最小化或关闭窗口仍会收进托盘，退出控制台只会关闭这个壳；如果你希望登录后自动恢复，请启用开机启动。",
            actionLabel: "启用开机启动",
            actionKey: DesktopHealthActionKeys.ToggleAutoStart);
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
}

public sealed record DesktopHealthChecklistSnapshot
{
    public string ChecklistStatus { get; init; } = string.Empty;

    public IReadOnlyList<DesktopHealthCheckItem> Checks { get; init; } = [];
}
