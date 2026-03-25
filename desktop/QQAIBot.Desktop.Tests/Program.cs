using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.IO;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

using QQAIBot.Desktop.Infrastructure;
using QQAIBot.Desktop.Models;
using QQAIBot.Desktop.Services;
using QQAIBot.Desktop.ViewModels;
using QQAIBot.Desktop;

var testFailures = new List<string>();
Thread? uiThread = null;
Dispatcher? uiDispatcher = null;
QQAIBot.Desktop.App? uiApp = null;
TaskCompletionSource uiDispatcherReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
TaskCompletionSource uiThreadStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);

await RunTestAsync("LocalEnvConfigFallbackReader reads ALLOWED_GROUP_IDS without mutating env files", TestEnvConfigSnapshotStoreReadsLegacyAllowedGroupIdsWithoutMutationAsync);
await RunTestAsync("LocalEnvConfigFallbackReader load does not create env files when missing", TestEnvConfigSnapshotStoreLoadDoesNotCreateMissingEnvAsync);
await RunTestAsync("TestEnvConfigSnapshotWriter saves ALLOWED_CHAT_IDS only", TestEnvConfigSnapshotStoreSavesAllowedChatIdsOnlyAsync);
await RunTestAsync("TestEnvConfigSnapshotWriter + fallback reader round-trip OpenAI reasoning, verbosity, and tool flags", TestEnvConfigSnapshotStoreRoundTripsOpenAiRouteControlsAsync);
await RunTestAsync("PathDiscoveryService identifies backend root", TestPathDiscoveryServiceBackendRootAsync);
await RunTestAsync("DesktopActivityStatePolicy normalizes selection and retention semantics", TestDesktopActivityStatePolicySemanticsAsync);
await RunTestAsync("LocalActivityStateStore round-trips and normalizes persisted activity state", TestLocalActivityStateStoreRoundTripAsync);
await RunTestAsync("LocalActivityStateStore prunes activity older than the retention window", TestLocalActivityStateStoreRetentionAsync);
await RunTestAsync("LocalActivityStateStore drops incompatible versions and deletes default state files", TestLocalActivityStateStoreVersionCleanupAsync);
await RunTestAsync("BackendControlApiService uses camelCase control API contract", TestBackendControlApiServiceCamelCaseContractAsync);
await RunTestAsync("BackendControlApiService classifies 401 responses as unauthorized", TestBackendControlApiServiceUnauthorizedAsync);
await RunTestAsync("BackendControlApiService exposes rejected config errors separately from transport failures", TestBackendControlApiServiceRejectedSaveAsync);
await RunTestAsync("BackendControlApiService treats empty successful config responses as unknown failures", TestBackendControlApiServiceEmptyConfigResponseAsync);
await RunTestAsync("BackendControlApiService treats empty successful save responses as unknown failures", TestBackendControlApiServiceEmptySaveResponseAsync);
await RunTestAsync("BackendControlApiService treats empty successful status/start/stop responses as unknown failures", TestBackendControlApiServiceEmptyRuntimeResponsesAsync);
await RunTestAsync("BotProcessService starts backend process, emits logs, and stops cleanly", TestBotProcessServiceStartStopAsync);
await RunTestAsync("BotProcessService detaches without stopping backend process", TestBotProcessServiceDetachKeepsBackendAliveAsync);
await RunTestAsync("BotProcessService throws for missing backend directory", TestBotProcessServiceMissingDirectoryAsync);
await RunTestAsync("AsyncRelayCommand blocks re-entry while running", TestAsyncRelayCommandReentryAsync);
await RunTestAsync("RelayCommand respects can-execute predicate", TestRelayCommandCanExecuteAsync);
await RunTestAsync("AutoStartService builds launch command and resolves executable path", TestAutoStartServiceCommandResolutionAsync);
await RunTestAsync("AutoStartService enables and disables startup through registry store abstraction", TestAutoStartServiceRegistryStoreAsync);
await RunTestAsync("AutoStartService writes and removes startup value through Windows registry store on custom key path", TestAutoStartServiceWindowsRegistryStoreAsync);
await RunTestAsync("SingleInstanceCoordinator signals activate event to primary instance", TestSingleInstanceCoordinatorActivateAsync);
await RunTestAsync("SingleInstanceCoordinator signals ensure-runtime event to primary instance", TestSingleInstanceCoordinatorEnsureRuntimeAsync);
await RunTestAsync("Desktop app secondary process restores primary instance and ensure-runtime process signals runtime", TestDesktopCrossProcessSingleInstanceActivationAsync);
await RunTestAsync("MainWindow smoke automation binds controls and routes save/start/stop commands", TestMainWindowSmokeAutomationAsync);
await RunTestAsync("MainViewModel dispose does not stop backend launcher ownership after attach", TestMainViewModelDisposeDoesNotStopBackendProcessAsync);
await RunTestAsync("MainViewModel auto-recovers control API before showing outage warning", TestMainViewModelAutoRecoversControlApiBeforeWarningAsync);
await RunTestAsync("MainViewModel rejects unknown control API config failures before file fallback", TestMainViewModelRejectsUnknownConfigFailureBeforeFallbackAsync);
await RunTestAsync("MainViewModel rejects unauthorized control API config failures before file fallback", TestMainViewModelRejectsUnauthorizedConfigFailureBeforeFallbackAsync);
await RunTestAsync("MainViewModel loads through recovered control API before file fallback", TestMainViewModelLoadsThroughRecoveredControlApiAsync);
await RunTestAsync("MainViewModel saves through recovered control API instead of env fallback", TestMainViewModelSavesThroughRecoveredControlApiAsync);
await RunTestAsync("MainViewModel surfaces rejected control API saves without env fallback or recovery", TestMainViewModelSurfacesRejectedControlApiSaveAsync);
await RunTestAsync("MainViewModel restores local activity state for recent events and pin/filter preferences", TestMainViewModelRestoresLocalActivityStateAsync);
await RunTestAsync("MainWindow auto-starts backend when control API is unreachable on load", TestMainWindowAutoStartsBackendWhenControlApiIsUnavailableAsync);
await RunTestAsync("MainWindow external activation restores minimized window and triggers ensure-runtime", TestMainWindowExternalActivationAsync);
await RunTestAsync("MainWindow hides to tray when minimized and shows tray balloon", TestMainWindowTrayMinimizeBehaviorAsync);
await RunTestAsync("MainWindow close hides to tray and shows balloon tip when tray is enabled", TestMainWindowTrayCloseBehaviorAsync);
await RunTestAsync("MainWindow tray menu exposes expected actions and routes open/start/stop", TestMainWindowTrayMenuActionsAsync);

if (testFailures.Count > 0)
{
    await ShutdownUiThreadAsync();
    Console.Error.WriteLine();
    Console.Error.WriteLine($"Desktop regression failed: {testFailures.Count} test(s).");

    foreach (var failure in testFailures)
    {
        Console.Error.WriteLine(failure);
    }

    Environment.Exit(1);
}

await ShutdownUiThreadAsync();
Console.WriteLine();
Console.WriteLine("Desktop regression passed.");

return;

async Task RunTestAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        Console.WriteLine($"[PASS] {name}");
    }
    catch (Exception ex)
    {
        testFailures.Add($"[FAIL] {name}: {ex}");
    }
}

async Task TestEnvConfigSnapshotStoreReadsLegacyAllowedGroupIdsWithoutMutationAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-legacy-");
    var envPath = Path.Combine(rootPath, ".env");
    var originalText =
        string.Join(
            Environment.NewLine,
            [
                "OPENAI_API_KEY=test-key",
                "ALLOWED_GROUP_IDS=chat-a,chat-b",
                "ALLOWED_USER_IDS=user-a"
            ]) + Environment.NewLine;
    await File.WriteAllTextAsync(
        envPath,
        originalText,
        Encoding.UTF8);

    var service = new LocalEnvConfigFallbackReader();
    var document = await service.LoadAsync(rootPath);
    var loadedText = await File.ReadAllTextAsync(envPath, Encoding.UTF8);

    AssertEqual("chat-a,chat-b", document.Config.AllowedChatIds, "AllowedChatIds should load from legacy key.");
    AssertEqual(originalText, loadedText, "LoadAsync should not mutate the env file when reading legacy keys.");
}

async Task TestEnvConfigSnapshotStoreLoadDoesNotCreateMissingEnvAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-missing-");
    var envPath = Path.Combine(rootPath, ".env");
    var service = new LocalEnvConfigFallbackReader();

    var document = await service.LoadAsync(rootPath);

    AssertEqual(string.Empty, document.Config.OpenAiApiKey, "Missing env load should return an empty document.");
    AssertFalse(File.Exists(envPath), "LoadAsync should not create .env when the file is missing.");
}

async Task TestEnvConfigSnapshotStoreSavesAllowedChatIdsOnlyAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-save-");
    var service = new TestEnvConfigSnapshotWriter();
    var document = new EnvDocument
    {
        Config = new BotConfig
        {
            OpenAiApiKey = "test-key",
            AllowedChatIds = "chat-x,chat-y",
            AllowedUserIds = "user-x"
        }
    };

    await service.SaveAsync(rootPath, document);

    var envText = await File.ReadAllTextAsync(Path.Combine(rootPath, ".env"), Encoding.UTF8);
    AssertContains(envText, "ALLOWED_CHAT_IDS=chat-x,chat-y", "Saved env should contain ALLOWED_CHAT_IDS.");
    AssertDoesNotContain(envText, "ALLOWED_GROUP_IDS=", "Saved env should not contain legacy key.");
}

async Task TestEnvConfigSnapshotStoreRoundTripsOpenAiRouteControlsAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-env-openai-controls-");
    var writer = new TestEnvConfigSnapshotWriter();
    var reader = new LocalEnvConfigFallbackReader();
    var document = new EnvDocument
    {
        Config = new BotConfig
        {
            OpenAiApiKey = "advanced-key",
            OpenAiDefaultApiKey = "default-key",
            OpenAiDefaultReasoningEffort = "medium",
            OpenAiAdvancedReasoningEffort = "high",
            OpenAiDefaultTextVerbosity = "low",
            OpenAiAdvancedTextVerbosity = "high",
            OpenAiDefaultEnableWebSearch = "false",
            OpenAiAdvancedEnableWebSearch = "true",
            OpenAiDefaultEnableCodeInterpreter = "false",
            OpenAiAdvancedEnableCodeInterpreter = "true"
        }
    };

    await writer.SaveAsync(rootPath, document);

    var envText = await File.ReadAllTextAsync(Path.Combine(rootPath, ".env"), Encoding.UTF8);
    AssertContains(envText, "OPENAI_DEFAULT_REASONING_EFFORT=medium", "Saved env should contain default reasoning effort.");
    AssertContains(envText, "OPENAI_ADVANCED_REASONING_EFFORT=high", "Saved env should contain advanced reasoning effort.");
    AssertContains(envText, "OPENAI_DEFAULT_TEXT_VERBOSITY=low", "Saved env should contain default text verbosity.");
    AssertContains(envText, "OPENAI_ADVANCED_TEXT_VERBOSITY=high", "Saved env should contain advanced text verbosity.");
    AssertContains(envText, "OPENAI_DEFAULT_ENABLE_WEB_SEARCH=false", "Saved env should contain default web search toggle.");
    AssertContains(envText, "OPENAI_ADVANCED_ENABLE_WEB_SEARCH=true", "Saved env should contain advanced web search toggle.");
    AssertContains(envText, "OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER=false", "Saved env should contain default code interpreter toggle.");
    AssertContains(envText, "OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER=true", "Saved env should contain advanced code interpreter toggle.");

    var loadedDocument = await reader.LoadAsync(rootPath);
    AssertEqual("medium", loadedDocument.Config.OpenAiDefaultReasoningEffort, "Default reasoning effort should round-trip.");
    AssertEqual("high", loadedDocument.Config.OpenAiAdvancedReasoningEffort, "Advanced reasoning effort should round-trip.");
    AssertEqual("low", loadedDocument.Config.OpenAiDefaultTextVerbosity, "Default text verbosity should round-trip.");
    AssertEqual("high", loadedDocument.Config.OpenAiAdvancedTextVerbosity, "Advanced text verbosity should round-trip.");
    AssertEqual("false", loadedDocument.Config.OpenAiDefaultEnableWebSearch, "Default web search toggle should round-trip.");
    AssertEqual("true", loadedDocument.Config.OpenAiAdvancedEnableWebSearch, "Advanced web search toggle should round-trip.");
    AssertEqual("false", loadedDocument.Config.OpenAiDefaultEnableCodeInterpreter, "Default code interpreter toggle should round-trip.");
    AssertEqual("true", loadedDocument.Config.OpenAiAdvancedEnableCodeInterpreter, "Advanced code interpreter toggle should round-trip.");
}

Task TestPathDiscoveryServiceBackendRootAsync()
{
    var rootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-root-{Guid.NewGuid():N}");
    Directory.CreateDirectory(rootPath);
    Directory.CreateDirectory(Path.Combine(rootPath, "src"));
    File.WriteAllText(Path.Combine(rootPath, "package.json"), "{}");
    File.WriteAllText(Path.Combine(rootPath, "src", "index.mjs"), "console.log('ok');");

    AssertTrue(PathDiscoveryService.IsBackendRoot(rootPath), "Expected temp directory to be a backend root.");
    AssertFalse(PathDiscoveryService.IsBackendRoot(Path.Combine(rootPath, "missing")), "Missing directory should not be a backend root.");
    return Task.CompletedTask;
}

