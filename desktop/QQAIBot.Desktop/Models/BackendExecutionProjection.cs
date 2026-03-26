namespace QQAIBot.Desktop.Models;

public sealed class BackendExecutionProjection
{
    public string Kind { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string[] Stages { get; set; } = [];

    public string FailedStage { get; set; } = string.Empty;

    public string[] CompletedStages { get; set; } = [];

    public bool Degraded { get; set; }

    public string[] Recoveries { get; set; } = [];
}
