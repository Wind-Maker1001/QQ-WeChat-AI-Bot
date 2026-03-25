using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public interface IActivityStateStore
{
    DesktopActivityState Load(string backendRootPath);

    void Save(string backendRootPath, DesktopActivityState state);
}
