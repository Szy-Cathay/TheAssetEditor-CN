using System.Windows;
using System.Windows.Input;
using CommonControls;

namespace CommonControls.BaseDialogs
{
    /// <summary>
    /// Interaction logic for TextInputWindow.xaml
    /// </summary>
    public partial class TextInputWindow : Window
    {
        public TextInputWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
        }

        public TextInputWindow(string title, string initialValue = "", bool focusTextInput = false)
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
            Title = title;
            TextValue = initialValue;
            Owner = Application.Current.MainWindow;

            if (focusTextInput)
            {
                TextBoxItem.Focus();
            }
        }

        public string TextValue
        {
            get => TextBoxItem.Text;
            set => TextBoxItem.Text = value;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Key_Down(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                DialogResult = true;
                Close();
            }

            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}
