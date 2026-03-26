using QQAIBot.Desktop.Models;

namespace QQAIBot.Desktop.Services;

public static class DesktopControlPlaneFeedback
{
    public static void ApplyOutcome(
        string statusText,
        IEnumerable<string>? logMessages,
        IEnumerable<TrayNotification>? notifications,
        Action<string> setStatusText,
        Action<string> addLog,
        Action<TrayNotification>? notify)
    {
        setStatusText(statusText);

        if (logMessages is not null)
        {
            foreach (var logMessage in logMessages)
            {
                if (!string.IsNullOrWhiteSpace(logMessage))
                {
                    addLog(logMessage);
                }
            }
        }

        if (notifications is not null && notify is not null)
        {
            foreach (var notification in notifications)
            {
                if (notification is not null)
                {
                    notify(notification);
                }
            }
        }
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
}
