using System.Diagnostics;
using System.IO;

namespace QQAIBot.Desktop.Services;

public sealed class BotProcessService : IBotProcessService
{
    private Process? _process;

    public event EventHandler<string>? LogReceived;

    public event EventHandler? ProcessExited;

    public bool IsRunning => _process is { HasExited: false };

    public void Start(string workingDirectory)
    {
        if (IsRunning)
        {
            return;
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"Backend directory does not exist: {workingDirectory}");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "./src/index.mjs",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            },
            EnableRaisingEvents = true
        };

        process.Exited += OnProcessExited;

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start backend Node process.");
        }

        _process = process;
        RaiseLog($"Started backend process, PID={process.Id}, command=node ./src/index.mjs");
    }

    public async Task StopAsync()
    {
        if (_process is null)
        {
            return;
        }

        var process = _process;

        if (!process.HasExited)
        {
            RaiseLog($"Stopping backend process, PID={process.Id}");
            process.Kill(true);
            await process.WaitForExitAsync();
        }

        DetachProcess(process);
        process.Dispose();
        _process = null;
    }

    public void Detach()
    {
        if (_process is null)
        {
            return;
        }

        var process = _process;
        RaiseLog($"Detached desktop ownership from backend process, PID={process.Id}");
        DetachProcess(process);
        process.Dispose();
        _process = null;
    }

    public void Dispose()
    {
        if (_process is null)
        {
            return;
        }

        DetachProcess(_process);
        _process.Dispose();
        _process = null;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_process is not null)
        {
            RaiseLog($"Backend process exited, ExitCode={_process.ExitCode}");
        }

        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseLog(string message)
    {
        LogReceived?.Invoke(this, message);
    }

    private void DetachProcess(Process process)
    {
        process.Exited -= OnProcessExited;
    }
}
