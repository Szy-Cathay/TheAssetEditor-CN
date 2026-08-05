using System.Windows;
using WindowHandling;

namespace Shared.Ui.BaseDialogs.StandardDialog;

public enum MessageDialogButtonSet
{
    Ok,
    YesNo,
}

public partial class MessageDialogWindow : AssetEditorWindow
{
    public MessageDialogWindow(
        string title,
        string message,
        MessageDialogButtonSet buttonSet)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;

        if (buttonSet == MessageDialogButtonSet.YesNo)
        {
            OkButton.Visibility = Visibility.Collapsed;
            YesButton.Visibility = Visibility.Visible;
            NoButton.Visibility = Visibility.Visible;
        }
    }

    public string Message => MessageText.Text;

    public int ActionButtonCount =>
        OkButton.Visibility == Visibility.Visible ? 1 : 2;

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
