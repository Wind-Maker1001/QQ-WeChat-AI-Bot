using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public interface IBackendControlApiService : IDisposable
{
    BackendControlApiFailure LastFailure { get; }

    void SetAccessToken(string? accessToken);

    Task<BackendRuntimeStatus?> TryGetStatusAsync(CancellationToken cancellationToken = default);

    Task<BackendControlConfigResponse?> TryGetConfigAsync(CancellationToken cancellationToken = default);

    Task<BackendControlConfigResponse?> TrySaveConfigAsync(BotConfig config, CancellationToken cancellationToken = default);

    Task<BackendRuntimeStatus?> TryStartAsync(CancellationToken cancellationToken = default);

    Task<BackendRuntimeStatus?> TryStopAsync(CancellationToken cancellationToken = default);
}
