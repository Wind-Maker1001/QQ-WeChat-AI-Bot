namespace QQAIBot.Desktop.Services;

public interface IBotProcessService : IDisposable
{
    event EventHandler<string>? LogReceived;

    event EventHandler? ProcessExited;

    bool IsRunning { get; }

    void Start(string workingDirectory);

    void Detach();

    Task StopAsync();
}
