namespace QQAIBot.Desktop.Models;

public sealed class EnvDocument
{
    public BotConfig Config { get; set; } = new();

    public Dictionary<string, string> ExtraValues { get; } = new(StringComparer.OrdinalIgnoreCase);
}
