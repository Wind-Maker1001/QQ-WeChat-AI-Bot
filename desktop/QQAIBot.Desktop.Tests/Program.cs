using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.ObjectModel;
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
await RunTestAsync("BackendExecutionProjectionFormatter formats direct and degraded deliberation projections", TestBackendExecutionProjectionFormatterAsync);
await RunTestAsync("BackendLlmProjectionFormatter formats request and failure details", TestBackendLlmProjectionFormatterAsync);
await RunTestAsync("BackendActivityProjectionFormatter formats summaries and timelines", TestBackendActivityProjectionFormatterAsync);
await RunTestAsync("BackendRecentActivityProjector updates order, selection, and de-duplicates replayed events", TestBackendRecentActivityProjectorAsync);
await RunTestAsync("BackendRecentActivityViewStateHelper filters items and resolves visible selection", TestBackendRecentActivityViewStateHelperAsync);
await RunTestAsync("BackendRecentActivityCoordinator composes runtime update, restore, and filter selection", TestBackendRecentActivityCoordinatorAsync);
await RunTestAsync("BackendRuntimeSnapshotCoordinator projects runtime status and activity restore state", TestBackendRuntimeSnapshotCoordinatorAsync);
await RunTestAsync("BackendRuntimeSnapshotViewHelper replaces recent activities and enumerates snapshot property names", TestBackendRuntimeSnapshotViewHelperAsync);
await RunTestAsync("BackendControlApiStatusPollCoordinator evaluates unreachable, unauthorized, and success transitions", TestBackendControlApiStatusPollCoordinatorAsync);
await RunTestAsync("BackendControlApiStatusWaiter waits through unreachable retries and stops on immediate failures", TestBackendControlApiStatusWaiterAsync);
await RunTestAsync("BackendControlApiRecoveryCoordinator handles recovered, started, and aborted recovery paths", TestBackendControlApiRecoveryCoordinatorAsync);
await RunTestAsync("BackendControlPlaneCoordinator handles load and save recovery paths", TestBackendControlPlaneCoordinatorAsync);
await RunTestAsync("BackendRuntimeControlCoordinator handles start and stop branches", TestBackendRuntimeControlCoordinatorAsync);
await RunTestAsync("BackendControlPlaneFacade composes load, save, start, and stop entry points", TestBackendControlPlaneFacadeAsync);
await RunTestAsync("DesktopControlPlaneFeedback applies outcomes and errors to shell callbacks", TestDesktopControlPlaneFeedbackAsync);
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
await RunTestAsync("MainViewModel keeps edited BOT_SYSTEM_PROMPT when save response omits it", TestMainViewModelPreservesEditedBotSystemPromptWhenSaveResponseOmitsItAsync);
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
            BotSystemPrompt = "Base prompt line 1\nBase prompt line 2",
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
    AssertContains(envText, "BOT_SYSTEM_PROMPT=Base prompt line 1\\nBase prompt line 2", "Saved env should contain bot system prompt.");

    var loadedDocument = await reader.LoadAsync(rootPath);
    AssertEqual("Base prompt line 1\nBase prompt line 2", loadedDocument.Config.BotSystemPrompt, "Bot system prompt should round-trip.");
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
                                    executionKind = BackendExecutionProjectionTags.DirectKind,
                                    executionSummary = BackendExecutionProjectionTags.DirectKind,
                                    executionProjection = new
                                    {
                                        kind = BackendExecutionProjectionTags.DirectKind,
                                        summary = BackendExecutionProjectionTags.DirectKind,
                                        stages = new[] { BackendExecutionProjectionTags.DirectStage },
                                        failedStage = "",
                                        completedStages = new[] { BackendExecutionProjectionTags.DirectStage },
                                        degraded = false,
                                        recoveries = Array.Empty<string>()
                                    },
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
    AssertEqual(BackendExecutionProjectionTags.DirectKind, status.LastQqLlmRequest?.ExecutionKind ?? string.Empty, "LastQqLlmRequest execution kind should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectKind, status.LastQqLlmRequest?.ExecutionSummary ?? string.Empty, "LastQqLlmRequest execution summary should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectKind, status.LastQqLlmRequest?.ExecutionProjection?.Kind ?? string.Empty, "LastQqLlmRequest execution projection kind should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectStage, status.LastQqLlmRequest?.ExecutionProjection?.Stages?.FirstOrDefault() ?? string.Empty, "LastQqLlmRequest execution projection stages should deserialize from camelCase.");
    AssertEqual(string.Empty, status.LastQqLlmRequest?.ExecutionProjection?.FailedStage ?? string.Empty, "LastQqLlmRequest execution projection failed stage should deserialize from camelCase.");
    AssertEqual(BackendExecutionProjectionTags.DirectStage, status.LastQqLlmRequest?.ExecutionProjection?.CompletedStages?.FirstOrDefault() ?? string.Empty, "LastQqLlmRequest execution projection completed stages should deserialize from camelCase.");
    AssertFalse(status.LastQqLlmRequest?.ExecutionProjection?.Degraded == true, "LastQqLlmRequest execution projection degraded flag should deserialize from camelCase.");
    AssertEqual(0, status.LastQqLlmRequest?.ExecutionProjection?.Recoveries?.Length ?? 0, "LastQqLlmRequest execution projection recoveries should deserialize from camelCase.");
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

Task TestBackendExecutionProjectionFormatterAsync()
{
    var directText = BackendExecutionProjectionFormatter.Format(
        new BackendExecutionProjection
        {
            Kind = BackendExecutionProjectionTags.DirectKind,
            Summary = BackendExecutionProjectionTags.DirectKind,
            Stages = [BackendExecutionProjectionTags.DirectStage],
            CompletedStages = [BackendExecutionProjectionTags.DirectStage]
        },
        BackendExecutionProjectionTags.DirectKind,
        BackendExecutionProjectionTags.DirectKind);

    AssertEqual("execution=direct", directText, "Direct execution formatting should suppress redundant completed-path details.");

    var degradedText = BackendExecutionProjectionFormatter.Format(
        new BackendExecutionProjection
        {
            Kind = BackendExecutionProjectionTags.DeliberationKind,
            Summary = BackendExecutionProjectionTags.DeliberationSummary,
            Stages = BackendExecutionProjectionTags.DeliberationStages,
            CompletedStages =
            [
                BackendExecutionProjectionTags.PlannerStage,
                BackendExecutionProjectionTags.DraftStage
            ],
            Degraded = true,
            Recoveries = [BackendExecutionProjectionTags.RewriteFallbackToDraftRecovery]
        },
        BackendExecutionProjectionTags.DeliberationKind,
        BackendExecutionProjectionTags.DeliberationSummary);

    AssertContains(degradedText, "execution=deliberation", "Deliberation formatting should include execution kind.");
    AssertContains(degradedText, "stages=planner->draft->rewrite", "Deliberation formatting should include planned stage path.");
    AssertContains(degradedText, "completed=planner->draft", "Deliberation formatting should include actual completed stages when degraded.");
    AssertContains(degradedText, "degraded=yes", "Deliberation formatting should mark degraded success.");
    AssertContains(degradedText, "recoveries=rewrite-fallback-to-draft", "Deliberation formatting should include recovery tags.");
    return Task.CompletedTask;
}

Task TestBackendLlmProjectionFormatterAsync()
{
    var requestDetail = BackendLlmProjectionFormatter.FormatRequestDetail(
        new BackendLlmRequestStatus
        {
            Route = "advanced",
            RouteReason = "directive:/gpt",
            MatchedPrefix = "/gpt",
            CapturedAt = "2026-03-24T00:00:01.000Z",
            EffectiveReasoningEffort = "high",
            EffectiveTextVerbosity = "high",
            EffectiveTools = ["web_search", "code_interpreter"],
            ImageCount = 1,
            ExecutionProjection = new BackendExecutionProjection
            {
                Kind = BackendExecutionProjectionTags.DeliberationKind,
                Summary = BackendExecutionProjectionTags.DeliberationSummary,
                Stages = BackendExecutionProjectionTags.DeliberationStages,
                CompletedStages = [BackendExecutionProjectionTags.PlannerStage, BackendExecutionProjectionTags.DraftStage],
                Degraded = true,
                Recoveries = [BackendExecutionProjectionTags.RewriteFallbackToDraftRecovery]
            },
            DecisionSummary = new BackendDecisionSummary
            {
                Trigger = new BackendDecisionTrigger
                {
                    Kind = "directive",
                    MatchedPrefix = "/gpt"
                },
                ReasonGroups = new BackendDecisionReasonGroups
                {
                    TriggerReasons = ["directive:/gpt"],
                    CapabilityReasons = [],
                    UpgradeReasons = []
                }
            }
        });

    AssertContains(requestDetail, "trigger=directive:/gpt", "Request detail should include decision trigger.");
    AssertContains(requestDetail, "execution=deliberation", "Request detail should include execution kind.");
    AssertContains(requestDetail, "completed=planner->draft", "Request detail should include completed stages when degraded.");
    AssertContains(requestDetail, "recoveries=rewrite-fallback-to-draft", "Request detail should include recovery tags.");

    var failureDetail = BackendLlmProjectionFormatter.FormatFailureDetail(
        new BackendLlmFailureStatus
        {
            Route = "advanced",
            MatchedPrefix = "/gpt",
            Error = "provider rejected request",
            CapturedAt = "2026-03-24T00:00:03.000Z",
            ExecutionProjection = new BackendExecutionProjection
            {
                Kind = BackendExecutionProjectionTags.DeliberationKind,
                Summary = BackendExecutionProjectionTags.DeliberationSummary,
                Stages = BackendExecutionProjectionTags.DeliberationStages,
                FailedStage = BackendExecutionProjectionTags.DraftStage,
                CompletedStages = [BackendExecutionProjectionTags.PlannerStage]
            },
            DecisionSummary = new BackendDecisionSummary
            {
                Trigger = new BackendDecisionTrigger
                {
                    Kind = "directive",
                    MatchedPrefix = "/gpt"
                },
                ReasonGroups = new BackendDecisionReasonGroups
                {
                    CapabilityReasons = ["web_search"],
                    UpgradeReasons = ["capability_upgrade"]
                }
            }
        });

    AssertContains(failureDetail, "trigger=directive:/gpt", "Failure detail should include decision trigger.");
    AssertContains(failureDetail, "failed_stage=draft", "Failure detail should include failed stage.");
    AssertContains(failureDetail, "capability=web_search", "Failure detail should include capability reasons.");
    AssertContains(failureDetail, "upgrade=capability_upgrade", "Failure detail should include upgrade reasons.");
    AssertContains(failureDetail, "error=provider rejected request", "Failure detail should include backend error.");
    return Task.CompletedTask;
}

Task TestBackendActivityProjectionFormatterAsync()
{
    var request = new BackendLlmRequestStatus
    {
        Route = "advanced",
        Model = "gpt-5.4",
        EffectiveApiStyle = "responses",
        CapturedAt = "2026-03-24T00:00:05.000Z"
    };
    var failure = new BackendLlmFailureStatus
    {
        Route = "advanced",
        CapturedAt = "2026-03-24T00:00:03.000Z",
        Error = "provider rejected request"
    };

    AssertEqual(
        "advanced / gpt-5.4 / responses",
        BackendActivityProjectionFormatter.FormatRequestSummary(request, "empty"),
        "Activity formatter should build request summaries.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatActivitySummary(request, failure, "empty"),
        "Latest event: request",
        "Activity formatter should report the latest event.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatRecentActivity(
            [
                new BackendRecentActivityItem
                {
                    EventType = "Request",
                    Summary = "advanced / gpt-5.4 / responses"
                }
            ],
            "empty"),
        "Request | advanced / gpt-5.4 / responses",
        "Activity formatter should render recent activity lists.");
    AssertEqual(
        "Recovered after failure",
        BackendActivityProjectionFormatter.FormatActivityState(request, failure),
        "Activity formatter should describe recovered state.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatLatestSuccess(request),
        "Success |",
        "Activity formatter should render latest success checkpoint.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatLatestFailure(failure),
        "Failure |",
        "Activity formatter should render latest failure checkpoint.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatRecoveryState(request, failure),
        "Recovery |",
        "Activity formatter should render recovery checkpoint.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatRequestTimeline(request),
        "Request |",
        "Activity formatter should render request timeline.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatFailureSummary(failure, "empty"),
        "advanced /",
        "Activity formatter should render failure summary.");
    AssertContains(
        BackendActivityProjectionFormatter.FormatFailureTimeline(failure),
        "Failure |",
        "Activity formatter should render failure timeline.");
    return Task.CompletedTask;
}

