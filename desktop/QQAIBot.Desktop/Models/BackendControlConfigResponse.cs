namespace QQAIBot.Desktop.Models;

public sealed class BackendControlConfigResponse : BotConfig
{
    public string ConfigPath { get; set; } = string.Empty;

    public string BootstrapEnvPath { get; set; } = string.Empty;

    public bool RestartRequired { get; set; }

    public string SavedAt { get; set; } = string.Empty;
}
