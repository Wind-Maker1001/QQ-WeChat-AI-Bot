using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public sealed class LocalStateSnapshotService : ILocalStateSnapshotService
{
    private readonly DesktopActivityStateStoragePolicy _activityStateStoragePolicy;

    public LocalStateSnapshotService(DesktopActivityStateStoragePolicy? activityStateStoragePolicy = null)
    {
        _activityStateStoragePolicy = activityStateStoragePolicy ?? new DesktopActivityStateStoragePolicy();
    }

    public async Task<LocalStateSnapshotResult> ExportAsync(string backendRootPath)
    {
        return await ExportInternalAsync(backendRootPath, includeEnv: true, archiveNameSuffix: "");
    }

    public async Task<LocalStateSnapshotResult> ExportSafeAsync(string backendRootPath)
    {
        return await ExportInternalAsync(backendRootPath, includeEnv: false, archiveNameSuffix: "-safe");
    }

    private async Task<LocalStateSnapshotResult> ExportInternalAsync(
        string backendRootPath,
        bool includeEnv,
        string archiveNameSuffix)
    {
        if (string.IsNullOrWhiteSpace(backendRootPath))
        {
            throw new ArgumentException("Backend root path is required.", nameof(backendRootPath));
        }

        if (!Directory.Exists(backendRootPath))
        {
            throw new DirectoryNotFoundException($"Backend root does not exist: {backendRootPath}");
        }

        var snapshotDirectory = Path.Combine(backendRootPath, "artifacts", "state-snapshots");
        Directory.CreateDirectory(snapshotDirectory);

        var archivePath = BuildUniqueArchivePath(snapshotDirectory, archiveNameSuffix);
        var includedEntries = new List<string>();
        var envPath = Path.Combine(backendRootPath, ".env");
        var dataPath = Path.Combine(backendRootPath, "data");
        var activityStatePath = _activityStateStoragePolicy.ResolveStateFilePath(backendRootPath);

        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            if (includeEnv && File.Exists(envPath))
            {
                archive.CreateEntryFromFile(envPath, "app/.env");
                includedEntries.Add("app/.env");
            }

            if (Directory.Exists(dataPath))
            {
                AddDirectoryToArchive(archive, dataPath, "app/data", includedEntries);
            }

            if (File.Exists(activityStatePath))
            {
                archive.CreateEntryFromFile(activityStatePath, "desktop/activity-state.json");
                includedEntries.Add("desktop/activity-state.json");
            }

            var manifest = JsonSerializer.Serialize(
                new
                {
                    createdAt = DateTimeOffset.Now.ToString("O"),
                    backendRootPath,
                    includesSecrets = includeEnv && File.Exists(envPath),
                    includedEntries
                },
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
            var manifestEntry = archive.CreateEntry("manifest.json");

            await using var stream = manifestEntry.Open();
            await using var writer = new StreamWriter(stream, Encoding.UTF8);
            await writer.WriteAsync(manifest);
            await writer.FlushAsync();
        }

        return new LocalStateSnapshotResult
        {
            ArchivePath = archivePath,
            IncludedEntries = includedEntries,
            IncludesSecrets = includeEnv && File.Exists(envPath)
        };
    }

    public async Task<IReadOnlyList<LocalStateSnapshotDescriptor>> ListAsync(string backendRootPath)
    {
        if (string.IsNullOrWhiteSpace(backendRootPath) || !Directory.Exists(backendRootPath))
        {
            return [];
        }

        var snapshotDirectory = Path.Combine(backendRootPath, "artifacts", "state-snapshots");

        if (!Directory.Exists(snapshotDirectory))
        {
            return [];
        }

        var archivePaths = Directory.GetFiles(snapshotDirectory, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        var descriptors = new List<LocalStateSnapshotDescriptor>(archivePaths.Length);

        foreach (var archivePath in archivePaths)
        {
            descriptors.Add(await BuildDescriptorAsync(archivePath));
        }

        return descriptors;
    }

    public Task DeleteAsync(string archivePath)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException($"State snapshot archive does not exist: {archivePath}");
        }

        File.Delete(archivePath);
        return Task.CompletedTask;
    }

    public async Task<LocalStateSnapshotPreviewResult> PreviewAsync(string backendRootPath, string archivePath)
    {
        if (string.IsNullOrWhiteSpace(backendRootPath))
        {
            throw new ArgumentException("Backend root path is required.", nameof(backendRootPath));
        }

        if (!Directory.Exists(backendRootPath))
        {
            throw new DirectoryNotFoundException($"Backend root does not exist: {backendRootPath}");
        }

        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException($"State snapshot archive does not exist: {archivePath}");
        }

        var envPath = Path.Combine(backendRootPath, ".env");
        var dataPath = Path.Combine(backendRootPath, "data");
        var activityStatePath = _activityStateStoragePolicy.ResolveStateFilePath(backendRootPath);
        var sessionsPath = Path.Combine(dataPath, "sessions.json");

        using var archive = ZipFile.OpenRead(archivePath);
        var envEntry = archive.GetEntry("app/.env");
        var snapshotEnv = envEntry is not null
            ? ParseSimpleEnv(await ReadEntryTextAsync(envEntry))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>
        {
            await BuildFilePreviewLineAsync(envEntry, envPath, ".env"),
            await BuildFilePreviewLineAsync(archive.GetEntry("desktop/activity-state.json"), activityStatePath, "desktop activity state")
        };
        lines.AddRange(await BuildSessionPreviewLinesAsync(archive.GetEntry("app/data/sessions.json"), sessionsPath));
        lines.AddRange(await BuildDirectoryPreviewLinesAsync(archive, "app/data/", dataPath, "data/"));
        lines.AddRange(BuildTrackedEnvKeyPreviewLines(snapshotEnv, currentEnvPath: envPath));
        var recommendations = BuildPreviewRecommendations(lines, snapshotEnv);

        return new LocalStateSnapshotPreviewResult
        {
            ArchivePath = archivePath,
            Lines = lines,
            Recommendations = recommendations
        };
    }

    public async Task<LocalStateSnapshotRestoreResult> RestoreLatestAsync(string backendRootPath)
    {
        if (string.IsNullOrWhiteSpace(backendRootPath))
        {
            throw new ArgumentException("Backend root path is required.", nameof(backendRootPath));
        }

        if (!Directory.Exists(backendRootPath))
        {
            throw new DirectoryNotFoundException($"Backend root does not exist: {backendRootPath}");
        }

        var snapshotDirectory = Path.Combine(backendRootPath, "artifacts", "state-snapshots");

        if (!Directory.Exists(snapshotDirectory))
        {
            throw new FileNotFoundException($"State snapshot folder does not exist: {snapshotDirectory}");
        }

        var latestArchivePath = Directory.GetFiles(snapshotDirectory, "*.zip", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(latestArchivePath))
        {
            throw new FileNotFoundException($"No state snapshots found under: {snapshotDirectory}");
        }

        return await RestoreAsync(backendRootPath, latestArchivePath);
    }

    public async Task<LocalStateSnapshotRestoreResult> RestoreAsync(string backendRootPath, string archivePath)
    {
        if (string.IsNullOrWhiteSpace(backendRootPath))
        {
            throw new ArgumentException("Backend root path is required.", nameof(backendRootPath));
        }

        if (!Directory.Exists(backendRootPath))
        {
            throw new DirectoryNotFoundException($"Backend root does not exist: {backendRootPath}");
        }

        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
        {
            throw new FileNotFoundException($"State snapshot archive does not exist: {archivePath}");
        }

        var restoredEntries = new List<string>();
        var activityStatePath = _activityStateStoragePolicy.ResolveStateFilePath(backendRootPath);

        using (var archive = ZipFile.OpenRead(archivePath))
        {
            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.FullName) || entry.FullName.EndsWith('/'))
                {
                    continue;
                }

                if (string.Equals(entry.FullName, "app/.env", StringComparison.Ordinal))
                {
                    await ExtractEntryAsync(entry, Path.Combine(backendRootPath, ".env"));
                    restoredEntries.Add(entry.FullName);
                    continue;
                }

                if (entry.FullName.StartsWith("app/data/", StringComparison.Ordinal))
                {
                    var relativePath = entry.FullName["app/data/".Length..]
                        .Replace('/', Path.DirectorySeparatorChar);
                    await ExtractEntryAsync(entry, Path.Combine(backendRootPath, "data", relativePath));
                    restoredEntries.Add(entry.FullName);
                    continue;
                }

                if (string.Equals(entry.FullName, "desktop/activity-state.json", StringComparison.Ordinal))
                {
                    await ExtractEntryAsync(entry, activityStatePath);
                    restoredEntries.Add(entry.FullName);
                }
            }
        }

        return new LocalStateSnapshotRestoreResult
        {
            ArchivePath = archivePath,
            RestoredEntries = restoredEntries
        };
    }

    private static void AddDirectoryToArchive(
        ZipArchive archive,
        string sourceDirectoryPath,
        string archiveRoot,
        List<string> includedEntries)
    {
        foreach (var filePath in Directory.GetFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectoryPath, filePath)
                .Replace('\\', '/');
            var archivePath = $"{archiveRoot}/{relativePath}";
            archive.CreateEntryFromFile(filePath, archivePath);
            includedEntries.Add(archivePath);
        }
    }

    private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string destinationPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using var entryStream = entry.Open();
        await using var destinationStream = File.Create(destinationPath);
        await entryStream.CopyToAsync(destinationStream);
    }

    private static async Task<string> BuildFilePreviewLineAsync(
        ZipArchiveEntry? entry,
        string currentPath,
        string label)
    {
        if (entry is null)
        {
            return $"{label}: not included in this snapshot";
        }

        if (!File.Exists(currentPath))
        {
            return $"{label}: current file is missing and will be restored";
        }

        var snapshotBytes = await ReadEntryBytesAsync(entry);
        var currentBytes = await File.ReadAllBytesAsync(currentPath);

        return snapshotBytes.SequenceEqual(currentBytes)
            ? $"{label}: matches current state"
            : $"{label}: differs from current state";
    }

    private static async Task<IReadOnlyList<string>> BuildSessionPreviewLinesAsync(
        ZipArchiveEntry? entry,
        string currentPath)
    {
        if (entry is null)
        {
            return ["sessions.json conversations: not included in this snapshot"];
        }

        var snapshotSummary = await ReadConversationSummaryAsync(await ReadEntryTextAsync(entry));
        var currentSummary = File.Exists(currentPath)
            ? await ReadConversationSummaryAsync(await File.ReadAllTextAsync(currentPath))
            : new ConversationSummary("<missing>", "<missing>", []);

        return
        [
            $"sessions.json conversations: current {currentSummary.CountText} -> snapshot {snapshotSummary.CountText}",
            $"sessions.json latest activity: current {currentSummary.LatestActivityText} -> snapshot {snapshotSummary.LatestActivityText}",
            BuildChangedConversationPreviewLine(currentSummary, snapshotSummary)
        ];
    }

    private static async Task<IReadOnlyList<string>> BuildDirectoryPreviewLinesAsync(
        ZipArchive archive,
        string archivePrefix,
        string currentDirectoryPath,
        string label)
    {
        var snapshotEntries = archive.Entries
            .Where((entry) =>
                !string.IsNullOrWhiteSpace(entry.FullName) &&
                entry.FullName.StartsWith(archivePrefix, StringComparison.Ordinal) &&
                !entry.FullName.EndsWith('/'))
            .ToArray();

        if (snapshotEntries.Length == 0)
        {
            return [$"{label}: not included in this snapshot"];
        }

        if (!Directory.Exists(currentDirectoryPath))
        {
            return
            [
                $"{label}: current folder is missing and will be restored",
                $"data files restored: {FormatDataPreviewList(snapshotEntries.Select((entry) => $"{entry.FullName[archivePrefix.Length..]} (missing)"))}"
            ];
        }

        var currentFiles = Directory.GetFiles(currentDirectoryPath, "*", SearchOption.AllDirectories)
            .ToDictionary(
                (filePath) => Path.GetRelativePath(currentDirectoryPath, filePath).Replace('\\', '/'),
                StringComparer.OrdinalIgnoreCase);
        var snapshotMap = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshotEntries)
        {
            snapshotMap[entry.FullName[archivePrefix.Length..]] = entry;
        }

        var affectedFiles = new List<string>();
        var currentOnlyFiles = currentFiles.Keys
            .Where((key) => !snapshotMap.ContainsKey(key))
            .OrderBy((key) => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var pair in snapshotMap)
        {
            if (!currentFiles.TryGetValue(pair.Key, out var currentFilePath))
            {
                affectedFiles.Add($"{pair.Key} (missing)");
                continue;
            }

            var snapshotBytes = await ReadEntryBytesAsync(pair.Value);
            var currentBytes = await File.ReadAllBytesAsync(currentFilePath);

            if (!snapshotBytes.SequenceEqual(currentBytes))
            {
                affectedFiles.Add($"{pair.Key} (changed)");
            }
        }

        if (affectedFiles.Count == 0 && currentOnlyFiles.Length == 0)
        {
            return [$"{label}: matches current state"];
        }

        return
        [
            $"{label}: differs from current state",
            $"data files restored: {(affectedFiles.Count > 0 ? FormatDataPreviewList(affectedFiles) : "none")}",
            $"data current-only files: {(currentOnlyFiles.Length > 0 ? FormatDataPreviewList(currentOnlyFiles.Select((key) => $"{key} (current-only)")) : "none")}"
        ];
    }

    private static IReadOnlyList<string> BuildTrackedEnvKeyPreviewLines(
        IReadOnlyDictionary<string, string> snapshotEnv,
        string currentEnvPath)
    {
        if (snapshotEnv.Count == 0)
        {
            return [".env tracked keys: not included in this snapshot"];
        }

        var currentEnv = File.Exists(currentEnvPath)
            ? ParseSimpleEnv(File.ReadAllText(currentEnvPath))
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changedKeys = snapshotEnv.Keys
            .Concat(currentEnv.Keys)
            .Where(IsTrackedEnvKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where((key) =>
            {
                currentEnv.TryGetValue(key, out var currentValue);
                snapshotEnv.TryGetValue(key, out var snapshotValue);
                return !string.Equals(currentValue ?? string.Empty, snapshotValue ?? string.Empty, StringComparison.Ordinal);
            })
            .OrderBy((key) => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (changedKeys.Length == 0)
        {
            return [".env tracked keys: no tracked key changes"];
        }

        var lines = new List<string>
        {
            $".env tracked keys changed: {string.Join(", ", changedKeys)}"
        };

        foreach (var key in changedKeys)
        {
            currentEnv.TryGetValue(key, out var currentValue);
            snapshotEnv.TryGetValue(key, out var snapshotValue);
            lines.Add(
                $"{key}: {FormatTrackedEnvValue(key, currentValue, exists: currentEnv.ContainsKey(key))} -> {FormatTrackedEnvValue(key, snapshotValue, exists: snapshotEnv.ContainsKey(key))}");
        }

        return lines;
    }

    private static IReadOnlyList<string> BuildPreviewRecommendations(
        IReadOnlyList<string> previewLines,
        IReadOnlyDictionary<string, string> snapshotEnv)
    {
        var recommendations = new List<string>();
        var hasDiff =
            previewLines.Any((line) => line.Contains("differs from current state", StringComparison.Ordinal)) ||
            previewLines.Any((line) => line.Contains("changed conversations:", StringComparison.Ordinal) && !line.EndsWith("none", StringComparison.Ordinal)) ||
            previewLines.Any((line) => line.Contains("->", StringComparison.Ordinal));

        if (hasDiff)
        {
            recommendations.Add("Recommended: export your current state before restoring so you can roll back if needed.");
        }

        var includesSecrets = snapshotEnv.Any((pair) => IsSensitiveTrackedEnvKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value));

        if (includesSecrets)
        {
            recommendations.Add("Caution: this snapshot includes .env secrets. Avoid sharing the archive outside this device.");
        }

        if (recommendations.Count == 0)
        {
            recommendations.Add("Current state already looks close to this snapshot. Restore only if you intentionally want to roll back.");
        }

        return recommendations;
    }

    private static string BuildUniqueArchivePath(string snapshotDirectory, string suffix)
    {
        var baseName = $"runtime-state-{DateTime.Now:yyyyMMdd-HHmmss}{suffix}";
        var archivePath = Path.Combine(snapshotDirectory, $"{baseName}.zip");
        var duplicateSuffix = 1;

        while (File.Exists(archivePath))
        {
            archivePath = Path.Combine(snapshotDirectory, $"{baseName}-{duplicateSuffix}.zip");
            duplicateSuffix += 1;
        }

        return archivePath;
    }

    private static async Task<LocalStateSnapshotDescriptor> BuildDescriptorAsync(string archivePath)
    {
        var fileName = Path.GetFileName(archivePath);
        var sizeBytes = new FileInfo(archivePath).Length;
        var createdAtText = File.GetLastWriteTime(archivePath).ToString("yyyy-MM-dd HH:mm:ss");
        var includesSecrets = false;
        var includedEntries = new List<string>();

        using var archive = ZipFile.OpenRead(archivePath);
        var manifestEntry = archive.GetEntry("manifest.json");

        if (manifestEntry is not null)
        {
            await using var stream = manifestEntry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var manifestJson = await reader.ReadToEndAsync();
            using var document = JsonDocument.Parse(manifestJson);
            var root = document.RootElement;

            if (root.TryGetProperty("createdAt", out var createdAtProperty) &&
                createdAtProperty.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(createdAtProperty.GetString(), out var parsedCreatedAt))
            {
                createdAtText = parsedCreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            }

            if (root.TryGetProperty("includesSecrets", out var includesSecretsProperty) &&
                (includesSecretsProperty.ValueKind == JsonValueKind.True ||
                 includesSecretsProperty.ValueKind == JsonValueKind.False))
            {
                includesSecrets = includesSecretsProperty.GetBoolean();
            }

            if (root.TryGetProperty("includedEntries", out var includedEntriesProperty) &&
                includedEntriesProperty.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in includedEntriesProperty.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(item.GetString()))
                    {
                        includedEntries.Add(item.GetString()!);
                    }
                }
            }
        }

        if (includedEntries.Count == 0)
        {
            includedEntries.AddRange(
                archive.Entries
                    .Where(static entry => !string.IsNullOrWhiteSpace(entry.FullName) && !entry.FullName.EndsWith('/'))
                    .Select(static entry => entry.FullName));
        }

        var sizeText = FormatByteSize(sizeBytes);
        var summary = $"{createdAtText} | {sizeText} | {includedEntries.Count} entr{(includedEntries.Count == 1 ? "y" : "ies")} | {(includesSecrets ? "includes secrets" : "no secrets flag")}";
        var detail = string.Join(Environment.NewLine, includedEntries.Take(5));

        return new LocalStateSnapshotDescriptor
        {
            ArchivePath = archivePath,
            FileName = fileName,
            SizeBytes = sizeBytes,
            SizeText = sizeText,
            CreatedAtText = createdAtText,
            Summary = summary,
            Detail = detail,
            IncludesSecrets = includesSecrets,
            IncludedEntries = includedEntries
        };
    }

    private static string FormatByteSize(long sizeBytes)
    {
        const double kilobyte = 1024d;
        const double megabyte = kilobyte * 1024d;

        if (sizeBytes >= megabyte)
        {
            return $"{sizeBytes / megabyte:0.0} MB";
        }

        if (sizeBytes >= kilobyte)
        {
            return $"{sizeBytes / kilobyte:0.0} KB";
        }

        return $"{sizeBytes} B";
    }

    private static async Task<byte[]> ReadEntryBytesAsync(ZipArchiveEntry entry)
    {
        await using var entryStream = entry.Open();
        using var memoryStream = new MemoryStream();
        await entryStream.CopyToAsync(memoryStream);
        return memoryStream.ToArray();
    }

    private static async Task<string> ReadEntryTextAsync(ZipArchiveEntry entry)
    {
        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static Dictionary<string, string> ParseSimpleEnv(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');

            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..];

            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static bool IsTrackedEnvKey(string key)
    {
        return key.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("NAPCAT_", StringComparison.OrdinalIgnoreCase) ||
               key.StartsWith("WECHAT_", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "QQ_AI_BOT_CONTROL_API_TOKEN", StringComparison.OrdinalIgnoreCase);
    }

    private static Task<ConversationSummary> ReadConversationSummaryAsync(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Task.FromResult(new ConversationSummary("<unknown>", "<unknown>", []));
            }

            var properties = document.RootElement.EnumerateObject().ToArray();
            var countText = properties.Length.ToString();
            var entries = properties
                .Select((property) =>
                {
                    DateTimeOffset? parsedUpdatedAt = null;

                    if (property.Value.ValueKind == JsonValueKind.Object &&
                        property.Value.TryGetProperty("updatedAt", out var updatedAtProperty) &&
                        updatedAtProperty.ValueKind == JsonValueKind.String &&
                        DateTimeOffset.TryParse(updatedAtProperty.GetString(), out var candidateUpdatedAt))
                    {
                        parsedUpdatedAt = candidateUpdatedAt.ToUniversalTime();
                    }

                    return new ConversationEntrySummary(
                        property.Name,
                        property.Value.GetRawText(),
                        parsedUpdatedAt);
                })
                .ToArray();
            var latestActivity = properties
                .Select((property) =>
                {
                    if (property.Value.ValueKind != JsonValueKind.Object ||
                        !property.Value.TryGetProperty("updatedAt", out var updatedAtProperty) ||
                        updatedAtProperty.ValueKind != JsonValueKind.String ||
                        !DateTimeOffset.TryParse(updatedAtProperty.GetString(), out var parsedUpdatedAt))
                    {
                        return (DateTimeOffset?)null;
                    }

                    return parsedUpdatedAt.ToUniversalTime();
                })
                .Where((value) => value is not null)
                .Max();

            var latestActivityText = latestActivity is DateTimeOffset parsedLatestActivity
                ? parsedLatestActivity.ToString("yyyy-MM-dd HH:mm:ss 'UTC'")
                : properties.Length == 0
                    ? "<none>"
                    : "<unknown>";

            return Task.FromResult(new ConversationSummary(countText, latestActivityText, entries));
        }
        catch
        {
            return Task.FromResult(new ConversationSummary("<unknown>", "<unknown>", []));
        }
    }

    private static string FormatTrackedEnvValue(string key, string? value, bool exists)
    {
        if (!exists)
        {
            return "<missing>";
        }

        var normalizedValue = value ?? string.Empty;

        if (normalizedValue.Length == 0)
        {
            return "<empty>";
        }

        if (IsSensitiveTrackedEnvKey(key))
        {
            return MaskSensitiveValue(normalizedValue);
        }

        return normalizedValue.Length > 72
            ? $"{normalizedValue[..69]}..."
            : normalizedValue;
    }

    private static bool IsSensitiveTrackedEnvKey(string key)
    {
        return key.EndsWith("_API_KEY", StringComparison.OrdinalIgnoreCase) ||
               key.EndsWith("_TOKEN", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, "QQ_AI_BOT_CONTROL_API_TOKEN", StringComparison.OrdinalIgnoreCase);
    }

    private static string MaskSensitiveValue(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "<empty>";
        }

        if (value.Length <= 4)
        {
            return new string('*', value.Length);
        }

        return $"{new string('*', Math.Min(8, value.Length - 4))}{value[^4..]}";
    }

    private static string BuildChangedConversationPreviewLine(
        ConversationSummary currentSummary,
        ConversationSummary snapshotSummary)
    {
        var currentEntries = currentSummary.Entries.ToDictionary((entry) => entry.Key, StringComparer.Ordinal);
        var snapshotEntries = snapshotSummary.Entries.ToDictionary((entry) => entry.Key, StringComparer.Ordinal);
        var changedEntries = currentEntries.Keys
            .Concat(snapshotEntries.Keys)
            .Distinct(StringComparer.Ordinal)
            .Select((key) =>
            {
                currentEntries.TryGetValue(key, out var currentEntry);
                snapshotEntries.TryGetValue(key, out var snapshotEntry);

                if (currentEntry is null && snapshotEntry is not null)
                {
                    return new ChangedConversationSummary(
                        key,
                        "missing locally",
                        snapshotEntry.UpdatedAt);
                }

                if (currentEntry is not null && snapshotEntry is null)
                {
                    return new ChangedConversationSummary(
                        key,
                        "current-only",
                        currentEntry.UpdatedAt);
                }

                if (currentEntry is not null &&
                    snapshotEntry is not null &&
                    !string.Equals(currentEntry.RawText, snapshotEntry.RawText, StringComparison.Ordinal))
                {
                    return new ChangedConversationSummary(
                        key,
                        "changed",
                        MaxTimestamp(currentEntry.UpdatedAt, snapshotEntry.UpdatedAt));
                }

                return null;
            })
            .Where((entry) => entry is not null)
            .OrderByDescending((entry) => entry!.UpdatedAt)
            .ThenBy((entry) => entry!.Key, StringComparer.Ordinal)
            .ToArray();

        if (changedEntries.Length == 0)
        {
            return "sessions.json changed conversations: none";
        }

        var previewItems = changedEntries
            .Take(3)
            .Select((entry) => $"{FormatConversationPreviewKey(entry!.Key)} ({entry.Status})");
        var previewText = string.Join(", ", previewItems);
        var remainingCount = changedEntries.Length - Math.Min(3, changedEntries.Length);

        if (remainingCount > 0)
        {
            previewText = $"{previewText}, +{remainingCount} more";
        }

        return $"sessions.json changed conversations: {previewText}";
    }

    private static string FormatConversationPreviewKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "<unknown>";
        }

        var parts = key.Split('|', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 3)
        {
            return key;
        }

        var channel = DecodeConversationKeyPart(parts[0], "channel=");
        var chat = DecodeConversationKeyPart(parts[1], "chat=");
        var user = DecodeConversationKeyPart(parts[2], "user=");

        return string.IsNullOrWhiteSpace(channel) || string.IsNullOrWhiteSpace(chat) || string.IsNullOrWhiteSpace(user)
            ? key
            : $"{channel}:{chat}/{user}";
    }

    private static string DecodeConversationKeyPart(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        var encoded = value[prefix.Length..];

        try
        {
            return Uri.UnescapeDataString(encoded);
        }
        catch
        {
            return encoded;
        }
    }

    private static DateTimeOffset? MaxTimestamp(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left.Value >= right.Value ? left : right;
    }

    private sealed record ConversationEntrySummary(string Key, string RawText, DateTimeOffset? UpdatedAt);

    private sealed record ConversationSummary(
        string CountText,
        string LatestActivityText,
        IReadOnlyList<ConversationEntrySummary> Entries);

    private sealed record ChangedConversationSummary(
        string Key,
        string Status,
        DateTimeOffset? UpdatedAt);

    private static string FormatDataPreviewList(IEnumerable<string> items)
    {
        var previewItems = items.Take(5).ToArray();
        var previewList = string.Join(", ", previewItems);
        var remainingCount = items.Count() - previewItems.Length;

        if (remainingCount > 0)
        {
            previewList = $"{previewList}, +{remainingCount} more";
        }

        return previewList;
    }
}
