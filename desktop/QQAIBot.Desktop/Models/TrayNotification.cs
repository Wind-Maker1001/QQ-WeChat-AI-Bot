using Forms = System.Windows.Forms;

namespace QQAIBot.Desktop.Models;

public sealed class TrayNotification
{
    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public Forms.ToolTipIcon Icon { get; set; } = Forms.ToolTipIcon.Info;
}
