using System.IO;
using System.Text;
using System.Text.Json;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class LocalEnvConfigFallbackReader : ILocalConfigFallbackReader
{
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
        ApplyEnvValues(document, lines, preferConfigValuesFromEnv: !File.Exists(runtimeSettingsPath));
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
        IEnumerable<string> lines,
        bool preferConfigValuesFromEnv)
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

            if (!TryApplyConfigValue(document.Config, key, value, preferConfigValuesFromEnv))
            {
                document.ExtraValues[key] = value;
            }
        }
    }

    private static bool TryApplyConfigValue(
        BotConfig config,
        string key,
        string value,
        bool preferConfigValuesFromEnv)
    {
        if (!preferConfigValuesFromEnv)
        {
            return key switch
            {
                "ALLOWED_GROUP_IDS" => true,
                "OPENAI_API_KEY" => true,
                "OPENAI_ADVANCED_API_KEY" => true,
                "OPENAI_DEFAULT_API_KEY" => true,
                "OPENAI_MODEL" => true,
                "OPENAI_DEFAULT_MODEL" => true,
                "OPENAI_ADVANCED_MODEL" => true,
                "OPENAI_BASE_URL" => true,
                "OPENAI_ADVANCED_BASE_URL" => true,
                "OPENAI_DEFAULT_BASE_URL" => true,
                "OPENAI_DEFAULT_REASONING_EFFORT" => true,
                "OPENAI_ADVANCED_REASONING_EFFORT" => true,
                "OPENAI_DEFAULT_TEXT_VERBOSITY" => true,
                "OPENAI_ADVANCED_TEXT_VERBOSITY" => true,
                "OPENAI_DEFAULT_ENABLE_WEB_SEARCH" => true,
                "OPENAI_ADVANCED_ENABLE_WEB_SEARCH" => true,
                "OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER" => true,
                "OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER" => true,
                "OPENAI_ADVANCED_TRIGGER_PREFIXES" => true,
                "DEEPSEEK_FALLBACK_ENABLED" => true,
                "DEEPSEEK_API_KEY" => true,
                "DEEPSEEK_MODEL" => true,
                "DEEPSEEK_BASE_URL" => true,
                "NAPCAT_WS_URL" => true,
                "NAPCAT_TOKEN" => true,
                "WECHAT_BRIDGE_URL" => true,
                "WECHAT_BRIDGE_TOKEN" => true,
                "WECHAT_BOT_PREFIX" => true,
                "BOT_PREFIX" => true,
                "BOT_SYSTEM_PROMPT" => true,
                "BOT_PERSONA" => true,
                "MAX_OUTPUT_CHARS" => true,
                "ALLOWED_CHAT_IDS" => true,
                "ALLOWED_USER_IDS" => true,
                _ => false
            };
        }

        switch (key)
        {
            case "OPENAI_API_KEY":
            case "OPENAI_ADVANCED_API_KEY":
                config.OpenAiApiKey = value;
                return true;
            case "OPENAI_DEFAULT_API_KEY":
                config.OpenAiDefaultApiKey = value;
                return true;
            case "OPENAI_MODEL":
            case "OPENAI_ADVANCED_MODEL":
                config.OpenAiModel = value;
                return true;
            case "OPENAI_DEFAULT_MODEL":
                config.OpenAiDefaultModel = value;
                return true;
            case "OPENAI_BASE_URL":
            case "OPENAI_ADVANCED_BASE_URL":
                config.OpenAiBaseUrl = value;
                return true;
            case "OPENAI_DEFAULT_BASE_URL":
                config.OpenAiDefaultBaseUrl = value;
                return true;
            case "OPENAI_DEFAULT_REASONING_EFFORT":
                config.OpenAiDefaultReasoningEffort = value;
                return true;
            case "OPENAI_ADVANCED_REASONING_EFFORT":
                config.OpenAiAdvancedReasoningEffort = value;
                return true;
            case "OPENAI_DEFAULT_TEXT_VERBOSITY":
                config.OpenAiDefaultTextVerbosity = value;
                return true;
            case "OPENAI_ADVANCED_TEXT_VERBOSITY":
                config.OpenAiAdvancedTextVerbosity = value;
                return true;
            case "OPENAI_DEFAULT_ENABLE_WEB_SEARCH":
                config.OpenAiDefaultEnableWebSearch = value;
                return true;
            case "OPENAI_ADVANCED_ENABLE_WEB_SEARCH":
                config.OpenAiAdvancedEnableWebSearch = value;
                return true;
            case "OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER":
                config.OpenAiDefaultEnableCodeInterpreter = value;
                return true;
            case "OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER":
                config.OpenAiAdvancedEnableCodeInterpreter = value;
                return true;
            case "OPENAI_ADVANCED_TRIGGER_PREFIXES":
                config.OpenAiAdvancedTriggerPrefixes = value;
                return true;
            case "DEEPSEEK_FALLBACK_ENABLED":
                config.DeepSeekFallbackEnabled = value;
                return true;
            case "DEEPSEEK_API_KEY":
                config.DeepSeekApiKey = value;
                return true;
            case "DEEPSEEK_MODEL":
                config.DeepSeekModel = value;
                return true;
            case "DEEPSEEK_BASE_URL":
                config.DeepSeekBaseUrl = value;
                return true;
            case "NAPCAT_WS_URL":
                config.NapCatWsUrl = value;
                return true;
            case "NAPCAT_TOKEN":
                config.NapCatToken = value;
                return true;
            case "WECHAT_BRIDGE_URL":
                config.WechatBridgeUrl = value;
                return true;
            case "WECHAT_BRIDGE_TOKEN":
                config.WechatBridgeToken = value;
                return true;
            case "WECHAT_BOT_PREFIX":
                config.WechatBotPrefix = value;
                return true;
            case "BOT_PREFIX":
                config.BotPrefix = value;
                return true;
            case "BOT_SYSTEM_PROMPT":
                config.BotSystemPrompt = value;
                return true;
            case "BOT_PERSONA":
                config.BotPersona = value;
                return true;
            case "MAX_OUTPUT_CHARS":
                config.MaxOutputChars = value;
                return true;
            case "ALLOWED_CHAT_IDS":
                config.AllowedChatIds = value;
                return true;
            case "ALLOWED_GROUP_IDS":
                if (string.IsNullOrWhiteSpace(config.AllowedChatIds))
                {
                    config.AllowedChatIds = value;
                }

                return true;
            case "ALLOWED_USER_IDS":
                config.AllowedUserIds = value;
                return true;
            default:
                return false;
        }
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