Task TestBackendRecentActivityProjectorAsync()
{
    var existingItem = new BackendRecentActivityItem
    {
        EventKey = "existing",
        CapturedAt = "2026-03-24T00:00:01.000Z",
        EventType = "Request",
        Summary = "existing item",
        Meta = "2026-03-24 08:00:01",
        Detail = "existing detail"
    };
    var request = new BackendLlmRequestStatus
    {
        Route = "advanced",
        Model = "gpt-5.4",
        EffectiveApiStyle = "responses",
        CapturedAt = "2026-03-24T00:00:04.000Z",
        ResponseId = "resp-1"
    };
    var failure = new BackendLlmFailureStatus
    {
        Route = "advanced",
        Error = "provider rejected request",
        CapturedAt = "2026-03-24T00:00:05.000Z"
    };

    var pinnedResult = BackendRecentActivityProjector.Project(
        request,
        failure,
        [existingItem],
        lastRequestEventKey: string.Empty,
        lastFailureEventKey: string.Empty,
        isPinned: true,
        selectedItem: existingItem);

    AssertEqual(3, pinnedResult.Items.Count, "Recent activity projector should append new request and failure events.");
    AssertEqual("Failure", pinnedResult.Items[0].EventType, "Newest failure should be inserted first.");
    AssertEqual("Request", pinnedResult.Items[1].EventType, "New request should remain ahead of older history.");
    AssertEqual(existingItem, pinnedResult.SelectedItem, "Pinned selection should stay on the existing item.");

    var unpinnedReplayResult = BackendRecentActivityProjector.Project(
        request,
        failure,
        pinnedResult.Items,
        pinnedResult.LastRequestEventKey,
        pinnedResult.LastFailureEventKey,
        isPinned: false,
        selectedItem: existingItem);

    AssertEqual(3, unpinnedReplayResult.Items.Count, "Replayed request/failure events should not be duplicated.");
    AssertEqual(existingItem, unpinnedReplayResult.SelectedItem, "Without new events, replay should not disturb the current selection.");
    return Task.CompletedTask;
}

