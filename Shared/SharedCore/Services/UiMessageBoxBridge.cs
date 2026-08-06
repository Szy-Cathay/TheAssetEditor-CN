using System.Windows;

namespace Shared.Core.Services;

public static class UiMessageBoxBridge
{
    private static Func<
        string,
        string,
        MessageBoxButton,
        MessageBoxImage,
        MessageBoxResult>? _show;

    public static void Configure(
        Func<
            string,
            string,
            MessageBoxButton,
            MessageBoxImage,
            MessageBoxResult> show)
    {
        _show = show;
    }

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
        return _show?.Invoke(message, title, buttons, image) ??
               MessageBox.Show(message, title, buttons, image);
    }
}
