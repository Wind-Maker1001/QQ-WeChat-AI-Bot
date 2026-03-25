using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class BackendControlApiService : IBackendControlApiService
{
    private static readonly Uri BaseAddress = new("http://127.0.0.1:3199/");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private BackendControlApiFailure _lastFailure = new();

    public BackendControlApiFailure LastFailure => _lastFailure;

    public BackendControlApiService(Uri? baseAddress = null, TimeSpan? timeout = null, HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;

            if (_httpClient.BaseAddress is null)
            {
                _httpClient.BaseAddress = baseAddress ?? BaseAddress;
            }

            return;
        }

        _httpClient = new HttpClient
        {
            BaseAddress = baseAddress ?? BaseAddress,
            Timeout = timeout ?? TimeSpan.FromSeconds(4)
        };
        _ownsHttpClient = true;
    }

    public async Task<BackendRuntimeStatus?> TryGetStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("status", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                SetFailure(
                    BackendControlApiFailureKind.Rejected,
                    await ReadApiErrorMessageAsync(response, cancellationToken));
                return null;
            }

            var status = await response.Content.ReadFromJsonAsync<BackendRuntimeStatus>(JsonOptions, cancellationToken);

            if (status is null)
            {
                SetFailure(
                    BackendControlApiFailureKind.Unknown,
                    "Control API returned an empty status response.");
                return null;
            }

            ClearFailure();
            return status;
        }
        catch (Exception ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
            return null;
        }
    }

    public async Task<BackendControlConfigResponse?> TryGetConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync("config", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                SetFailure(
                    BackendControlApiFailureKind.Rejected,
                    await ReadApiErrorMessageAsync(response, cancellationToken));
                return null;
            }

            var config = await response.Content.ReadFromJsonAsync<BackendControlConfigResponse>(JsonOptions, cancellationToken);

            if (config is null)
            {
                SetFailure(
                    BackendControlApiFailureKind.Unknown,
                    "Control API returned an empty config response.");
                return null;
            }

            ClearFailure();
            return config;
        }
        catch (Exception ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
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

            if (!response.IsSuccessStatusCode)
            {
                SetFailure(
                    BackendControlApiFailureKind.Rejected,
                    await ReadApiErrorMessageAsync(response, cancellationToken));
                return null;
            }

            var saveResult = await response.Content.ReadFromJsonAsync<BackendControlConfigResponse>(JsonOptions, cancellationToken);

            if (saveResult is null)
            {
                SetFailure(
                    BackendControlApiFailureKind.Unknown,
                    "Control API returned an empty save response.");
                return null;
            }

            ClearFailure();
            return saveResult;
        }
        catch (HttpRequestException ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            SetFailure(BackendControlApiFailureKind.Unknown, ex.Message);
            return null;
        }
    }

    public async Task<BackendRuntimeStatus?> TryStartAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync("start", content: null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                SetFailure(
                    BackendControlApiFailureKind.Rejected,
                    await ReadApiErrorMessageAsync(response, cancellationToken));
                return null;
            }

            var status = await response.Content.ReadFromJsonAsync<BackendRuntimeStatus>(JsonOptions, cancellationToken);

            if (status is null)
            {
                SetFailure(
                    BackendControlApiFailureKind.Unknown,
                    "Control API returned an empty start response.");
                return null;
            }

            ClearFailure();
            return status;
        }
        catch (HttpRequestException ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            SetFailure(BackendControlApiFailureKind.Unknown, ex.Message);
            return null;
        }
    }

    public async Task<BackendRuntimeStatus?> TryStopAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _httpClient.PostAsync("stop", content: null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                SetFailure(
                    BackendControlApiFailureKind.Rejected,
                    await ReadApiErrorMessageAsync(response, cancellationToken));
                return null;
            }

            var status = await response.Content.ReadFromJsonAsync<BackendRuntimeStatus>(JsonOptions, cancellationToken);

            if (status is null)
            {
                SetFailure(
                    BackendControlApiFailureKind.Unknown,
                    "Control API returned an empty stop response.");
                return null;
            }

            ClearFailure();
            return status;
        }
        catch (HttpRequestException ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            SetFailure(BackendControlApiFailureKind.Unreachable, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            SetFailure(BackendControlApiFailureKind.Unknown, ex.Message);
            return null;
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    private void ClearFailure()
    {
        _lastFailure = new BackendControlApiFailure();
    }

    private void SetFailure(BackendControlApiFailureKind kind, string message)
    {
        _lastFailure = new BackendControlApiFailure
        {
            Kind = kind,
            Message = string.IsNullOrWhiteSpace(message) ? "Unknown control API failure." : message
        };
    }

    private static async Task<string> ReadApiErrorMessageAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                var payload = JsonSerializer.Deserialize<ControlApiErrorPayload>(rawBody, JsonOptions);

                if (!string.IsNullOrWhiteSpace(payload?.Error))
                {
                    return payload.Error;
                }
            }
            catch
            {
                // Fallback below.
            }
        }

        return $"Control API rejected the request with status {(int)response.StatusCode}.";
    }

    private sealed class ControlApiErrorPayload
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;
    }
}
