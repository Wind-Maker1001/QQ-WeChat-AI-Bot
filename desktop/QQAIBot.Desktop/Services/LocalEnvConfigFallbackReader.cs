using System.IO;
using System.Text;
using System.Text.Json;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class LocalEnvConfigFallbackReader : ILocalConfigFallbackReader
{
    private const string LocalControlApiEnvPrefix = "QQ_AI_BOT_CONTROL_API_";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions RuntimeSettingsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<EnvDocument> LoadAsync(string rootPath)
    {
        var document = new EnvDocument();
        var runtimeSettingsPath = Path.Combine(rootPath, "data", "runtime-settings.json");
        var envPath = Path.Combine(rootPath, ".env");

        if (File.Exists(runtimeSettingsPath))
        {
            document.Config = await LoadRuntimeSettingsConfigAsync(runtimeSettingsPath);
        }

        if (!File.Exists(envPath))
        {
            return document;
        }

        var lines = await File.ReadAllLinesAsync(envPath, Utf8NoBom);
        ApplyEnvValues(document, lines);
        return document;
    }

    private static async Task<BotConfig> LoadRuntimeSettingsConfigAsync(string runtimeSettingsPath)
    {
        await using var stream = File.OpenRead(runtimeSettingsPath);
        var runtimeSettingsDocument = await JsonSerializer.DeserializeAsync<RuntimeSettingsDocument>(
            stream,
            RuntimeSettingsJsonOptions);

        return runtimeSettingsDocument?.Settings ?? new BotConfig();
    }

    private static void ApplyEnvValues(
        EnvDocument document,
        IEnumerable<string> lines)
    {
        foreach (var originalLine in lines)
        {
            var line = originalLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var rawValue = line[(separatorIndex + 1)..];
            var value = DecodeEnvValue(Unquote(rawValue.Trim()));

            if (ShouldCaptureLocalExtraValue(key))
            {
                document.ExtraValues[key] = value;
            }
        }
    }

    private static bool ShouldCaptureLocalExtraValue(string key)
    {
        return key.StartsWith(LocalControlApiEnvPrefix, StringComparison.Ordinal);
    }

    private static string DecodeEnvValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2)
        {
            var doubleQuoted = value.StartsWith('"') && value.EndsWith('"');
            var singleQuoted = value.StartsWith('\'') && value.EndsWith('\'');

            if (doubleQuoted || singleQuoted)
            {
                return value[1..^1];
            }
        }

        return value;
    }

    private sealed class RuntimeSettingsDocument
    {
        public BotConfig Settings { get; set; } = new();
    }
}
