using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendExecutionProjectionFormatter
{
    public static string Format(
        BackendExecutionProjection? executionProjection,
        string? executionKind,
        string? executionSummary)
    {
        var normalizedKind = DefaultIfBlank(executionProjection?.Kind, DefaultIfBlank(executionKind, "unknown"));
        var normalizedSummary = DefaultIfBlank(
            executionProjection?.Summary,
            DefaultIfBlank(executionSummary, string.Empty));
        var normalizedStages = executionProjection?.Stages?
            .Where(static stage => !string.IsNullOrWhiteSpace(stage))
            .ToArray() ?? [];
        var normalizedCompletedStages = executionProjection?.CompletedStages?
            .Where(static stage => !string.IsNullOrWhiteSpace(stage))
            .ToArray() ?? [];
        var normalizedFailedStage = DefaultIfBlank(executionProjection?.FailedStage, string.Empty);
        var normalizedRecoveries = executionProjection?.Recoveries?
            .Where(static recovery => !string.IsNullOrWhiteSpace(recovery))
            .ToArray() ?? [];
        var normalizedRecoveriesText = normalizedRecoveries.Length > 0
            ? string.Join("+", normalizedRecoveries)
            : string.Empty;
        var normalizedDegraded = executionProjection?.Degraded == true || normalizedRecoveries.Length > 0;
        var stagePath = normalizedStages.Length > 0 ? string.Join("->", normalizedStages) : string.Empty;
        var completedStagePath = normalizedCompletedStages.Length > 0
            ? string.Join("->", normalizedCompletedStages)
            : string.Empty;
        var shouldShowFallbackPath =
            !string.IsNullOrWhiteSpace(normalizedSummary) &&
            !string.Equals(normalizedSummary, normalizedKind, StringComparison.Ordinal);
        var shouldShowCompletedStages =
            !string.IsNullOrWhiteSpace(completedStagePath) &&
            (!string.IsNullOrWhiteSpace(normalizedFailedStage) ||
             !string.Equals(completedStagePath, stagePath, StringComparison.Ordinal));

        if (normalizedStages.Length > 0 &&
            !string.Equals(stagePath, normalizedKind, StringComparison.Ordinal))
        {
            var executionParts = new List<string>
            {
                $"execution={normalizedKind}",
                $"stages={stagePath}"
            };

            if (shouldShowCompletedStages)
            {
                executionParts.Add($"completed={completedStagePath}");
            }

            if (!string.IsNullOrWhiteSpace(normalizedFailedStage))
            {
                executionParts.Add($"failed_stage={normalizedFailedStage}");
            }

            if (normalizedDegraded)
            {
                executionParts.Add("degraded=yes");
            }

            if (!string.IsNullOrWhiteSpace(normalizedRecoveriesText))
            {
                executionParts.Add($"recoveries={normalizedRecoveriesText}");
            }

            return string.Join(" | ", executionParts);
        }

        if (!shouldShowFallbackPath)
        {
            var executionParts = new List<string>
            {
                $"execution={normalizedKind}"
            };

            if (shouldShowCompletedStages)
            {
                executionParts.Add($"completed={completedStagePath}");
            }

            if (!string.IsNullOrWhiteSpace(normalizedFailedStage))
            {
                executionParts.Add($"failed_stage={normalizedFailedStage}");
            }

            if (normalizedDegraded)
            {
                executionParts.Add("degraded=yes");
            }

            if (!string.IsNullOrWhiteSpace(normalizedRecoveriesText))
            {
                executionParts.Add($"recoveries={normalizedRecoveriesText}");
            }

            return string.Join(" | ", executionParts);
        }

        var fallbackParts = new List<string>
        {
            $"execution={normalizedKind}",
            $"path={normalizedSummary}"
        };

        if (shouldShowCompletedStages)
        {
            fallbackParts.Add($"completed={completedStagePath}");
        }

        if (!string.IsNullOrWhiteSpace(normalizedFailedStage))
        {
            fallbackParts.Add($"failed_stage={normalizedFailedStage}");
        }

        if (normalizedDegraded)
        {
            fallbackParts.Add("degraded=yes");
        }

        if (!string.IsNullOrWhiteSpace(normalizedRecoveriesText))
        {
            fallbackParts.Add($"recoveries={normalizedRecoveriesText}");
        }

        return string.Join(" | ", fallbackParts);
    }

    private static string DefaultIfBlank(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
