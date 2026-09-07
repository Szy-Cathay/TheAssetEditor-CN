using Shared.Ui.Common.MenuSystem;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KitbasherEditor.ViewModels.MenuBarViews;

namespace KitbasherEditor.Views
{
    /// <summary>
    /// Interaction logic for MenuBarView.xaml
    /// </summary>
    public partial class MenuBarView : UserControl
    {
        private Window? _hostWindow;

        public MenuBarView()
        {
            InitializeComponent();
            AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler((_, _) =>
            {
                if (DataContext is MenuBarViewModel viewModel)
                    viewModel.RefreshMenuLabels();
            }));
        }

        private void SelectionTool_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MenuBarViewModel viewModel)
                viewModel.FocusScene();
        }

        private void ShadingPopup_Opened(object? sender, EventArgs e)
        {
            if (DataContext is MenuBarViewModel viewModel)
                viewModel.ViewportShading.RefreshSettings();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (ShadingPopup.IsOpen)
                    Keyboard.Focus(ShadingPanel);
            }));
        }

        private void ShadingPopup_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Escape or Key.Enter) || e.OriginalSource is ComboBox { IsDropDownOpen: true })
                return;
            ShadingArrowBtn.IsChecked = false;
            e.Handled = true;
            if (DataContext is MenuBarViewModel viewModel)
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(viewModel.FocusScene));
        }

        private void HostWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ShadingPopup.IsOpen && !ShadingArrowBtn.IsMouseOver && !ShadingPanel.IsMouseOver
                && (e.OriginalSource is not DependencyObject source || !ShadingPanel.IsAncestorOf(source)))
                ShadingArrowBtn.IsChecked = false;
        }

        private void HostWindow_CloseShadingPopup(object? sender, EventArgs e) => ShadingArrowBtn.IsChecked = false;

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            DetachWindowHandlers();
            var window = Window.GetWindow(this);
            if (window != null)
            {
                _hostWindow = window;
                window.KeyUp += HandleKeyPress;
                window.KeyDown += HandleKeyDown;
                window.PreviewMouseDown += HostWindow_PreviewMouseDown;
                window.Deactivated += HostWindow_CloseShadingPopup;
                window.LocationChanged += HostWindow_CloseShadingPopup;
                window.SizeChanged += HostWindow_CloseShadingPopup;
            }
        }

        private void UserControl_Unloaded(
            object sender,
            RoutedEventArgs e) => DetachWindowHandlers();

        private void DetachWindowHandlers()
        {
            ShadingArrowBtn.IsChecked = false;
            if (_hostWindow == null)
                return;

            _hostWindow.KeyUp -= HandleKeyPress;
            _hostWindow.KeyDown -= HandleKeyDown;
            _hostWindow.PreviewMouseDown -= HostWindow_PreviewMouseDown;
            _hostWindow.Deactivated -= HostWindow_CloseShadingPopup;
            _hostWindow.LocationChanged -= HostWindow_CloseShadingPopup;
            _hostWindow.SizeChanged -= HostWindow_CloseShadingPopup;
            _hostWindow = null;
        }

        private void HandleKeyPress(object sender, KeyEventArgs e)
        {
            // Only handle keyboard events if this editor is visible (active tab)
            if (!IsEditorVisible())
                return;

            if (e.OriginalSource is TextBox)
            {
                if (DataContext is MenuBarViewModel viewModel)
                    viewModel.ClearKeyState(e.Key, e.SystemKey);
                e.Handled = true;
                return;
            }

            if (DataContext is IKeyboardHandler keyboardHandler)
            {
                var res = keyboardHandler.OnKeyReleased(e.Key, e.SystemKey, Keyboard.Modifiers);
                if (res)
                    e.Handled = true;
            }
        }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            // Only handle keyboard events if this editor is visible (active tab)
            if (!IsEditorVisible())
                return;

            if (e.OriginalSource is TextBox)
                return;

            if (DataContext is IKeyboardHandler keyboardHandler)
            {
                keyboardHandler.OnKeyDown(e.Key, e.SystemKey, Keyboard.Modifiers);
            }
        }

        /// <summary>
        /// Close falloff popup on Enter key and return focus to the 3D viewport
        /// </summary>
        private void FalloffTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Commit the value and close popup
                var textBox = sender as TextBox;
                if (textBox != null)
                {
                    var binding = BindingOperations.GetBindingExpression(textBox, TextBox.TextProperty);
                    binding?.UpdateSource();
                }
                PropArrowBtn.IsChecked = false;
                e.Handled = true;
                if (DataContext is MenuBarViewModel viewModel)
                {
                    // Let the Popup finish closing before moving keyboard focus.
                    Dispatcher.BeginInvoke(
                        DispatcherPriority.Input,
                        new Action(viewModel.FocusScene));
                }
            }
        }

        /// <summary>
        /// Check if this editor is currently visible (active tab in the tab control)
        /// This prevents keyboard events from being processed by inactive editors
        /// </summary>
        private bool IsEditorVisible()
        {
            // Check if the control is visible and rendered
            if (!IsVisible)
                return false;

            // Check if the control has positive actual width/height (rendered)
            if (ActualWidth == 0 || ActualHeight == 0)
                return false;

            // Walk up the visual tree to check if any parent is collapsed or hidden
            DependencyObject current = this;
            while (current != null)
            {
                if (current is FrameworkElement element)
                {
                    if (!element.IsVisible)
                        return false;
                }
                current = VisualTreeHelper.GetParent(current);
            }

            return true;
        }
    }
}
