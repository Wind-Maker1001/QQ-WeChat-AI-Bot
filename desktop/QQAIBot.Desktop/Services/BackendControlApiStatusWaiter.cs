using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class BackendControlApiStatusWaiter
{
    public static async Task<BackendRuntimeStatus?> WaitForStatusAsync(
        Func<CancellationToken, Task<BackendRuntimeStatus?>> tryGetStatusAsync,
        Func<BackendControlApiFailure> getLastFailure,
        int maxAttempts = 10,
        TimeSpan? retryDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveRetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(300);
        var effectiveDelayAsync = delayAsync ?? DefaultDelayAsync;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var status = await tryGetStatusAsync(cancellationToken);

            if (status is not null)
            {
                return status;
            }

            if (getLastFailure().Kind != BackendControlApiFailureKind.Unreachable)
            {
                return null;
            }

            await effectiveDelayAsync(effectiveRetryDelay, cancellationToken);
        }

        return null;
    }

    private static Task DefaultDelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        return Task.Delay(delay, cancellationToken);
    }
}
