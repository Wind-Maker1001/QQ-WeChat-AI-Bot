using System.IO;
using System.Text;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class LocalEnvConfigFallbackReader : ILocalConfigFallbackReader
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<EnvDocument> LoadAsync(string rootPath)
    {
        var envPath = Path.Combine(rootPath, ".env");

        if (!File.Exists(envPath))
        {
            return new EnvDocument();
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
                case "OPENAI_DEFAULT_REASONING_EFFORT":
                    document.Config.OpenAiDefaultReasoningEffort = value;
                    break;
                case "OPENAI_ADVANCED_REASONING_EFFORT":
                    document.Config.OpenAiAdvancedReasoningEffort = value;
                    break;
                case "OPENAI_DEFAULT_TEXT_VERBOSITY":
                    document.Config.OpenAiDefaultTextVerbosity = value;
                    break;
                case "OPENAI_ADVANCED_TEXT_VERBOSITY":
                    document.Config.OpenAiAdvancedTextVerbosity = value;
                    break;
                case "OPENAI_DEFAULT_ENABLE_WEB_SEARCH":
                    document.Config.OpenAiDefaultEnableWebSearch = value;
                    break;
                case "OPENAI_ADVANCED_ENABLE_WEB_SEARCH":
                    document.Config.OpenAiAdvancedEnableWebSearch = value;
                    break;
                case "OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER":
                    document.Config.OpenAiDefaultEnableCodeInterpreter = value;
                    break;
                case "OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER":
                    document.Config.OpenAiAdvancedEnableCodeInterpreter = value;
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
                case "WECHAT_BRIDGE_URL":
                    document.Config.WechatBridgeUrl = value;
                    break;
                case "WECHAT_BRIDGE_TOKEN":
                    document.Config.WechatBridgeToken = value;
                    break;
                case "WECHAT_BOT_PREFIX":
                    document.Config.WechatBotPrefix = value;
                    break;
                case "BOT_PREFIX":
                    document.Config.BotPrefix = value;
                    break;
                case "BOT_SYSTEM_PROMPT":
                    document.Config.BotSystemPrompt = value;
                    break;
                case "BOT_PERSONA":
                    document.Config.BotPersona = value;
                    break;
                case "MAX_OUTPUT_CHARS":
                    document.Config.MaxOutputChars = value;
                    break;
                case "ALLOWED_CHAT_IDS":
                    document.Config.AllowedChatIds = value;
                    break;
                case "ALLOWED_GROUP_IDS":
                    if (string.IsNullOrWhiteSpace(document.Config.AllowedChatIds))
                    {
                        document.Config.AllowedChatIds = value;
                    }
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
