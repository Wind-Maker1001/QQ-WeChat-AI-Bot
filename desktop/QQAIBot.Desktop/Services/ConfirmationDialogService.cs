namespace QQAIBot.Desktop.Services;

public sealed class ConfirmationDialogService : IConfirmationDialogService
{
    public bool Confirm(string title, string message)
    {
        var result = System.Windows.MessageBox.Show(
            message,
            title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        return result == System.Windows.MessageBoxResult.Yes;
    }
}
