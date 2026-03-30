using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopControlPlaneFeedback
{
    public static void ApplyCommandResult(
        DesktopCommandResult result,
        bool showDialog,
        Action<string> setStatusText,
        Action<string> addLog,
        Action<TrayNotification>? notify,
        Action<string, string>? showErrorDialog,
        Action<string>? routeSuggestedAction)
    {
        setStatusText(result.StatusText);
        ForwardLogMessages(result.LogMessages, addLog);
        ForwardNotifications(result.Notifications, notify);

        if (result.Error is not null && showDialog && showErrorDialog is not null)
        {
            showErrorDialog(result.Error.DialogTitle, result.Error.DialogMessage);
        }

        if (!string.IsNullOrWhiteSpace(result.SuggestedHealthActionKey) &&
            routeSuggestedAction is not null)
        {
            routeSuggestedAction(result.SuggestedHealthActionKey);
        }
    }

    public static void ApplyOutcome(
        string statusText,
        IEnumerable<string>? logMessages,
        IEnumerable<TrayNotification>? notifications,
        Action<string> setStatusText,
        Action<string> addLog,
        Action<TrayNotification>? notify)
    {
        setStatusText(statusText);
        ForwardLogMessages(logMessages, addLog);
        ForwardNotifications(notifications, notify);
    }

    public static void ApplyError(
        string statusText,
        string logMessage,
        bool showDialog,
        string dialogTitle,
        string dialogMessage,
        Action<string> setStatusText,
        Action<string> addLog,
        Action<string, string>? showErrorDialog)
    {
        setStatusText(statusText);

        if (!string.IsNullOrWhiteSpace(logMessage))
        {
            addLog(logMessage);
        }

        if (showDialog && showErrorDialog is not null)
        {
            showErrorDialog(dialogTitle, dialogMessage);
        }
    }

    private static void ForwardLogMessages(IEnumerable<string>? logMessages, Action<string> addLog)
    {
        if (logMessages is null)
        {
            return;
        }

        foreach (var logMessage in logMessages)
        {
            if (!string.IsNullOrWhiteSpace(logMessage))
            {
                addLog(logMessage);
            }
        }
    }

    private static void ForwardNotifications(IEnumerable<TrayNotification>? notifications, Action<TrayNotification>? notify)
    {
        if (notifications is null || notify is null)
        {
            return;
        }

        foreach (var notification in notifications)
        {
            if (notification is not null)
            {
                notify(notification);
            }
        }
    }
}
