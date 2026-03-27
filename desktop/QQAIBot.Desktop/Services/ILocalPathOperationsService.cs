namespace QQAIBot.Desktop.Services;

public interface ILocalPathOperationsService
{
    void OpenFolder(string path);

    int ClearDirectoryContents(string path);
}
