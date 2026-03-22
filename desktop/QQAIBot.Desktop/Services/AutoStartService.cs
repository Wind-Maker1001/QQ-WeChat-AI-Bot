using System.IO;
using System.Reflection;

using Microsoft.Win32;

namespace QQAIBot.Desktop.Services;

public sealed class AutoStartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "QQAIBot.Desktop";
    private const string AutoStartArgument = "--minimized";
    private const string EnsureRuntimeArgument = "--ensure-runtime";

    public bool IsEnabled()
    {
        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = runKey?.GetValue(AppValueName) as string;
        return !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Failed to open HKCU Run registry key.");

        if (!enabled)
        {
            runKey.DeleteValue(AppValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = ResolveAutoStartExecutablePath();

        runKey.SetValue(
            AppValueName,
            $"\"{executablePath}\" {AutoStartArgument} {EnsureRuntimeArgument}",
            RegistryValueKind.String
        );
    }

    private static string ResolveAutoStartExecutablePath()
    {
        var processPath = Environment.ProcessPath;

        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        if (!string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            var candidatePath = Path.Combine(AppContext.BaseDirectory, $"{entryAssemblyName}.exe");

            if (File.Exists(candidatePath))
            {
                return candidatePath;
            }
        }

        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath;
        }

        throw new InvalidOperationException("Cannot resolve current executable path.");
    }
}