Task TestLocalActivityStateStoreRoundTripAsync()
{
    var storeRootPath = Path.Combine(Path.GetTempPath(), $"desktop-activity-store-{Guid.NewGuid():N}");
    var backendRootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-{Guid.NewGuid():N}");
    var storagePolicy = new DesktopActivityStateStoragePolicy(storeRootPath);
    var store = new LocalActivityStateStore(
        storeRootPath,
        new DesktopActivityStatePolicy
        {
            Version = 1,
            MaxRecentActivitiesPerChannel = 4,
            RetentionWindow = TimeSpan.FromDays(30)
        },
        storagePolicy);

    store.Save(
        backendRootPath,
        new DesktopActivityState
        {
            Version = 1,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-1",
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
                    EventType = "Request",
                    Summary = "request summary",
                    Meta = "meta",
                    Detail = "detail"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "req-1",
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
                    EventType = "Request",
                    Summary = "duplicate should drop",
                    Meta = "meta",
                    Detail = "detail"
                }
            ],
            WechatRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "fail-1",
                    CapturedAt = DateTimeOffset.UtcNow.ToString("O"),
                    EventType = "Failure",
                    Summary = "failure summary",
                    Meta = "meta",
                    Detail = "detail",
                    IsFailure = true
                }
            ],
            SelectedQqEventKey = "req-1",
            SelectedWechatEventKey = "fail-1",
            PinSelectedQqActivity = true,
            ShowOnlyWechatFailures = true
        });

    var restored = store.Load(backendRootPath);

    AssertEqual(1, restored.Version, "LocalActivityStateStore should normalize to the current version.");
    AssertEqual(1, restored.QqRecentActivities.Count, "LocalActivityStateStore should deduplicate recent activity items.");
    AssertEqual("req-1", restored.SelectedQqEventKey, "LocalActivityStateStore should retain valid selected QQ event keys.");
    AssertEqual("fail-1", restored.SelectedWechatEventKey, "LocalActivityStateStore should retain valid selected Wechat event keys.");
    AssertTrue(restored.PinSelectedQqActivity, "LocalActivityStateStore should preserve pin state.");
    AssertTrue(restored.ShowOnlyWechatFailures, "LocalActivityStateStore should preserve filter state.");
    return Task.CompletedTask;
}

Task TestDesktopActivityStatePolicySemanticsAsync()
{
    var policy = new DesktopActivityStatePolicy
    {
        Version = 3,
        MaxRecentActivitiesPerChannel = 2,
        RetentionWindow = TimeSpan.FromDays(7)
    };
    var now = DateTimeOffset.UtcNow;
    var normalized = policy.Normalize(
        new DesktopActivityState
        {
            Version = 3,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-old",
                    CapturedAt = now.AddDays(-30).ToString("O"),
                    EventType = "Request",
                    Summary = "old"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "req-new",
                    CapturedAt = now.ToString("O"),
                    EventType = "Request",
                    Summary = "new"
                }
            ],
            SelectedQqEventKey = "missing"
        });

    AssertNotNull(normalized, "DesktopActivityStatePolicy should normalize compatible states.");
    AssertEqual(3, normalized!.Version, "DesktopActivityStatePolicy should preserve configured version.");
    AssertEqual(1, normalized.QqRecentActivities.Count, "DesktopActivityStatePolicy should prune activities outside the retention window.");
    AssertEqual(string.Empty, normalized.SelectedQqEventKey, "DesktopActivityStatePolicy should clear invalid selected event keys.");
    AssertEqual(string.Empty, policy.ResolveSelectedEventKey(normalized.QqRecentActivities, "missing"), "DesktopActivityStatePolicy should reject unknown selection keys.");
    AssertEqual("req-new", policy.ResolveSelectedEventKey(normalized.QqRecentActivities, "req-new"), "DesktopActivityStatePolicy should accept valid selection keys.");
    var snapshot = policy.CreateSnapshot(
        normalized.QqRecentActivities,
        normalized.WechatRecentActivities,
        normalized.QqRecentActivities.First(),
        null,
        pinSelectedQqActivity: true,
        pinSelectedWechatActivity: false,
        showOnlyQqFailures: false,
        showOnlyWechatFailures: true);
    AssertEqual(3, snapshot.Version, "DesktopActivityStatePolicy should stamp snapshots with the configured version.");
    AssertEqual("req-new", snapshot.SelectedQqEventKey, "DesktopActivityStatePolicy should persist selected event keys through snapshot creation.");
    AssertEqual(null, policy.ResolveSelectedItem(normalized.QqRecentActivities, "missing"), "DesktopActivityStatePolicy should resolve missing selected items to null.");
    AssertEqual("req-new", policy.ResolveSelectedItem(normalized.QqRecentActivities, "req-new")?.EventKey ?? string.Empty, "DesktopActivityStatePolicy should resolve selected items by key.");
    return Task.CompletedTask;
}

Task TestLocalActivityStateStoreRetentionAsync()
{
    var storeRootPath = Path.Combine(Path.GetTempPath(), $"desktop-activity-store-{Guid.NewGuid():N}");
    var backendRootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-{Guid.NewGuid():N}");
    var storagePolicy = new DesktopActivityStateStoragePolicy(storeRootPath);
    var store = new LocalActivityStateStore(
        storeRootPath,
        new DesktopActivityStatePolicy
        {
            Version = 1,
            MaxRecentActivitiesPerChannel = 6,
            RetentionWindow = TimeSpan.FromDays(7)
        },
        storagePolicy);
    var now = DateTimeOffset.UtcNow;

    store.Save(
        backendRootPath,
        new DesktopActivityState
        {
            Version = 1,
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "req-old",
                    CapturedAt = now.AddDays(-30).ToString("O"),
                    EventType = "Request",
                    Summary = "old request",
                    Meta = "old",
                    Detail = "old detail"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "req-new",
                    CapturedAt = now.ToString("O"),
                    EventType = "Request",
                    Summary = "new request",
                    Meta = "new",
                    Detail = "new detail"
                }
            ]
        });

    var restored = store.Load(backendRootPath);

    AssertEqual(1, restored.QqRecentActivities.Count, "LocalActivityStateStore should prune activity older than the retention window.");
    AssertEqual("req-new", restored.QqRecentActivities[0].EventKey, "LocalActivityStateStore should retain recent activity inside the retention window.");
    return Task.CompletedTask;
}

Task TestLocalActivityStateStoreVersionCleanupAsync()
{
    var storeRootPath = Path.Combine(Path.GetTempPath(), $"desktop-activity-store-{Guid.NewGuid():N}");
    var backendRootPath = Path.Combine(Path.GetTempPath(), $"desktop-backend-{Guid.NewGuid():N}");
    Directory.CreateDirectory(storeRootPath);
    var storagePolicy = new DesktopActivityStateStoragePolicy(storeRootPath);
    var store = new LocalActivityStateStore(
        storeRootPath,
        new DesktopActivityStatePolicy
        {
            Version = 2,
            MaxRecentActivitiesPerChannel = 6,
            RetentionWindow = TimeSpan.FromDays(14)
        },
        storagePolicy);
    var filePath = storagePolicy.ResolveStateFilePath(backendRootPath);

    File.WriteAllText(
        filePath,
        "{\"version\":999,\"qqRecentActivities\":[{\"eventKey\":\"req-1\"}]}",
        Encoding.UTF8);

    var restored = store.Load(backendRootPath);
    AssertEqual(0, restored.QqRecentActivities.Count, "Incompatible activity-state versions should be dropped.");
    AssertFalse(File.Exists(filePath), "Loading incompatible activity-state versions should clean up the persisted file.");

    store.Save(backendRootPath, new DesktopActivityState());
    AssertFalse(File.Exists(filePath), "Saving an empty/default activity state should delete the persisted file.");
    return Task.CompletedTask;
}

