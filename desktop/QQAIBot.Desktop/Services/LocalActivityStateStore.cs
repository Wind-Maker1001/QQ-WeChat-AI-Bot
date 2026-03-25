using System.IO;
using System.Text;
using System.Text.Json;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class LocalActivityStateStore : IActivityStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly DesktopActivityStatePolicy _policy;
    private readonly DesktopActivityStateStoragePolicy _storagePolicy;

    public LocalActivityStateStore(
        string? storeRootPath = null,
        DesktopActivityStatePolicy? policy = null,
        DesktopActivityStateStoragePolicy? storagePolicy = null)
    {
        _policy = policy ?? DesktopActivityStatePolicy.Default;
        _storagePolicy = storagePolicy ?? new DesktopActivityStateStoragePolicy(storeRootPath);
    }

    public DesktopActivityState Load(string backendRootPath)
    {
        var filePath = _storagePolicy.ResolveStateFilePath(backendRootPath);

        if (!File.Exists(filePath))
        {
            return new DesktopActivityState();
        }

        try
        {
            var json = File.ReadAllText(filePath, Encoding.UTF8);
            var state = JsonSerializer.Deserialize<DesktopActivityState>(json, JsonOptions);
            var normalizedState = _policy.Normalize(state);

            if (normalizedState is null)
            {
                TryDelete(filePath);
                return _policy.CreateDefaultState();
            }

            if (_policy.IsDefaultState(normalizedState))
            {
                TryDelete(filePath);
            }

            return normalizedState;
        }
        catch
        {
            return _policy.CreateDefaultState();
        }
    }

    public void Save(string backendRootPath, DesktopActivityState state)
    {
        var filePath = _storagePolicy.ResolveStateFilePath(backendRootPath);
        var normalizedState = _policy.Normalize(state) ?? _policy.CreateDefaultState();

        if (_policy.IsDefaultState(normalizedState))
        {
            TryDelete(filePath);
            return;
        }

        Directory.CreateDirectory(_storagePolicy.StoreRootPath);
        var json = JsonSerializer.Serialize(normalizedState, JsonOptions);
        var tempFilePath = $"{filePath}.tmp";
        File.WriteAllText(tempFilePath, json, Encoding.UTF8);
        File.Move(tempFilePath, filePath, overwrite: true);
    }

    private static void TryDelete(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }
}
