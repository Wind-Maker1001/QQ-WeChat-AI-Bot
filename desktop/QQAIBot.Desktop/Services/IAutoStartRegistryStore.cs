namespace QQAIBot.Desktop.Services;

public interface IAutoStartRegistryStore
{
    string? GetValue(string valueName);

    void SetValue(string valueName, string value);

    void DeleteValue(string valueName);
}
