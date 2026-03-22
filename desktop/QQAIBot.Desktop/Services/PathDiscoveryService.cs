using System.IO;
using System.Linq;

namespace QQAIBot.Desktop.Services;

public static class PathDiscoveryService
{
    public static bool TryDiscoverBackendRoot(out string path)
    {
        var probeRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        }
        .Where(static item => !string.IsNullOrWhiteSpace(item))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var probeRoot in probeRoots)
        {
            var directory = new DirectoryInfo(probeRoot);

            while (directory is not null)
            {
                if (IsBackendRoot(directory.FullName))
                {
                    path = directory.FullName;
                    return true;
                }

                directory = directory.Parent;
            }
        }

        path = Directory.GetCurrentDirectory();
        return false;
    }

    public static bool IsBackendRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        return File.Exists(Path.Combine(path, "package.json"))
            && File.Exists(Path.Combine(path, "src", "index.mjs"));
    }
}
