using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class BackendControlApiService : IDisposable
{
    private static readonly Uri BaseAddress = new("http://127.0.0.1:3199/");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public BackendControlApiService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = BaseAddress,
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    public async Task<BackendRuntimeStatus?> TryGetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BackendRuntimeStatus>("status", JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BackendControlConfigResponse?> TryGetConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<BackendControlConfigResponse>("config", JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BackendControlConfigResponse?> TrySaveConfigAsync(
        BotConfig config,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PutAsJsonAsync("config", config, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<BackendControlConfigResponse>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BackendRuntimeStatus?> TryStartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync("start", content: null, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<BackendRuntimeStatus>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public async Task<BackendRuntimeStatus?> TryStopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync("stop", content: null, cancellationToken);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<BackendRuntimeStatus>(JsonOptions, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
