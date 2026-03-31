using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

internal static class DesktopConfigWorkflowTests
{
    public static Task TestEditorProjectionAsync()
    {
        var nextState = DesktopConfigWorkflow.UpdateEditorState(
            new DesktopShellSourceState(),
            new BotConfig
            {
                OpenAiApiKey = "advanced-key",
                BotPrefix = "/ai"
            },
            controlApiToken: "local-token",
            hasUnsavedChanges: true,
            lastLoadedAtText: "2026-03-31 10:00:00",
            lastSavedAtText: "2026-03-31 10:05:00",
            autoStartEnabled: true,
            canStartBackend: false,
            logText: "workflow-log");

        AssertEqual("advanced-key", nextState.ConfigEditorState.Config.OpenAiApiKey, "Config workflow should copy config values.");
        AssertEqual("local-token", nextState.ConfigEditorState.ControlApiToken, "Config workflow should update the control API token.");
        AssertTrue(nextState.ConfigEditorState.HasUnsavedChanges, "Config workflow should preserve dirty-state flags.");
        AssertTrue(nextState.RuntimeSourceState.AutoStartEnabled, "Config workflow should update runtime shell toggles.");
        AssertEqual("workflow-log", nextState.UiFeedbackState.LogText, "Config workflow should project the latest shell log text.");
        return Task.CompletedTask;
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual}");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
