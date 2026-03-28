using System.Linq;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopGuideFlowBuilder
{
    public static DesktopGuideFlow Build(DesktopGuideFlowContext? context)
    {
        var effectiveContext = context ?? new DesktopGuideFlowContext();
        var firstRunGuideSteps = BuildFirstRunGuideSteps(effectiveContext);
        var isFirstRunGuideComplete = firstRunGuideSteps.Count > 0 && firstRunGuideSteps.All(static step => step.IsComplete);
        var dailyUseGuideSteps = BuildDailyUseGuideSteps(effectiveContext);
        var isDailyUseGuideComplete = dailyUseGuideSteps.Count > 0 && dailyUseGuideSteps.All(static step => step.IsComplete);
        var overallReadinessRecentActivityText = BuildOverallReadinessRecentActivityText(
            effectiveContext.QqRecentActivities,
            effectiveContext.WechatRecentActivities);
        var (overallReadinessActionLabel, overallReadinessActionKey) = ResolveOverallReadinessAction(
            isDailyUseGuideComplete,
            isFirstRunGuideComplete,
            firstRunGuideSteps,
            dailyUseGuideSteps,
            effectiveContext.QqRecentActivities,
            effectiveContext.WechatRecentActivities);

        return new DesktopGuideFlow
        {
            FirstRunGuideSteps = firstRunGuideSteps,
            IsFirstRunGuideComplete = isFirstRunGuideComplete,
            FirstRunGuideText = BuildFirstRunGuideText(effectiveContext),
            FirstRunGuideProgressText = BuildGuideProgressText(firstRunGuideSteps),
            FirstRunGuideCurrentStepText = BuildGuideCurrentStepText(firstRunGuideSteps),
            FirstRunGuideCompletionText = isFirstRunGuideComplete
                ? "首次安装已完成，现在可以把 Local AI Runtime 当作日常常驻控制台使用。"
                : string.Empty,
            FirstRunStepsText = BuildFirstRunStepsText(effectiveContext),
            DailyUseGuideSteps = dailyUseGuideSteps,
            IsDailyUseGuideComplete = isDailyUseGuideComplete,
            DailyUseGuideText = effectiveContext.IsProcessRunning
                ? "日常常驻只记住这 3 件事。"
                : "日常常驻：先把入口和托盘规则记住。",
            DailyUseGuideProgressText = BuildGuideProgressText(dailyUseGuideSteps),
            DailyUseGuideCurrentStepText = BuildGuideCurrentStepText(dailyUseGuideSteps),
            DailyUseGuideCompletionText = isDailyUseGuideComplete
                ? "已进入日常常驻模式。之后从 Local AI Runtime 重新接回即可。"
                : string.Empty,
            DailyUseStepsText = effectiveContext.IsProcessRunning
                ? "1. 最小化或关闭窗口：只会进托盘，backend 继续运行。" + Environment.NewLine +
                  "2. 退出控制台：只退出桌面壳，停止后端才会让 QQ / 微信下线。" + Environment.NewLine +
                  "3. 之后从桌面快捷方式或开始菜单里的 Local AI Runtime 重新接回控制面。"
                : "1. 先从这个窗口启动后端，再开始常驻使用。" + Environment.NewLine +
                  "2. 之后最小化或关闭窗口都会进托盘，不会清掉本地状态。" + Environment.NewLine +
                  "3. 需要重新打开时，从桌面快捷方式或开始菜单里的 Local AI Runtime 重新接回。",
            IsOverallReadinessReady = isDailyUseGuideComplete,
            IsOverallReadinessSetupComplete = isFirstRunGuideComplete,
            OverallReadinessStateText = isDailyUseGuideComplete
                ? "可日常使用"
                : isFirstRunGuideComplete
                    ? "设置完成"
                    : "设置进行中",
            OverallReadinessSummaryText = isDailyUseGuideComplete
                ? "runtime 已在线，常驻模式基础设置已完成，当前没有需要立即处理的问题。"
                : isFirstRunGuideComplete
                    ? "必填设置已经完成。把下面高亮的日常使用步骤补齐后，长期使用会更顺手。"
                    : "先完成下面高亮的首次使用步骤。完成后，这个窗口就可以作为你的日常托盘控制台。",
            OverallReadinessRecentActivityText = overallReadinessRecentActivityText,
            OverallReadinessActionLabel = overallReadinessActionLabel,
            OverallReadinessActionKey = overallReadinessActionKey
        };
    }

    private static string BuildFirstRunGuideText(DesktopGuideFlowContext context)
    {
        if (!context.IsBackendRootValid)
        {
            return "首次打开只看这 3 步。";
        }

        var blockingChecks = context.HealthChecks
            .Where(static check => check.IsBlocking)
            .Select(static check => check.Title)
            .ToArray();

        if (blockingChecks.Length > 0)
        {
            return $"首次打开：先补齐 {string.Join("、", blockingChecks)}。";
        }

        if (context.CanStartBackend)
        {
            return "首次打开：必填项已经齐了。";
        }

        return "首次打开：现在可以直接进入常驻使用。";
    }

    private static string BuildFirstRunStepsText(DesktopGuideFlowContext context)
    {
        if (!context.IsBackendRootValid)
        {
            return "1. 选择本地 runtime 目录。" + Environment.NewLine +
                   "2. 点击重新加载，确认配置已读到这台机器。" + Environment.NewLine +
                   "3. 再补 API key 和通道配置。";
        }

        var blockingChecks = context.HealthChecks
            .Where(static check => check.IsBlocking)
            .Select(static check => check.Title)
            .ToArray();

        if (blockingChecks.Length > 0)
        {
            return "1. 先补齐必填项：" + string.Join("、", blockingChecks) + "。" + Environment.NewLine +
                   "2. 保存配置，让当前窗口里的改动真正生效。" + Environment.NewLine +
                   "3. 启动后端，再在 System Check 里确认 QQ ready。";
        }

        if (context.CanStartBackend)
        {
            return "1. 保存当前配置，保持窗口显示的状态为最新。" + Environment.NewLine +
                   "2. 启动后端，让 control API、QQ runtime 和可选 WeChat runtime 上线。" + Environment.NewLine +
                   "3. 在 System Check 里确认 QQ ready，再开始日常使用。";
        }

        return "1. 必填项已经齐了，当前 runtime 可继续使用。" + Environment.NewLine +
               "2. 之后最小化或关闭窗口都会进托盘。" + Environment.NewLine +
               "3. 需要重新接回时，从 Local AI Runtime 快捷方式或开始菜单打开即可。";
    }

    private static IReadOnlyList<DesktopGuideStepItem> BuildFirstRunGuideSteps(DesktopGuideFlowContext context)
    {
        var steps = new List<DesktopGuideStepItem>();
        var blockingChecks = context.HealthChecks
            .Where(static check => check.IsBlocking)
            .ToArray();
        var firstBlockingCheck = blockingChecks.FirstOrDefault();
        var hasUnsavedBlockingWork = context.IsBackendRootValid && context.HasUnsavedChanges;
        var isBlockedBySetup = blockingChecks.Length > 0 || hasUnsavedBlockingWork;

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "连接 runtime 目录",
                Detail = context.IsBackendRootValid
                    ? "这个窗口已经指向一个可用的本地 runtime 目录。"
                    : "选择安装后的本地 runtime 目录，它应包含 package.json 和 src\\index.mjs。",
                StatusText = context.IsBackendRootValid ? "已完成" : "下一步",
                IsComplete = context.IsBackendRootValid,
                ActionLabel = context.IsBackendRootValid ? string.Empty : "选择目录",
                ActionKey = context.IsBackendRootValid ? string.Empty : DesktopHealthActionKeys.FocusBackendRoot
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "完成必填配置",
                Detail = BuildFirstRunSetupStepDetail(context, blockingChecks, hasUnsavedBlockingWork),
                StatusText = blockingChecks.Length == 0 && !hasUnsavedBlockingWork ? "已完成" : "下一步",
                IsComplete = blockingChecks.Length == 0 && !hasUnsavedBlockingWork,
                ActionLabel = BuildFirstRunSetupStepActionLabel(context, firstBlockingCheck, hasUnsavedBlockingWork),
                ActionKey = BuildFirstRunSetupStepActionKey(context, firstBlockingCheck, hasUnsavedBlockingWork)
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "让 runtime 上线",
                Detail = BuildFirstRunRuntimeStepDetail(context, isBlockedBySetup),
                StatusText = IsFirstRunRuntimeStepComplete(context, isBlockedBySetup) ? "已完成" : "下一步",
                IsComplete = IsFirstRunRuntimeStepComplete(context, isBlockedBySetup),
                ActionLabel = BuildFirstRunRuntimeStepActionLabel(context, isBlockedBySetup),
                ActionKey = BuildFirstRunRuntimeStepActionKey(context, isBlockedBySetup)
            });

        return FinalizeGuideSteps(steps);
    }

    private static string BuildFirstRunSetupStepDetail(
        DesktopGuideFlowContext context,
        IReadOnlyList<DesktopHealthCheckItem> blockingChecks,
        bool hasUnsavedBlockingWork)
    {
        if (!context.IsBackendRootValid)
        {
            return "选好目录后，再把 System Check 里显示的 API key 和通道配置补齐。";
        }

        if (hasUnsavedBlockingWork)
        {
            return "先保存当前窗口里的修改，再尝试启动 runtime。";
        }

        if (blockingChecks.Count == 0)
        {
            return "API key 和通道必填设置看起来已经齐了。";
        }

        return $"先完成这些必填项：{string.Join("、", blockingChecks.Select(static check => check.Title))}。";
    }

    private static string BuildFirstRunSetupStepActionLabel(
        DesktopGuideFlowContext context,
        DesktopHealthCheckItem? firstBlockingCheck,
        bool hasUnsavedBlockingWork)
    {
        if (!context.IsBackendRootValid)
        {
            return string.Empty;
        }

        if (hasUnsavedBlockingWork)
        {
            return "保存配置";
        }

        return firstBlockingCheck?.ActionLabel ?? string.Empty;
    }

    private static string BuildFirstRunSetupStepActionKey(
        DesktopGuideFlowContext context,
        DesktopHealthCheckItem? firstBlockingCheck,
        bool hasUnsavedBlockingWork)
    {
        if (!context.IsBackendRootValid)
        {
            return string.Empty;
        }

        if (hasUnsavedBlockingWork)
        {
            return DesktopHealthActionKeys.SaveConfig;
        }

        return firstBlockingCheck?.ActionKey ?? string.Empty;
    }

    private static string BuildFirstRunRuntimeStepDetail(
        DesktopGuideFlowContext context,
        bool isBlockedBySetup)
    {
        if (!context.IsBackendRootValid)
        {
            return "先把目录和上面的必填设置完成，runtime 启动入口才会可用。";
        }

        if (isBlockedBySetup)
        {
            return "先完成上面的配置步骤，再从这个窗口启动后端。";
        }

        if (context.CanStartBackend)
        {
            return "启动后端，让 control API 和 QQ runtime 一起上线。";
        }

        if (!context.IsQqRuntimeReady)
        {
            return "后端已经在运行。确认 NapCat 已连接后，再回到 System Check 查看状态。";
        }

        return "QQ 已就绪。现在可以把它当作日常桌面控制台使用。";
    }

    private static string BuildFirstRunRuntimeStepActionLabel(
        DesktopGuideFlowContext context,
        bool isBlockedBySetup)
    {
        if (!context.IsBackendRootValid || isBlockedBySetup)
        {
            return string.Empty;
        }

        if (context.CanStartBackend)
        {
            return "启动后端";
        }

        if (!context.IsQqRuntimeReady)
        {
            return "检查 NapCat 配置";
        }

        return string.Empty;
    }

    private static string BuildFirstRunRuntimeStepActionKey(
        DesktopGuideFlowContext context,
        bool isBlockedBySetup)
    {
        if (!context.IsBackendRootValid || isBlockedBySetup)
        {
            return string.Empty;
        }

        if (context.CanStartBackend)
        {
            return DesktopHealthActionKeys.StartBackend;
        }

        if (!context.IsQqRuntimeReady)
        {
            return DesktopHealthActionKeys.FocusNapCatUrl;
        }

        return string.Empty;
    }

    private static bool IsFirstRunRuntimeStepComplete(
        DesktopGuideFlowContext context,
        bool isBlockedBySetup)
    {
        return context.IsBackendRootValid &&
               !isBlockedBySetup &&
               !context.CanStartBackend &&
               context.IsQqRuntimeReady;
    }

    private static IReadOnlyList<DesktopGuideStepItem> BuildDailyUseGuideSteps(DesktopGuideFlowContext context)
    {
        var steps = new List<DesktopGuideStepItem>();

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "保持 runtime 可接回",
                Detail = context.IsProcessRunning
                    ? "后端已经在线。你可以把窗口收进托盘，之后再接回。"
                    : "先启动后端，再进入日常托盘使用。",
                StatusText = context.IsProcessRunning ? "已完成" : "下一步",
                IsComplete = context.IsProcessRunning,
                ActionLabel = context.IsProcessRunning ? string.Empty : "启动后端",
                ActionKey = context.IsProcessRunning ? string.Empty : DesktopHealthActionKeys.StartBackend
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "决定是否随系统启动",
                Detail = context.AutoStartEnabled
                    ? "常驻模式的开机启动已经开启。Windows 登录后，Local AI Runtime 会以最小化方式重新出现。"
                    : "如果你希望 Windows 登录后自动恢复 Local AI Runtime，就开启开机启动。",
                StatusText = context.AutoStartEnabled ? "已完成" : "可选",
                IsComplete = context.AutoStartEnabled,
                ActionLabel = context.AutoStartEnabled ? string.Empty : "启用开机启动",
                ActionKey = context.AutoStartEnabled ? string.Empty : DesktopHealthActionKeys.ToggleAutoStart
            });

        steps.Add(
            new DesktopGuideStepItem
            {
                Title = "有异常时查看最新问题",
                Detail = string.IsNullOrWhiteSpace(context.HealthLatestIssueActionLabel)
                    ? "当前没有需要立刻处理的问题。需要时再看托盘、最近活动或 System Check。"
                    : context.HealthLatestIssueText,
                StatusText = string.IsNullOrWhiteSpace(context.HealthLatestIssueActionLabel) ? "就绪" : "查看",
                IsComplete = string.IsNullOrWhiteSpace(context.HealthLatestIssueActionLabel),
                ActionLabel = context.HealthLatestIssueActionLabel,
                ActionKey = context.HealthLatestIssueActionKey
            });

        return FinalizeGuideSteps(steps);
    }

    private static IReadOnlyList<DesktopGuideStepItem> FinalizeGuideSteps(IReadOnlyList<DesktopGuideStepItem> steps)
    {
        var currentStepIndex = steps
            .Select((step, index) => new { Step = step, Index = index })
            .FirstOrDefault(static item => !item.Step.IsComplete)
            ?.Index;

        return steps
            .Select((step, index) => step with
            {
                StepNumber = (index + 1).ToString(),
                IsCurrent = currentStepIndex is int currentIndex && currentIndex == index
            })
            .ToArray();
    }

    private static string BuildGuideProgressText(IReadOnlyList<DesktopGuideStepItem> steps)
    {
        if (steps.Count == 0)
        {
            return "已完成 0/0";
        }

        var completedCount = steps.Count(static step => step.IsComplete);
        return $"已完成 {completedCount}/{steps.Count}";
    }

    private static string BuildGuideCurrentStepText(IReadOnlyList<DesktopGuideStepItem> steps)
    {
        if (steps.Count == 0)
        {
            return "当前步骤：无";
        }

        var currentStep = steps.FirstOrDefault(static step => step.IsCurrent);
        return currentStep is null
            ? "当前步骤：全部完成"
            : $"当前步骤：{currentStep.Title}";
    }

    private static string BuildOverallReadinessRecentActivityText(
        IReadOnlyList<BackendRecentActivityItem> qqRecentActivities,
        IReadOnlyList<BackendRecentActivityItem> wechatRecentActivities)
    {
        var latestActivity = GetLatestRecentActivity(qqRecentActivities, wechatRecentActivities);
        if (latestActivity is null)
        {
            return "最近活动：还没有捕获到最近的 QQ / 微信活动。";
        }

        var (channel, item) = latestActivity.Value;
        var eventType = string.IsNullOrWhiteSpace(item.EventType) ? "活动" : item.EventType;
        var summary = string.IsNullOrWhiteSpace(item.Summary) ? "无摘要" : item.Summary;
        var capturedAt = string.IsNullOrWhiteSpace(item.Meta) ? item.CapturedAt : item.Meta;
        var capturedAtText = string.IsNullOrWhiteSpace(capturedAt) ? "时间未知" : capturedAt;

        return $"最近活动：{channel} {eventType} | {summary} | {capturedAtText}";
    }

    private static (string ActionLabel, string ActionKey) ResolveOverallReadinessAction(
        bool isOverallReadinessReady,
        bool isFirstRunGuideComplete,
        IReadOnlyList<DesktopGuideStepItem> firstRunGuideSteps,
        IReadOnlyList<DesktopGuideStepItem> dailyUseGuideSteps,
        IReadOnlyList<BackendRecentActivityItem> qqRecentActivities,
        IReadOnlyList<BackendRecentActivityItem> wechatRecentActivities)
    {
        if (isOverallReadinessReady)
        {
            return HasAnyRecentActivity(qqRecentActivities, wechatRecentActivities)
                ? ("查看最近活动", DesktopHealthActionKeys.FocusLatestActivity)
                : (string.Empty, string.Empty);
        }

        var actionStep = !isFirstRunGuideComplete
            ? firstRunGuideSteps.FirstOrDefault(static step => step.IsCurrent)
            : dailyUseGuideSteps.FirstOrDefault(static step => step.IsCurrent);

        return (actionStep?.ActionLabel ?? string.Empty, actionStep?.ActionKey ?? string.Empty);
    }

    private static bool HasAnyRecentActivity(
        IReadOnlyList<BackendRecentActivityItem> qqRecentActivities,
        IReadOnlyList<BackendRecentActivityItem> wechatRecentActivities)
    {
        return qqRecentActivities.Count > 0 || wechatRecentActivities.Count > 0;
    }

    private static (string Channel, BackendRecentActivityItem Item)? GetLatestRecentActivity(
        IReadOnlyList<BackendRecentActivityItem> qqRecentActivities,
        IReadOnlyList<BackendRecentActivityItem> wechatRecentActivities)
    {
        var qqLatest = qqRecentActivities.FirstOrDefault();
        var wechatLatest = wechatRecentActivities.FirstOrDefault();

        if (qqLatest is null && wechatLatest is null)
        {
            return null;
        }

        if (qqLatest is null)
        {
            return ("微信", wechatLatest!);
        }

        if (wechatLatest is null)
        {
            return ("QQ", qqLatest);
        }

        return ParseCapturedAt(qqLatest.CapturedAt) >= ParseCapturedAt(wechatLatest.CapturedAt)
            ? ("QQ", qqLatest)
            : ("微信", wechatLatest);
    }

    private static DateTimeOffset ParseCapturedAt(string? capturedAt)
    {
        return DateTimeOffset.TryParse(capturedAt, out var parsedCapturedAt)
            ? parsedCapturedAt
            : DateTimeOffset.MinValue;
    }
}

