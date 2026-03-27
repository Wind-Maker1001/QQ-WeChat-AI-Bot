using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QQAIBot.Desktop.Services;

public sealed class NotifyIconHost : INotifyIconHost
{
    private readonly Forms.NotifyIcon _notifyIcon;

    public NotifyIconHost(Forms.ContextMenuStrip contextMenuStrip)
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
            Text = "Local AI Runtime",
            Visible = true,
            ContextMenuStrip = contextMenuStrip
        };
        _notifyIcon.DoubleClick += OnDoubleClick;
    }

    public event EventHandler? DoubleClick;

    public bool Visible
    {
        get => _notifyIcon.Visible;
        set => _notifyIcon.Visible = value;
    }

    public string Text
    {
        get => _notifyIcon.Text;
        set => _notifyIcon.Text = value;
    }

    public string BalloonTipTitle
    {
        get => _notifyIcon.BalloonTipTitle;
        set => _notifyIcon.BalloonTipTitle = value;
    }

    public string BalloonTipText
    {
        get => _notifyIcon.BalloonTipText;
        set => _notifyIcon.BalloonTipText = value;
    }

    public Forms.ToolTipIcon BalloonTipIcon
    {
        get => _notifyIcon.BalloonTipIcon;
        set => _notifyIcon.BalloonTipIcon = value;
    }

    public void ShowBalloonTip(int timeout)
    {
        _notifyIcon.ShowBalloonTip(timeout);
    }

    public void Dispose()
    {
        _notifyIcon.DoubleClick -= OnDoubleClick;
        _notifyIcon.Dispose();
    }

    private void OnDoubleClick(object? sender, EventArgs e)
    {
        DoubleClick?.Invoke(this, EventArgs.Empty);
    }
}