async Task TestBackendControlApiServiceCamelCaseContractAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var seenPutBody = string.Empty;
    var seenAuthorizationHeaders = new List<string>();
    var serverTask = Task.Run(async () =>
    {
        for (var index = 0; index < 3; index++)
        {
            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;
            seenAuthorizationHeaders.Add(request.Headers["Authorization"] ?? string.Empty);

            try
            {
                switch ($"{request.HttpMethod} {request.Url?.AbsolutePath}")
                {
                    case "GET /config":
                        await WriteJsonAsync(
                            response,
                            new
                            {
                                openAiApiKey = "test-key",
                                allowedChatIds = "chat-a,chat-b",
                                allowedUserIds = "user-a",
                                envPath = "D:\\temp\\.env",
                                restartRequired = false
                            });
                        break;
                    case "GET /status":
                        await WriteJsonAsync(
                            response,
                            new
                            {
                                runtimeActive = true,
                                runtimeReady = true,
                                napcatConnected = true,
                                activeLockCount = 0,
                                wechatConfigured = true,
                                wechatRuntimeActive = true,
                                wechatRuntimeReady = true,
                                wechatBridgeConnected = true,
                                lastQqLlmRequest = new
                                {
                                    route = "default",
                                    routeReason = "directive:/ai+web_search",
                                    matchedPrefix = "/ai",
                                    model = "gpt-5.4",
                                    effectiveApiStyle = "responses",
                                    effectiveReasoningEffort = "medium",
                                    effectiveTextVerbosity = "medium",
                                    effectiveTools = new[] { "web_search" },
                                    decisionSummary = new
                                    {
                                        trigger = new
                                        {
                                            kind = "directive",
                                            matchedPrefix = "/ai"
                                        },
                                        reasonTags = new[] { "directive:/ai", "web_search" },
                                        reasonGroups = new
                                        {
                                            triggerReasons = new[] { "directive:/ai" },
                                            capabilityReasons = new[] { "web_search" },
                                            upgradeReasons = Array.Empty<string>()
                                        },
                                        requestedCapabilities = new
                                        {
                                            reasoningEffort = "medium",
                                            textVerbosity = "medium",
                                            enableWebSearch = true,
                                            enableCodeInterpreter = false,
                                            needsResponsesCapabilities = true
                                        },
                                        routeReason = "directive:/ai+web_search",
                                        matchedPrefix = "/ai"
                                    },
                                    imageCount = 0
                                }
                            });
                        break;
                    case "PUT /config":
                        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                        {
                            seenPutBody = await reader.ReadToEndAsync();
                        }

                        await WriteJsonAsync(
                            response,
                            new
                            {
                                openAiApiKey = "test-key",
                                allowedChatIds = "chat-x,chat-y",
                                allowedUserIds = "user-a",
                                envPath = "D:\\temp\\.env",
                                restartRequired = false,
                                savedAt = "2026-03-23T00:00:00.000Z"
                            });
                        break;
                    default:
                        response.StatusCode = 404;
                        await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("{\"error\":\"not found\"}"));
                        break;
                }
            }
            finally
            {
                response.Close();
            }
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    service.SetAccessToken("desktop-secret");

    var config = await service.TryGetConfigAsync() ?? throw new InvalidOperationException("Expected config response.");
    AssertEqual("chat-a,chat-b", config.AllowedChatIds, "Config should deserialize allowedChatIds.");

    var status = await service.TryGetStatusAsync() ?? throw new InvalidOperationException("Expected status response.");
    AssertTrue(status.RuntimeActive, "RuntimeActive should deserialize from camelCase.");
    AssertTrue(status.RuntimeReady, "RuntimeReady should deserialize from camelCase.");
    AssertTrue(status.WechatConfigured, "WechatConfigured should deserialize from camelCase.");
    AssertTrue(status.WechatBridgeConnected, "WechatBridgeConnected should deserialize from camelCase.");
    AssertTrue(status.WechatRuntimeReady, "WechatRuntimeReady should deserialize from camelCase.");
    AssertEqual("default", status.LastQqLlmRequest?.Route ?? string.Empty, "LastQqLlmRequest route should deserialize from camelCase.");
    AssertEqual("responses", status.LastQqLlmRequest?.EffectiveApiStyle ?? string.Empty, "LastQqLlmRequest api style should deserialize from camelCase.");
    AssertEqual("directive", status.LastQqLlmRequest?.DecisionSummary?.Trigger?.Kind ?? string.Empty, "DecisionSummary trigger kind should deserialize from camelCase.");
    AssertEqual("/ai", status.LastQqLlmRequest?.DecisionSummary?.MatchedPrefix ?? string.Empty, "DecisionSummary matchedPrefix should deserialize from camelCase.");
    AssertTrue(status.LastQqLlmRequest?.DecisionSummary?.ReasonGroups?.CapabilityReasons?.Contains("web_search") == true, "DecisionSummary capability reasons should deserialize from camelCase.");

    var saveResult = await service.TrySaveConfigAsync(
        new BotConfig
        {
            OpenAiApiKey = "test-key",
            AllowedChatIds = "chat-x,chat-y",
            AllowedUserIds = "user-a"
        }) ?? throw new InvalidOperationException("Expected save response.");
    AssertEqual("chat-x,chat-y", saveResult.AllowedChatIds, "Save response should deserialize allowedChatIds.");
    AssertContains(seenPutBody, "\"allowedChatIds\":\"chat-x,chat-y\"", "PUT body should use camelCase allowedChatIds.");
    AssertDoesNotContain(seenPutBody, "allowedGroupIds", "PUT body should not contain legacy field.");
    AssertTrue(seenAuthorizationHeaders.All(static header => header == "Bearer desktop-secret"), "Control API service should send the configured bearer token on every request.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceUnauthorizedAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            response.StatusCode = 401;
            await response.OutputStream.WriteAsync(
                Encoding.UTF8.GetBytes("{\"error\":\"Control API authentication failed.\"}"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    service.SetAccessToken("wrong-token");
    var config = await service.TryGetConfigAsync();

    AssertEqual(null, config, "Unauthorized config request should return null.");
    AssertEqual(BackendControlApiFailureKind.Unauthorized, service.LastFailure.Kind, "401 responses should be classified as unauthorized.");
    AssertContains(service.LastFailure.Message, "authentication failed", "Unauthorized failures should preserve the backend error text.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceRejectedSaveAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            response.StatusCode = 400;
            await response.OutputStream.WriteAsync(
                Encoding.UTF8.GetBytes("{\"error\":\"WECHAT_BRIDGE_URL must be a valid ws:// or wss:// URL\"}"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    var saveResult = await service.TrySaveConfigAsync(new BotConfig
    {
        WechatBridgeUrl = "not-a-valid-wechat-url"
    });

    AssertEqual(null, saveResult, "Rejected save should return null.");
    AssertEqual(BackendControlApiFailureKind.Rejected, service.LastFailure.Kind, "Rejected save should set failure kind.");
    AssertContains(service.LastFailure.Message, "WECHAT_BRIDGE_URL", "Rejected save should preserve backend error text.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceEmptyConfigResponseAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("null"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    var config = await service.TryGetConfigAsync();

    AssertEqual(null, config, "Empty successful config response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful config response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty config response", "Empty successful config response should preserve the synthesized failure message.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceEmptySaveResponseAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        var context = await listener.GetContextAsync();
        var response = context.Response;

        try
        {
            await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("null"));
        }
        finally
        {
            response.Close();
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));
    var saveResult = await service.TrySaveConfigAsync(new BotConfig
    {
        OpenAiApiKey = "test-key"
    });

    AssertEqual(null, saveResult, "Empty successful save response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful save response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty save response", "Empty successful save response should preserve the synthesized failure message.");

    listener.Stop();
    await serverTask;
}

async Task TestBackendControlApiServiceEmptyRuntimeResponsesAsync()
{
    var port = GetFreeTcpPort();
    var prefix = $"http://127.0.0.1:{port}/";
    using var listener = new HttpListener();
    listener.Prefixes.Add(prefix);
    listener.Start();

    var serverTask = Task.Run(async () =>
    {
        for (var index = 0; index < 3; index++)
        {
            var context = await listener.GetContextAsync();
            var response = context.Response;

            try
            {
                await response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("null"));
            }
            finally
            {
                response.Close();
            }
        }
    });

    using var service = new BackendControlApiService(new Uri(prefix), TimeSpan.FromSeconds(2));

    var status = await service.TryGetStatusAsync();
    AssertEqual(null, status, "Empty successful status response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful status response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty status response", "Status null-body failure message should be preserved.");

    var startStatus = await service.TryStartAsync();
    AssertEqual(null, startStatus, "Empty successful start response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful start response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty start response", "Start null-body failure message should be preserved.");

    var stopStatus = await service.TryStopAsync();
    AssertEqual(null, stopStatus, "Empty successful stop response should return null.");
    AssertEqual(BackendControlApiFailureKind.Unknown, service.LastFailure.Kind, "Empty successful stop response should be classified as unknown.");
    AssertContains(service.LastFailure.Message, "empty stop response", "Stop null-body failure message should be preserved.");

    listener.Stop();
    await serverTask;
}

async Task TestBotProcessServiceStartStopAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-bot-process-");
    var srcPath = Path.Combine(rootPath, "src");
    Directory.CreateDirectory(srcPath);
    await File.WriteAllTextAsync(
        Path.Combine(srcPath, "index.mjs"),
        """
        console.log('desktop-bot-process-started');
        setInterval(() => {}, 1000);
        process.on('SIGTERM', () => process.exit(0));
        """,
        Encoding.UTF8);

    var exitSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var service = new BotProcessService();
    service.ProcessExited += (_, _) => exitSignal.TrySetResult();

    service.Start(rootPath);

    AssertTrue(service.IsRunning, "BotProcessService should report running after Start.");

    await service.StopAsync();

    AssertFalse(service.IsRunning, "BotProcessService should report stopped after StopAsync.");
    await WaitForAsync(() => exitSignal.Task.IsCompleted, "bot process exit event");
}

async Task TestBotProcessServiceDetachKeepsBackendAliveAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-bot-detach-");
    var srcPath = Path.Combine(rootPath, "src");
    Directory.CreateDirectory(srcPath);
    await File.WriteAllTextAsync(
        Path.Combine(srcPath, "index.mjs"),
        """
        console.log('desktop-bot-detach-started');
        setInterval(() => {}, 1000);
        process.on('SIGTERM', () => process.exit(0));
        """,
        Encoding.UTF8);

    var logs = new List<string>();
    using var service = new BotProcessService();
    service.LogReceived += (_, message) => logs.Add(message);

    service.Start(rootPath);
    await WaitForAsync(
        () => logs.Any(log => log.Contains("Started backend process, PID=", StringComparison.Ordinal)),
        "backend start log");

    var pidLog = logs.First(log => log.Contains("Started backend process, PID=", StringComparison.Ordinal));
    var pidStart = pidLog.IndexOf("PID=", StringComparison.Ordinal);
    var pidText = new string(pidLog[(pidStart + 4)..].TakeWhile(char.IsDigit).ToArray());
    var pid = int.Parse(pidText, System.Globalization.CultureInfo.InvariantCulture);

    service.Detach();

    AssertFalse(service.IsRunning, "BotProcessService should release ownership after Detach.");

    using var process = Process.GetProcessById(pid);
    AssertFalse(process.HasExited, "Detached backend process should still be alive.");

    process.Kill(entireProcessTree: true);
    await WaitForProcessExitAsync(process, "detached backend process");
}

Task TestBotProcessServiceMissingDirectoryAsync()
{
    using var service = new BotProcessService();
    var missingPath = Path.Combine(Path.GetTempPath(), $"desktop-missing-{Guid.NewGuid():N}");

    try
    {
        service.Start(missingPath);
        throw new InvalidOperationException("Expected missing directory to throw.");
    }
    catch (DirectoryNotFoundException)
    {
        return Task.CompletedTask;
    }
}

async Task TestAsyncRelayCommandReentryAsync()
{
    var startSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var invocationCount = 0;

    var command = new AsyncRelayCommand(async () =>
    {
        Interlocked.Increment(ref invocationCount);
        startSignal.TrySetResult();
        await releaseSignal.Task;
    });

    AssertTrue(command.CanExecute(null), "AsyncRelayCommand should be executable before start.");
    command.Execute(null);
    await startSignal.Task;

    AssertFalse(command.CanExecute(null), "AsyncRelayCommand should block re-entry while running.");
    command.Execute(null);
    AssertEqual(1, invocationCount, "AsyncRelayCommand should ignore re-entry.");

    releaseSignal.TrySetResult();
    await WaitForAsync(() => command.CanExecute(null), "async relay command completion");
    AssertEqual(1, invocationCount, "AsyncRelayCommand should execute only once.");
}

Task TestRelayCommandCanExecuteAsync()
{
    var executed = false;
    var command = new RelayCommand(
        _ => executed = true,
        _ => true);

    AssertTrue(command.CanExecute(null), "RelayCommand should allow execution when predicate returns true.");
    command.Execute(null);
    AssertTrue(executed, "RelayCommand should execute action.");

    executed = false;
    var blockedCommand = new RelayCommand(
        _ => executed = true,
        _ => false);

    AssertFalse(blockedCommand.CanExecute(null), "RelayCommand should block execution when predicate returns false.");
    if (blockedCommand.CanExecute(null))
    {
        blockedCommand.Execute(null);
    }

    AssertFalse(executed, "Blocked RelayCommand should not execute action.");
    return Task.CompletedTask;
}

Task TestAutoStartServiceCommandResolutionAsync()
{
    var command = AutoStartService.BuildAutoStartCommand(@"C:\Apps\QQAIBot.Desktop.exe");
    AssertEqual(
        "\"C:\\Apps\\QQAIBot.Desktop.exe\" --minimized --ensure-runtime",
        command,
        "Auto-start command should include expected arguments.");

    var directExecutable = AutoStartService.ResolveAutoStartExecutablePath(
        processPath: @"C:\Apps\QQAIBot.Desktop.exe",
        appBaseDirectory: @"C:\Apps",
        entryAssemblyName: "QQAIBot.Desktop",
        fileExists: _ => false);
    AssertEqual(
        @"C:\Apps\QQAIBot.Desktop.exe",
        directExecutable,
        "Direct executable path should be preferred when process is not dotnet.");

    var resolvedFromBaseDirectory = AutoStartService.ResolveAutoStartExecutablePath(
        processPath: @"C:\Program Files\dotnet\dotnet.exe",
        appBaseDirectory: @"C:\Apps",
        entryAssemblyName: "QQAIBot.Desktop",
        fileExists: candidate => string.Equals(candidate, @"C:\Apps\QQAIBot.Desktop.exe", StringComparison.OrdinalIgnoreCase));
    AssertEqual(
        @"C:\Apps\QQAIBot.Desktop.exe",
        resolvedFromBaseDirectory,
        "Published exe in app base directory should be used when process is dotnet.");

    return Task.CompletedTask;
}

Task TestAutoStartServiceRegistryStoreAsync()
{
    var store = new FakeAutoStartRegistryStore();
    var service = new AutoStartService(
        registryStore: store,
        resolveExecutablePath: () => @"C:\Apps\QQAIBot.Desktop.exe");

    AssertFalse(service.IsEnabled(), "AutoStartService should start disabled when registry value is missing.");

    service.SetEnabled(true);

    AssertTrue(service.IsEnabled(), "AutoStartService should report enabled after writing registry value.");
    AssertEqual(
        "\"C:\\Apps\\QQAIBot.Desktop.exe\" --minimized --ensure-runtime",
        store.GetValue("QQAIBot.Desktop") ?? string.Empty,
        "AutoStartService should write expected startup command.");

    service.SetEnabled(false);

    AssertFalse(service.IsEnabled(), "AutoStartService should report disabled after deleting registry value.");
    AssertEqual(null, store.GetValue("QQAIBot.Desktop"), "AutoStartService should delete startup value when disabled.");
    return Task.CompletedTask;
}

Task TestAutoStartServiceWindowsRegistryStoreAsync()
{
    var suffix = Guid.NewGuid().ToString("N");
    var runKeyPath = $@"Software\QQAIBot.Desktop.Tests\{suffix}";

    try
    {
        var service = new AutoStartService(
            registryStore: new WindowsAutoStartRegistryStore(runKeyPath),
            resolveExecutablePath: () => @"C:\Apps\QQAIBot.Desktop.exe");

        AssertFalse(service.IsEnabled(), "Registry-backed AutoStartService should start disabled on empty test key.");

        service.SetEnabled(true);

        using (var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
        {
            AssertEqual(
                "\"C:\\Apps\\QQAIBot.Desktop.exe\" --minimized --ensure-runtime",
                runKey?.GetValue("QQAIBot.Desktop") as string,
                "Registry-backed AutoStartService should write expected startup command.");
        }

        AssertTrue(service.IsEnabled(), "Registry-backed AutoStartService should report enabled after writing value.");

        service.SetEnabled(false);

        using (var runKey = Registry.CurrentUser.OpenSubKey(runKeyPath, writable: false))
        {
            AssertEqual(
                null,
                runKey?.GetValue("QQAIBot.Desktop") as string,
                "Registry-backed AutoStartService should remove startup value when disabled.");
        }

        AssertFalse(service.IsEnabled(), "Registry-backed AutoStartService should report disabled after removing value.");
    }
    finally
    {
        Registry.CurrentUser.DeleteSubKeyTree(runKeyPath, throwOnMissingSubKey: false);
    }

    return Task.CompletedTask;
}

async Task TestSingleInstanceCoordinatorActivateAsync()
{
    var suffix = Guid.NewGuid().ToString("N");
    using var primary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");
    using var secondary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");

    var activateSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var ensureSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    AssertTrue(
        primary.TryAcquirePrimaryOwnership(
            () => activateSignal.TrySetResult(),
            () => ensureSignal.TrySetResult()),
        "Primary coordinator should acquire single-instance ownership.");

    secondary.SignalPrimaryInstance(ensureRuntime: false);

    await WaitForAsync(() => activateSignal.Task.IsCompleted, "activate signal delivery");
    AssertFalse(ensureSignal.Task.IsCompleted, "Activate signal should not trigger ensure-runtime callback.");
}

async Task TestSingleInstanceCoordinatorEnsureRuntimeAsync()
{
    var suffix = Guid.NewGuid().ToString("N");
    using var primary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");
    using var secondary = new SingleInstanceCoordinator(
        mutexName: $"QQAIBot.Desktop.Tests.SingleInstance.{suffix}",
        activateEventName: $"QQAIBot.Desktop.Tests.Activate.{suffix}",
        ensureRuntimeEventName: $"QQAIBot.Desktop.Tests.EnsureRuntime.{suffix}");

    var activateSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var ensureSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    AssertTrue(
        primary.TryAcquirePrimaryOwnership(
            () => activateSignal.TrySetResult(),
            () => ensureSignal.TrySetResult()),
        "Primary coordinator should acquire single-instance ownership.");

    secondary.SignalPrimaryInstance(ensureRuntime: true);

    await WaitForAsync(() => ensureSignal.Task.IsCompleted, "ensure-runtime signal delivery");
    AssertFalse(activateSignal.Task.IsCompleted, "Ensure-runtime signal should not trigger activate callback.");
}

async Task TestDesktopCrossProcessSingleInstanceActivationAsync()
{
    var rootPath = await CreateTempDirectoryAsync("desktop-cross-process-");
    Directory.CreateDirectory(Path.Combine(rootPath, "src"));
    await File.WriteAllTextAsync(Path.Combine(rootPath, "package.json"), "{}", Encoding.UTF8);
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, "src", "index.mjs"),
        "setInterval(() => {}, 1000); process.on('SIGTERM', () => process.exit(0));",
        Encoding.UTF8);
    await File.WriteAllTextAsync(
        Path.Combine(rootPath, ".env"),
        string.Join(
            Environment.NewLine,
            [
                "OPENAI_API_KEY=test-key",
                "NAPCAT_TOKEN=test-token",
                "WECHAT_BRIDGE_URL=",
                "BOT_PREFIX=/ai"
            ]) + Environment.NewLine,
        Encoding.UTF8);

    var signalFilePath = Path.Combine(rootPath, "desktop-signals.log");
    var scopeSuffix = Guid.NewGuid().ToString("N");
    var desktopExePath = ResolveDesktopExecutablePath();

    using var primaryProcess = StartDesktopProcess(
        desktopExePath,
        rootPath,
        [],
        signalFilePath,
        scopeSuffix);

    try
    {
        await WaitForSignalAsync(signalFilePath, "window-loaded");

        using var activateProcess = StartDesktopProcess(
            desktopExePath,
            rootPath,
            [],
            signalFilePath,
            scopeSuffix);
        await WaitForProcessExitAsync(activateProcess, "secondary desktop activation process");
        await WaitForSignalAsync(signalFilePath, "restore-from-external-activation");

        using var ensureRuntimeProcess = StartDesktopProcess(
            desktopExePath,
            rootPath,
            ["--ensure-runtime"],
            signalFilePath,
            scopeSuffix);
        await WaitForProcessExitAsync(ensureRuntimeProcess, "secondary desktop ensure-runtime process");
        await WaitForSignalAsync(signalFilePath, "ensure-runtime-from-external-activation");
    }
    finally
    {
        await StopProcessAsync(primaryProcess);
    }
}

async Task TestMainWindowSmokeAutomationAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-smoke-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;

    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "main window initial config load");
                await WaitForAsync(
                    () => (window.FindName("WechatBotPrefixTextBox") as TextBox)?.Text == "/ai",
                    "wechat prefix textbox binding");
                await WaitForAsync(
                    () => (window.FindName("OpenAiAdvancedEnableWebSearchCheckBox") as CheckBox)?.IsChecked == true,
                    "openai advanced web search checkbox binding");
                await WaitForAsync(
                    () => ((window.FindName("DefaultBotInstructionsTextBox") as TextBox)?.Text ?? string.Empty).Contains("你是 QQ 群助手。", StringComparison.Ordinal),
                    "default bot instructions textbox binding");
                await WaitForAsync(
                    () => ((window.FindName("LatestQqLlmSummaryTextBlock") as TextBlock)?.Text ?? string.Empty).Contains("default / gpt-5.4 / responses", StringComparison.Ordinal),
                    "latest qq llm summary binding");

                var wechatPrefixTextBox = window.FindName("WechatBotPrefixTextBox") as TextBox
                    ?? throw new InvalidOperationException("WechatBotPrefixTextBox not found.");
                var defaultBotInstructionsTextBox = window.FindName("DefaultBotInstructionsTextBox") as TextBox
                    ?? throw new InvalidOperationException("DefaultBotInstructionsTextBox not found.");
                var botPersonaTextBox = window.FindName("BotPersonaTextBox") as TextBox
                    ?? throw new InvalidOperationException("BotPersonaTextBox not found.");
                var effectiveBotInstructionsTextBox = window.FindName("EffectiveBotInstructionsTextBox") as TextBox
                    ?? throw new InvalidOperationException("EffectiveBotInstructionsTextBox not found.");
                var allowedChatIdsTextBox = window.FindName("AllowedChatIdsTextBox") as TextBox
                    ?? throw new InvalidOperationException("AllowedChatIdsTextBox not found.");
                var defaultWebSearchCheckBox = window.FindName("OpenAiDefaultEnableWebSearchCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiDefaultEnableWebSearchCheckBox not found.");
                var advancedWebSearchCheckBox = window.FindName("OpenAiAdvancedEnableWebSearchCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiAdvancedEnableWebSearchCheckBox not found.");
                var defaultCodeInterpreterCheckBox = window.FindName("OpenAiDefaultEnableCodeInterpreterCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiDefaultEnableCodeInterpreterCheckBox not found.");
                var advancedCodeInterpreterCheckBox = window.FindName("OpenAiAdvancedEnableCodeInterpreterCheckBox") as CheckBox
                    ?? throw new InvalidOperationException("OpenAiAdvancedEnableCodeInterpreterCheckBox not found.");
                var latestQqLlmSummaryTextBlock = window.FindName("LatestQqLlmSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqLlmSummaryTextBlock not found.");
                var latestQqActivitySummaryTextBlock = window.FindName("LatestQqActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqActivitySummaryTextBlock not found.");
                var qqFailuresOnlyToggleButton = window.FindName("QqFailuresOnlyToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("QqFailuresOnlyToggleButton not found.");
                var qqPinSelectionToggleButton = window.FindName("QqPinSelectionToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("QqPinSelectionToggleButton not found.");
                var clearQqActivityHistoryButton = window.FindName("ClearQqActivityHistoryButton") as Button
                    ?? throw new InvalidOperationException("ClearQqActivityHistoryButton not found.");
                var latestQqRecentActivityListBox = window.FindName("LatestQqRecentActivityListBox") as ListBox
                    ?? throw new InvalidOperationException("LatestQqRecentActivityListBox not found.");
                var selectedQqRecentActivitySummaryTextBlock = window.FindName("SelectedQqRecentActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedQqRecentActivitySummaryTextBlock not found.");
                var selectedQqRecentActivityMetaTextBlock = window.FindName("SelectedQqRecentActivityMetaTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedQqRecentActivityMetaTextBlock not found.");
                var selectedQqRecentActivityDetailTextBlock = window.FindName("SelectedQqRecentActivityDetailTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedQqRecentActivityDetailTextBlock not found.");
                var latestQqActivityStateTextBlock = window.FindName("LatestQqActivityStateTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqActivityStateTextBlock not found.");
                var latestQqLatestSuccessTextBlock = window.FindName("LatestQqLatestSuccessTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqLatestSuccessTextBlock not found.");
                var latestQqLatestFailureTextBlock = window.FindName("LatestQqLatestFailureTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqLatestFailureTextBlock not found.");
                var latestQqRecoveryTextBlock = window.FindName("LatestQqRecoveryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqRecoveryTextBlock not found.");
                var latestQqRequestTimelineTextBlock = window.FindName("LatestQqRequestTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqRequestTimelineTextBlock not found.");
                var latestQqDecisionTriggerTextBlock = window.FindName("LatestQqDecisionTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqDecisionTriggerTextBlock not found.");
                var latestQqDecisionCapabilityTextBlock = window.FindName("LatestQqDecisionCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqDecisionCapabilityTextBlock not found.");
                var latestQqDecisionUpgradeTextBlock = window.FindName("LatestQqDecisionUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqDecisionUpgradeTextBlock not found.");
                var latestQqRequestedCapabilitiesTextBlock = window.FindName("LatestQqRequestedCapabilitiesTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqRequestedCapabilitiesTextBlock not found.");
                var latestWechatLlmSummaryTextBlock = window.FindName("LatestWechatLlmSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatLlmSummaryTextBlock not found.");
                var latestWechatActivitySummaryTextBlock = window.FindName("LatestWechatActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatActivitySummaryTextBlock not found.");
                var wechatFailuresOnlyToggleButton = window.FindName("WechatFailuresOnlyToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("WechatFailuresOnlyToggleButton not found.");
                var wechatPinSelectionToggleButton = window.FindName("WechatPinSelectionToggleButton") as System.Windows.Controls.Primitives.ToggleButton
                    ?? throw new InvalidOperationException("WechatPinSelectionToggleButton not found.");
                var clearWechatActivityHistoryButton = window.FindName("ClearWechatActivityHistoryButton") as Button
                    ?? throw new InvalidOperationException("ClearWechatActivityHistoryButton not found.");
                var latestWechatRecentActivityListBox = window.FindName("LatestWechatRecentActivityListBox") as ListBox
                    ?? throw new InvalidOperationException("LatestWechatRecentActivityListBox not found.");
                var selectedWechatRecentActivitySummaryTextBlock = window.FindName("SelectedWechatRecentActivitySummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedWechatRecentActivitySummaryTextBlock not found.");
                var selectedWechatRecentActivityMetaTextBlock = window.FindName("SelectedWechatRecentActivityMetaTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedWechatRecentActivityMetaTextBlock not found.");
                var selectedWechatRecentActivityDetailTextBlock = window.FindName("SelectedWechatRecentActivityDetailTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("SelectedWechatRecentActivityDetailTextBlock not found.");
                var latestWechatActivityStateTextBlock = window.FindName("LatestWechatActivityStateTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatActivityStateTextBlock not found.");
                var latestWechatLatestSuccessTextBlock = window.FindName("LatestWechatLatestSuccessTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatLatestSuccessTextBlock not found.");
                var latestWechatLatestFailureTextBlock = window.FindName("LatestWechatLatestFailureTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatLatestFailureTextBlock not found.");
                var latestWechatRecoveryTextBlock = window.FindName("LatestWechatRecoveryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatRecoveryTextBlock not found.");
                var latestWechatRequestTimelineTextBlock = window.FindName("LatestWechatRequestTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatRequestTimelineTextBlock not found.");
                var latestWechatDecisionTriggerTextBlock = window.FindName("LatestWechatDecisionTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatDecisionTriggerTextBlock not found.");
                var latestWechatDecisionCapabilityTextBlock = window.FindName("LatestWechatDecisionCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatDecisionCapabilityTextBlock not found.");
                var latestWechatDecisionUpgradeTextBlock = window.FindName("LatestWechatDecisionUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatDecisionUpgradeTextBlock not found.");
                var latestWechatRequestedCapabilitiesTextBlock = window.FindName("LatestWechatRequestedCapabilitiesTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatRequestedCapabilitiesTextBlock not found.");
                var latestQqFailureSummaryTextBlock = window.FindName("LatestQqFailureSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureSummaryTextBlock not found.");
                var latestQqFailureTimelineTextBlock = window.FindName("LatestQqFailureTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureTimelineTextBlock not found.");
                var latestQqFailureTriggerTextBlock = window.FindName("LatestQqFailureTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureTriggerTextBlock not found.");
                var latestQqFailureCapabilityTextBlock = window.FindName("LatestQqFailureCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureCapabilityTextBlock not found.");
                var latestQqFailureUpgradeTextBlock = window.FindName("LatestQqFailureUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureUpgradeTextBlock not found.");
                var latestQqFailureErrorTextBlock = window.FindName("LatestQqFailureErrorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestQqFailureErrorTextBlock not found.");
                var latestWechatFailureSummaryTextBlock = window.FindName("LatestWechatFailureSummaryTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureSummaryTextBlock not found.");
                var latestWechatFailureTimelineTextBlock = window.FindName("LatestWechatFailureTimelineTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureTimelineTextBlock not found.");
                var latestWechatFailureTriggerTextBlock = window.FindName("LatestWechatFailureTriggerTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureTriggerTextBlock not found.");
                var latestWechatFailureCapabilityTextBlock = window.FindName("LatestWechatFailureCapabilityTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureCapabilityTextBlock not found.");
                var latestWechatFailureUpgradeTextBlock = window.FindName("LatestWechatFailureUpgradeTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureUpgradeTextBlock not found.");
                var latestWechatFailureErrorTextBlock = window.FindName("LatestWechatFailureErrorTextBlock") as TextBlock
                    ?? throw new InvalidOperationException("LatestWechatFailureErrorTextBlock not found.");
                var runtimeReadyText = viewModel.RuntimeReadyText;
                var wechatRuntimeReadyText = viewModel.WechatRuntimeReadyText;
                var saveButton = window.FindName("SaveButton") as Button
                    ?? throw new InvalidOperationException("SaveButton not found.");
                var startButton = window.FindName("StartButton") as Button
                    ?? throw new InvalidOperationException("StartButton not found.");
                var stopButton = window.FindName("StopButton") as Button
                    ?? throw new InvalidOperationException("StopButton not found.");

                AssertEqual("/ai", wechatPrefixTextBox.Text, "WechatBotPrefix textbox should reflect loaded config.");
                AssertContains(defaultBotInstructionsTextBox.Text, "你是 QQ 群助手。", "Default bot instructions textbox should show the backend default system prompt.");
                AssertContains(defaultBotInstructionsTextBox.Text, "默认使用简体中文。", "Default bot instructions textbox should show the backend language guidance.");
                AssertEqual(defaultBotInstructionsTextBox.Text, effectiveBotInstructionsTextBox.Text, "Effective bot instructions should match the default prompt when BOT_PERSONA is empty.");
                AssertEqual("chat-a,chat-b", allowedChatIdsTextBox.Text, "AllowedChatIds textbox should reflect loaded config.");
                AssertEqual(false, defaultWebSearchCheckBox.IsChecked ?? false, "Default web search checkbox should reflect loaded config.");
                AssertEqual(true, advancedWebSearchCheckBox.IsChecked ?? false, "Advanced web search checkbox should reflect loaded config.");
                AssertEqual(false, defaultCodeInterpreterCheckBox.IsChecked ?? false, "Default code interpreter checkbox should reflect loaded config.");
                AssertEqual(true, advancedCodeInterpreterCheckBox.IsChecked ?? false, "Advanced code interpreter checkbox should reflect loaded config.");
                AssertEqual("default / gpt-5.4 / responses", latestQqLlmSummaryTextBlock.Text, "Latest QQ LLM summary should reflect runtime status.");
                AssertEqual("advanced / gpt-5.4 / responses", latestWechatLlmSummaryTextBlock.Text, "Latest Wechat LLM summary should reflect runtime status.");
                AssertContains(latestQqActivitySummaryTextBlock.Text, "Latest event: request", "Latest QQ activity summary should reflect recovery when request is newer than failure.");
                AssertEqual(2, latestQqRecentActivityListBox.Items.Count, "Latest QQ recent activity list should include two events.");
                AssertContains(selectedQqRecentActivitySummaryTextBlock.Text, "default / gpt-5.4 / responses", "Selected QQ recent activity summary should show the latest request event.");
                AssertContains(selectedQqRecentActivityMetaTextBlock.Text, "2026-03-24", "Selected QQ recent activity meta should show the captured time.");
                AssertContains(selectedQqRecentActivityDetailTextBlock.Text, "trigger=default", "Selected QQ recent activity detail should show the request detail.");
                AssertEqual("Recovered after failure", latestQqActivityStateTextBlock.Text, "Latest QQ activity state should reflect recovery.");
                AssertContains(latestQqLatestSuccessTextBlock.Text, "Success |", "Latest QQ latest success should render activity checkpoint.");
                AssertContains(latestQqLatestFailureTextBlock.Text, "Failure |", "Latest QQ latest failure should render activity checkpoint.");
                AssertContains(latestQqRecoveryTextBlock.Text, "Recovery |", "Latest QQ recovery text should render recovery checkpoint.");
                AssertContains(latestQqRequestTimelineTextBlock.Text, "Request |", "Latest QQ request timeline should render the request header.");
                AssertEqual("default", latestQqDecisionTriggerTextBlock.Text, "Latest QQ decision trigger should reflect structured inspection binding.");
                AssertEqual("default", latestQqDecisionCapabilityTextBlock.Text, "Latest QQ decision capability should reflect structured inspection binding.");
                AssertEqual("none", latestQqDecisionUpgradeTextBlock.Text, "Latest QQ decision upgrade should reflect structured inspection binding.");
                AssertContains(latestQqRequestedCapabilitiesTextBlock.Text, "reasoning=medium", "Latest QQ requested capabilities should reflect structured inspection binding.");
                AssertContains(latestWechatActivitySummaryTextBlock.Text, "Latest event: failure", "Latest Wechat activity summary should reflect failure when failure is newer than request.");
                AssertEqual(2, latestWechatRecentActivityListBox.Items.Count, "Latest Wechat recent activity list should include two events.");
                AssertContains(selectedWechatRecentActivitySummaryTextBlock.Text, "advanced / provider rejected request", "Selected Wechat recent activity summary should show the latest failure event.");
                AssertContains(selectedWechatRecentActivityMetaTextBlock.Text, "2026-03-24", "Selected Wechat recent activity meta should show the captured time.");
                AssertContains(selectedWechatRecentActivityDetailTextBlock.Text, "error=provider rejected request", "Selected Wechat recent activity detail should show the failure detail.");
                AssertEqual("Failure is latest event", latestWechatActivityStateTextBlock.Text, "Latest Wechat activity state should reflect failure-latest state.");
                AssertContains(latestWechatLatestSuccessTextBlock.Text, "Success |", "Latest Wechat latest success should render activity checkpoint.");
                AssertContains(latestWechatLatestFailureTextBlock.Text, "Failure |", "Latest Wechat latest failure should render activity checkpoint.");
                AssertEqual("Recovery | pending", latestWechatRecoveryTextBlock.Text, "Latest Wechat recovery text should reflect pending recovery.");
                AssertContains(latestWechatRequestTimelineTextBlock.Text, "Request |", "Latest Wechat request timeline should render the request header.");
                AssertEqual("directive:/gpt", latestWechatDecisionTriggerTextBlock.Text, "Latest Wechat decision trigger should reflect structured inspection binding.");
                AssertEqual("default", latestWechatDecisionCapabilityTextBlock.Text, "Latest Wechat decision capability should reflect structured inspection binding.");
                AssertEqual("none", latestWechatDecisionUpgradeTextBlock.Text, "Latest Wechat decision upgrade should reflect structured inspection binding.");
                AssertContains(latestWechatRequestedCapabilitiesTextBlock.Text, "code=on", "Latest Wechat requested capabilities should reflect structured inspection binding.");
                AssertContains(latestQqFailureSummaryTextBlock.Text, "default /", "Latest QQ failure summary should reflect structured failure binding.");
                AssertContains(latestQqFailureTimelineTextBlock.Text, "Failure |", "Latest QQ failure timeline should render the failure header.");
                AssertEqual("default", latestQqFailureTriggerTextBlock.Text, "Latest QQ failure trigger should reflect structured failure binding.");
                AssertEqual("web_search", latestQqFailureCapabilityTextBlock.Text, "Latest QQ failure capability should reflect structured failure binding.");
                AssertEqual("none", latestQqFailureUpgradeTextBlock.Text, "Latest QQ failure upgrade should reflect structured failure binding.");
                AssertEqual("search timed out", latestQqFailureErrorTextBlock.Text, "Latest QQ failure error should reflect structured failure binding.");
                AssertContains(latestWechatFailureSummaryTextBlock.Text, "advanced /", "Latest Wechat failure summary should reflect structured failure binding.");
                AssertContains(latestWechatFailureTimelineTextBlock.Text, "Failure |", "Latest Wechat failure timeline should render the failure header.");
                AssertEqual("directive:/gpt", latestWechatFailureTriggerTextBlock.Text, "Latest Wechat failure trigger should reflect structured failure binding.");
                AssertEqual("default", latestWechatFailureCapabilityTextBlock.Text, "Latest Wechat failure capability should reflect structured failure binding.");
                AssertEqual("capability_upgrade", latestWechatFailureUpgradeTextBlock.Text, "Latest Wechat failure upgrade should reflect structured failure binding.");
                AssertEqual("provider rejected request", latestWechatFailureErrorTextBlock.Text, "Latest Wechat failure error should reflect structured failure binding.");
                AssertContains(viewModel.LatestQqLlmDetailText, "trigger=default", "Latest QQ LLM detail should prefer structured decision trigger.");
                AssertContains(viewModel.LatestQqLlmDetailText, "capability=default", "Latest QQ LLM detail should show structured capability reasons.");
                AssertContains(viewModel.LatestQqLlmDetailText, "upgrade=none", "Latest QQ LLM detail should show structured upgrade reasons.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "trigger=directive:/gpt", "Latest Wechat LLM detail should prefer structured directive trigger.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "capability=default", "Latest Wechat LLM detail should show structured capability reasons when none are present.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "upgrade=none", "Latest Wechat LLM detail should show structured upgrade reasons when none are present.");
                AssertFalse(qqPinSelectionToggleButton.IsChecked ?? true, "QQ pin toggle should be off by default.");
                AssertFalse(wechatPinSelectionToggleButton.IsChecked ?? true, "Wechat pin toggle should be off by default.");

                qqPinSelectionToggleButton.IsChecked = true;
                var applyStatusMethod = typeof(MainViewModel).GetMethod(
                    "ApplyBackendRuntimeStatus",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("ApplyBackendRuntimeStatus not found.");
                fakeBackend.Status!.LastQqLlmRequest = new BackendLlmRequestStatus
                {
                    Route = "advanced",
                    Model = "gpt-5.4-mini",
                    EffectiveApiStyle = "responses",
                    EffectiveReasoningEffort = "high",
                    EffectiveTextVerbosity = "high",
                    EffectiveTools = ["web_search"],
                    DecisionSummary = new BackendDecisionSummary
                    {
                        Trigger = new BackendDecisionTrigger
                        {
                            Kind = "directive",
                            MatchedPrefix = "/vision"
                        },
                        ReasonTags = ["directive:/vision"],
                        ReasonGroups = new BackendDecisionReasonGroups
                        {
                            TriggerReasons = ["directive:/vision"],
                            CapabilityReasons = [],
                            UpgradeReasons = []
                        },
                        RequestedCapabilities = new BackendRequestedCapabilities
                        {
                            ReasoningEffort = "high",
                            TextVerbosity = "high",
                            EnableWebSearch = true,
                            EnableCodeInterpreter = false,
                            NeedsResponsesCapabilities = true
                        },
                        RouteReason = "directive:/vision",
                        MatchedPrefix = "/vision"
                    },
                    ImageCount = 1,
                    CapturedAt = "2026-03-24T00:00:05.000Z",
                    ResponseId = "resp-new-1"
                };
                applyStatusMethod.Invoke(viewModel, [fakeBackend.Status, true]);
                await WaitForAsync(() => latestQqRecentActivityListBox.Items.Count == 3, "QQ recent activity grows after pinned update");
                AssertContains(selectedQqRecentActivitySummaryTextBlock.Text, "default / gpt-5.4 / responses", "Pinned QQ selection should remain on the prior event after a newer request arrives.");

                qqFailuresOnlyToggleButton.IsChecked = true;
                wechatFailuresOnlyToggleButton.IsChecked = true;
                await WaitForAsync(() => latestQqRecentActivityListBox.Items.Count == 1, "QQ failures-only filter");
                await WaitForAsync(() => latestWechatRecentActivityListBox.Items.Count == 1, "Wechat failures-only filter");
                AssertContains(selectedQqRecentActivitySummaryTextBlock.Text, "default / search timed out", "QQ failures-only filter should select the failure event.");
                AssertContains(selectedWechatRecentActivitySummaryTextBlock.Text, "advanced / provider rejected request", "Wechat failures-only filter should keep the failure event selected.");
                clearQqActivityHistoryButton.Command.Execute(null);
                await WaitForAsync(() => latestQqRecentActivityListBox.Items.Count == 0, "QQ clear activity history");
                AssertContains(selectedQqRecentActivitySummaryTextBlock.Text, "Select a QQ activity event", "Clearing QQ activity history should clear the selected detail.");
                AssertFalse(qqPinSelectionToggleButton.IsChecked ?? true, "Clearing QQ activity history should reset the pin toggle.");
                AssertTrue(clearWechatActivityHistoryButton.Command.CanExecute(null), "Wechat clear activity history button should be enabled while events exist.");
                AssertEqual("QQ channel ready", runtimeReadyText, "Runtime ready text should reflect runtime status.");
                AssertEqual("Wechat channel ready", wechatRuntimeReadyText, "Wechat runtime ready text should reflect runtime status.");

                wechatPrefixTextBox.Text = "/wx";
                botPersonaTextBox.Text = "冷静、专业。";
                allowedChatIdsTextBox.Text = "chat-x,chat-y";
                defaultWebSearchCheckBox.IsChecked = true;
                advancedWebSearchCheckBox.IsChecked = false;
                defaultCodeInterpreterCheckBox.IsChecked = true;
                advancedCodeInterpreterCheckBox.IsChecked = false;
                await WaitForAsync(
                    () => effectiveBotInstructionsTextBox.Text.Contains("附加人格设定:", StringComparison.Ordinal)
                        && effectiveBotInstructionsTextBox.Text.Contains("冷静、专业。", StringComparison.Ordinal),
                    "effective bot instructions preview update");

                saveButton.Command.Execute(null);
                await WaitForAsync(() => fakeBackend.SaveConfigCallCount == 1, "save command invocation");
                AssertNotNull(fakeBackend.LastSavedConfig, "Saved config payload should be captured.");
                AssertEqual("/wx", fakeBackend.LastSavedConfig!.WechatBotPrefix, "Save should use edited WechatBotPrefix.");
                AssertEqual("冷静、专业。", fakeBackend.LastSavedConfig.BotPersona, "Save should use edited BOT_PERSONA.");
                AssertEqual("chat-x,chat-y", fakeBackend.LastSavedConfig.AllowedChatIds, "Save should use edited AllowedChatIds.");
                AssertEqual("true", fakeBackend.LastSavedConfig.OpenAiDefaultEnableWebSearch, "Save should use edited default web search toggle.");
                AssertEqual("false", fakeBackend.LastSavedConfig.OpenAiAdvancedEnableWebSearch, "Save should use edited advanced web search toggle.");
                AssertEqual("true", fakeBackend.LastSavedConfig.OpenAiDefaultEnableCodeInterpreter, "Save should use edited default code interpreter toggle.");
                AssertEqual("false", fakeBackend.LastSavedConfig.OpenAiAdvancedEnableCodeInterpreter, "Save should use edited advanced code interpreter toggle.");

                startButton.Command.Execute(null);
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "start command invocation");

                stopButton.Command.Execute(null);
                await WaitForAsync(() => fakeBackend.StopCallCount == 1, "stop command invocation");
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelDisposeDoesNotStopBackendProcessAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-dispose-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            fakeBotProcess.Start(rootPath);
            await viewModel.DisposeAsync();

            AssertEqual(0, fakeBotProcess.StopCallCount, "DisposeAsync should not stop backend process ownership.");
            AssertTrue(fakeBotProcess.IsRunning, "Fake backend process should stay running after DisposeAsync.");
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelAutoRecoversControlApiBeforeWarningAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-recover-control-api-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var notifications = new List<TrayNotification>();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            viewModel.NotificationRequested += (_, notification) => notifications.Add(notification);

            try
            {
                var tickMethod = typeof(MainViewModel).GetMethod(
                    "OnStatusPollTimerTick",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("OnStatusPollTimerTick not found.");

                tickMethod.Invoke(viewModel, [null, EventArgs.Empty]);
                tickMethod.Invoke(viewModel, [null, EventArgs.Empty]);

                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "control api recovery backend start");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "control api recovery start command");
                await WaitForAsync(() => viewModel.IsControlApiReachable, "control api recovery reachable");

                AssertEqual(0, notifications.Count, "Control API recovery should happen before outage warning is shown.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelRejectsUnknownConfigFailureBeforeFallbackAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-load-unknown-config-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Config = null;
    fakeBackend.ConfigFailureKind = BackendControlApiFailureKind.Unknown;
    fakeBackend.ConfigFailureMessage = "Control API returned an empty config response.";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                var loadMethod = typeof(MainViewModel).GetMethod(
                    "LoadConfigFromAuthoritativeSourceAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("LoadConfigFromAuthoritativeSourceAsync not found.");

                var loadTask = loadMethod.Invoke(viewModel, []) as Task
                    ?? throw new InvalidOperationException("LoadConfigFromAuthoritativeSourceAsync did not return a Task.");

                await AssertThrowsAsync<InvalidOperationException>(
                    () => loadTask,
                    "Unknown config failures should throw before local fallback is considered.");

                AssertEqual(0, fakeLocalFallbackReader.LoadCallCount, "Unknown config failures should not touch the local fallback reader.");
                AssertEqual(0, fakeBotProcess.StartCallCount, "Unknown config failures should not trigger control API recovery.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelRejectsUnauthorizedConfigFailureBeforeFallbackAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-load-unauthorized-config-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Config = null;
    fakeBackend.ConfigFailureKind = BackendControlApiFailureKind.Unauthorized;
    fakeBackend.ConfigFailureMessage = "Control API authentication failed.";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                var loadMethod = typeof(MainViewModel).GetMethod(
                    "LoadConfigFromAuthoritativeSourceAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("LoadConfigFromAuthoritativeSourceAsync not found.");

                var loadTask = loadMethod.Invoke(viewModel, []) as Task
                    ?? throw new InvalidOperationException("LoadConfigFromAuthoritativeSourceAsync did not return a Task.");

                await AssertThrowsAsync<InvalidOperationException>(
                    () => loadTask,
                    "Unauthorized config failures should throw before local fallback is considered.");

                AssertEqual(0, fakeLocalFallbackReader.LoadCallCount, "Unauthorized config failures should not touch the local fallback reader.");
                AssertEqual(0, fakeBotProcess.StartCallCount, "Unauthorized config failures should not trigger control API recovery.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelLoadsThroughRecoveredControlApiAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-load-control-api-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;
    fakeBackend.RequireReachableForConfig = true;
    fakeLocalFallbackReader.Document.Config.WechatBotPrefix = "/file-only";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                await viewModel.InitializeAsync();

                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "load recovery backend start");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "load recovery control api start");
                await WaitForAsync(() => viewModel.IsControlApiReachable, "load recovery reachable");

                AssertTrue(fakeLocalFallbackReader.LoadCallCount >= 1, "Load may read the local env file for control API authentication preflight.");
                AssertEqual("/ai", viewModel.WechatBotPrefix, "Recovered control API load should use backend config instead of file snapshot.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelSavesThroughRecoveredControlApiAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-control-api-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;
    fakeBackend.RequireReachableForSave = true;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                viewModel.WechatBotPrefix = "/wx";
                viewModel.SaveCommand.Execute(null);

                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "save recovery backend start");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "save recovery control api start");
                await WaitForAsync(() => fakeBackend.SaveConfigCallCount >= 2, "save retry through recovered control api");

                AssertEqual(0, fakeLocalFallbackReader.SaveCallCount, "Save should not fall back to direct env file writes.");
                AssertNotNull(fakeBackend.LastSavedConfig, "Recovered control API save should capture config payload.");
                AssertEqual("/wx", fakeBackend.LastSavedConfig!.WechatBotPrefix, "Recovered control API save should use edited config.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelSurfacesRejectedControlApiSaveAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-rejected-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.SaveFailureKind = BackendControlApiFailureKind.Rejected;
    fakeBackend.SaveFailureMessage = "WECHAT_BRIDGE_URL must be a valid ws:// or wss:// URL";

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                viewModel.WechatBridgeUrl = "not-a-valid-wechat-url";

                var saveMethod = typeof(MainViewModel).GetMethod(
                    "SaveConfigAsync",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                    binder: null,
                    [typeof(bool)],
                    modifiers: null)
                    ?? throw new InvalidOperationException("SaveConfigAsync(bool) not found.");

                var saveTask = saveMethod.Invoke(viewModel, [false]) as Task<bool>
                    ?? throw new InvalidOperationException("SaveConfigAsync(bool) did not return Task<bool>.");

                var saved = await saveTask;

                AssertFalse(saved, "Rejected save should report failure.");
                AssertEqual(1, fakeBackend.SaveConfigCallCount, "Rejected save should not retry through recovery.");
                AssertEqual(0, fakeBotProcess.StartCallCount, "Rejected save should not start local backend recovery.");
                AssertEqual(0, fakeBackend.StartCallCount, "Rejected save should not issue control API start.");
                AssertEqual(0, fakeLocalFallbackReader.SaveCallCount, "Rejected save should not fall back to env file writes.");
                AssertContains(viewModel.StatusText, "Save failed", "Rejected save should surface save failure status.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainViewModelRestoresLocalActivityStateAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-activity-restore-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeActivityStateStore.Seed(
        rootPath,
        new DesktopActivityState
        {
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "request-1",
                    EventType = "Request",
                    Summary = "default / gpt-5.4 / responses",
                    Meta = "2026-03-24 08:00:00",
                    Detail = "request detail",
                    IsFailure = false
                }
            ],
            WechatRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "failure-1",
                    EventType = "Failure",
                    Summary = "advanced / provider rejected request",
                    Meta = "2026-03-24 08:00:01",
                    Detail = "failure detail",
                    IsFailure = true
                }
            ],
            SelectedQqEventKey = "request-1",
            SelectedWechatEventKey = "failure-1",
            PinSelectedQqActivity = true,
            PinSelectedWechatActivity = false,
            ShowOnlyQqFailures = false,
            ShowOnlyWechatFailures = true
        });

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);

            try
            {
                viewModel.BackendRootPath = Path.Combine(rootPath, "missing");
                viewModel.BackendRootPath = rootPath;
                AssertEqual(1, viewModel.QqRecentActivities.Count, "QQ recent activities should restore from local store.");
                AssertEqual(1, viewModel.WechatRecentActivities.Count, "Wechat recent activities should restore from local store.");
                AssertEqual("default / gpt-5.4 / responses", viewModel.SelectedQqRecentActivitySummaryText, "QQ selected activity should restore from local store.");
                AssertEqual("advanced / provider rejected request", viewModel.SelectedWechatRecentActivitySummaryText, "Wechat selected activity should restore from local store.");
                AssertTrue(viewModel.PinSelectedQqActivity, "QQ pin selection should restore from local store.");
                AssertFalse(viewModel.PinSelectedWechatActivity, "Wechat pin selection should restore from local store.");
                AssertFalse(viewModel.ShowOnlyQqFailures, "QQ filter should restore from local store.");
                AssertTrue(viewModel.ShowOnlyWechatFailures, "Wechat filter should restore from local store.");
                AssertTrue(fakeActivityStateStore.LoadCallCount >= 1, "MainViewModel should load local activity state during initialization or explicit backend-root assignment.");
            }
            finally
            {
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowAutoStartsBackendWhenControlApiIsUnavailableAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-auto-start-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.Status = null;

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBotProcess.StartCallCount == 1, "auto-start backend on unreachable control API", timeoutMs: 5000);
                AssertTrue(fakeBotProcess.IsRunning, "Fake bot process should be running after auto-start.");
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowExternalActivationAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-activation-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: true,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: false);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "main window minimized initial load");

                AssertEqual(WindowState.Minimized, window.WindowState, "Window should start minimized.");
                AssertEqual(0d, window.Opacity, "Window should start hidden when launched minimized.");
                AssertFalse(window.ShowInTaskbar, "Window should not be in taskbar when launched minimized.");

                window.RestoreFromExternalActivation();
                await WaitForAsync(() => window.WindowState == WindowState.Normal, "window restore after external activation");

                AssertEqual(WindowState.Normal, window.WindowState, "RestoreFromExternalActivation should normalize window state.");
                AssertEqual(1d, window.Opacity, "RestoreFromExternalActivation should make window visible.");
                AssertTrue(window.ShowInTaskbar, "RestoreFromExternalActivation should show taskbar entry.");

                window.EnsureRuntimeFromExternalActivation();
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "ensure-runtime external activation");
            }
            finally
            {
                window.Close();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayMinimizeBehaviorAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-minimize-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray minimize initial load");

                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => !window.IsVisible, "window hidden to tray after minimize");

                AssertFalse(window.ShowInTaskbar, "Window should hide from taskbar after minimizing to tray.");
                AssertTrue(fakeNotifyIcon.Visible, "Tray icon should stay visible while window is hidden.");

                AssertEqual(1, fakeNotifyIcon.ShowBalloonTipCallCount, "First minimize should show one tray balloon.");
                AssertContains(fakeNotifyIcon.BalloonTipText, "tray", "Minimize-to-tray should set balloon message.");
            }
            finally
            {
                window.ExitFromTrayForTests();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayCloseBehaviorAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-close-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray close initial load");

                window.Close();
                await WaitForAsync(() => !window.IsVisible, "window hidden to tray after close");

                AssertEqual("QQ AI Bot", fakeNotifyIcon.BalloonTipTitle, "Closing to tray should set balloon title.");
                AssertContains(fakeNotifyIcon.BalloonTipText, "tray", "Closing to tray should set balloon message.");
                AssertEqual(1, fakeNotifyIcon.ShowBalloonTipCallCount, "Closing to tray should show one balloon tip.");
                AssertTrue(fakeNotifyIcon.Visible, "Tray icon should remain visible after close-to-tray.");
            }
            finally
            {
                window.ExitFromTrayForTests();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

async Task TestMainWindowTrayMenuActionsAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-ui-tray-menu-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var fakeNotifyIcon = new FakeNotifyIconHost();
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    try
    {
        Directory.SetCurrentDirectory(rootPath);

        await RunOnStaThreadAsync(async () =>
        {
            var viewModel = new MainViewModel(
                fakeAutoStart,
                fakeLocalFallbackReader,
                fakeBackend,
                fakeBotProcess,
                fakeActivityStateStore);
            var window = new MainWindow(
                launchMinimizedToTray: false,
                ensureRuntimeOnStartup: false,
                viewModel: viewModel,
                enableNotifyIcon: true,
                notifyIconHost: fakeNotifyIcon);

            try
            {
                window.Show();
                await WaitForAsync(() => fakeBackend.GetConfigCallCount > 0, "tray menu initial load");

                var labels = window.GetTrayMenuLabelsForTests();
                AssertEqual(3, labels.Count, "Tray menu should expose three primary actions.");
                AssertEqual("Open", labels[0], "First tray menu action should be Open.");
                AssertEqual("Start Backend", labels[1], "Second tray menu action should be Start Backend.");
                AssertEqual("Stop Backend", labels[2], "Third tray menu action should be Stop Backend.");

                window.WindowState = WindowState.Minimized;
                await WaitForAsync(() => !window.IsVisible, "window hidden before tray open action");

                window.InvokeTrayMenuActionForTests("Open");
                await WaitForAsync(() => window.IsVisible && window.WindowState == WindowState.Normal, "tray open action");

                window.InvokeTrayMenuActionForTests("Start Backend");
                await WaitForAsync(() => fakeBackend.StartCallCount == 1, "tray start action");
                await WaitForAsync(() => viewModel.StopCommand.CanExecute(null), "stop command enabled after tray start");

                window.InvokeTrayMenuActionForTests("Stop Backend");
                await WaitForAsync(() => fakeBackend.StopCallCount == 1, "tray stop action");
            }
            finally
            {
                window.ExitFromTrayForTests();
                await viewModel.DisposeAsync();
            }
        });
    }
    finally
    {
        Directory.SetCurrentDirectory(originalCurrentDirectory);
    }
}

