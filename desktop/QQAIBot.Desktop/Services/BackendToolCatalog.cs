using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendToolCatalog
{
    public static string[] DeriveRequestedToolKinds(
        BackendRequestedTools? requestedTools,
        BackendRequestedCapabilities? requestedCapabilities,
        IEnumerable<string>? fallbackTools = null)
    {
        if (requestedTools?.Requested is { Length: > 0 })
        {
            return NormalizeToolKinds(requestedTools.Requested);
        }

        var compatibilityTools = new List<string>();

        if (requestedCapabilities?.EnableWebSearch == true)
        {
            compatibilityTools.Add("web_search");
        }

        if (requestedCapabilities?.EnableCodeInterpreter == true)
        {
            compatibilityTools.Add("code_interpreter");
        }

        if (compatibilityTools.Count > 0)
        {
            return NormalizeToolKinds(compatibilityTools);
        }

        return NormalizeToolKinds(fallbackTools);
    }

    public static bool ContainsTool(IEnumerable<string>? toolKinds, string toolKind)
    {
        return NormalizeToolKinds(toolKinds).Contains(toolKind, StringComparer.Ordinal);
    }

    public static string FormatToolLabel(string? toolKind)
    {
        return toolKind switch
        {
            "web_search" => "web_search (Web Search)",
            "code_interpreter" => "code_interpreter (Code Interpreter)",
            "local_runtime_state" => "local_runtime_state (Local Runtime State)",
            "local_snapshot_inspect" => "local_snapshot_inspect (Local Snapshot Inspect)",
            _ => string.IsNullOrWhiteSpace(toolKind) ? "unknown" : toolKind.Trim()
        };
    }

    public static string FormatToolList(IEnumerable<string>? toolKinds, string emptyText = "none")
    {
        var normalizedTools = NormalizeToolKinds(toolKinds)
            .Select(FormatToolLabel)
            .ToArray();

        return normalizedTools.Length > 0
            ? string.Join(", ", normalizedTools)
            : emptyText;
    }

    public static string FormatSuppressedTools(IEnumerable<QQAIBot.Desktop.Models.BackendSuppressedTool>? suppressedTools, string emptyText = "none")
    {
        var normalized = suppressedTools?
            .Where(static suppressedTool => suppressedTool is not null && !string.IsNullOrWhiteSpace(suppressedTool.ToolKind))
            .Select(static suppressedTool => $"{FormatToolLabel(suppressedTool.ToolKind)} ({(string.IsNullOrWhiteSpace(suppressedTool.Reason) ? "unknown" : suppressedTool.Reason)})")
            .ToArray() ?? [];

        return normalized.Length > 0
            ? string.Join(", ", normalized)
            : emptyText;
    }

    private static string[] NormalizeToolKinds(IEnumerable<string>? toolKinds)
    {
        return toolKinds?
            .Where(static toolKind => !string.IsNullOrWhiteSpace(toolKind))
            .Select(static toolKind => toolKind.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? [];
    }
}
