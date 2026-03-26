using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendControlPlaneFacade
{
    public static Task<(BackendControlConfigResponse? ApiConfig, BackendRuntimeStatus? ApiStatus)> LoadAuthoritativeConfigAsync(
        Func<CancellationToken, Task<BackendControlConfigResponse?>> tryGetConfigAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Func<Task> tryRecoverControlApiAsync,
        CancellationToken cancellationToken = default)
    {
        return BackendControlPlaneCoordinator.LoadAuthoritativeConfigAsync(
            tryGetConfigAsync,
            getLastFailure,
            tryGetStatusAsync,
            isImmediateFailure,
            tryRecoverControlApiAsync,
            cancellationToken);
    }

    public static async Task<BackendControlConfigResponse> SaveConfigAsync(
        Func<Task> prepareAsync,
        BotConfig config,
        Func<BotConfig, CancellationToken, Task<BackendControlConfigResponse?>> trySaveConfigAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Func<Task> tryRecoverControlApiAsync,
        CancellationToken cancellationToken = default)
    {
        await prepareAsync();

        return await BackendControlPlaneCoordinator.SaveThroughControlApiAsync(
            config,
            trySaveConfigAsync,
            getLastFailure,
            tryGetStatusAsync,
            isImmediateFailure,
            tryRecoverControlApiAsync,
            cancellationToken);
    }

    public static async Task<BackendRuntimeControlOutcome> StartBackendAsync(
        Func<Task> prepareAsync,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Action startProcess,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> waitForStatusAsync,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryStartAsync,
        CancellationToken cancellationToken = default)
    {
        await prepareAsync();

        return await BackendRuntimeControlCoordinator.StartAsync(
            tryGetStatusAsync,
            getLastFailure,
            isImmediateFailure,
            startProcess,
            waitForStatusAsync,
            tryStartAsync,
            cancellationToken);
    }

    public static async Task<BackendRuntimeControlOutcome> StopBackendAsync(
        Func<Task> prepareAsync,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryStopAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Func<bool> isProcessRunning,
        Func<Task> stopProcessAsync,
        CancellationToken cancellationToken = default)
    {
        await prepareAsync();

        return await BackendRuntimeControlCoordinator.StopAsync(
            tryStopAsync,
            getLastFailure,
            isImmediateFailure,
            isProcessRunning,
            stopProcessAsync,
            cancellationToken);
    }
}
