using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopOperationErrorFormatter
{
    public static DesktopUserFacingOperationError Build(
        string operationLabel,
        string fallbackStatusText,
        string technicalMessage,
        BackendControlApiFailure? controlApiFailure = null,
        string? configPath = null,
        string? bootstrapEnvPath = null,
        bool localControlSettingsOperation = false,
        bool canStartBackend = false)
    {
        var detail = DefaultIfBlank(controlApiFailure?.Message, technicalMessage);

        if ((controlApiFailure?.Kind ?? BackendControlApiFailureKind.None) == BackendControlApiFailureKind.None &&
            detail.Contains("legacy config contract", StringComparison.OrdinalIgnoreCase))
        {
            return BuildIncompatibleError(
                operationLabel,
                fallbackStatusText,
                detail);
        }

        if (localControlSettingsOperation)
        {
            return new DesktopUserFacingOperationError
            {
                StatusText = fallbackStatusText,
                DialogTitle = $"{operationLabel}失败",
                DialogMessage = BuildDialogMessage(
                    whatHappened: $"{operationLabel}无法更新这台机器上的本地 desktop 附着设置。",
                    whyText: "这个动作只会改 desktop 如何重新接回本地 control API，本身不会修改 QQ、微信或模型设置。",
                    doNowText: "检查 backend 的 .env 文件是否可写，然后重新保存本地控制设置。",
                    improvementText: "完成后，这个窗口就能在不改通道配置的前提下重新附着到本地 runtime。",
                    technicalDetail: detail,
                    additionalContext:
                    [
                        string.IsNullOrWhiteSpace(bootstrapEnvPath) ? null : $"目标文件：{bootstrapEnvPath}"
                    ]),
                SuggestedActionLabel = "查看本机令牌",
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
                bootstrapEnvPath),
            BackendControlApiFailureKind.Rejected => BuildRejectedError(
                operationLabel,
                fallbackStatusText,
                detail,
                configPath),
            BackendControlApiFailureKind.Incompatible => BuildIncompatibleError(
                operationLabel,
                fallbackStatusText,
                detail),
            BackendControlApiFailureKind.Unknown => BuildUnknownError(
                operationLabel,
                fallbackStatusText,
                detail,
                configPath),
            BackendControlApiFailureKind.Unreachable => BuildUnreachableError(
                operationLabel,
                fallbackStatusText,
                detail,
                configPath,
                canStartBackend),
            _ => new DesktopUserFacingOperationError
            {
                StatusText = fallbackStatusText,
                DialogTitle = $"{operationLabel}失败",
                DialogMessage = BuildDialogMessage(
                    whatHappened: $"{operationLabel}失败。",
                    whyText: "Desktop 当前拿到的信息不足，无法把这次失败清楚归类。",
                    doNowText: "先查看日志面板，再从这个窗口重试。",
                    improvementText: "只要失败步骤恢复正常，desktop 就能继续正常控制。",
                    technicalDetail: DefaultIfBlank(technicalMessage, "没有捕获到额外错误细节。")),
                SuggestedActionLabel = "查看日志",
                SuggestedActionKey = DesktopHealthActionKeys.ShowLogs
            }
        };
    }

    private static DesktopUserFacingOperationError BuildUnauthorizedError(
        string operationLabel,
        string fallbackStatusText,
        string detail,
        string? bootstrapEnvPath)
    {
        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}：本地控制令牌不一致",
            DialogTitle = "需要本地控制令牌",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel}无法继续，因为 desktop 对本地 control API 的鉴权失败了。",
                whyText: "backend 的 .env 和这个 desktop 窗口当前保存的是不同的 QQ_AI_BOT_CONTROL_API_TOKEN。",
                doNowText: "打开这个窗口里的本机连接设置，让 QQ_AI_BOT_CONTROL_API_TOKEN 与 backend .env 保持一致，然后再重试。",
                improvementText: "完成后，这个窗口就能再次验证实时 runtime 状态，并继续发送启动、停止、加载或保存动作。",
                technicalDetail: detail,
                additionalContext:
                [
                    string.IsNullOrWhiteSpace(bootstrapEnvPath) ? null : $"本机连接 .env：{bootstrapEnvPath}"
                ]),
            SuggestedActionLabel = "查看本机令牌",
            SuggestedActionKey = DesktopHealthActionKeys.FocusControlApiToken
        };
    }

    private static DesktopUserFacingOperationError BuildRejectedError(
        string operationLabel,
        string fallbackStatusText,
        string detail,
        string? configPath)
    {
        var guidance = ResolveRejectedGuidance(detail);

        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}：control API 拒绝了请求",
            DialogTitle = $"{operationLabel}被拒绝",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel}已经到达本地 control API，但 backend 拒绝了当前值。",
                whyText: guidance.WhyText,
                doNowText: $"先检查下面提到的字段，再重试。{guidance.NextStepText}",
                improvementText: guidance.ImprovementText,
                technicalDetail: detail,
                additionalContext:
                [
                    string.IsNullOrWhiteSpace(configPath) ? null : $"运行配置文件：{configPath}"
                ]),
            SuggestedActionLabel = guidance.Label,
            SuggestedActionKey = guidance.Key
        };
    }

    private static DesktopUserFacingOperationError BuildUnknownError(
        string operationLabel,
        string fallbackStatusText,
        string detail,
        string? configPath)
    {
        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}：control API 响应异常",
            DialogTitle = $"{operationLabel}返回了意外响应",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel}已经到达本地 control API，但返回结果无法被正确识别。",
                whyText: "Desktop 原本预期收到正常的控制面响应，但 backend 返回了不完整或意外的数据。",
                doNowText: "先从这个窗口重新加载配置，然后再重试同一个动作。",
                improvementText: "完成后，desktop 可以先重建一份干净的 runtime 视图，再继续操作。",
                technicalDetail: detail,
                additionalContext:
                [
                    string.IsNullOrWhiteSpace(configPath) ? null : $"运行配置文件：{configPath}"
                ]),
            SuggestedActionLabel = "重新加载配置",
            SuggestedActionKey = DesktopHealthActionKeys.ReloadConfig
        };
    }

    private static DesktopUserFacingOperationError BuildIncompatibleError(
        string operationLabel,
        string fallbackStatusText,
        string detail)
    {
        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}：control API 版本不兼容",
            DialogTitle = "本地 backend 版本过旧",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel}已经到达本地 control API，但当前 backend 返回的是旧版配置协议。",
                whyText: "这个 backend 仍然使用旧字段（例如 envPath），缺少当前 desktop 需要的 configPath 和新配置字段，所以保存后会丢失 DeepSeek 等较新的设置。",
                doNowText: "先停止当前 backend，然后从当前工作区或最新安装版本重新启动；如果你不确定位置，先打开 backend 目录。",
                improvementText: "完成后，这个窗口会和 backend 使用同一套新配置协议，保存后不会再把 DeepSeek 等字段回滚。",
                technicalDetail: detail),
            SuggestedActionLabel = "打开 backend 目录",
            SuggestedActionKey = DesktopHealthActionKeys.OpenBackendFolder
        };
    }

    private static DesktopUserFacingOperationError BuildUnreachableError(
        string operationLabel,
        string fallbackStatusText,
        string detail,
        string? configPath,
        bool canStartBackend)
    {
        return new DesktopUserFacingOperationError
        {
            StatusText = $"{fallbackStatusText}：无法连接 control API",
            DialogTitle = $"{operationLabel}无法连接本地 control API",
            DialogMessage = BuildDialogMessage(
                whatHappened: $"{operationLabel}无法继续，因为 desktop 当前没有附着到一个在线的 control API。",
                whyText: canStartBackend
                    ? "backend 很可能已经停止，所以这个窗口现在只能看到最近一次的本地文件状态。"
                    : "backend 可能还在恢复中，或者 desktop 暂时从实时 runtime 状态上脱开了。",
                doNowText: canStartBackend
                    ? "先从这个窗口启动 backend，等状态刷新后再重试。"
                    : "等 backend 恢复后重新加载配置；如果它本来就该在线，那下一步请检查日志面板。",
                improvementText: "完成后，desktop 就能刷新实时状态并继续控制 runtime。",
                technicalDetail: detail,
                additionalContext:
                [
                    string.IsNullOrWhiteSpace(configPath) ? null : $"运行配置文件：{configPath}"
                ]),
            SuggestedActionLabel = canStartBackend ? "启动后端" : "重新加载配置",
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
                "查看微信配置",
                DesktopHealthActionKeys.FocusWechatUrl,
                "当前保存的微信桥接设置还没有通过 backend 校验。",
                "打开这个窗口里的微信桥接设置，修正保存值后再重新保存。",
                "保存的桥接设置与有效在线桥接服务一致后，微信 worker 就能启动。");
        }

        if (detail.Contains("NAPCAT_WS_URL", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "查看 NapCat 地址",
                DesktopHealthActionKeys.FocusNapCatUrl,
                "当前保存的 NapCat websocket 地址还无效。",
                "打开 NapCat 设置，修正 NAPCAT_WS_URL，然后重新保存。",
                "保存的 NapCat 地址有效后，QQ 才能尝试连接。");
        }

        if (detail.Contains("NAPCAT_TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "查看 NapCat Token",
                DesktopHealthActionKeys.FocusNapCatToken,
                "QQ 仍然缺少向 NapCat 鉴权所需的凭据。",
                "打开 NapCat 设置，补上 NAPCAT_TOKEN，然后重新保存。",
                "完成后，QQ 在启动前就具备了所需凭据。");
        }

        if (detail.Contains("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_DEFAULT_API_KEY", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_DEFAULT_MODEL", StringComparison.OrdinalIgnoreCase) ||
            detail.Contains("OPENAI_ADVANCED_MODEL", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "查看 API 密钥",
                DesktopHealthActionKeys.FocusOpenAiDefaultKey,
                "当前保存的模型路由还缺少必需的 API 或模型值。",
                "打开 API 密钥区域，补齐缺失的模型或密钥值后再保存。",
                "必需值补齐后，runtime 才能调用配置好的模型路由。");
        }

        if (detail.Contains("DEEPSEEK_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "查看 DeepSeek API Key",
                DesktopHealthActionKeys.FocusDeepSeekApiKey,
                "当前保存的 DeepSeek 回退设置缺少必需的 API key。",
                "如果要启用 DeepSeek fallback，请先填写 DEEPSEEK_API_KEY，然后重新保存。",
                "DeepSeek fallback 的必需字段补齐后，保存的回退设置才能真正生效。");
        }

        if (detail.Contains("QQ_AI_BOT_CONTROL_API_TOKEN", StringComparison.OrdinalIgnoreCase))
        {
            return new RejectedGuidance(
                "查看本机令牌",
                DesktopHealthActionKeys.FocusControlApiToken,
                "backend 拒绝了当前保存的本地控制令牌值。",
                "打开本机连接设置，修正 QQ_AI_BOT_CONTROL_API_TOKEN，然后重试。",
                "完成后，这个 desktop 就能重新接回 runtime。");
        }

        return new RejectedGuidance(
            "重新加载配置",
            DesktopHealthActionKeys.ReloadConfig,
            "当前值还没有通过 backend 校验。",
            "先重新加载配置，再检查技术细节里提到的字段，然后重试。",
            "无效值修正后，desktop 就能保存一份干净的配置。");
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
            "发生了什么",
            whatHappened
        };

        foreach (var contextLine in additionalContext.Where(static line => !string.IsNullOrWhiteSpace(line)))
        {
            lines.Add(contextLine!);
        }

        lines.Add(string.Empty);
        lines.Add("原因");
        lines.Add(whyText);
        lines.Add(string.Empty);
        lines.Add("现在这样做");
        lines.Add(doNowText);
        lines.Add(string.Empty);
        lines.Add("完成后会改善什么");
        lines.Add(improvementText);
        lines.Add(string.Empty);
        lines.Add("技术细节");
        lines.Add(DefaultIfBlank(technicalDetail, "没有捕获到额外错误细节。"));

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
