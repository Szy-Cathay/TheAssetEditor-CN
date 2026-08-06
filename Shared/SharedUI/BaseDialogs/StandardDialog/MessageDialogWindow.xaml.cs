using System.Windows;
using WindowHandling;

namespace Shared.Ui.BaseDialogs.StandardDialog;

public enum MessageDialogButtonSet
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel,
}

public partial class MessageDialogWindow : AssetEditorWindow
{
    public MessageBoxResult Result { get; private set; } =
        MessageBoxResult.Cancel;

    public MessageDialogWindow(
        string title,
        string message,
        MessageDialogButtonSet buttonSet)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;

        if (buttonSet is MessageDialogButtonSet.YesNo or
            MessageDialogButtonSet.YesNoCancel)
        {
            OkButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Visible;
            NoButton.Visibility = Visibility.Visible;
        }
        if (buttonSet is MessageDialogButtonSet.OkCancel or
            MessageDialogButtonSet.YesNoCancel)
        {
            CancelButton.Visibility = Visibility.Visible;
        }
    }

    public string Message => MessageText.Text;

    public int ActionButtonCount =>
        (OkButton.Visibility == Visibility.Visible ? 1 : 0) +
        (YesButton.Visibility == Visibility.Visible ? 1 : 0) +
        (NoButton.Visibility == Visibility.Visible ? 1 : 0) +
        (CancelButton.Visibility == Visibility.Visible ? 1 : 0);

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.OK;
        DialogResult = true;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Yes;
        DialogResult = true;
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.No;
        DialogResult = false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
    }
}
