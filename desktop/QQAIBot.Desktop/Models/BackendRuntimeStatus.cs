namespace QQAIBot.Desktop.Models;

public sealed class BackendRuntimeStatus
{
    public string StartedAt { get; set; } = string.Empty;

    public int ProcessId { get; set; }

    public bool RuntimeActive { get; set; }

    public bool RuntimeReady { get; set; }

    public bool NapcatConnected { get; set; }

    public int ActiveLockCount { get; set; }

    public bool ConfigRestartRequired { get; set; }

    public string LastConfigSavedAt { get; set; } = string.Empty;

    public string ControlApiUrl { get; set; } = string.Empty;

    public string ConfigPath { get; set; } = string.Empty;

    public int? WorkerProcessId { get; set; }

    public string WorkerStartedAt { get; set; } = string.Empty;

    public BackendLlmRequestStatus? LastQqLlmRequest { get; set; }

    public bool WechatConfigured { get; set; }

    public bool WechatRuntimeActive { get; set; }

    public bool WechatRuntimeReady { get; set; }

    public bool WechatBridgeConnected { get; set; }

    public int? WechatWorkerProcessId { get; set; }

    public string WechatWorkerStartedAt { get; set; } = string.Empty;

    public BackendLlmRequestStatus? LastWechatLlmRequest { get; set; }
}