static async Task<string> CreateTempDirectoryAsync(string prefix)
{
    return await Task.FromResult(Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"{prefix}{Guid.NewGuid():N}")).FullName);
}

static async Task<DesktopUiTestContext> CreateDesktopUiTestContextAsync(string prefix)
{
    var rootPath = await CreateTempDirectoryAsync(prefix);
    Directory.CreateDirectory(Path.Combine(rootPath, "src"));
    await File.WriteAllTextAsync(Path.Combine(rootPath, "package.json"), "{}", Encoding.UTF8);
    await File.WriteAllTextAsync(Path.Combine(rootPath, "src", "index.mjs"), "console.log('ok');", Encoding.UTF8);

    var fakeBackend = new FakeBackendControlApiService
    {
        Status = new BackendRuntimeStatus
        {
            RuntimeActive = false,
            RuntimeReady = true,
            NapcatConnected = false,
            WechatConfigured = true,
            WechatRuntimeActive = false,
            WechatRuntimeReady = true,
            WechatBridgeConnected = false,
            LastQqLlmRequest = new BackendLlmRequestStatus
            {
                Route = "default",
                Model = "gpt-5.4",
                EffectiveApiStyle = "responses",
                EffectiveReasoningEffort = "medium",
                EffectiveTextVerbosity = "medium",
                EffectiveTools = [],
                RouteReason = "default",
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "default",
                        MatchedPrefix = string.Empty
                    },
                    ReasonTags = ["default"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = [],
                        CapabilityReasons = [],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "medium",
                        TextVerbosity = "medium",
                        EnableWebSearch = false,
                        EnableCodeInterpreter = false,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "default",
                    MatchedPrefix = string.Empty
                },
                ImageCount = 0,
                CapturedAt = "2026-03-24T00:00:04.000Z"
            },
            LastQqLlmFailure = new BackendLlmFailureStatus
            {
                Route = "default",
                RouteReason = "web_search",
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "default",
                        MatchedPrefix = string.Empty
                    },
                    ReasonTags = ["web_search"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = [],
                        CapabilityReasons = ["web_search"],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "high",
                        TextVerbosity = "high",
                        EnableWebSearch = true,
                        EnableCodeInterpreter = false,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "web_search",
                    MatchedPrefix = string.Empty
                },
                Error = "search timed out",
                CapturedAt = "2026-03-24T00:00:02.000Z"
            },
            LastWechatLlmRequest = new BackendLlmRequestStatus
            {
                Route = "advanced",
                Model = "gpt-5.4",
                EffectiveApiStyle = "responses",
                EffectiveReasoningEffort = "high",
                EffectiveTextVerbosity = "high",
                EffectiveTools = ["web_search", "code_interpreter"],
                RouteReason = "directive:/gpt",
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "directive",
                        MatchedPrefix = "/gpt"
                    },
                    ReasonTags = ["directive:/gpt"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = ["directive:/gpt"],
                        CapabilityReasons = [],
                        UpgradeReasons = []
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "high",
                        TextVerbosity = "high",
                        EnableWebSearch = true,
                        EnableCodeInterpreter = true,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "directive:/gpt",
                    MatchedPrefix = "/gpt"
                },
                ImageCount = 1,
                CapturedAt = "2026-03-24T00:00:01.000Z"
            },
            LastWechatLlmFailure = new BackendLlmFailureStatus
            {
                Route = "advanced",
                RouteReason = "directive:/gpt+capability_upgrade",
                DecisionSummary = new BackendDecisionSummary
                {
                    Trigger = new BackendDecisionTrigger
                    {
                        Kind = "directive",
                        MatchedPrefix = "/gpt"
                    },
                    ReasonTags = ["directive:/gpt", "capability_upgrade"],
                    ReasonGroups = new BackendDecisionReasonGroups
                    {
                        TriggerReasons = ["directive:/gpt"],
                        CapabilityReasons = [],
                        UpgradeReasons = ["capability_upgrade"]
                    },
                    RequestedCapabilities = new BackendRequestedCapabilities
                    {
                        ReasoningEffort = "high",
                        TextVerbosity = "high",
                        EnableWebSearch = false,
                        EnableCodeInterpreter = false,
                        NeedsResponsesCapabilities = true
                    },
                    RouteReason = "directive:/gpt+capability_upgrade",
                    MatchedPrefix = "/gpt"
                },
                Error = "provider rejected request",
                CapturedAt = "2026-03-24T00:00:03.000Z"
            }
        },
        Config = new BackendControlConfigResponse
        {
            OpenAiApiKey = "test-key",
            OpenAiDefaultReasoningEffort = "medium",
            OpenAiAdvancedReasoningEffort = "high",
            OpenAiDefaultTextVerbosity = "medium",
            OpenAiAdvancedTextVerbosity = "high",
            OpenAiDefaultEnableWebSearch = "false",
            OpenAiAdvancedEnableWebSearch = "true",
            OpenAiDefaultEnableCodeInterpreter = "false",
            OpenAiAdvancedEnableCodeInterpreter = "true",
            NapCatWsUrl = "ws://127.0.0.1:3001",
            WechatBridgeUrl = "ws://127.0.0.1:3198",
            WechatBotPrefix = "/ai",
            AllowedChatIds = "chat-a,chat-b",
            AllowedUserIds = "user-a",
            EnvPath = Path.Combine(rootPath, ".env"),
            RestartRequired = false
        }
    };
    var fakeLocalFallbackReader = new FakeLocalConfigFallbackReader(
        new EnvDocument
        {
            Config = new BotConfig
            {
                OpenAiApiKey = "test-key",
                OpenAiDefaultReasoningEffort = "medium",
                OpenAiAdvancedReasoningEffort = "high",
                OpenAiDefaultTextVerbosity = "medium",
                OpenAiAdvancedTextVerbosity = "high",
                OpenAiDefaultEnableWebSearch = "false",
                OpenAiAdvancedEnableWebSearch = "true",
                OpenAiDefaultEnableCodeInterpreter = "false",
                OpenAiAdvancedEnableCodeInterpreter = "true",
                NapCatWsUrl = "ws://127.0.0.1:3001",
                WechatBridgeUrl = "ws://127.0.0.1:3198",
                WechatBotPrefix = "/ai",
                AllowedChatIds = "chat-a,chat-b",
                AllowedUserIds = "user-a"
            }
        });
    var fakeActivityStateStore = new FakeActivityStateStore();

    return new DesktopUiTestContext(
        rootPath,
        fakeBackend,
        fakeLocalFallbackReader,
        new FakeAutoStartService(),
        new FakeBotProcessService(),
        fakeActivityStateStore);
}

