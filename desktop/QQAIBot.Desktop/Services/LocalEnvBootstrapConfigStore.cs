using System.IO;
using System.Text;

namespace QQAIBot.Desktop.Services;

public sealed class LocalEnvBootstrapConfigStore : ILocalBootstrapConfigStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task SaveExtraValueAsync(string rootPath, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path is required.", nameof(rootPath));
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Env key is required.", nameof(key));
        }

        Directory.CreateDirectory(rootPath);
        var envPath = Path.Combine(rootPath, ".env");
        var normalizedValue = value?.Trim() ?? string.Empty;
        var lines = File.Exists(envPath)
            ? await File.ReadAllLinesAsync(envPath, Utf8NoBom)
            : [];
        var updatedLines = new List<string>();
        var replaced = false;

        foreach (var originalLine in lines)
        {
            var trimmedLine = originalLine.Trim();

            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
            {
                updatedLines.Add(originalLine);
                continue;
            }

            var separatorIndex = originalLine.IndexOf('=');

            if (separatorIndex <= 0)
            {
                updatedLines.Add(originalLine);
                continue;
            }

            var existingKey = originalLine[..separatorIndex].Trim();

            if (!string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                updatedLines.Add(originalLine);
                continue;
            }

            replaced = true;

            if (!string.IsNullOrWhiteSpace(normalizedValue))
            {
                updatedLines.Add($"{key}={normalizedValue}");
            }
        }

        if (!replaced && !string.IsNullOrWhiteSpace(normalizedValue))
        {
            updatedLines.Add($"{key}={normalizedValue}");
        }

        await File.WriteAllLinesAsync(envPath, updatedLines, Utf8NoBom);
    }
}
