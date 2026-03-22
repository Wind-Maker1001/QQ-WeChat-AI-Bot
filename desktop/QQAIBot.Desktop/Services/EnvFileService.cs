using System.IO;
using System.Linq;
using System.Text;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class EnvFileService
{
    private static readonly string[] KnownKeys =
    {
        "OPENAI_API_KEY",
        "OPENAI_DEFAULT_API_KEY",
        "OPENAI_ADVANCED_API_KEY",
        "OPENAI_MODEL",
        "OPENAI_DEFAULT_MODEL",
        "OPENAI_ADVANCED_MODEL",
        "OPENAI_BASE_URL",
        "OPENAI_DEFAULT_BASE_URL",
        "OPENAI_ADVANCED_BASE_URL",
        "OPENAI_ADVANCED_TRIGGER_PREFIXES",
        "OPENAI_DEFAULT_API_STYLE",
        "OPENAI_ADVANCED_API_STYLE",
        "NAPCAT_WS_URL",
        "NAPCAT_TOKEN",
        "BOT_PREFIX",
        "BOT_PERSONA",
        "MAX_OUTPUT_CHARS",
        "ALLOWED_GROUP_IDS",
        "ALLOWED_USER_IDS"
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string GetEnvPath(string rootPath)
    {
        return Path.Combine(rootPath, ".env");
    }

    public string GetEnvExamplePath(string rootPath)
    {
        return Path.Combine(rootPath, ".env.example");
    }

    public async Task<EnvDocument> LoadAsync(string rootPath)
    {
        var envPath = GetEnvPath(rootPath);
        var examplePath = GetEnvExamplePath(rootPath);

        if (!File.Exists(envPath))
        {
            if (File.Exists(examplePath))
            {
                File.Copy(examplePath, envPath);
            }
            else
            {
                var emptyDocument = new EnvDocument();
                await SaveAsync(rootPath, emptyDocument);
            }
        }

        var lines = await File.ReadAllLinesAsync(envPath, Utf8NoBom);
        var document = new EnvDocument();

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

            switch (key)
            {
                case "OPENAI_API_KEY":
                    document.Config.OpenAiApiKey = value;
                    break;
                case "OPENAI_ADVANCED_API_KEY":
                    document.Config.OpenAiApiKey = value;
                    break;
                case "OPENAI_DEFAULT_API_KEY":
                    document.Config.OpenAiDefaultApiKey = value;
                    break;
                case "OPENAI_MODEL":
                    document.Config.OpenAiModel = value;
                    break;
                case "OPENAI_DEFAULT_MODEL":
                    document.Config.OpenAiDefaultModel = value;
                    break;
                case "OPENAI_ADVANCED_MODEL":
                    document.Config.OpenAiModel = value;
                    break;
                case "OPENAI_BASE_URL":
                    document.Config.OpenAiBaseUrl = value;
                    break;
                case "OPENAI_ADVANCED_BASE_URL":
                    document.Config.OpenAiBaseUrl = value;
                    break;
                case "OPENAI_DEFAULT_BASE_URL":
                    document.Config.OpenAiDefaultBaseUrl = value;
                    break;
                case "OPENAI_ADVANCED_TRIGGER_PREFIXES":
                    document.Config.OpenAiAdvancedTriggerPrefixes = value;
                    break;
                case "NAPCAT_WS_URL":
                    document.Config.NapCatWsUrl = value;
                    break;
                case "NAPCAT_TOKEN":
                    document.Config.NapCatToken = value;
                    break;
                case "BOT_PREFIX":
                    document.Config.BotPrefix = value;
                    break;
                case "BOT_PERSONA":
                    document.Config.BotPersona = value;
                    break;
                case "MAX_OUTPUT_CHARS":
                    document.Config.MaxOutputChars = value;
                    break;
                case "ALLOWED_GROUP_IDS":
                    document.Config.AllowedGroupIds = value;
                    break;
                case "ALLOWED_USER_IDS":
                    document.Config.AllowedUserIds = value;
                    break;
                default:
                    document.ExtraValues[key] = value;
                    break;
            }
        }

        return document;
    }

    public async Task SaveAsync(string rootPath, EnvDocument document)
    {
        Directory.CreateDirectory(rootPath);

        var envPath = GetEnvPath(rootPath);
        var config = document.Config;
        var lines = new List<string>
        {
            $"OPENAI_API_KEY={config.OpenAiApiKey}",
            $"OPENAI_DEFAULT_API_KEY={config.OpenAiDefaultApiKey}",
            $"OPENAI_ADVANCED_API_KEY={config.OpenAiApiKey}",
            $"OPENAI_MODEL={config.OpenAiModel}",
            $"OPENAI_DEFAULT_MODEL={config.OpenAiDefaultModel}",
            $"OPENAI_ADVANCED_MODEL={config.OpenAiModel}",
            $"OPENAI_BASE_URL={config.OpenAiBaseUrl}",
            $"OPENAI_DEFAULT_BASE_URL={config.OpenAiDefaultBaseUrl}",
            $"OPENAI_ADVANCED_BASE_URL={config.OpenAiBaseUrl}",
            "OPENAI_DEFAULT_API_STYLE=chat_completions",
            "OPENAI_ADVANCED_API_STYLE=responses",
            $"OPENAI_ADVANCED_TRIGGER_PREFIXES={config.OpenAiAdvancedTriggerPrefixes}",
            $"NAPCAT_WS_URL={config.NapCatWsUrl}",
            $"NAPCAT_TOKEN={config.NapCatToken}",
            $"BOT_PREFIX={config.BotPrefix}",
            $"BOT_PERSONA={EncodeEnvValue(config.BotPersona)}",
            $"MAX_OUTPUT_CHARS={config.MaxOutputChars}",
            $"ALLOWED_GROUP_IDS={config.AllowedGroupIds}",
            $"ALLOWED_USER_IDS={config.AllowedUserIds}"
        };

        foreach (var pair in document.ExtraValues
                     .Where(static pair => !KnownKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{pair.Key}={pair.Value}");
        }

        await File.WriteAllLinesAsync(envPath, lines, Utf8NoBom);
    }

    private static string EncodeEnvValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
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
}
