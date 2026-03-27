namespace QQAIBot.Desktop.Services;

public interface ILocalBootstrapConfigStore
{
    Task SaveExtraValueAsync(string rootPath, string key, string? value);
}