static async Task WaitForAsync(Func<bool> predicate, string label, int timeoutMs = 5000, int intervalMs = 50)
{
    var startedAt = Environment.TickCount64;

    while (Environment.TickCount64 - startedAt < timeoutMs)
    {
        if (predicate())
        {
            return;
        }

        await Task.Delay(intervalMs);
    }

    throw new InvalidOperationException($"Timed out waiting for {label}.");
}

static async Task WaitForSignalAsync(string signalFilePath, string expectedEventName, int timeoutMs = 15000)
{
    await WaitForAsync(
        () =>
        {
            if (!File.Exists(signalFilePath))
            {
                return false;
            }

            var content = File.ReadAllText(signalFilePath, Encoding.UTF8);
            return content.Contains(expectedEventName, StringComparison.Ordinal);
        },
        $"desktop signal {expectedEventName}",
        timeoutMs);
}

static string ResolveDesktopExecutablePath()
{
    var candidatePath = Path.Combine(AppContext.BaseDirectory, "QQAIBot.Desktop.exe");

    if (File.Exists(candidatePath))
    {
        return candidatePath;
    }

    throw new InvalidOperationException("Could not locate QQAIBot.Desktop.exe next to desktop test harness.");
}

static Process StartDesktopProcess(
    string executablePath,
    string workingDirectory,
    IEnumerable<string> arguments,
    string signalFilePath,
    string scopeSuffix)
{
    var process = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = string.Join(' ', arguments),
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        }
    };
    process.StartInfo.Environment["QQ_AI_BOT_DESKTOP_TEST_SIGNAL_FILE"] = signalFilePath;
    process.StartInfo.Environment["QQ_AI_BOT_DESKTOP_SINGLE_INSTANCE_SUFFIX"] = scopeSuffix;

    if (!process.Start())
    {
        throw new InvalidOperationException($"Failed to start desktop process: {executablePath}");
    }

    return process;
}

