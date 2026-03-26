namespace QQAIBot.Desktop.Models;

public sealed record BackendRuntimeSnapshotViewState
{
    public int? WorkerProcessId { get; init; }

    public int? WechatWorkerProcessId { get; init; }

    public bool? RuntimeActive { get; init; }

    public bool? RuntimeReady { get; init; }

    public bool? WechatConfigured { get; init; }

    public bool? WechatRuntimeActive { get; init; }

    public bool? WechatRuntimeReady { get; init; }

    public bool? WechatBridgeConnected { get; init; }

    public bool? ControlApiReachable { get; init; }

    public BackendLlmRequestStatus? LastQqLlmRequest { get; init; }

    public BackendLlmFailureStatus? LastQqLlmFailure { get; init; }

    public BackendLlmRequestStatus? LastWechatLlmRequest { get; init; }

    public BackendLlmFailureStatus? LastWechatLlmFailure { get; init; }
}