Task TestBackendRecentActivityViewStateHelperAsync()
{
    var requestItem = new BackendRecentActivityItem
    {
        EventKey = "request-1",
        EventType = "Request",
        Summary = "request"
    };
    var failureItem = new BackendRecentActivityItem
    {
        EventKey = "failure-1",
        EventType = "Failure",
        Summary = "failure",
        IsFailure = true
    };
    var items = new[] { requestItem, failureItem };

    AssertTrue(
        BackendRecentActivityViewStateHelper.ShouldInclude(requestItem, failuresOnly: false),
        "View-state helper should include requests when failures-only is disabled.");
    AssertFalse(
        BackendRecentActivityViewStateHelper.ShouldInclude(requestItem, failuresOnly: true),
        "View-state helper should hide requests when failures-only is enabled.");
    AssertTrue(
        BackendRecentActivityViewStateHelper.ShouldInclude(failureItem, failuresOnly: true),
        "View-state helper should include failures when failures-only is enabled.");

    AssertEqual(
        requestItem,
        BackendRecentActivityViewStateHelper.ResolveVisibleSelection(items, requestItem, failuresOnly: false),
        "Visible selected item should be retained.");
    AssertEqual(
        failureItem,
        BackendRecentActivityViewStateHelper.ResolveVisibleSelection(items, requestItem, failuresOnly: true),
        "When the selected item becomes hidden, selection should fall back to the first visible item.");
    AssertEqual(
        null,
        BackendRecentActivityViewStateHelper.ResolveVisibleSelection([requestItem], requestItem, failuresOnly: true),
        "When no items remain visible, selection should clear.");
    return Task.CompletedTask;
}

Task TestBackendRecentActivityCoordinatorAsync()
{
    var request = new BackendLlmRequestStatus
    {
        Route = "advanced",
        Model = "gpt-5.4",
        EffectiveApiStyle = "responses",
        CapturedAt = "2026-03-24T00:00:04.000Z",
        ResponseId = "resp-1"
    };
    var failure = new BackendLlmFailureStatus
    {
        Route = "advanced",
        Error = "provider rejected request",
        CapturedAt = "2026-03-24T00:00:05.000Z"
    };

    var runtimeProjection = BackendRecentActivityCoordinator.ProjectRuntimeUpdate(
        request,
        failure,
        existingItems: [],
        lastRequestEventKey: string.Empty,
        lastFailureEventKey: string.Empty,
        isPinned: false,
        selectedItem: null);

    AssertEqual(2, runtimeProjection.Items.Count, "Coordinator should surface projected runtime activity items.");
    AssertEqual("Failure", runtimeProjection.Items[0].EventType, "Coordinator should preserve newest-first ordering from the projector.");
    AssertEqual("Request", runtimeProjection.Items[1].EventType, "Coordinator should retain the request event after the failure.");

    var policy = new DesktopActivityStatePolicy();
    var restoredSelection = BackendRecentActivityCoordinator.ResolveSelectionAfterRestore(
        policy,
        runtimeProjection.Items,
        runtimeProjection.Items[1].EventKey,
        failuresOnly: true);

    AssertEqual(
        runtimeProjection.Items[0].EventKey,
        restoredSelection?.EventKey ?? string.Empty,
        "Coordinator should re-resolve restored selection through current filter visibility.");

    var visibleSelection = BackendRecentActivityCoordinator.ResolveSelectionAfterFilterChange(
        runtimeProjection.Items,
        runtimeProjection.Items[1],
        failuresOnly: true);

    AssertEqual(
        runtimeProjection.Items[0].EventKey,
        visibleSelection?.EventKey ?? string.Empty,
        "Coordinator should fall back to the first visible item after a filter change.");
    return Task.CompletedTask;
}