static async Task WaitForProcessExitAsync(Process process, string label, int timeoutMs = 15000)
{
    using var cts = new CancellationTokenSource(timeoutMs);

    try
    {
        await process.WaitForExitAsync(cts.Token);
    }
    catch (OperationCanceledException)
    {
        throw new InvalidOperationException($"Timed out waiting for {label} to exit.");
    }
}

static async Task StopProcessAsync(Process process, int timeoutMs = 15000)
{
    if (process.HasExited)
    {
        process.Dispose();
        return;
    }

    process.Kill(entireProcessTree: true);
    await WaitForProcessExitAsync(process, "desktop process stop", timeoutMs);
    process.Dispose();
}

static int GetFreeTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    return ((IPEndPoint)listener.LocalEndpoint).Port;
}

static async Task WriteJsonAsync(HttpListenerResponse response, object payload)
{
    var body = JsonSerializer.Serialize(payload);
    var bytes = Encoding.UTF8.GetBytes(body);
    response.ContentType = "application/json; charset=utf-8";
    response.ContentLength64 = bytes.Length;
    await response.OutputStream.WriteAsync(bytes);
}

async Task RunOnStaThreadAsync(Func<Task> action)
{
    if (uiDispatcher is null)
    {
        uiThread = new Thread(() =>
        {
            try
            {
                var app = new QQAIBot.Desktop.App();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                uiApp = app;
                uiDispatcher = Dispatcher.CurrentDispatcher;
                uiDispatcherReady.TrySetResult();
                Dispatcher.Run();
                uiThreadStopped.TrySetResult();
            }
            catch (Exception ex)
            {
                uiDispatcherReady.TrySetException(ex);
                uiThreadStopped.TrySetException(ex);
            }
            finally
            {
                uiApp = null;
                uiDispatcher = null;
            }
        });

        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.IsBackground = true;
        uiThread.Start();
        await uiDispatcherReady.Task;
    }

    var dispatcher = uiDispatcher ?? throw new InvalidOperationException("UI dispatcher is not initialized.");
    await dispatcher.InvokeAsync(action).Task.Unwrap();
}

