using System.Windows;

using CommonControls;

namespace CommonControls.BaseDialogs;

public partial class TitleDescriptionInputWindow : Window
{
    public string TitleValue => TitleTextBox.Text;
    public string DescriptionValue => DescriptionTextBox.Text;

    public TitleDescriptionInputWindow(
        string title,
        string titleLabel,
        string descriptionLabel,
        string initialTitle,
        string initialDescription)
    {
        InitializeComponent();
        DarkTitleBarHelper.Enable(this);
        Title = title;
        TitleLabel.Text = titleLabel;
        DescriptionLabel.Text = descriptionLabel;
        TitleTextBox.Text = initialTitle;
        DescriptionTextBox.Text = initialDescription;
        if (Application.Current?.MainWindow is { } owner &&
            !ReferenceEquals(owner, this))
        {
            Owner = owner;
        }
        TitleTextBox.Focus();
        TitleTextBox.SelectAll();
        UpdateOkButton();
    }

    private void TitleTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateOkButton();
    }

    private void UpdateOkButton()
    {
        if (OkButton != null)
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(TitleTextBox.Text);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleTextBox.Text))
            return;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
