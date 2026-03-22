namespace QQAIBot.Desktop.Models;

public sealed class BackendRuntimeStatus
{
    public string StartedAt { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public bool RuntimeActive { get; set; }

    public bool NapcatConnected { get; set; }

    public int ActiveLockCount { get; set; }

    public bool ConfigRestartRequired { get; set; }

    public string LastConfigSavedAt { get; set; } = string.Empty;

    public string ControlApiUrl { get; set; } = string.Empty;

    public string ConfigPath { get; set; } = string.Empty;

    public int? WorkerProcessId { get; set; }

    public string WorkerStartedAt { get; set; } = string.Empty;
}
