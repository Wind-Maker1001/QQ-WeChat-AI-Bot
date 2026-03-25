using Forms = System.Windows.Forms;

namespace QQAIBot.Desktop.Services;

public interface INotifyIconHost : IDisposable
{
    event EventHandler? DoubleClick;

    bool Visible { get; set; }

    string Text { get; set; }

    string BalloonTipTitle { get; set; }

    string BalloonTipText { get; set; }

    Forms.ToolTipIcon BalloonTipIcon { get; set; }

    void ShowBalloonTip(int timeout);
}
