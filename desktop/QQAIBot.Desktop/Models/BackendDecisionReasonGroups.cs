namespace QQAIBot.Desktop.Models;

public sealed class BackendDecisionReasonGroups
{
    public string[] TriggerReasons { get; set; } = [];

    public string[] CapabilityReasons { get; set; } = [];

    public string[] UpgradeReasons { get; set; } = [];
}
