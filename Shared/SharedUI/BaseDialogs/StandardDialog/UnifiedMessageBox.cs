using System.Linq;
using System.Windows;
using Shared.Core.Services;

using DialogResult = System.Windows.Forms.DialogResult;
using MessageBoxButtons = System.Windows.Forms.MessageBoxButtons;
using MessageBoxIcon = System.Windows.Forms.MessageBoxIcon;

namespace Shared.Ui.BaseDialogs.StandardDialog;

public static class UnifiedMessageBox
{
    public static MessageBoxResult Show(string message) =>
        Show(message, string.Empty);

    public static MessageBoxResult Show(string message, string title) =>
        Show(message, title, MessageBoxButton.OK);

    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons) =>
        Show(message, title, buttons, MessageBoxImage.None);

    public static MessageBoxResult Show(
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        var application = Application.Current;
        if (application?.Dispatcher is null ||
            application.TryFindResource("CustomWindowStyle") is null)
        {
            return System.Windows.MessageBox.Show(
                message,
                title,
                buttons,
                image);
        }

        if (!application.Dispatcher.CheckAccess())
        {
            return application.Dispatcher.Invoke(
                () => Show(message, title, buttons, image));
        }

        var dialog = new MessageDialogWindow(
            title,
            message,
            ToButtonSet(buttons));
        var owner = application.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive) ??
            application.MainWindow;
        if (owner is { IsLoaded: true } && owner != dialog)
            dialog.Owner = owner;
        dialog.ShowDialog();
        return dialog.Result;
    }

    public static DialogResult Show(
        string message,
        string title,
        MessageBoxButtons buttons,
        MessageBoxIcon icon)
    {
        var result = Show(
            message,
            title,
            buttons == MessageBoxButtons.YesNo
                ? MessageBoxButton.YesNo
                : MessageBoxButton.OK,
            ToImage(icon));
        return result switch
        {
            MessageBoxResult.Yes => DialogResult.Yes,
            MessageBoxResult.No => DialogResult.No,
            MessageBoxResult.Cancel => DialogResult.Cancel,
            _ => DialogResult.OK,
        };
    }

    private static MessageDialogButtonSet ToButtonSet(
        MessageBoxButton buttons) => buttons switch
        {
            MessageBoxButton.OKCancel => MessageDialogButtonSet.OkCancel,
            MessageBoxButton.YesNo => MessageDialogButtonSet.YesNo,
            MessageBoxButton.YesNoCancel =>
                MessageDialogButtonSet.YesNoCancel,
            _ => MessageDialogButtonSet.Ok,
        };

    private static MessageBoxImage ToImage(MessageBoxIcon icon) =>
        icon switch
        {
            MessageBoxIcon.Error => MessageBoxImage.Error,
            MessageBoxIcon.Warning => MessageBoxImage.Warning,
            MessageBoxIcon.Question => MessageBoxImage.Question,
            MessageBoxIcon.Information => MessageBoxImage.Information,
            _ => MessageBoxImage.None,
        };
}
