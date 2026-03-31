using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopSnapshotWorkflow
{
    public static DesktopShellSourceState ApplySnapshotExportResult(
        DesktopShellSourceState sourceState,
        string archivePath)
    {
        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                LastStateSnapshotText = archivePath
            }
        };
    }

    public static DesktopShellSourceState ApplySnapshotList(
        DesktopShellSourceState sourceState,
        IReadOnlyList<LocalStateSnapshotDescriptor> snapshots,
        LocalStateSnapshotDescriptor? selectedSnapshot)
    {
        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                StateSnapshots = snapshots,
                SelectedStateSnapshot = selectedSnapshot,
                SelectedStateSnapshotPreview = null
            }
        };
    }

    public static DesktopShellSourceState ClearSnapshotSelection(
        DesktopShellSourceState sourceState)
    {
        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                StateSnapshots = [],
                SelectedStateSnapshot = null,
                SelectedStateSnapshotPreview = null
            }
        };
    }

    public static DesktopShellSourceState UpdateSelectedSnapshot(
        DesktopShellSourceState sourceState,
        LocalStateSnapshotDescriptor? snapshot)
    {
        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                SelectedStateSnapshot = snapshot,
                SelectedStateSnapshotPreview = null
            }
        };
    }

    public static DesktopShellSourceState ClearSelectedSnapshotPreview(
        DesktopShellSourceState sourceState)
    {
        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                SelectedStateSnapshotPreview = null
            }
        };
    }

    public static DesktopShellSourceState ApplySelectedSnapshotPreview(
        DesktopShellSourceState sourceState,
        LocalStateSnapshotPreviewResult preview)
    {
        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                SelectedStateSnapshotPreview = preview
            }
        };
    }

    public static DesktopShellSourceState ApplyRestoreResult(
        DesktopShellSourceState sourceState,
        string archivePath,
        LocalStateSnapshotRestoreResult restoreResult,
        LocalStateSnapshotPreviewResult? restorePreview)
    {
        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                LastStateRestoreText = archivePath,
                LastStateRestoreResult = restoreResult,
                LastStateRestorePreview = restorePreview
            }
        };
    }

    public static DesktopShellSourceState ClearRestoreResultIfMatchesArchive(
        DesktopShellSourceState sourceState,
        string archivePath)
    {
        if (!string.Equals(sourceState.SnapshotSourceState.LastStateRestoreText, archivePath, StringComparison.OrdinalIgnoreCase))
        {
            return sourceState;
        }

        return sourceState with
        {
            SnapshotSourceState = sourceState.SnapshotSourceState with
            {
                LastStateRestoreText = "尚未恢复状态快照",
                LastStateRestoreResult = null,
                LastStateRestorePreview = null
            }
        };
    }

    public static DesktopConfirmationPrompt BuildRestoreConfirmation(
        LocalStateSnapshotDescriptor snapshot,
        LocalStateSnapshotPreviewResult preview,
        string title)
    {
        var presentation = LocalStateSnapshotPresentationBuilder.BuildSelectionPresentation(snapshot, preview);

        return new DesktopConfirmationPrompt
        {
            Title = title,
            Message = string.Join(
                Environment.NewLine,
                [
                    $"恢复快照：{snapshot.FileName}",
                    snapshot.Summary,
                    "会被覆盖的内容：",
                    presentation.ImpactText,
                    string.Empty,
                    "恢复前建议：",
                    presentation.SafetyHeadlineText,
                    presentation.SafetyRecommendationText,
                    presentation.RollbackHintText,
                    string.Empty,
                    "当前状态 vs 快照",
                    presentation.DiffText,
                    presentation.AdviceText,
                    "是否继续？"
                ]),
            ArchivePath = snapshot.ArchivePath
        };
    }

    public static DesktopConfirmationPrompt BuildDeleteConfirmation(LocalStateSnapshotDescriptor snapshot)
    {
        return new DesktopConfirmationPrompt
        {
            Title = "删除选中快照",
            Message = string.Join(
                Environment.NewLine,
                [
                    $"删除快照：{snapshot.FileName}",
                    snapshot.Summary,
                    "这会从本地快照目录中永久移除当前选中的归档。",
                    "是否继续？"
                ]),
            ArchivePath = snapshot.ArchivePath
        };
    }
}
