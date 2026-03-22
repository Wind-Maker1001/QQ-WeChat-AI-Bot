using System.IO;
using System.Diagnostics;
using System.Text;

namespace QQAIBot.Desktop.Services;

public sealed class BotProcessService : IDisposable
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
            throw new DirectoryNotFoundException($"后端目录不存在: {workingDirectory}");
        }

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "./src/index.mjs",
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;
        process.Exited += OnProcessExited;

        if (!process.Start())
        {
            throw new InvalidOperationException("启动 Node 后端失败。");
        }

        _process = process;
        RaiseLog($"已启动后端进程，PID={process.Id}，命令=node ./src/index.mjs");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
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
            RaiseLog($"正在停止后端进程，PID={process.Id}");
            process.Kill(true);
            await process.WaitForExitAsync();
        }

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

    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            RaiseLog(e.Data);
        }
    }

    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(e.Data))
        {
            RaiseLog($"[stderr] {e.Data}");
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_process is not null)
        {
            RaiseLog($"后端进程已退出，ExitCode={_process.ExitCode}");
        }

        ProcessExited?.Invoke(this, EventArgs.Empty);
    }

    private void RaiseLog(string message)
    {
        LogReceived?.Invoke(this, message);
    }

    private void DetachProcess(Process process)
    {
        process.OutputDataReceived -= OnOutputDataReceived;
        process.ErrorDataReceived -= OnErrorDataReceived;
        process.Exited -= OnProcessExited;
    }
}
