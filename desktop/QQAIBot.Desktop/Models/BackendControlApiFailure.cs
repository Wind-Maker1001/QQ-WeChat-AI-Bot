namespace QQAIBot.Desktop.Models;

public enum BackendControlApiFailureKind
{
    None = 0,
    Unreachable = 1,
    Rejected = 2,
    Unauthorized = 3,
    Unknown = 4,
    Incompatible = 5
}

public sealed class BackendControlApiFailure
{
    public BackendControlApiFailureKind Kind { get; init; } = BackendControlApiFailureKind.None;

    public string Message { get; init; } = string.Empty;
}