async Task ShutdownUiThreadAsync()
{
    var dispatcher = uiDispatcher;

    if (dispatcher is null)
    {
        return;
    }

    await dispatcher.InvokeAsync(() =>
    {
        (uiApp ?? System.Windows.Application.Current)?.Shutdown();

        if (!dispatcher.HasShutdownStarted)
        {
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
        }
    }).Task;

    await Task.WhenAny(uiThreadStopped.Task, Task.Delay(5000));
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertFalse(bool condition, string message)
{
    if (condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertNotNull(object? value, string message)
{
    if (value is null)
    {
        throw new InvalidOperationException(message);
    }
}

static void AssertEqual<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message} Expected: {expected}; Actual: {actual}");
    }
}

static void AssertContains(string text, string expectedSubstring, string message)
{
    if (!text.Contains(expectedSubstring, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Missing substring: {expectedSubstring}");
    }
}

static void AssertDoesNotContain(string text, string forbiddenSubstring, string message)
{
    if (text.Contains(forbiddenSubstring, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message} Forbidden substring: {forbiddenSubstring}");
    }
}

static async Task AssertThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

sealed class FakeAutoStartService : IAutoStartService
{
    public bool Enabled { get; private set; }

    public bool IsEnabled() => Enabled;

    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
    }
}

sealed class FakeAutoStartRegistryStore : IAutoStartRegistryStore
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    public string? GetValue(string valueName)
    {
        return _values.TryGetValue(valueName, out var value) ? value : null;
    }

    public void SetValue(string valueName, string value)
    {
        _values[valueName] = value;
    }

    public void DeleteValue(string valueName)
    {
        _values.Remove(valueName);
    }
}

