namespace QQAIBot.Desktop.Models;

public sealed class BackendRequestedTools
{
    public string[] Requested { get; set; } = [];

    public string[] Required { get; set; } = [];
}