Task TestBackendRuntimeSnapshotCoordinatorAsync()
{
    var status = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        RuntimeReady = true,
        WorkerProcessId = 321,
        WechatWorkerProcessId = 654,
        WechatConfigured = true,
        WechatRuntimeActive = true,
        WechatRuntimeReady = false,
        WechatBridgeConnected = true,
        LastQqLlmRequest = new BackendLlmRequestStatus
        {
            Route = "advanced",
            Model = "gpt-5.4",
            EffectiveApiStyle = "responses",
            CapturedAt = "2026-03-24T00:00:04.000Z",
            ResponseId = "resp-1"
        },
        LastWechatLlmFailure = new BackendLlmFailureStatus
        {
            Route = "advanced",
            Error = "provider rejected request",
            CapturedAt = "2026-03-24T00:00:05.000Z"
        }
    };

    var runtimeProjection = BackendRuntimeSnapshotCoordinator.ProjectRuntimeStatus(
        status,
        controlApiReachable: true,
        new BackendChannelActivityContext([], string.Empty, string.Empty, false, null),
        new BackendChannelActivityContext([], string.Empty, string.Empty, false, null));

    AssertTrue(runtimeProjection.SnapshotState.RuntimeActive == true, "Runtime snapshot coordinator should project runtime-active state.");
    AssertEqual(321, runtimeProjection.SnapshotState.WorkerProcessId ?? 0, "Runtime snapshot coordinator should project QQ worker PID.");
    AssertEqual(654, runtimeProjection.SnapshotState.WechatWorkerProcessId ?? 0, "Runtime snapshot coordinator should project Wechat worker PID.");
    AssertEqual(1, runtimeProjection.QqActivity.Items.Count, "Runtime snapshot coordinator should project QQ activity updates.");
    AssertEqual(1, runtimeProjection.WechatActivity.Items.Count, "Runtime snapshot coordinator should project Wechat activity updates.");
    AssertEqual("Request", runtimeProjection.QqActivity.Items[0].EventType, "QQ activity projection should retain request event type.");
    AssertEqual("Failure", runtimeProjection.WechatActivity.Items[0].EventType, "Wechat activity projection should retain failure event type.");
    AssertTrue(runtimeProjection.SnapshotState.ControlApiReachable == true, "Runtime snapshot coordinator should retain control API reachability.");

    var restoreProjection = BackendRuntimeSnapshotCoordinator.ProjectActivityRestore(
        new DesktopActivityStatePolicy(),
        new DesktopActivityState
        {
            QqRecentActivities =
            [
                new BackendRecentActivityItem
                {
                    EventKey = "request-1",
                    EventType = "Request",
                    Summary = "request"
                },
                new BackendRecentActivityItem
                {
                    EventKey = "failure-1",
                    EventType = "Failure",
                    Summary = "failure",
                    IsFailure = true
                }
            ],
            SelectedQqEventKey = "request-1",
            ShowOnlyQqFailures = true
        });

    AssertTrue(restoreProjection.ShowOnlyQqFailures, "Activity restore projection should retain failure-only filter state.");
    AssertEqual("failure-1", restoreProjection.SelectedQqRecentActivity?.EventKey ?? string.Empty, "Activity restore projection should resolve selection through current filter visibility.");
    return Task.CompletedTask;
}

Task TestBackendControlApiStatusPollCoordinatorAsync()
{
    var previousSnapshot = new BackendRuntimeSnapshotViewState
    {
        ControlApiReachable = true,
        RuntimeActive = true,
        WorkerProcessId = 100,
        WechatWorkerProcessId = 200
    };

    var unreachableOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
        previousSnapshot,
        new BackendControlApiPollState(),
        status: null,
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        recoveryAttemptThreshold: 2,
        outageNotificationThreshold: 3);

    AssertFalse(unreachableOutcome.ShouldApplyRuntimeStatus, "Unreachable status failure should not apply runtime status.");
    AssertFalse(unreachableOutcome.ShouldAttemptRecovery, "First unreachable failure should not trigger recovery.");
    AssertFalse(unreachableOutcome.NextRuntimeSnapshot.ControlApiReachable == true, "Unreachable status failure should mark control API as unreachable.");
    AssertEqual(1, unreachableOutcome.NextPollState.ConsecutiveFailures, "Unreachable status failure should increment consecutive failures.");

    var unauthorizedOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
        previousSnapshot,
        new BackendControlApiPollState(),
        status: null,
        new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        recoveryAttemptThreshold: 2,
        outageNotificationThreshold: 3);

    AssertFalse(unauthorizedOutcome.ShouldApplyRuntimeStatus, "Unauthorized status failure should not apply runtime status.");
    AssertEqual(true, unauthorizedOutcome.NextRuntimeSnapshot.ControlApiReachable, "Unauthorized status failure should still treat the control API as reachable.");
    AssertTrue(unauthorizedOutcome.NextPollState.UnauthorizedNotified, "Unauthorized status failure should set the unauthorized notification flag.");
    AssertTrue(unauthorizedOutcome.LogMessages.Any(static message => message.Contains("authentication failed", StringComparison.Ordinal)), "Unauthorized status failure should emit the authentication guidance log.");

    var successOutcome = BackendControlApiStatusPollCoordinator.Evaluate(
        new BackendRuntimeSnapshotViewState
        {
            ControlApiReachable = false,
            RuntimeActive = true,
            WorkerProcessId = 100,
            WechatWorkerProcessId = 200
        },
        new BackendControlApiPollState
        {
            ConsecutiveFailures = 3,
            OutageNotified = true
        },
        new BackendRuntimeStatus
        {
            RuntimeActive = false,
            WorkerProcessId = 101,
            WechatWorkerProcessId = 201
        },
        new BackendControlApiFailure(),
        recoveryAttemptThreshold: 2,
        outageNotificationThreshold: 3);

    AssertTrue(successOutcome.ShouldApplyRuntimeStatus, "Successful status poll should apply runtime status.");
    AssertEqual(0, successOutcome.NextPollState.ConsecutiveFailures, "Successful status poll should reset control API failure counters.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("became reachable again", StringComparison.Ordinal)), "Successful status poll should log control API recovery.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("Worker restarted", StringComparison.Ordinal)), "Successful status poll should log QQ worker restart.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("Wechat worker restarted", StringComparison.Ordinal)), "Successful status poll should log Wechat worker restart.");
    AssertTrue(successOutcome.LogMessages.Any(static message => message.Contains("Runtime became inactive", StringComparison.Ordinal)), "Successful status poll should log runtime-stop transition.");
    AssertEqual(4, successOutcome.Notifications.Count, "Successful status poll should emit recovery and restart/runtime notifications.");
    return Task.CompletedTask;
}

async Task TestBackendControlApiStatusWaiterAsync()
{
    var callCount = 0;
    var lastFailure = new BackendControlApiFailure
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };

    var recoveredStatus = await BackendControlApiStatusWaiter.WaitForStatusAsync(
        tryGetStatusAsync: (_cancellationToken) =>
        {
            callCount += 1;
            return Task.FromResult(callCount >= 3
                ? new BackendRuntimeStatus
                {
                    RuntimeActive = true
                }
                : null as BackendRuntimeStatus);
        },
        getLastFailure: () => lastFailure,
        maxAttempts: 5,
        retryDelay: TimeSpan.Zero,
        delayAsync: static (_delay, _token) => Task.CompletedTask);

    AssertTrue(recoveredStatus?.RuntimeActive == true, "Status waiter should return the first recovered runtime status.");
    AssertEqual(3, callCount, "Status waiter should retry until the control API becomes reachable.");

    var immediateFailureWaitCallCount = 0;
    var immediateFailure = await BackendControlApiStatusWaiter.WaitForStatusAsync(
        tryGetStatusAsync: (_cancellationToken) =>
        {
            immediateFailureWaitCallCount += 1;
            return Task.FromResult<BackendRuntimeStatus?>(null);
        },
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        maxAttempts: 5,
        retryDelay: TimeSpan.Zero,
        delayAsync: static (_delay, _token) => Task.CompletedTask);

    AssertEqual(null, immediateFailure, "Status waiter should stop immediately on non-unreachable failures.");
    AssertEqual(1, immediateFailureWaitCallCount, "Status waiter should not keep retrying after an immediate failure.");
}

