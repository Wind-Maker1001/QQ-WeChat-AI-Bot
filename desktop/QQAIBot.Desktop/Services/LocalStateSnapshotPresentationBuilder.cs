using System.Linq;
using System.IO;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class LocalStateSnapshotPresentationBuilder
{
    private const string ControlApiTokenEnvKey = "QQ_AI_BOT_CONTROL_API_TOKEN";

    public static LocalStateSnapshotSelectionPresentation BuildSelectionPresentation(
        LocalStateSnapshotDescriptor? snapshot,
        LocalStateSnapshotPreviewResult? preview,
        string? diffTextOverride = null,
        string? adviceTextOverride = null)
    {
        if (snapshot is null)
        {
            return new LocalStateSnapshotSelectionPresentation
            {
                SummaryText = "选择一个快照以查看或恢复。",
                DetailText = "快照详情会显示在这里。",
                ImpactText = "恢复会覆盖这个快照中展示的状态。删除会永久移除当前选中的归档。",
                DiffText = diffTextOverride ?? "选择一个快照以预览差异。",
                AdviceText = adviceTextOverride ?? "恢复建议会显示在这里。",
                SafetyHeadlineText = "恢复前检查：先选择一个快照，看看会覆盖哪些当前状态。",
                SafetyRecommendationText = "建议的第一步：先选中快照查看覆盖风险，再决定是否恢复。",
                RollbackHintText = "安全回滚快照会保留当前状态的回退点，并且不复制 .env 密钥。"
            };
        }

        return new LocalStateSnapshotSelectionPresentation
        {
            SummaryText = snapshot.Summary,
            DetailText = snapshot.Detail,
            ImpactText = BuildSelectionImpactText(snapshot),
            DiffText = diffTextOverride ?? BuildSelectionDiffText(preview),
            AdviceText = adviceTextOverride ?? BuildSelectionAdviceText(preview),
            SafetyHeadlineText = BuildSelectionSafetyHeadline(snapshot, preview),
            SafetyRecommendationText = BuildSelectionSafetyRecommendation(snapshot, preview),
            RollbackHintText = BuildSelectionRollbackHint(snapshot, preview)
        };
    }

    public static LocalStateSnapshotRestorePresentation BuildRestorePresentation(
        LocalStateSnapshotRestoreResult? restoreResult,
        LocalStateSnapshotPreviewResult? preview,
        LocalStateSnapshotRestoreRuntimeContext runtimeContext)
    {
        if (restoreResult is null)
        {
            return LocalStateSnapshotRestorePresentation.Empty;
        }

        var changedTrackedEnvKeys = GetChangedTrackedEnvKeys(preview);
        var restoredEnv = DidRestoreOverwriteEnv(restoreResult);
        var restoredSessionStore = DidRestoreRestoreSessionStore(restoreResult);
        var changedControlApiToken = changedTrackedEnvKeys.Contains(ControlApiTokenEnvKey, StringComparer.OrdinalIgnoreCase);
        var changedNapCatSettings = changedTrackedEnvKeys.Any(
            static key => key.StartsWith("NAPCAT_", StringComparison.OrdinalIgnoreCase));
        var changedWechatSettings = changedTrackedEnvKeys.Any(
            static key => key.StartsWith("WECHAT_", StringComparison.OrdinalIgnoreCase));
        var effectiveWechatRuntimeReady =
            !runtimeContext.IsWechatConfigured || runtimeContext.IsWechatRuntimeReady;
        var actions = BuildRestoreActions(
            runtimeContext,
            restoredEnv,
            changedControlApiToken,
            changedNapCatSettings,
            changedWechatSettings,
            effectiveWechatRuntimeReady);

        return new LocalStateSnapshotRestorePresentation
        {
            SummaryText = BuildRestoreSummaryText(restoreResult, preview),
            IssueText = BuildRestoreIssueText(
                runtimeContext,
                restoredEnv,
                restoredSessionStore,
                changedControlApiToken,
                changedNapCatSettings,
                changedWechatSettings,
                effectiveWechatRuntimeReady),
            TargetsText = BuildRestoreTargetsText(restoreResult),
            SessionsText = BuildRestoreSessionSummaryText(preview),
            LatestActivityText = BuildRestoreLatestActivitySummaryText(preview),
            AdviceText = BuildRestoreAdviceText(preview),
            ControlPlaneText = BuildRestoreControlPlaneText(
                runtimeContext,
                changedControlApiToken,
                restoredEnv),
            RuntimeText = BuildRestoreRuntimeText(
                runtimeContext,
                restoredEnv,
                restoredSessionStore,
                changedNapCatSettings,
                changedWechatSettings,
                effectiveWechatRuntimeReady),
            NextStepText = BuildRestoreNextStepText(
                actions,
                runtimeContext,
                restoredEnv,
                restoredSessionStore,
                changedControlApiToken,
                changedNapCatSettings,
                changedWechatSettings),
            PrimaryAction = actions.ElementAtOrDefault(0) ?? LocalStateSnapshotRestoreAction.Empty,
            SecondaryAction = actions.ElementAtOrDefault(1) ?? LocalStateSnapshotRestoreAction.Empty,
            TertiaryAction = actions.ElementAtOrDefault(2) ?? LocalStateSnapshotRestoreAction.Empty
        };
    }

    private static string BuildSelectionImpactText(LocalStateSnapshotDescriptor snapshot)
    {
        var impactParts = new List<string>();

        if (SnapshotIncludesEntry(snapshot, "app/.env"))
        {
            impactParts.Add(".env");
        }

        if (SnapshotIncludesPrefix(snapshot, "app/data/"))
        {
            impactParts.Add("data/");
        }

        if (SnapshotIncludesEntry(snapshot, "desktop/activity-state.json"))
        {
            impactParts.Add("桌面活动状态");
        }

        var overwriteTargetText = impactParts.Count > 0
            ? string.Join(", ", impactParts)
            : "这个快照里列出的文件";
        var secretRiskText = snapshot.IncludesSecrets
            ? "这个快照包含 .env 密钥。"
            : "这个快照未标记包含 .env 密钥。";

        return $"恢复这个快照会覆盖 {overwriteTargetText}。{secretRiskText}";
    }

    private static string BuildSelectionDiffText(LocalStateSnapshotPreviewResult? preview)
    {
        return preview?.Lines is { Count: > 0 }
            ? string.Join(Environment.NewLine, preview.Lines)
            : "选择一个快照以预览差异。";
    }

    private static string BuildSelectionAdviceText(LocalStateSnapshotPreviewResult? preview)
    {
        return preview?.Recommendations is { Count: > 0 }
            ? string.Join(Environment.NewLine, preview.Recommendations)
            : "恢复建议会显示在这里。";
    }

    private static string BuildSelectionSafetyHeadline(
        LocalStateSnapshotDescriptor snapshot,
        LocalStateSnapshotPreviewResult? preview)
    {
        var hasMeaningfulDiff = PreviewHasMeaningfulDiff(preview);
        var overwritesEnv = SnapshotIncludesEntry(snapshot, "app/.env");
        var overwritesData = SnapshotIncludesPrefix(snapshot, "app/data/");
        var includesSecrets = snapshot.IncludesSecrets;

        if (hasMeaningfulDiff && (overwritesEnv || overwritesData) && includesSecrets)
        {
            return "恢复前检查：高风险。这个操作会覆盖当前 runtime 状态，而且归档里包含 .env 密钥。";
        }

        if (hasMeaningfulDiff && (overwritesEnv || overwritesData))
        {
            return "恢复前检查：请仔细确认。这个操作会覆盖这台机器上的当前 runtime 状态。";
        }

        if (includesSecrets)
        {
            return "恢复前检查：恢复影响较小，但这个归档包含 .env 密钥，请仅在本机保留。";
        }

        return "恢复前检查：当前状态看起来已经和这个快照接近。";
    }

    private static string BuildSelectionSafetyRecommendation(
        LocalStateSnapshotDescriptor snapshot,
        LocalStateSnapshotPreviewResult? preview)
    {
        if (PreviewHasMeaningfulDiff(preview))
        {
            return "建议的第一步：先导出一份当前状态的安全回滚快照。这样会保留当前会话和桌面活动，但不会复制 .env 密钥，而且当前选中的恢复目标不会变。";
        }

        return snapshot.IncludesSecrets
            ? "建议的第一步：只有在你确实要回滚到这个保存状态时再恢复，并且不要分享这个包含 .env 密钥的归档。"
            : "建议的第一步：先看下面的差异，再决定是否真的要回滚。";
    }

    private static string BuildSelectionRollbackHint(
        LocalStateSnapshotDescriptor snapshot,
        LocalStateSnapshotPreviewResult? preview)
    {
        if (PreviewHasMeaningfulDiff(preview))
        {
            return "回滚保护：安全回滚快照会保存当前的 data/ 和桌面活动状态，但不会复制 .env 密钥，因此后悔时更容易退回。";
        }

        return snapshot.IncludesSecrets
            ? "回滚保护：这个归档本身已经包含密钥，所以在测试任何恢复流程前，优先再导出一份新的安全回滚快照。"
            : "回滚保护：如果你还想保留一个容易退回的落点，先导出安全回滚快照。";
    }

    private static string BuildRestoreSummaryText(
        LocalStateSnapshotRestoreResult restoreResult,
        LocalStateSnapshotPreviewResult? preview)
    {
        var restoredTargets = new List<string>();

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "app/.env", StringComparison.Ordinal)))
        {
            restoredTargets.Add(".env");
        }

        if (restoreResult.RestoredEntries.Any(static entry => entry.StartsWith("app/data/", StringComparison.Ordinal)))
        {
            restoredTargets.Add("data/");
        }

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "desktop/activity-state.json", StringComparison.Ordinal)))
        {
            restoredTargets.Add("桌面活动状态");
        }

        var targetText = restoredTargets.Count > 0
            ? string.Join(", ", restoredTargets)
            : "受跟踪的状态文件";
        var archiveName = Path.GetFileName(restoreResult.ArchivePath);
        var sessionCountLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json 会话数：", StringComparison.Ordinal));
        var latestActivityLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json 最近活动：", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(sessionCountLine) && string.IsNullOrWhiteSpace(latestActivityLine))
        {
            return $"已从 {archiveName} 恢复 {targetText}。";
        }

        var summaryParts = new List<string>
        {
            $"已从 {archiveName} 恢复 {targetText}。"
        };

        if (!string.IsNullOrWhiteSpace(sessionCountLine))
        {
            summaryParts.Add(
                $"当前会话数应与快照一致：{ExtractSnapshotSide(sessionCountLine)} 个会话。");
        }

        if (!string.IsNullOrWhiteSpace(latestActivityLine))
        {
            summaryParts.Add(
                $"当前最近会话活动应与快照一致：{ExtractSnapshotSide(latestActivityLine)}。");
        }

        return string.Join(" ", summaryParts);
    }

    private static string BuildRestoreIssueText(
        LocalStateSnapshotRestoreRuntimeContext runtimeContext,
        bool restoredEnv,
        bool restoredSessionStore,
        bool changedControlApiToken,
        bool changedNapCatSettings,
        bool changedWechatSettings,
        bool effectiveWechatRuntimeReady)
    {
        if (runtimeContext.ControlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return changedControlApiToken
                ? "问题：这次恢复改动了本地控制令牌，所以 desktop 当前保存的令牌已经和 backend 不一致。"
                : "问题：恢复后 desktop 还无法重新接回，因为本地 control API 令牌已经不匹配。";
        }

        if (!runtimeContext.IsControlApiReachable)
        {
            return runtimeContext.CanStartBackend
                ? "问题：恢复已经完成，但 backend 宿主当前已停止，所以 desktop 还接不回去。"
                : "问题：恢复已经完成，但 desktop 仍在等待重新接回本地 control API。";
        }

        if (runtimeContext.CanStartBackend)
        {
            return restoredEnv
                ? "问题：恢复已经完成，而且快照改动了 runtime 设置，但 backend 宿主当前已停止。"
                : "问题：恢复后 backend 宿主当前仍处于停止状态。";
        }

        if (!runtimeContext.IsQqRuntimeReady)
        {
            return changedNapCatSettings
                ? "问题：QQ 还没有 ready，因为恢复的快照改动了 NapCat 设置，而 runtime 还没有重新连上。"
                : "问题：backend 已在运行，但 QQ 在恢复后仍未 ready。请检查 NapCat 连接设置。";
        }

        if (runtimeContext.IsWechatConfigured && !effectiveWechatRuntimeReady)
        {
            return changedWechatSettings
                ? "问题：微信仍未 ready，因为恢复的快照改动了桥接设置，而 worker 还没有重新连上。"
                : "问题：微信已配置，但桥接在恢复后仍未 ready。请检查桥接设置。";
        }

        return restoredEnv || restoredSessionStore
            ? "问题：当前没有发现立即需要处理的恢复后问题，恢复出来的状态已经可以开始核对。"
            : "问题：当前没有发现立即需要处理的恢复后问题。";
    }

    private static string BuildRestoreTargetsText(LocalStateSnapshotRestoreResult restoreResult)
    {
        var restoredTargets = new List<string>();

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "app/.env", StringComparison.Ordinal)))
        {
            restoredTargets.Add(".env");
        }

        if (restoreResult.RestoredEntries.Any(static entry => entry.StartsWith("app/data/", StringComparison.Ordinal)))
        {
            restoredTargets.Add("data/");
        }

        if (restoreResult.RestoredEntries.Any(static entry => string.Equals(entry, "desktop/activity-state.json", StringComparison.Ordinal)))
        {
            restoredTargets.Add("桌面活动状态");
        }

        return restoredTargets.Count > 0
            ? $"恢复目标：{string.Join(", ", restoredTargets)}"
            : "恢复目标：受跟踪的状态文件";
    }

    private static string BuildRestoreSessionSummaryText(LocalStateSnapshotPreviewResult? preview)
    {
        var sessionCountLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json 会话数：", StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(sessionCountLine)
            ? "会话摘要：暂无"
            : $"会话摘要：{sessionCountLine}";
    }

    private static string BuildRestoreLatestActivitySummaryText(LocalStateSnapshotPreviewResult? preview)
    {
        var latestActivityLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith("sessions.json 最近活动：", StringComparison.Ordinal));

        return string.IsNullOrWhiteSpace(latestActivityLine)
            ? "最近活动摘要：暂无"
            : $"最近活动摘要：{latestActivityLine}";
    }

    private static string BuildRestoreAdviceText(LocalStateSnapshotPreviewResult? preview)
    {
        if (preview?.Recommendations is not { Count: > 0 })
        {
            return "恢复后建议：检查恢复出来的状态，确认它和你的预期一致。";
        }

        return $"恢复后建议：{string.Join(" ", preview.Recommendations)}";
    }

    private static string BuildRestoreControlPlaneText(
        LocalStateSnapshotRestoreRuntimeContext runtimeContext,
        bool changedControlApiToken,
        bool restoredEnv)
    {
        if (runtimeContext.ControlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return changedControlApiToken
                ? "控制面检查：恢复的快照把 QQ_AI_BOT_CONTROL_API_TOKEN 改成了另一个值，所以这个窗口必须先在本地保存相同的令牌，才能重新控制 runtime。"
                : "控制面检查：backend 拒绝了本地 desktop 令牌。请先在这个窗口里保存匹配的 QQ_AI_BOT_CONTROL_API_TOKEN，再重新加载。";
        }

        if (!runtimeContext.IsControlApiReachable)
        {
            return runtimeContext.CanStartBackend
                ? "控制面检查：backend 宿主已停止，所以本地 control API 在重新启动前都会离线。"
                : "控制面检查：desktop 仍在等待本地 control API 恢复。重新接回之前，runtime 状态可能还是旧的。";
        }

        return "控制面检查：desktop 已连上本地 control API，现在可以继续执行后续动作。";
    }

    private static string BuildRestoreRuntimeText(
        LocalStateSnapshotRestoreRuntimeContext runtimeContext,
        bool restoredEnv,
        bool restoredSessionStore,
        bool changedNapCatSettings,
        bool changedWechatSettings,
        bool effectiveWechatRuntimeReady)
    {
        if (runtimeContext.ControlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            return "运行时检查：backend 可能还在运行，但在本地控制令牌重新一致之前，这个窗口无法确认 QQ 或微信是否 ready。";
        }

        if (!runtimeContext.IsControlApiReachable)
        {
            return runtimeContext.CanStartBackend
                ? "运行时检查：backend 当前已停止，所以恢复后的 QQ 和微信设置还没有生效。"
                : "运行时检查：desktop 还没有接上实时 runtime 状态，因此暂时无法确认 QQ 和微信状态。";
        }

        if (runtimeContext.CanStartBackend)
        {
            return restoredEnv
                ? "运行时检查：backend 当前已停止，所以恢复后的通道设置和会话状态都还在等待生效。"
                : "运行时检查：backend 当前已停止，所以 QQ 和微信都还没有激活。";
        }

        if (!runtimeContext.IsQqRuntimeReady)
        {
            return changedNapCatSettings
                ? "运行时检查：QQ 仍在等待 NapCat，而且这个快照改动了 NapCat 设置。请确认保存下来的 URL 和令牌仍然和在线的 NapCat 服务一致。"
                : "运行时检查：QQ 仍在等待 NapCat。请确认服务在线，并且保存的 URL 或令牌是正确的。";
        }

        if (runtimeContext.IsWechatConfigured && !effectiveWechatRuntimeReady)
        {
            return changedWechatSettings
                ? "运行时检查：QQ 已 ready，但微信仍在等待恢复后的桥接设置与在线桥接服务重新对上。"
                : "运行时检查：QQ 已 ready，微信也已配置，但桥接仍未 ready。";
        }

        return restoredSessionStore
            ? "运行时检查：QQ 和微信状态看起来正常，恢复后的会话存储现在也应该已经生效。"
            : "运行时检查：恢复后 QQ 和微信状态看起来正常。";
    }

    private static string BuildRestoreNextStepText(
        IReadOnlyList<LocalStateSnapshotRestoreAction> actions,
        LocalStateSnapshotRestoreRuntimeContext runtimeContext,
        bool restoredEnv,
        bool restoredSessionStore,
        bool changedControlApiToken,
        bool changedNapCatSettings,
        bool changedWechatSettings)
    {
        var primaryAction = actions.FirstOrDefault();

        if (primaryAction is null || string.IsNullOrWhiteSpace(primaryAction.Key))
        {
            return "下一步：检查当前状态和最近活动。";
        }

        return primaryAction.Key switch
        {
            DesktopHealthActionKeys.FocusControlApiToken => changedControlApiToken
                ? "下一步：先在这个窗口里更新本地控制令牌，让它和恢复后的 backend .env 保持一致，然后重新加载配置。"
                : "下一步：打开本地控制设置，确认 QQ_AI_BOT_CONTROL_API_TOKEN 与 backend 一致，然后重新加载配置。",
            DesktopHealthActionKeys.StartBackend => restoredEnv || restoredSessionStore
                ? "下一步：启动 backend，让恢复后的配置和会话状态重新变成在线状态。"
                : "下一步：直接从这个窗口启动 backend。",
            DesktopHealthActionKeys.FocusNapCatUrl => changedNapCatSettings
                ? "下一步：核对恢复后的 NapCat URL 和令牌，再确认在线的 NapCat 服务仍然与它们一致。"
                : "下一步：检查 NapCat 连通性；如果你改动了保存的设置，再重新加载配置。",
            DesktopHealthActionKeys.FocusWechatUrl => changedWechatSettings
                ? "下一步：核对恢复后的微信桥接设置，并确认桥接服务在线。"
                : "下一步：检查微信桥接服务；如果你改动了保存的设置，再重新加载配置。",
            DesktopHealthActionKeys.ReloadConfig => !runtimeContext.IsControlApiReachable
                ? "下一步：等本地 control API 恢复后重新加载配置，让这个窗口刷新实时状态。"
                : "下一步：重新加载配置，确认恢复出来的状态。",
            _ => $"下一步：{primaryAction.Label}"
        };
    }

    private static IReadOnlyList<LocalStateSnapshotRestoreAction> BuildRestoreActions(
        LocalStateSnapshotRestoreRuntimeContext runtimeContext,
        bool restoredEnv,
        bool changedControlApiToken,
        bool changedNapCatSettings,
        bool changedWechatSettings,
        bool effectiveWechatRuntimeReady)
    {
        var actions = new List<LocalStateSnapshotRestoreAction>();

        void addAction(string label, string key)
        {
            if (string.IsNullOrWhiteSpace(label) || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            if (actions.Any((action) => string.Equals(action.Key, key, StringComparison.Ordinal)))
            {
                return;
            }

            actions.Add(new LocalStateSnapshotRestoreAction(label, key));
        }

        if (runtimeContext.ControlApiFailure.Kind == BackendControlApiFailureKind.Unauthorized)
        {
            addAction(
                changedControlApiToken ? "更新本地控制令牌" : "打开本地控制设置",
                DesktopHealthActionKeys.FocusControlApiToken);
            addAction("重新加载配置", DesktopHealthActionKeys.ReloadConfig);

            if (runtimeContext.CanStartBackend)
            {
                addAction("启动后端", DesktopHealthActionKeys.StartBackend);
            }

            return actions;
        }

        if (!runtimeContext.IsControlApiReachable)
        {
            if (runtimeContext.CanStartBackend)
            {
                addAction("启动后端", DesktopHealthActionKeys.StartBackend);
                addAction("重新加载配置", DesktopHealthActionKeys.ReloadConfig);
            }
            else
            {
                addAction("重新加载配置", DesktopHealthActionKeys.ReloadConfig);
            }

            if (restoredEnv)
            {
                addAction("打开本地控制设置", DesktopHealthActionKeys.FocusControlApiToken);
            }

            return actions;
        }

        if (runtimeContext.CanStartBackend)
        {
            addAction("启动后端", DesktopHealthActionKeys.StartBackend);
            addAction("重新加载配置", DesktopHealthActionKeys.ReloadConfig);
            addAction("打开本地控制设置", DesktopHealthActionKeys.FocusControlApiToken);
            return actions;
        }

        if (!runtimeContext.IsQqRuntimeReady || !effectiveWechatRuntimeReady)
        {
            if (!runtimeContext.IsQqRuntimeReady)
            {
                addAction(
                    changedNapCatSettings ? "查看恢复后的 NapCat 设置" : "检查 NapCat 配置",
                    DesktopHealthActionKeys.FocusNapCatUrl);
                addAction("重新加载配置", DesktopHealthActionKeys.ReloadConfig);
                return actions;
            }

            addAction(
                changedWechatSettings ? "查看恢复后的微信桥接" : "检查微信桥接",
                DesktopHealthActionKeys.FocusWechatUrl);
            addAction("重新加载配置", DesktopHealthActionKeys.ReloadConfig);
            return actions;
        }

        addAction("重新加载配置", DesktopHealthActionKeys.ReloadConfig);
        addAction("打开本地控制设置", DesktopHealthActionKeys.FocusControlApiToken);
        return actions;
    }

    private static bool PreviewHasMeaningfulDiff(LocalStateSnapshotPreviewResult? preview)
    {
        if (preview?.Lines is not { Count: > 0 })
        {
            return false;
        }

        return preview.Lines.Any((line) =>
            line.Contains("与当前状态不同", StringComparison.Ordinal) ||
            line.Contains("当前文件缺失，恢复时会补回", StringComparison.Ordinal) ||
            line.Contains("当前目录缺失，恢复时会补回", StringComparison.Ordinal) ||
            (line.Contains("-> 快照 ", StringComparison.Ordinal) &&
             !line.EndsWith("-> 快照 <missing>", StringComparison.Ordinal)) ||
            (line.StartsWith("sessions.json 变更会话：", StringComparison.Ordinal) &&
             !line.EndsWith("无", StringComparison.Ordinal)));
    }

    private static bool SnapshotIncludesEntry(LocalStateSnapshotDescriptor snapshot, string entry)
    {
        return snapshot.IncludedEntries.Any(
            (includedEntry) => string.Equals(includedEntry, entry, StringComparison.Ordinal));
    }

    private static bool SnapshotIncludesPrefix(LocalStateSnapshotDescriptor snapshot, string prefix)
    {
        return snapshot.IncludedEntries.Any(
            (includedEntry) => includedEntry.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool DidRestoreOverwriteEnv(LocalStateSnapshotRestoreResult restoreResult)
    {
        return restoreResult.RestoredEntries.Any(
            static entry => string.Equals(entry, "app/.env", StringComparison.Ordinal));
    }

    private static bool DidRestoreRestoreSessionStore(LocalStateSnapshotRestoreResult restoreResult)
    {
        return restoreResult.RestoredEntries.Any(
            static entry => entry.StartsWith("app/data/", StringComparison.Ordinal));
    }

    private static string[] GetChangedTrackedEnvKeys(LocalStateSnapshotPreviewResult? preview)
    {
        var trackedKeysLine = preview?.Lines.FirstOrDefault(
            static line => line.StartsWith(".env 跟踪键变更：", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(trackedKeysLine))
        {
            return [];
        }

        return trackedKeysLine[".env 跟踪键变更：".Length..]
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    }

    private static string ExtractSnapshotSide(string line)
    {
        const string marker = "-> 快照 ";
        var markerIndex = line.IndexOf(marker, StringComparison.Ordinal);

        if (markerIndex < 0)
        {
            return line;
        }

        return line[(markerIndex + marker.Length)..].Trim();
    }
}

public sealed record LocalStateSnapshotSelectionPresentation
{
    public string SummaryText { get; init; } = "选择一个快照以查看或恢复。";

    public string DetailText { get; init; } = "快照详情会显示在这里。";

    public string ImpactText { get; init; } = "恢复会覆盖这个快照中展示的状态。删除会永久移除当前选中的归档。";

    public string DiffText { get; init; } = "选择一个快照以预览差异。";

    public string AdviceText { get; init; } = "恢复建议会显示在这里。";

    public string SafetyHeadlineText { get; init; } = "恢复前检查：先选择一个快照，看看会覆盖哪些当前状态。";

    public string SafetyRecommendationText { get; init; } = "建议的第一步：先选中快照查看覆盖风险，再决定是否恢复。";

    public string RollbackHintText { get; init; } = "安全回滚快照会保留当前状态的回退点，并且不复制 .env 密钥。";
}

public sealed record LocalStateSnapshotRestoreRuntimeContext
{
    public BackendControlApiFailure ControlApiFailure { get; init; } = new();

    public bool IsControlApiReachable { get; init; }

    public bool CanStartBackend { get; init; }

    public bool IsQqRuntimeReady { get; init; }

    public bool IsWechatConfigured { get; init; }

    public bool IsWechatRuntimeReady { get; init; }
}

public sealed record LocalStateSnapshotRestoreAction(string Label, string Key)
{
    public static LocalStateSnapshotRestoreAction Empty { get; } = new(string.Empty, string.Empty);
}

public sealed record LocalStateSnapshotRestorePresentation
{
    public static LocalStateSnapshotRestorePresentation Empty { get; } = new();

    public string SummaryText { get; init; } = "恢复结果摘要会显示在这里。";

    public string IssueText { get; init; } = "恢复后的问题摘要会显示在这里。";

    public string TargetsText { get; init; } = "恢复目标会显示在这里。";

    public string SessionsText { get; init; } = "会话摘要会显示在这里。";

    public string LatestActivityText { get; init; } = "最近活动摘要会显示在这里。";

    public string AdviceText { get; init; } = "恢复后的建议会显示在这里。";

    public string ControlPlaneText { get; init; } = "恢复后的控制面检查会显示在这里。";

    public string RuntimeText { get; init; } = "恢复后的运行时检查会显示在这里。";

    public string NextStepText { get; init; } = "恢复后的下一步建议会显示在这里。";

    public LocalStateSnapshotRestoreAction PrimaryAction { get; init; } = LocalStateSnapshotRestoreAction.Empty;

    public LocalStateSnapshotRestoreAction SecondaryAction { get; init; } = LocalStateSnapshotRestoreAction.Empty;

    public LocalStateSnapshotRestoreAction TertiaryAction { get; init; } = LocalStateSnapshotRestoreAction.Empty;
}
