using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public interface ILocalStateSnapshotService
{
    Task<LocalStateSnapshotResult> ExportAsync(string backendRootPath);

    Task<LocalStateSnapshotResult> ExportSafeAsync(string backendRootPath);

    Task<IReadOnlyList<LocalStateSnapshotDescriptor>> ListAsync(string backendRootPath);

    Task DeleteAsync(string archivePath);

    Task<LocalStateSnapshotPreviewResult> PreviewAsync(string backendRootPath, string archivePath);

    Task<LocalStateSnapshotRestoreResult> RestoreAsync(string backendRootPath, string archivePath);

    Task<LocalStateSnapshotRestoreResult> RestoreLatestAsync(string backendRootPath);
}
