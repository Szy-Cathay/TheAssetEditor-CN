using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AssetEditor.ViewModels;
using CommonControls;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.Common;

namespace AssetEditor.Views
{
    public partial class MainWindow : Window
    {
        Point _lastMouseDown;
        IEditorInterface _draggedItem;

        private readonly IServiceProvider _serviceProvider;
        private readonly PackAutoSaveService _autoSaveService;
        private bool _closeCheckRunning;
        private bool _closeApproved;

        public MainWindow(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;

            InitializeComponent();
            WindowState = serviceProvider
                .GetRequiredService<ApplicationSettingsService>()
                .CurrentSettings.StartMaximised
                ? WindowState.Maximized
                : WindowState.Normal;

            DarkTitleBarHelper.Enable(this);
            KeyDown += MainWindow_KeyDown;

            // Start auto-save service
            _autoSaveService = serviceProvider.GetRequiredService<PackAutoSaveService>();
            _autoSaveService.Start();

            Closing += OnWindowClosing;
            Closed += OnWindowClosed;
        }

        private async void OnWindowClosing(
            object? sender,
            CancelEventArgs e)
        {
            if (_closeApproved)
                return;

            if (DataContext is not MainViewModel viewModel)
                return;

            e.Cancel = true;
            if (_closeCheckRunning)
                return;

            _closeCheckRunning = true;
            try
            {
                await viewModel.ClosingWithProgressCompletion(
                    null,
                    ApplicationLoadingProgress.CompleteAsync);
                if (!viewModel.IsClosingWithoutPrompt)
                    return;

                _closeApproved = true;
                await System.Windows.Threading.Dispatcher.Yield(
                    DispatcherPriority.Normal);
                Close();
            }
            finally
            {
                _closeCheckRunning = false;
            }
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            _autoSaveService?.Stop();
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            {
                if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                {
                    if (e.Key == Key.F)
                    {
                        var packFileBrowserView = FindChild<PackFileBrowserView>(this);

                        if (packFileBrowserView != null)
                            packFileBrowserView.TriggerPreviewKeyDown();
                    }
                }
            }
        }

        private void TabItem_MouseDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    if (e.OriginalSource is DependencyObject source &&
                        FindParent<ButtonBase>(source) is not null)
                    {
                        _draggedItem = null;
                        return;
                    }

                    _lastMouseDown = e.GetPosition(EditorsTabControl);

                    var item = (TabItem)sender;
                    item.Focusable = true;
                    item.Focus();
                    item.Focusable = false;

                    _draggedItem = item.DataContext as IEditorInterface;
                }
            }
            catch
            {
            }
        }

        private void TabItem_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    var currentPosition = e.GetPosition(EditorsTabControl);

                    if ((Math.Abs(currentPosition.X - _lastMouseDown.X) > 10.0) ||
                        (Math.Abs(currentPosition.Y - _lastMouseDown.Y) > 10.0))
                    {
                        if (_draggedItem != null)
                            DragDrop.DoDragDrop(EditorsTabControl, EditorsTabControl.SelectedValue, DragDropEffects.Move);
                    }
                }
                else
                    _draggedItem = null;
            }
            catch
            {
            }
        }

        private void TabItem_Drop(object sender, DragEventArgs e)
        {
            try
            {
                var dropTargetItem = sender as TabItem;
                var pos = e.GetPosition(dropTargetItem);
                var insertAfterTargetNode = pos.X - dropTargetItem.ActualWidth / 2 > 0;

                if (DataContext is IDropTarget<IEditorInterface, bool> dropContainer)
                {
                    if (_draggedItem == null)
                        return;

                    if (dropTargetItem?.DataContext is not IEditorInterface dropTargetNode)
                        return;

                    if (dropContainer.AllowDrop(_draggedItem, dropTargetNode, insertAfterTargetNode))
                    {
                        dropContainer.Drop(_draggedItem, dropTargetNode, insertAfterTargetNode);
                        e.Effects = DragDropEffects.None;
                        e.Handled = true;
                    }
                }
            }
            catch
            {
            }
        }

        private void NewWindowMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Create a new DI scope for the new window
            // This ensures all scoped services (CommandExecutor, EventHub, etc.)
            // are independent from the main window
            var scope = _serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var mainWindow = scopedProvider.GetRequiredService<MainWindow>();
            mainWindow.DataContext = scopedProvider.GetRequiredService<MainViewModel>();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // Keep the scope alive until the window is closed
            mainWindow.Closed += (s, args) => scope.Dispose();

            mainWindow.Show();

            // Ensure the window doesn't cover up the windows bar
            mainWindow.MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
            mainWindow.MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth;
        }

        public void ToggleWindowState()
        {
            if (WindowState == WindowState.Maximized)
                SystemCommands.RestoreWindow(this);
            else
                SystemCommands.MaximizeWindow(this);
        }

        public void ShowSystemMenu(Point point)
        {
            // Increment coordinates to allow double-click
            ++point.X;
            ++point.Y;
            if (WindowState == WindowState.Normal)
            {
                point.X += Left;
                point.Y += Top;
            }
            SystemCommands.ShowSystemMenu(this, point);
        }

        private static T FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                    return typedChild;

                var childOfChild = FindChild<T>(child);
                if (childOfChild != null)
                    return childOfChild;
            }
            return null;
        }

        private static T? FindParent<T>(DependencyObject child)
            where T : DependencyObject
        {
            DependencyObject? current = child;
            while (current is not null)
            {
                if (current is T match)
                    return match;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
