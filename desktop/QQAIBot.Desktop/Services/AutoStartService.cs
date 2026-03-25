using System.IO;
using System.Reflection;

namespace QQAIBot.Desktop.Services;

public sealed class AutoStartService : IAutoStartService
{
    private const string AppValueName = "QQAIBot.Desktop";
    private const string AutoStartArgument = "--minimized";
    private const string EnsureRuntimeArgument = "--ensure-runtime";
    private readonly IAutoStartRegistryStore _registryStore;
    private readonly Func<string> _resolveExecutablePath;

    public AutoStartService(
        IAutoStartRegistryStore? registryStore = null,
        Func<string>? resolveExecutablePath = null)
    {
        _registryStore = registryStore ?? new WindowsAutoStartRegistryStore();
        _resolveExecutablePath = resolveExecutablePath ?? (() => ResolveAutoStartExecutablePath());
    }

    public bool IsEnabled()
    {
        var value = _registryStore.GetValue(AppValueName);
        return !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            _registryStore.DeleteValue(AppValueName);
            return;
        }

        var executablePath = _resolveExecutablePath();

        _registryStore.SetValue(
            AppValueName,
            BuildAutoStartCommand(executablePath));
    }

    public static string BuildAutoStartCommand(string executablePath)
    {
        return $"\"{executablePath}\" {AutoStartArgument} {EnsureRuntimeArgument}";
    }

    public static string ResolveAutoStartExecutablePath(
        string? processPath = null,
        string? appBaseDirectory = null,
        string? entryAssemblyName = null,
        Func<string, bool>? fileExists = null)
    {
        processPath ??= Environment.ProcessPath;
        appBaseDirectory ??= AppContext.BaseDirectory;
        entryAssemblyName ??= Assembly.GetEntryAssembly()?.GetName().Name;
        fileExists ??= File.Exists;

        if (!string.IsNullOrWhiteSpace(processPath) &&
            !string.Equals(Path.GetFileName(processPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        if (!string.IsNullOrWhiteSpace(entryAssemblyName))
        {
            var candidatePath = Path.Combine(appBaseDirectory, $"{entryAssemblyName}.exe");

            if (fileExists(candidatePath))
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
