using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopConfigWorkflow
{
    public static DesktopShellSourceState UpdateEditorState(
        DesktopShellSourceState sourceState,
        BotConfig config,
        string controlApiToken,
        bool hasUnsavedChanges,
        string lastLoadedAtText,
        string lastSavedAtText,
        bool autoStartEnabled,
        bool canStartBackend,
        string logText)
    {
        return sourceState with
        {
            ConfigEditorState = sourceState.ConfigEditorState with
            {
                Config = BuildConfigCopy(config),
                ControlApiToken = controlApiToken,
                HasUnsavedChanges = hasUnsavedChanges,
                LastLoadedAtText = lastLoadedAtText,
                LastSavedAtText = lastSavedAtText
            },
            RuntimeSourceState = sourceState.RuntimeSourceState with
            {
                AutoStartEnabled = autoStartEnabled,
                CanStartBackend = canStartBackend
            },
            UiFeedbackState = sourceState.UiFeedbackState with
            {
                LogText = logText
            }
        };
    }

    public static DesktopShellSourceState ApplyLoadedDocument(
        DesktopShellSourceState sourceState,
        EnvDocument nextDocument,
        string lastLoadedAtText)
    {
        return sourceState with
        {
            LocalDocumentSourceState = sourceState.LocalDocumentSourceState with
            {
                ConfigDocument = new DesktopConfigDocumentState
                {
                    Document = nextDocument
                }
            },
            ConfigEditorState = sourceState.ConfigEditorState with
            {
                Config = BuildConfigCopy(nextDocument.Config),
                ControlApiToken = ResolveControlApiToken(nextDocument),
                HasUnsavedChanges = false,
                LastLoadedAtText = lastLoadedAtText
            }
        };
    }

    public static DesktopShellSourceState ApplySavedLocalControlPlane(
        DesktopShellSourceState sourceState,
        EnvDocument nextDocument,
        string normalizedToken)
    {
        return sourceState with
        {
            LocalDocumentSourceState = sourceState.LocalDocumentSourceState with
            {
                ConfigDocument = new DesktopConfigDocumentState
                {
                    Document = nextDocument
                }
            },
            ConfigEditorState = sourceState.ConfigEditorState with
            {
                ControlApiToken = normalizedToken
            }
        };
    }

    public static DesktopShellSourceState ApplySaveDraft(
        DesktopShellSourceState sourceState,
        BotConfig config)
    {
        return sourceState with
        {
            ConfigEditorState = sourceState.ConfigEditorState with
            {
                Config = BuildConfigCopy(config)
            },
            LocalDocumentSourceState = sourceState.LocalDocumentSourceState with
            {
                ConfigDocument = sourceState.LocalDocumentSourceState.ConfigDocument with
                {
                    Document = CloneEnvDocument(sourceState.LocalDocumentSourceState.ConfigDocument.Document, config)
                }
            }
        };
    }

    public static DesktopShellSourceState ApplySavedConfig(
        DesktopShellSourceState sourceState,
        BotConfig mergedConfig,
        EnvDocument nextDocument,
        string lastSavedAtText)
    {
        return sourceState with
        {
            ConfigEditorState = sourceState.ConfigEditorState with
            {
                Config = BuildConfigCopy(mergedConfig),
                HasUnsavedChanges = false,
                LastSavedAtText = lastSavedAtText
            },
            LocalDocumentSourceState = sourceState.LocalDocumentSourceState with
            {
                ConfigDocument = sourceState.LocalDocumentSourceState.ConfigDocument with
                {
                    Document = nextDocument
                }
            }
        };
    }

    public static DesktopShellSourceState SetLocalDocument(
        DesktopShellSourceState sourceState,
        EnvDocument nextDocument)
    {
        return sourceState with
        {
            LocalDocumentSourceState = sourceState.LocalDocumentSourceState with
            {
                ConfigDocument = new DesktopConfigDocumentState
                {
                    Document = nextDocument
                }
            }
        };
    }

    public static string ResolveControlApiToken(EnvDocument document)
    {
        return document.ExtraValues.TryGetValue("QQ_AI_BOT_CONTROL_API_TOKEN", out var accessToken)
            ? accessToken
            : string.Empty;
    }

    public static BotConfig BuildConfigCopy(BotConfig config)
    {
        return new BotConfig
        {
            OpenAiApiKey = config.OpenAiApiKey,
            OpenAiDefaultApiKey = config.OpenAiDefaultApiKey,
            OpenAiDefaultModel = config.OpenAiDefaultModel,
            OpenAiModel = config.OpenAiModel,
            OpenAiBaseUrl = config.OpenAiBaseUrl,
            OpenAiDefaultBaseUrl = config.OpenAiDefaultBaseUrl,
            OpenAiDefaultReasoningEffort = config.OpenAiDefaultReasoningEffort,
            OpenAiAdvancedReasoningEffort = config.OpenAiAdvancedReasoningEffort,
            OpenAiDefaultTextVerbosity = config.OpenAiDefaultTextVerbosity,
            OpenAiAdvancedTextVerbosity = config.OpenAiAdvancedTextVerbosity,
            OpenAiDefaultEnableWebSearch = config.OpenAiDefaultEnableWebSearch,
            OpenAiAdvancedEnableWebSearch = config.OpenAiAdvancedEnableWebSearch,
            OpenAiDefaultEnableCodeInterpreter = config.OpenAiDefaultEnableCodeInterpreter,
            OpenAiAdvancedEnableCodeInterpreter = config.OpenAiAdvancedEnableCodeInterpreter,
            OpenAiAdvancedTriggerPrefixes = config.OpenAiAdvancedTriggerPrefixes,
            DeepSeekFallbackEnabled = config.DeepSeekFallbackEnabled,
            DeepSeekApiKey = config.DeepSeekApiKey,
            DeepSeekModel = config.DeepSeekModel,
            DeepSeekBaseUrl = config.DeepSeekBaseUrl,
            NapCatWsUrl = config.NapCatWsUrl,
            NapCatToken = config.NapCatToken,
            WechatBridgeUrl = config.WechatBridgeUrl,
            WechatBridgeToken = config.WechatBridgeToken,
            WechatBotPrefix = config.WechatBotPrefix,
            BotPrefix = config.BotPrefix,
            BotSystemPrompt = config.BotSystemPrompt,
            BotPersona = config.BotPersona,
            MaxOutputChars = config.MaxOutputChars,
            AllowedChatIds = config.AllowedChatIds,
            AllowedUserIds = config.AllowedUserIds
        };
    }

    public static BotConfig MergeSavedConfig(BotConfig submittedConfig, BotConfig savedConfig)
    {
        var mergedConfig = BuildConfigCopy(savedConfig);

        if (string.IsNullOrWhiteSpace(savedConfig.BotSystemPrompt))
        {
            mergedConfig.BotSystemPrompt = submittedConfig.BotSystemPrompt;
        }

        return mergedConfig;
    }

    public static EnvDocument CloneEnvDocument(EnvDocument source, BotConfig? config = null)
    {
        var nextDocument = new EnvDocument
        {
            Config = BuildConfigCopy(config ?? source.Config)
        };

        foreach (var pair in source.ExtraValues)
        {
            nextDocument.ExtraValues[pair.Key] = pair.Value;
        }

        return nextDocument;
    }
}
