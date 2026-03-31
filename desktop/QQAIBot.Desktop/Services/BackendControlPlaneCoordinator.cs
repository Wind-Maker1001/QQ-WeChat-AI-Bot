using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendControlPlaneCoordinator
{
    public static async Task<(BackendControlConfigResponse? ApiConfig, BackendRuntimeStatus? ApiStatus)> LoadAuthoritativeConfigAsync(
        Func<CancellationToken, Task<BackendControlConfigResponse?>> tryGetConfigAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Func<Task> tryRecoverControlApiAsync,
        CancellationToken cancellationToken = default)
    {
        var apiConfig = await tryGetConfigAsync(cancellationToken);
        var configFailure = getLastFailure();

        if (apiConfig is not null)
        {
            var apiStatus = await tryGetStatusAsync(cancellationToken);
            return (apiConfig, apiStatus);
        }

        if (isImmediateFailure(configFailure))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(configFailure.Message)
                    ? "Control API rejected the config request."
                    : configFailure.Message);
        }

        if (configFailure.Kind == BackendControlApiFailureKind.Unreachable)
        {
            await tryRecoverControlApiAsync();
            apiConfig = await tryGetConfigAsync(cancellationToken);

            if (apiConfig is not null)
            {
                var apiStatus = await tryGetStatusAsync(cancellationToken);
                return (apiConfig, apiStatus);
            }
        }

        var fallbackStatus = await tryGetStatusAsync(cancellationToken);
        return (null, fallbackStatus);
    }

    public static async Task<BackendControlConfigResponse> SaveThroughControlApiAsync(
        BotConfig config,
        Func<BotConfig, CancellationToken, Task<BackendControlConfigResponse?>> trySaveConfigAsync,
        Func<BackendControlApiFailure> getLastFailure,
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure, bool> isImmediateFailure,
        Func<Task> tryRecoverControlApiAsync,
        CancellationToken cancellationToken = default)
    {
        var apiResult = await trySaveConfigAsync(config, cancellationToken);

        if (apiResult is not null)
        {
            return apiResult;
        }

        var initialFailure = getLastFailure();

        if (isImmediateFailure(initialFailure))
        {
            throw new InvalidOperationException(initialFailure.Message);
        }

        var currentStatus = await tryGetStatusAsync(cancellationToken);

        if (currentStatus is null)
        {
            var currentStatusFailure = getLastFailure();
            if (isImmediateFailure(currentStatusFailure))
            {
                throw new InvalidOperationException(currentStatusFailure.Message);
            }

            await tryRecoverControlApiAsync();
            apiResult = await trySaveConfigAsync(config, cancellationToken);

            if (apiResult is not null)
            {
                return apiResult;
            }
        }

        var finalFailure = getLastFailure();
        var failureMessage = string.IsNullOrWhiteSpace(finalFailure.Message)
            ? "Control API is unavailable or rejected the config update."
            : finalFailure.Message;

        throw new InvalidOperationException(failureMessage);
    }
}
