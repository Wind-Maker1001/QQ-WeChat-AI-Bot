using Microsoft.Win32;

namespace QQAIBot.Desktop.Services;

public sealed class WindowsAutoStartRegistryStore : IAutoStartRegistryStore
{
    private readonly string _runKeyPath;

    public WindowsAutoStartRegistryStore(string runKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run")
    {
        _runKeyPath = runKeyPath;
    }

    public string? GetValue(string valueName)
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
        return runKey?.GetValue(valueName) as string;
    }

    public void SetValue(string valueName, string value)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to open HKCU Run registry key.");

        runKey.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string valueName)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to open HKCU Run registry key.");

        runKey.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
