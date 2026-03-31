using System.IO;
using System.Text;

using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;

internal static class DesktopShellProjectorTests
{
    public static async Task TestProjectsLocalDocumentStateAsync()
    {
        var backendRootPath = Path.Combine(Path.GetTempPath(), $"desktop-projector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(backendRootPath, "src"));
        Directory.CreateDirectory(Path.Combine(backendRootPath, "data", "image-cache", "nested"));
        await File.WriteAllTextAsync(Path.Combine(backendRootPath, "package.json"), "{}", Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(backendRootPath, "src", "index.mjs"), "console.log('ok');", Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(backendRootPath, "data", "sessions.json"), "{}", Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(backendRootPath, "data", "image-cache", "one.txt"), "one", Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(backendRootPath, "data", "image-cache", "nested", "two.txt"), "two", Encoding.UTF8);

        var sourceState = new DesktopShellSourceState
        {
            ConfigEditorState = new DesktopConfigEditorState
            {
                ControlApiToken = "local-token",
                Config = new BotConfig()
            },
            LocalDocumentSourceState = new DesktopLocalDocumentSourceState
            {
                BackendRootPath = backendRootPath,
                BackendRootDetected = true,
                ConfigDocument = new DesktopConfigDocumentState
                {
                    Document = new EnvDocument
                    {
                        ExtraValues =
                        {
                            ["QQ_AI_BOT_CONTROL_API_HOST"] = "127.0.0.9",
                            ["QQ_AI_BOT_CONTROL_API_PORT"] = "3201",
                            ["QQ_AI_BOT_CONTROL_API_TOKEN"] = "local-token"
                        }
                    }
                }
            }
        };

        var projectedState = DesktopShellProjector.Project(
            sourceState,
            new DesktopShellProjectionContext
            {
                ControlApiFailure = new BackendControlApiFailure(),
                ActivityStateStoragePolicy = new DesktopActivityStateStoragePolicy(Path.Combine(Path.GetTempPath(), $"desktop-projector-activity-{Guid.NewGuid():N}"))
            });

        AssertTrue(projectedState.LocalDocumentState.IsBackendRootValid, "Projector should identify valid backend roots.");
        AssertEqual("已自动检测到 backend 根目录", projectedState.LocalDocumentState.BackendRootStateText, "Projector should derive backend-root status text.");
        AssertEqual("会话历史文件已存在", projectedState.LocalDocumentState.SessionStoreStateText, "Projector should derive session-store status text.");
        AssertEqual("2 个缓存图片文件", projectedState.LocalDocumentState.ImageCacheStateText, "Projector should count nested image-cache entries.");
        AssertEqual("http://127.0.0.9:3201", projectedState.LocalDocumentState.ControlApiEndpointText, "Projector should build the local control API endpoint from local extras.");
        AssertEqual("本机令牌已配置", projectedState.LocalDocumentState.ControlApiTokenStateText, "Projector should surface configured local-token state.");
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} Expected={expected} Actual={actual}");
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