sealed class TestEnvConfigSnapshotWriter
{
    private static readonly string[] KnownKeys =
    {
        "OPENAI_API_KEY",
        "OPENAI_DEFAULT_API_KEY",
        "OPENAI_ADVANCED_API_KEY",
        "OPENAI_MODEL",
        "OPENAI_DEFAULT_MODEL",
        "OPENAI_ADVANCED_MODEL",
        "OPENAI_BASE_URL",
        "OPENAI_DEFAULT_BASE_URL",
        "OPENAI_ADVANCED_BASE_URL",
        "OPENAI_ADVANCED_TRIGGER_PREFIXES",
        "OPENAI_DEFAULT_API_STYLE",
        "OPENAI_ADVANCED_API_STYLE",
        "OPENAI_DEFAULT_REASONING_EFFORT",
        "OPENAI_ADVANCED_REASONING_EFFORT",
        "OPENAI_DEFAULT_TEXT_VERBOSITY",
        "OPENAI_ADVANCED_TEXT_VERBOSITY",
        "OPENAI_DEFAULT_ENABLE_WEB_SEARCH",
        "OPENAI_ADVANCED_ENABLE_WEB_SEARCH",
        "OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER",
        "OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER",
        "NAPCAT_WS_URL",
        "NAPCAT_TOKEN",
        "WECHAT_BRIDGE_URL",
        "WECHAT_BRIDGE_TOKEN",
        "WECHAT_BOT_PREFIX",
        "BOT_PREFIX",
        "BOT_PERSONA",
        "MAX_OUTPUT_CHARS",
        "ALLOWED_CHAT_IDS",
        "ALLOWED_USER_IDS"
    };

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task SaveAsync(string rootPath, EnvDocument document)
    {
        Directory.CreateDirectory(rootPath);

        var envPath = Path.Combine(rootPath, ".env");
        var config = document.Config;
        var lines = new List<string>
        {
            $"OPENAI_API_KEY={config.OpenAiApiKey}",
            $"OPENAI_DEFAULT_API_KEY={config.OpenAiDefaultApiKey}",
            $"OPENAI_ADVANCED_API_KEY={config.OpenAiApiKey}",
            $"OPENAI_MODEL={config.OpenAiModel}",
            $"OPENAI_DEFAULT_MODEL={config.OpenAiDefaultModel}",
            $"OPENAI_ADVANCED_MODEL={config.OpenAiModel}",
            $"OPENAI_BASE_URL={config.OpenAiBaseUrl}",
            $"OPENAI_DEFAULT_BASE_URL={config.OpenAiDefaultBaseUrl}",
            $"OPENAI_ADVANCED_BASE_URL={config.OpenAiBaseUrl}",
            "OPENAI_DEFAULT_API_STYLE=responses",
            "OPENAI_ADVANCED_API_STYLE=responses",
            $"OPENAI_DEFAULT_REASONING_EFFORT={config.OpenAiDefaultReasoningEffort}",
            $"OPENAI_ADVANCED_REASONING_EFFORT={config.OpenAiAdvancedReasoningEffort}",
            $"OPENAI_DEFAULT_TEXT_VERBOSITY={config.OpenAiDefaultTextVerbosity}",
            $"OPENAI_ADVANCED_TEXT_VERBOSITY={config.OpenAiAdvancedTextVerbosity}",
            $"OPENAI_DEFAULT_ENABLE_WEB_SEARCH={config.OpenAiDefaultEnableWebSearch}",
            $"OPENAI_ADVANCED_ENABLE_WEB_SEARCH={config.OpenAiAdvancedEnableWebSearch}",
            $"OPENAI_DEFAULT_ENABLE_CODE_INTERPRETER={config.OpenAiDefaultEnableCodeInterpreter}",
            $"OPENAI_ADVANCED_ENABLE_CODE_INTERPRETER={config.OpenAiAdvancedEnableCodeInterpreter}",
            $"OPENAI_ADVANCED_TRIGGER_PREFIXES={config.OpenAiAdvancedTriggerPrefixes}",
            $"NAPCAT_WS_URL={config.NapCatWsUrl}",
            $"NAPCAT_TOKEN={config.NapCatToken}",
            $"WECHAT_BRIDGE_URL={config.WechatBridgeUrl}",
            $"WECHAT_BRIDGE_TOKEN={config.WechatBridgeToken}",
            $"WECHAT_BOT_PREFIX={config.WechatBotPrefix}",
            $"BOT_PREFIX={config.BotPrefix}",
            $"BOT_PERSONA={EncodeEnvValue(config.BotPersona)}",
            $"MAX_OUTPUT_CHARS={config.MaxOutputChars}",
            $"ALLOWED_CHAT_IDS={config.AllowedChatIds}",
            $"ALLOWED_USER_IDS={config.AllowedUserIds}"
        };

        foreach (var pair in document.ExtraValues
                     .Where(static pair => !KnownKeys.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add($"{pair.Key}={pair.Value}");
        }

        await File.WriteAllLinesAsync(envPath, lines, Utf8NoBom);
    }

    private static string EncodeEnvValue(string value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

sealed class FakeLocalConfigFallbackReader : ILocalConfigFallbackReader
{
    public EnvDocument Document { get; }

    public int LoadCallCount { get; private set; }
    public int SaveCallCount { get; private set; }

    public FakeLocalConfigFallbackReader(EnvDocument document)
    {
        Document = document;
    }

    public Task<EnvDocument> LoadAsync(string rootPath)
    {
        LoadCallCount += 1;
        return Task.FromResult(Document);
    }
}

sealed class FakeBackendControlApiService : IBackendControlApiService
{
    public BackendRuntimeStatus? Status { get; set; }

    public BackendControlConfigResponse? Config { get; set; }

    public BackendControlApiFailure LastFailure { get; private set; } = new();

    public string AccessToken { get; private set; } = string.Empty;

    public int GetStatusCallCount { get; private set; }

    public int GetConfigCallCount { get; private set; }

    public int SaveConfigCallCount { get; private set; }

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public BotConfig? LastSavedConfig { get; private set; }

    public bool RequireReachableForSave { get; set; }
    public bool RequireReachableForConfig { get; set; }

    public BackendControlApiFailureKind ConfigFailureKind { get; set; } = BackendControlApiFailureKind.None;

    public string ConfigFailureMessage { get; set; } = string.Empty;

    public BackendControlApiFailureKind SaveFailureKind { get; set; } = BackendControlApiFailureKind.None;

    public string SaveFailureMessage { get; set; } = string.Empty;

    public void SetAccessToken(string? accessToken)
    {
        AccessToken = string.IsNullOrWhiteSpace(accessToken) ? string.Empty : accessToken.Trim();
    }

    public Task<BackendRuntimeStatus?> TryGetStatusAsync(CancellationToken cancellationToken = default)
    {
        GetStatusCallCount += 1;
        LastFailure = Status is null
            ? new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            }
            : new BackendControlApiFailure();
        return Task.FromResult(Status);
    }

    public Task<BackendControlConfigResponse?> TryGetConfigAsync(CancellationToken cancellationToken = default)
    {
        GetConfigCallCount += 1;
        if (ConfigFailureKind != BackendControlApiFailureKind.None)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = ConfigFailureKind,
                Message = ConfigFailureMessage
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        if (RequireReachableForConfig && Status is null)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        LastFailure = Config is null
            ? new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            }
            : new BackendControlApiFailure();
        return Task.FromResult<BackendControlConfigResponse?>(Config);
    }

    public Task<BackendControlConfigResponse?> TrySaveConfigAsync(BotConfig config, CancellationToken cancellationToken = default)
    {
        SaveConfigCallCount += 1;

        if (RequireReachableForSave && Status is null)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = BackendControlApiFailureKind.Unreachable,
                Message = "Control API is unreachable."
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        if (SaveFailureKind != BackendControlApiFailureKind.None)
        {
            LastFailure = new BackendControlApiFailure
            {
                Kind = SaveFailureKind,
                Message = SaveFailureMessage
            };
            return Task.FromResult<BackendControlConfigResponse?>(null);
        }

        LastSavedConfig = config;
        LastFailure = new BackendControlApiFailure();
        Config = new BackendControlConfigResponse
        {
            OpenAiApiKey = config.OpenAiApiKey,
            OpenAiDefaultApiKey = config.OpenAiDefaultApiKey,
            OpenAiDefaultModel = config.OpenAiDefaultModel,
            OpenAiModel = config.OpenAiModel,
            OpenAiBaseUrl = config.OpenAiBaseUrl,
            OpenAiDefaultBaseUrl = config.OpenAiDefaultBaseUrl,
            OpenAiDefaultReasoningEffort = config.OpenAiDefaultReasoningEffort,
            OpenAiAdvancedReasoningEffort = config.OpenAiAdvancedReasoningEffort,
            OpenAiDefaultTextVerbosity = config.OpenAiDefaultTextVerbosity,
            OpenAiAdvancedTextVerbosity = config.OpenAiAdvancedTextVerbosity,
            OpenAiDefaultEnableWebSearch = config.OpenAiDefaultEnableWebSearch,
            OpenAiAdvancedEnableWebSearch = config.OpenAiAdvancedEnableWebSearch,
            OpenAiDefaultEnableCodeInterpreter = config.OpenAiDefaultEnableCodeInterpreter,
            OpenAiAdvancedEnableCodeInterpreter = config.OpenAiAdvancedEnableCodeInterpreter,
            OpenAiAdvancedTriggerPrefixes = config.OpenAiAdvancedTriggerPrefixes,
            NapCatWsUrl = config.NapCatWsUrl,
            NapCatToken = config.NapCatToken,
            WechatBridgeUrl = config.WechatBridgeUrl,
            WechatBridgeToken = config.WechatBridgeToken,
            WechatBotPrefix = config.WechatBotPrefix,
            BotPrefix = config.BotPrefix,
            BotPersona = config.BotPersona,
            MaxOutputChars = config.MaxOutputChars,
            AllowedChatIds = config.AllowedChatIds,
            AllowedUserIds = config.AllowedUserIds,
            EnvPath = Config?.EnvPath ?? string.Empty,
            RestartRequired = false
        };

        return Task.FromResult<BackendControlConfigResponse?>(Config);
    }

    public Task<BackendRuntimeStatus?> TryStartAsync(CancellationToken cancellationToken = default)
    {
        StartCallCount += 1;
        Status ??= new BackendRuntimeStatus();
        Status.RuntimeActive = true;
        LastFailure = new BackendControlApiFailure();
        return Task.FromResult<BackendRuntimeStatus?>(Status);
    }

    public Task<BackendRuntimeStatus?> TryStopAsync(CancellationToken cancellationToken = default)
    {
        StopCallCount += 1;
        Status ??= new BackendRuntimeStatus();
        Status.RuntimeActive = false;
        LastFailure = new BackendControlApiFailure();
        return Task.FromResult<BackendRuntimeStatus?>(Status);
    }

    public void Dispose()
    {
    }
}

sealed class FakeBotProcessService : IBotProcessService
{
    public event EventHandler<string>? LogReceived;

    public event EventHandler? ProcessExited;

    public int StartCallCount { get; private set; }

    public int StopCallCount { get; private set; }

    public int DetachCallCount { get; private set; }

    public bool IsRunning { get; private set; }

    public void Start(string workingDirectory)
    {
        StartCallCount += 1;
        IsRunning = true;
        LogReceived?.Invoke(this, $"fake-start:{workingDirectory}");
    }

    public void Detach()
    {
        DetachCallCount += 1;
        IsRunning = false;
    }

    public Task StopAsync()
    {
        StopCallCount += 1;
        IsRunning = false;
        ProcessExited?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

sealed class FakeActivityStateStore : IActivityStateStore
{
    private readonly Dictionary<string, DesktopActivityState> _states = new(StringComparer.OrdinalIgnoreCase);

    public int LoadCallCount { get; private set; }

    public int SaveCallCount { get; private set; }

    public DesktopActivityState Load(string backendRootPath)
    {
        LoadCallCount += 1;
        return _states.TryGetValue(backendRootPath, out var state)
            ? Clone(state)
            : new DesktopActivityState();
    }

    public void Save(string backendRootPath, DesktopActivityState state)
    {
        SaveCallCount += 1;
        _states[backendRootPath] = Clone(state);
    }

    public void Seed(string backendRootPath, DesktopActivityState state)
    {
        _states[backendRootPath] = Clone(state);
    }

    private static DesktopActivityState Clone(DesktopActivityState state)
    {
        return new DesktopActivityState
        {
            Version = state.Version,
            QqRecentActivities = state.QqRecentActivities.Select(CloneItem).ToList(),
            WechatRecentActivities = state.WechatRecentActivities.Select(CloneItem).ToList(),
            SelectedQqEventKey = state.SelectedQqEventKey,
            SelectedWechatEventKey = state.SelectedWechatEventKey,
            PinSelectedQqActivity = state.PinSelectedQqActivity,
            PinSelectedWechatActivity = state.PinSelectedWechatActivity,
            ShowOnlyQqFailures = state.ShowOnlyQqFailures,
            ShowOnlyWechatFailures = state.ShowOnlyWechatFailures
        };
    }

    private static BackendRecentActivityItem CloneItem(BackendRecentActivityItem item)
    {
        return new BackendRecentActivityItem
        {
            EventKey = item.EventKey,
            CapturedAt = item.CapturedAt,
            EventType = item.EventType,
            Summary = item.Summary,
            Meta = item.Meta,
            Detail = item.Detail,
            IsFailure = item.IsFailure
        };
    }
}

sealed class FakeNotifyIconHost : INotifyIconHost
{
    public event EventHandler? DoubleClick;

    public bool Visible { get; set; } = true;

    public string Text { get; set; } = "QQ AI Bot";

    public string BalloonTipTitle { get; set; } = string.Empty;

    public string BalloonTipText { get; set; } = string.Empty;

    public System.Windows.Forms.ToolTipIcon BalloonTipIcon { get; set; }

    public int ShowBalloonTipCallCount { get; private set; }

    public void ShowBalloonTip(int timeout)
    {
        ShowBalloonTipCallCount += 1;
    }

    public void RaiseDoubleClick()
    {
        DoubleClick?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}

sealed record DesktopUiTestContext(
    string RootPath,
    FakeBackendControlApiService FakeBackend,
    FakeLocalConfigFallbackReader FakeLocalFallbackReader,
    FakeAutoStartService FakeAutoStart,
    FakeBotProcessService FakeBotProcess,
    FakeActivityStateStore FakeActivityStateStore);
