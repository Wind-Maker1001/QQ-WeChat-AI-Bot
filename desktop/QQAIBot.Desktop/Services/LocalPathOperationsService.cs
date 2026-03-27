using System.Diagnostics;
using System.IO;

namespace QQAIBot.Desktop.Services;

public sealed class LocalPathOperationsService : ILocalPathOperationsService
{
    public void OpenFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Folder path is required.", nameof(path));
        }

        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true
        });
    }

    public int ClearDirectoryContents(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Directory path is required.", nameof(path));
        }

        if (!Directory.Exists(path))
        {
            return 0;
        }

        var removedEntries = 0;

        foreach (var filePath in Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly))
        {
            File.Delete(filePath);
            removedEntries += 1;
        }

        foreach (var directoryPath in Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly))
        {
            removedEntries += CountEntries(directoryPath);
            Directory.Delete(directoryPath, recursive: true);
            removedEntries += 1;
        }

        return removedEntries;
    }

    private static int CountEntries(string path)
    {
        return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length +
               Directory.GetDirectories(path, "*", SearchOption.AllDirectories).Length;
    }
}
