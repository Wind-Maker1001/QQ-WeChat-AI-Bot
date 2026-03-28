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
        var checklist = DesktopHealthChecklistBuilder.Build(
            effectiveConfig,
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            isBackendRootValid,
            autoStartEnabled);
        var guidance = DesktopHealthGuidanceBuilder.Build(
            effectiveConfig,
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            checklist.Checks,
            isBackendRootValid,
            hasUnsavedChanges,
            autoStartEnabled);
        var status = DesktopHealthStatusBuilder.Build(
            effectiveConfig,
            effectiveRuntimeSnapshot,
            effectiveControlApiFailure,
            isBackendRootValid);
        var hasBlockingSetupItems = checklist.Checks.Any(static check => check.IsBlocking);
        var shouldPreferSaveAction = hasUnsavedChanges && isBackendRootValid && !hasBlockingSetupItems;
        var primaryAction = shouldPreferSaveAction
            ? "先保存这个窗口里显示的修改，再按下面的 runtime 指引继续处理。"
            : status.PrimaryAction;

        return new DesktopHealthReport
        {
            State = status.State,
            StateText = status.StateText,
            Summary = status.Summary,
            ChecklistStatus = checklist.ChecklistStatus,
            ReadyNowText = status.ReadyNowText,
            PrimaryAction = primaryAction,
            PrimaryActionLabel = shouldPreferSaveAction ? "保存配置" : status.PrimaryActionLabel,
            PrimaryActionKey = shouldPreferSaveAction ? DesktopHealthActionKeys.SaveConfig : status.PrimaryActionKey,
            RuntimeExplanation = status.RuntimeExplanation,
            LatestIssue = guidance.LatestIssueText,
            LatestIssueActionLabel = guidance.LatestIssueActionLabel,
            LatestIssueActionKey = guidance.LatestIssueActionKey,
            ActionSummary = guidance.ActionSummary,
            NextActions = guidance.NextActions,
            Checks = checklist.Checks
        };
    }
}
