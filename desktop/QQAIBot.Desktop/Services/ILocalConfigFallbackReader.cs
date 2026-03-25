using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public interface ILocalConfigFallbackReader
{
    Task<EnvDocument> LoadAsync(string rootPath);
}
