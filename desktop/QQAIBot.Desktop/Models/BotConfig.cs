namespace QQAIBot.Desktop.Models;

public class BotConfig
{
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiDefaultApiKey { get; set; } = string.Empty;
    public string OpenAiDefaultModel { get; set; } = "gpt-5.4";
    public string OpenAiModel { get; set; } = "gpt-5.4";
    public string OpenAiBaseUrl { get; set; } = string.Empty;
    public string OpenAiDefaultBaseUrl { get; set; } = string.Empty;
    public string OpenAiDefaultReasoningEffort { get; set; } = "medium";
    public string OpenAiDefaultTextVerbosity { get; set; } = "medium";
    public string OpenAiDefaultEnableWebSearch { get; set; } = "false";
    public string OpenAiDefaultEnableCodeInterpreter { get; set; } = "false";
    public string OpenAiAdvancedTriggerPrefixes { get; set; } = "/5.4,/gpt,/vision,/楂樼骇,/澶氭ā鎬?/鐪嬪浘,/鍥剧墖鍒嗘瀽";
    public string OpenAiAdvancedReasoningEffort { get; set; } = "high";
    public string OpenAiAdvancedTextVerbosity { get; set; } = "high";
    public string OpenAiAdvancedEnableWebSearch { get; set; } = "true";
    public string OpenAiAdvancedEnableCodeInterpreter { get; set; } = "true";
    public string NapCatWsUrl { get; set; } = "ws://127.0.0.1:3001";
    public string NapCatToken { get; set; } = string.Empty;
    public string WechatBridgeUrl { get; set; } = string.Empty;
    public string WechatBridgeToken { get; set; } = string.Empty;
    public string WechatBotPrefix { get; set; } = "/ai";
    public string BotPrefix { get; set; } = "/ai";
    public string BotSystemPrompt { get; set; } = string.Empty;
    public string BotPersona { get; set; } = string.Empty;
    public string MaxOutputChars { get; set; } = "800";
    public string AllowedChatIds { get; set; } = string.Empty;
    public string AllowedUserIds { get; set; } = string.Empty;
}