async Task TestBackendControlApiRecoveryCoordinatorAsync()
{
    var existingStatus = new BackendRuntimeStatus
    {
        ControlApiUrl = "http://127.0.0.1:3199"
    };
    var recoveredOutcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
        "status-poll",
        prepareAsync: () => Task.CompletedTask,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(existingStatus),
        getLastFailure: () => new BackendControlApiFailure(),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => false,
        startProcess: static () => throw new InvalidOperationException("Should not start process when status is already reachable."),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        detachProcess: static () => { });

    AssertEqual(existingStatus, recoveredOutcome.RecoveredStatus, "Recovery coordinator should return the already-recovered status.");
    AssertTrue(recoveredOutcome.LogMessages.Any(static message => message.Contains("recovered before local restart", StringComparison.Ordinal)), "Recovery coordinator should log when recovery happens before local restart.");

    var startedStatus = new BackendRuntimeStatus
    {
        ControlApiUrl = "http://127.0.0.1:3200"
    };
    var startCallCount = 0;
    var detachCallCount = 0;
    var startedOutcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
        "save-config",
        prepareAsync: () => Task.CompletedTask,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => false,
        startProcess: () => startCallCount += 1,
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(startedStatus),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        detachProcess: () => detachCallCount += 1);

    AssertEqual(startedStatus, startedOutcome.RecoveredStatus, "Recovery coordinator should return the started status when /start succeeds.");
    AssertEqual(1, startCallCount, "Recovery coordinator should start the local backend when it is not already running.");
    AssertEqual(1, detachCallCount, "Recovery coordinator should detach the local backend launcher after successful recovery.");
    AssertTrue(startedOutcome.LogMessages.Any(static message => message.Contains("Started local backend host", StringComparison.Ordinal)), "Recovery coordinator should log local backend startup.");
    AssertTrue(startedOutcome.LogMessages.Any(static message => message.Contains("recovery succeeded", StringComparison.Ordinal)), "Recovery coordinator should log recovery success.");

    var abortedOutcome = await BackendControlApiRecoveryCoordinator.TryRecoverAsync(
        "load-config",
        prepareAsync: () => Task.CompletedTask,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unauthorized,
            Message = "Control API authentication failed."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        startProcess: static () => throw new InvalidOperationException("Should not start process for immediate failures."),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        detachProcess: static () => { });

    AssertEqual(null, abortedOutcome.RecoveredStatus, "Recovery coordinator should abort on immediate failures.");
    AssertTrue(abortedOutcome.LogMessages.Any(static message => message.Contains("recovery aborted", StringComparison.Ordinal)), "Recovery coordinator should log recovery aborts.");
}

async Task TestBackendControlPlaneCoordinatorAsync()
{
    var recoveredConfig = new BackendControlConfigResponse
    {
        WechatBotPrefix = "/ai"
    };
    var recoveredStatus = new BackendRuntimeStatus
    {
        RuntimeActive = true
    };
    var configCallCount = 0;
    var statusCallCount = 0;
    var recoverCallCount = 0;
    BackendControlApiFailure lastFailure = new()
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };

    var loadResult = await BackendControlPlaneCoordinator.LoadAuthoritativeConfigAsync(
        tryGetConfigAsync: (_cancellationToken) =>
        {
            configCallCount += 1;
            return Task.FromResult(configCallCount >= 2 ? recoveredConfig : null);
        },
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) =>
        {
            statusCallCount += 1;
            return Task.FromResult(statusCallCount >= 2 ? recoveredStatus : null);
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () =>
        {
            recoverCallCount += 1;
            lastFailure = new BackendControlApiFailure();
            return Task.CompletedTask;
        });

    AssertEqual(recoveredConfig, loadResult.ApiConfig, "Control-plane coordinator should retry config load after recovery.");
    AssertEqual(recoveredStatus, loadResult.ApiStatus, "Control-plane coordinator should return the recovered status.");
    AssertEqual(1, recoverCallCount, "Control-plane coordinator should trigger recovery exactly once for unreachable config loads.");

    var saveCallCount = 0;
    var saveRecoverCallCount = 0;
    lastFailure = new BackendControlApiFailure
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };

    var saveResult = await BackendControlPlaneCoordinator.SaveThroughControlApiAsync(
        new BotConfig
        {
            WechatBotPrefix = "/wx"
        },
        trySaveConfigAsync: (config, _cancellationToken) =>
        {
            saveCallCount += 1;
            return Task.FromResult(saveCallCount >= 2
                ? new BackendControlConfigResponse
                {
                    WechatBotPrefix = config.WechatBotPrefix
                }
                : null);
        },
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () =>
        {
            saveRecoverCallCount += 1;
            lastFailure = new BackendControlApiFailure();
            return Task.CompletedTask;
        });

    AssertEqual("/wx", saveResult.WechatBotPrefix, "Control-plane coordinator should retry save after recovery.");
    AssertEqual(1, saveRecoverCallCount, "Control-plane coordinator should trigger recovery exactly once for unreachable saves.");
}

