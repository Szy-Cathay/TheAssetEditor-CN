using System.Windows;
using System.Windows.Controls;

namespace AssetEditor.Views
{
    /// <summary>
    /// Interaction logic for MenuBarView.xaml
    /// </summary>
    public partial class MenuBarView : UserControl
    {
        public MenuBarView()
        {
            InitializeComponent();
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e) =>
            SystemCommands.MinimizeWindow(Window.GetWindow(this));

        private void OnMaximizeRestoreClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window.WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(window);
            else
                SystemCommands.MaximizeWindow(window);
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) =>
            SystemCommands.CloseWindow(Window.GetWindow(this));
    }
}