public sealed record DesktopGuideFlowContext
{
    public bool IsBackendRootValid { get; init; }

    public bool HasUnsavedChanges { get; init; }

    public bool CanStartBackend { get; init; }

    public bool IsProcessRunning { get; init; }

    public bool IsQqRuntimeReady { get; init; }

    public bool AutoStartEnabled { get; init; }

    public string HealthLatestIssueText { get; init; } = string.Empty;

    public string HealthLatestIssueActionLabel { get; init; } = string.Empty;

    public string HealthLatestIssueActionKey { get; init; } = string.Empty;

    public IReadOnlyList<DesktopHealthCheckItem> HealthChecks { get; init; } = [];

    public IReadOnlyList<BackendRecentActivityItem> QqRecentActivities { get; init; } = [];

    public IReadOnlyList<BackendRecentActivityItem> WechatRecentActivities { get; init; } = [];
}

public sealed record DesktopGuideFlow
{
    public bool IsOverallReadinessReady { get; init; }

    public bool IsOverallReadinessSetupComplete { get; init; }

    public string OverallReadinessStateText { get; init; } = "设置进行中";

    public string OverallReadinessSummaryText { get; init; } = string.Empty;

    public string OverallReadinessRecentActivityText { get; init; } = "最近活动：还没有捕获到最近的 QQ / 微信活动。";

    public string OverallReadinessActionLabel { get; init; } = string.Empty;

    public string OverallReadinessActionKey { get; init; } = string.Empty;

    public IReadOnlyList<DesktopGuideStepItem> FirstRunGuideSteps { get; init; } = [];

    public bool IsFirstRunGuideComplete { get; init; }

    public string FirstRunGuideText { get; init; } = string.Empty;

    public string FirstRunGuideProgressText { get; init; } = string.Empty;

    public string FirstRunGuideCurrentStepText { get; init; } = string.Empty;

    public string FirstRunGuideCompletionText { get; init; } = string.Empty;

    public string FirstRunStepsText { get; init; } = string.Empty;

    public IReadOnlyList<DesktopGuideStepItem> DailyUseGuideSteps { get; init; } = [];

    public bool IsDailyUseGuideComplete { get; init; }

    public string DailyUseGuideText { get; init; } = string.Empty;

    public string DailyUseGuideProgressText { get; init; } = string.Empty;

    public string DailyUseGuideCurrentStepText { get; init; } = string.Empty;

    public string DailyUseGuideCompletionText { get; init; } = string.Empty;

    public string DailyUseStepsText { get; init; } = string.Empty;
}