async Task TestBackendRuntimeControlCoordinatorAsync()
{
    var attachStatus = new BackendRuntimeStatus
    {
        RuntimeActive = true,
        ControlApiUrl = "http://127.0.0.1:3199"
    };

    var startAttachOutcome = await BackendRuntimeControlCoordinator.StartAsync(
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(attachStatus),
        getLastFailure: () => new BackendControlApiFailure(),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        startProcess: static () => throw new InvalidOperationException("Should not start local backend when control API is already reachable."),
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null));

    AssertEqual(attachStatus, startAttachOutcome.AppliedStatus, "Runtime control coordinator should attach to the existing backend host.");
    AssertTrue(startAttachOutcome.ShouldDetachProcess, "Attach flow should detach the local launcher.");
    AssertTrue(startAttachOutcome.ShouldReloadConfig, "Attach flow should request a config reload.");

    BackendControlApiFailure lastFailure = new()
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };
    var startProcessCallCount = 0;
    var waitOutcome = await BackendRuntimeControlCoordinator.StartAsync(
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => lastFailure,
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        startProcess: () => startProcessCallCount += 1,
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null));

    AssertEqual(1, startProcessCallCount, "Runtime control coordinator should start the local backend when control API is unreachable.");
    AssertEqual("Backend started, waiting for control API", waitOutcome.StatusText, "Runtime control coordinator should expose waiting status when control API does not come up yet.");
    AssertFalse(waitOutcome.ControlApiReachable, "Waiting start outcome should mark control API as not yet reachable.");

    var stopWithApiStatus = new BackendRuntimeStatus
    {
        RuntimeActive = false,
        ControlApiUrl = "http://127.0.0.1:3199"
    };
    var stopApiOutcome = await BackendRuntimeControlCoordinator.StopAsync(
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(stopWithApiStatus),
        getLastFailure: () => new BackendControlApiFailure(),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        stopProcessAsync: () => Task.CompletedTask);

    AssertEqual(stopWithApiStatus, stopApiOutcome.AppliedStatus, "Runtime control coordinator should apply stop status returned by the control API.");
    AssertEqual("Backend stopped", stopApiOutcome.StatusText, "Runtime control coordinator should report stopped status from control API response.");
    AssertEqual(1, stopApiOutcome.Notifications.Count, "Control API stop should emit one tray notification.");

    var localStopCallCount = 0;
    var stopLocalOutcome = await BackendRuntimeControlCoordinator.StopAsync(
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        stopProcessAsync: () =>
        {
            localStopCallCount += 1;
            return Task.CompletedTask;
        });

    AssertEqual(1, localStopCallCount, "Runtime control coordinator should stop the local backend when control API is unreachable but the launcher still owns the process.");
    AssertEqual("Backend host stopped", stopLocalOutcome.StatusText, "Runtime control coordinator should report local backend shutdown.");

    var unreachableStopOutcome = await BackendRuntimeControlCoordinator.StopAsync(
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => false,
        stopProcessAsync: () => Task.CompletedTask);

    AssertEqual("Backend is not reachable", unreachableStopOutcome.StatusText, "Runtime control coordinator should surface unreachable-stop state when nothing is running locally.");
    AssertTrue(unreachableStopOutcome.LogMessages.Any(static message => message.Contains("no local backend host process is attached", StringComparison.Ordinal)), "Runtime control coordinator should log missing local backend ownership.");
}

async Task TestBackendControlPlaneFacadeAsync()
{
    var prepareCallCount = 0;
    var recoverCallCount = 0;
    BackendControlApiFailure lastFailure = new()
    {
        Kind = BackendControlApiFailureKind.Unreachable,
        Message = "Control API is unreachable."
    };
    var configCallCount = 0;
    var statusCallCount = 0;

    var loadResult = await BackendControlPlaneFacade.LoadAuthoritativeConfigAsync(
        tryGetConfigAsync: (_cancellationToken) =>
        {
            configCallCount += 1;
            return Task.FromResult(configCallCount >= 2
                ? new BackendControlConfigResponse
                {
                    WechatBotPrefix = "/ai"
                }
                : null as BackendControlConfigResponse);
        },
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) =>
        {
            statusCallCount += 1;
            return Task.FromResult(statusCallCount >= 2
                ? new BackendRuntimeStatus
                {
                    RuntimeActive = true
                }
                : null as BackendRuntimeStatus);
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () =>
        {
            recoverCallCount += 1;
            lastFailure = new BackendControlApiFailure();
            return Task.CompletedTask;
        });

    AssertEqual("/ai", loadResult.ApiConfig?.WechatBotPrefix ?? string.Empty, "Facade load should flow through control-plane load coordination.");
    AssertEqual(1, recoverCallCount, "Facade load should invoke recovery when needed.");

    var savePrepareCallCount = 0;
    lastFailure = new BackendControlApiFailure();
    var saveResult = await BackendControlPlaneFacade.SaveConfigAsync(
        prepareAsync: () =>
        {
            savePrepareCallCount += 1;
            return Task.CompletedTask;
        },
        config: new BotConfig
        {
            WechatBotPrefix = "/wx"
        },
        trySaveConfigAsync: (config, _cancellationToken) => Task.FromResult<BackendControlConfigResponse?>(
            new BackendControlConfigResponse
            {
                WechatBotPrefix = config.WechatBotPrefix
            }),
        getLastFailure: () => lastFailure,
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        tryRecoverControlApiAsync: () => Task.CompletedTask);

    AssertEqual(1, savePrepareCallCount, "Facade save should run preflight prepare step.");
    AssertEqual("/wx", saveResult.WechatBotPrefix, "Facade save should flow through control-plane save coordination.");

    var startPrepareCallCount = 0;
    var startProcessCallCount = 0;
    var startOutcome = await BackendControlPlaneFacade.StartBackendAsync(
        prepareAsync: () =>
        {
            startPrepareCallCount += 1;
            return Task.CompletedTask;
        },
        tryGetStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        startProcess: () => startProcessCallCount += 1,
        waitForStatusAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        tryStartAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null));

    AssertEqual(1, startPrepareCallCount, "Facade start should run preflight prepare step.");
    AssertEqual(1, startProcessCallCount, "Facade start should pass through runtime start coordination.");
    AssertEqual("Backend started, waiting for control API", startOutcome.StatusText, "Facade start should preserve waiting-for-control-api outcome.");

    var stopPrepareCallCount = 0;
    var stopLocalCallCount = 0;
    var stopOutcome = await BackendControlPlaneFacade.StopBackendAsync(
        prepareAsync: () =>
        {
            stopPrepareCallCount += 1;
            return Task.CompletedTask;
        },
        tryStopAsync: (_cancellationToken) => Task.FromResult<BackendRuntimeStatus?>(null),
        getLastFailure: () => new BackendControlApiFailure
        {
            Kind = BackendControlApiFailureKind.Unreachable,
            Message = "Control API is unreachable."
        },
        isImmediateFailure: static failure => failure.Kind is BackendControlApiFailureKind.Rejected or BackendControlApiFailureKind.Unauthorized or BackendControlApiFailureKind.Unknown,
        isProcessRunning: static () => true,
        stopProcessAsync: () =>
        {
            stopLocalCallCount += 1;
            return Task.CompletedTask;
        });

    AssertEqual(1, stopPrepareCallCount, "Facade stop should run preflight prepare step.");
    AssertEqual(1, stopLocalCallCount, "Facade stop should pass through runtime stop coordination.");
    AssertEqual("Backend host stopped", stopOutcome.StatusText, "Facade stop should preserve local-stop outcome.");
}

