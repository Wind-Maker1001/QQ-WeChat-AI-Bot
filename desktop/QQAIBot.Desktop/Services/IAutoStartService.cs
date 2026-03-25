namespace QQAIBot.Desktop.Services;

public interface IAutoStartService
{
    bool IsEnabled();

    void SetEnabled(bool enabled);
}
