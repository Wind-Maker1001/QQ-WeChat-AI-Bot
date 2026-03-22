namespace QQAIBot.Desktop.Models;

public sealed class BackendControlConfigResponse : BotConfig
{
    public string EnvPath { get; set; } = string.Empty;

    public bool RestartRequired { get; set; }

    public string SavedAt { get; set; } = string.Empty;
}