Task TestDesktopControlPlaneFeedbackAsync()
{
    var statusText = string.Empty;
    var logs = new List<string>();
    var notifications = new List<TrayNotification>();
    var dialogs = new List<(string Title, string Message)>();

    DesktopControlPlaneFeedback.ApplyOutcome(
        statusText: "Backend stopped",
        logMessages: ["Sent stop command via control API: http://127.0.0.1:3199"],
        notifications:
        [
            new TrayNotification
            {
                Title = "QQ AI Bot",
                Message = "Runtime stopped by user."
            }
        ],
        setStatusText: (text) => statusText = text,
        addLog: (message) => logs.Add(message),
        notify: (notification) => notifications.Add(notification));

    AssertEqual("Backend stopped", statusText, "Feedback helper should apply status text for successful outcomes.");
    AssertEqual(1, logs.Count, "Feedback helper should append outcome log messages.");
    AssertEqual(1, notifications.Count, "Feedback helper should forward tray notifications.");

    DesktopControlPlaneFeedback.ApplyError(
        statusText: "Save failed",
        logMessage: "Save config failed: boom",
        showDialog: true,
        dialogTitle: "Save failed",
        dialogMessage: "Save config failed:\nboom",
        setStatusText: (text) => statusText = text,
        addLog: (message) => logs.Add(message),
        showErrorDialog: (title, message) => dialogs.Add((title, message)));

    AssertEqual("Save failed", statusText, "Feedback helper should apply error status text.");
    AssertTrue(logs.Any(static message => message.Contains("Save config failed", StringComparison.Ordinal)), "Feedback helper should append error logs.");
    AssertEqual(1, dialogs.Count, "Feedback helper should trigger the error dialog when requested.");
    AssertEqual("Save failed", dialogs[0].Title, "Feedback helper should pass dialog title through.");
    AssertContains(dialogs[0].Message, "boom", "Feedback helper should pass dialog message through.");
    return Task.CompletedTask;
}

