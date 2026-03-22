namespace QQAIBot.Desktop.Models;

public class BotConfig
{
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiDefaultApiKey { get; set; } = string.Empty;
    public string OpenAiDefaultModel { get; set; } = "deepseek-chat";
    public string OpenAiModel { get; set; } = "gpt-5.4";
    public string OpenAiBaseUrl { get; set; } = string.Empty;
    public string OpenAiDefaultBaseUrl { get; set; } = "https://api.deepseek.com/v1";
    public string OpenAiAdvancedTriggerPrefixes { get; set; } = "/5.4,/gpt,/vision,/高级,/多模态,/看图,/图片分析";
    public string NapCatWsUrl { get; set; } = "ws://127.0.0.1:3001";
    public string NapCatToken { get; set; } = string.Empty;
    public string BotPrefix { get; set; } = "/ai";
    public string BotPersona { get; set; } = string.Empty;
    public string MaxOutputChars { get; set; } = "800";
    public string AllowedGroupIds { get; set; } = string.Empty;
    public string AllowedUserIds { get; set; } = string.Empty;
}
