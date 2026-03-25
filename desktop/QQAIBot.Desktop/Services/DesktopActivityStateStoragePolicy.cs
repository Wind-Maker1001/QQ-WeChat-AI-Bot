using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QQAIBot.Desktop.Services;

public sealed class DesktopActivityStateStoragePolicy
{
    public string StoreRootPath { get; }

    public DesktopActivityStateStoragePolicy(string? storeRootPath = null)
    {
        StoreRootPath = string.IsNullOrWhiteSpace(storeRootPath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QQAIBot.Desktop",
                "activity-state")
            : storeRootPath;
    }

    public string ResolveStateFilePath(string backendRootPath)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(backendRootPath)
            ? "default"
            : backendRootPath.Trim().ToLowerInvariant();
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return Path.Combine(StoreRootPath, $"{hash}.json");
    }
}