Task TestBackendRuntimeSnapshotViewHelperAsync()
{
    var target = new ObservableCollection<BackendRecentActivityItem>
    {
        new()
        {
            EventKey = "old",
            Summary = "old"
        }
    };

    BackendRuntimeSnapshotViewHelper.ReplaceRecentActivities(
        target,
        [
            new BackendRecentActivityItem
            {
                EventKey = "req-1",
                Summary = "request"
            },
            new BackendRecentActivityItem
            {
                EventKey = "",
                Summary = "should drop"
            }
        ]);

    AssertEqual(1, target.Count, "Runtime snapshot view helper should replace the collection and drop invalid activity items.");
    AssertEqual("req-1", target[0].EventKey, "Runtime snapshot view helper should preserve valid event keys.");

    var notifiedProperties = new List<string>();
    BackendRuntimeSnapshotViewHelper.NotifyRuntimeSnapshotChanged((propertyName) => notifiedProperties.Add(propertyName));

    AssertTrue(notifiedProperties.Contains(nameof(MainViewModel.LatestQqLlmDetailText)), "Runtime snapshot notifier should include QQ detail properties.");
    AssertTrue(notifiedProperties.Contains(nameof(MainViewModel.LatestWechatLlmDetailText)), "Runtime snapshot notifier should include Wechat detail properties.");
    AssertTrue(notifiedProperties.Contains(nameof(MainViewModel.SelectedQqRecentActivityDetailText)), "Runtime snapshot notifier should include QQ selected activity detail.");
    AssertTrue(notifiedProperties.Contains(nameof(MainViewModel.SelectedWechatRecentActivityDetailText)), "Runtime snapshot notifier should include Wechat selected activity detail.");
    return Task.CompletedTask;
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
                AssertFalse(defaultBotInstructionsTextBox.IsReadOnly, "System prompt textbox should be editable.");
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
                AssertContains(selectedQqRecentActivityDetailTextBlock.Text, "execution=direct", "Selected QQ recent activity detail should show the execution path.");
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
                AssertContains(selectedWechatRecentActivityDetailTextBlock.Text, "failed_stage=draft", "Selected Wechat recent activity detail should show the failed execution stage.");
                AssertContains(selectedWechatRecentActivityDetailTextBlock.Text, "completed=planner", "Selected Wechat recent activity detail should show completed execution stages.");
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
                AssertContains(viewModel.LatestQqLlmDetailText, "execution=direct", "Latest QQ LLM detail should show the execution path.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "trigger=directive:/gpt", "Latest Wechat LLM detail should prefer structured directive trigger.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "capability=default", "Latest Wechat LLM detail should show structured capability reasons when none are present.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "upgrade=none", "Latest Wechat LLM detail should show structured upgrade reasons when none are present.");
                AssertContains(viewModel.LatestWechatLlmDetailText, "execution=deliberation", "Latest Wechat LLM detail should show the execution path.");
                AssertContains(viewModel.LatestWechatLlmDetailText, $"stages={BackendExecutionProjectionTags.DeliberationSummary}", "Latest Wechat LLM detail should show the deliberation stage path.");
                AssertDoesNotContain(viewModel.LatestWechatLlmDetailText, "degraded=yes", "Latest Wechat LLM detail should not mark a full deliberation success as degraded.");
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
                    ExecutionKind = BackendExecutionProjectionTags.DeliberationKind,
                    ExecutionSummary = BackendExecutionProjectionTags.DeliberationSummary,
                    ExecutionProjection = new BackendExecutionProjection
                    {
                        Kind = BackendExecutionProjectionTags.DeliberationKind,
                        Summary = BackendExecutionProjectionTags.DeliberationSummary,
                        Stages = BackendExecutionProjectionTags.DeliberationStages,
                        FailedStage = "",
                        CompletedStages = [
                            BackendExecutionProjectionTags.PlannerStage,
                            BackendExecutionProjectionTags.DraftStage
                        ],
                        Degraded = true,
                        Recoveries = [BackendExecutionProjectionTags.RewriteFallbackToDraftRecovery]
                    },
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
                AssertContains(viewModel.LatestQqLlmDetailText, $"completed={BackendExecutionProjectionTags.PlannerStage}->{BackendExecutionProjectionTags.DraftStage}", "Latest QQ LLM detail should show partially completed deliberation stages.");
                AssertContains(viewModel.LatestQqLlmDetailText, "degraded=yes", "Latest QQ LLM detail should mark the partial deliberation success as degraded.");
                AssertContains(viewModel.LatestQqLlmDetailText, $"recoveries={BackendExecutionProjectionTags.RewriteFallbackToDraftRecovery}", "Latest QQ LLM detail should show the recovery that produced the partial success.");

                qqFailuresOnlyToggleButton.IsChecked = true;
                wechatFailuresOnlyToggleButton.IsChecked = true;
                await WaitForAsync(() => latestQqRecentActivityListBox.Items.Count == 1, "QQ failures-only filter");
                await WaitForAsync(() => latestWechatRecentActivityListBox.Items.Count == 1, "Wechat failures-only filter");
                AssertContains(selectedQqRecentActivitySummaryTextBlock.Text, "default / search timed out", "QQ failures-only filter should select the failure event.");
                AssertContains(selectedQqRecentActivityDetailTextBlock.Text, "failed_stage=direct", "QQ failures-only filter should surface the failed execution stage.");
                AssertContains(selectedWechatRecentActivitySummaryTextBlock.Text, "advanced / provider rejected request", "Wechat failures-only filter should keep the failure event selected.");
                clearQqActivityHistoryButton.Command.Execute(null);
                await WaitForAsync(() => latestQqRecentActivityListBox.Items.Count == 0, "QQ clear activity history");
                AssertContains(selectedQqRecentActivitySummaryTextBlock.Text, "Select a QQ activity event", "Clearing QQ activity history should clear the selected detail.");
                AssertFalse(qqPinSelectionToggleButton.IsChecked ?? true, "Clearing QQ activity history should reset the pin toggle.");
                AssertTrue(clearWechatActivityHistoryButton.Command.CanExecute(null), "Wechat clear activity history button should be enabled while events exist.");
                AssertEqual("QQ channel ready", runtimeReadyText, "Runtime ready text should reflect runtime status.");
                AssertEqual("Wechat channel ready", wechatRuntimeReadyText, "Wechat runtime ready text should reflect runtime status.");

                wechatPrefixTextBox.Text = "/wx";
                defaultBotInstructionsTextBox.Text = "Base prompt line 1\r\nBase prompt line 2";
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
                await WaitForAsync(
                    () => effectiveBotInstructionsTextBox.Text.Contains("Base prompt line 1", StringComparison.Ordinal),
                    "effective bot system prompt preview update");

                saveButton.Command.Execute(null);
                await WaitForAsync(() => fakeBackend.SaveConfigCallCount == 1, "save command invocation");
                AssertNotNull(fakeBackend.LastSavedConfig, "Saved config payload should be captured.");
                AssertEqual("/wx", fakeBackend.LastSavedConfig!.WechatBotPrefix, "Save should use edited WechatBotPrefix.");
                AssertEqual("Base prompt line 1\r\nBase prompt line 2", fakeBackend.LastSavedConfig.BotSystemPrompt, "Save should use edited BOT_SYSTEM_PROMPT.");
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

async Task TestMainViewModelPreservesEditedBotSystemPromptWhenSaveResponseOmitsItAsync()
{
    var context = await CreateDesktopUiTestContextAsync("desktop-viewmodel-save-bot-system-prompt-");
    var rootPath = context.RootPath;
    var fakeBackend = context.FakeBackend;
    var fakeLocalFallbackReader = context.FakeLocalFallbackReader;
    var fakeAutoStart = context.FakeAutoStart;
    var fakeBotProcess = context.FakeBotProcess;
    var fakeActivityStateStore = context.FakeActivityStateStore;
    var originalCurrentDirectory = Directory.GetCurrentDirectory();

    fakeBackend.OmitBotSystemPromptOnSaveResponse = true;

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
                viewModel.BotSystemPrompt = "Edited prompt line 1\r\nEdited prompt line 2";
                viewModel.SaveCommand.Execute(null);

                await WaitForAsync(
                    () => fakeBackend.SaveConfigCallCount == 1 && !viewModel.HasUnsavedChanges,
                    "save edited bot system prompt");

                AssertNotNull(fakeBackend.LastSavedConfig, "Save should capture edited config payload.");
                AssertEqual(
                    "Edited prompt line 1\r\nEdited prompt line 2",
                    fakeBackend.LastSavedConfig!.BotSystemPrompt,
                    "Save payload should include the edited BOT_SYSTEM_PROMPT.");
                AssertEqual(
                    "Edited prompt line 1\r\nEdited prompt line 2",
                    viewModel.BotSystemPrompt,
                    "View model should preserve the edited BOT_SYSTEM_PROMPT when save response omits it.");
                AssertContains(
                    viewModel.EffectiveBotInstructionsText,
                    "Edited prompt line 1",
                    "Effective bot instructions should continue to show the edited BOT_SYSTEM_PROMPT.");
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
                ExecutionKind = BackendExecutionProjectionTags.DirectKind,
                ExecutionSummary = BackendExecutionProjectionTags.DirectKind,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DirectKind,
                    Summary = BackendExecutionProjectionTags.DirectKind,
                    Stages = [BackendExecutionProjectionTags.DirectStage],
                    FailedStage = "",
                    CompletedStages = [BackendExecutionProjectionTags.DirectStage],
                    Degraded = false,
                    Recoveries = []
                },
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
                ExecutionKind = BackendExecutionProjectionTags.DirectKind,
                ExecutionSummary = BackendExecutionProjectionTags.DirectKind,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DirectKind,
                    Summary = BackendExecutionProjectionTags.DirectKind,
                    Stages = [BackendExecutionProjectionTags.DirectStage],
                    FailedStage = BackendExecutionProjectionTags.DirectStage,
                    CompletedStages = [],
                    Degraded = false,
                    Recoveries = []
                },
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
                ExecutionKind = BackendExecutionProjectionTags.DeliberationKind,
                ExecutionSummary = BackendExecutionProjectionTags.DeliberationSummary,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DeliberationKind,
                    Summary = BackendExecutionProjectionTags.DeliberationSummary,
                    Stages = BackendExecutionProjectionTags.DeliberationStages,
                    FailedStage = "",
                    CompletedStages = BackendExecutionProjectionTags.DeliberationStages,
                    Degraded = false,
                    Recoveries = []
                },
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
                ExecutionKind = BackendExecutionProjectionTags.DeliberationKind,
                ExecutionSummary = BackendExecutionProjectionTags.DeliberationSummary,
                ExecutionProjection = new BackendExecutionProjection
                {
                    Kind = BackendExecutionProjectionTags.DeliberationKind,
                    Summary = BackendExecutionProjectionTags.DeliberationSummary,
                    Stages = BackendExecutionProjectionTags.DeliberationStages,
                    FailedStage = BackendExecutionProjectionTags.DraftStage,
                    CompletedStages = [BackendExecutionProjectionTags.PlannerStage],
                    Degraded = false,
                    Recoveries = []
                },
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
        "BOT_SYSTEM_PROMPT",
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
            $"BOT_SYSTEM_PROMPT={EncodeEnvValue(config.BotSystemPrompt)}",
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
    public bool OmitBotSystemPromptOnSaveResponse { get; set; }

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
            BotSystemPrompt = OmitBotSystemPromptOnSaveResponse ? string.Empty : config.BotSystemPrompt,
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
