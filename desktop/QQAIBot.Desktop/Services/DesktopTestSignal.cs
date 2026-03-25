using System.IO;
using System.Text;

namespace QQAIBot.Desktop.Services;

public static class DesktopTestSignal
{
    public const string SignalFileEnvKey = "QQ_AI_BOT_DESKTOP_TEST_SIGNAL_FILE";

    public static void Emit(string eventName)
    {
        var signalFilePath = Environment.GetEnvironmentVariable(SignalFileEnvKey);

        if (string.IsNullOrWhiteSpace(signalFilePath) || string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        try
        {
            var directoryPath = Path.GetDirectoryName(signalFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.AppendAllText(
                signalFilePath,
                $"{DateTime.UtcNow:O} {eventName}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Test hook only; ignore failures.
        }
    }
}
