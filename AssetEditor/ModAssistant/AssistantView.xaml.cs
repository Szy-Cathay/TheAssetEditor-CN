using System.Diagnostics;
using System.Windows.Automation.Peers;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Navigation;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace AssetEditor.ModAssistant;

public partial class AssistantView : UserControl
{
    private bool _followingTail = true;
    public AssistantView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (DataContext is AssistantViewModel vm) vm.RefreshContextCommand.Execute(null); };
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is AssistantViewModel old) old.PropertyChanged -= ViewModelChanged;
            if (e.NewValue is AssistantViewModel current) current.PropertyChanged += ViewModelChanged;
        };
    }
    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AssistantViewModel.CurrentSession)) return;
        _followingTail = true;
        ConversationScroll.ScrollToEnd();
    }
    private void WorkspaceSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is not AssistantViewModel vm || ActualWidth <= 0) return;
        var compact = ActualWidth < 960;
        if (compact != vm.IsCompact) { vm.IsCompact = compact; vm.HistoryOpen = !compact; }
    }
    public static bool IsSendGesture(Key key, ModifierKeys modifiers) => key == Key.Enter && modifiers == ModifierKeys.Control;
    private void PromptKeyDown(object sender, KeyEventArgs e)
    {
        if (!IsSendGesture(e.Key, Keyboard.Modifiers) || DataContext is not AssistantViewModel vm) return;
        e.Handled = true;
        if (vm.SendCommand.CanExecute(null)) vm.SendCommand.Execute(null);
    }
    private void OpenAttachmentMenu(object sender, RoutedEventArgs e)
    {
        AttachmentButton.ContextMenu.PlacementTarget = AttachmentButton;
        AttachmentButton.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        AttachmentButton.ContextMenu.IsOpen = true;
    }
    private void ScrollToLatest(object sender, RoutedEventArgs e) { _followingTail = true; ConversationScroll.ScrollToEnd(); }
    private void ConversationWheel(object sender, MouseWheelEventArgs e)
    {
        // Handle at the transcript before nested document/text viewers consume the event.
        // A bounded detail box owns its wheel only while it can scroll in that direction.
        for (var node = e.OriginalSource as DependencyObject; node != null && node != ConversationScroll; node = ParentOf(node))
        {
            if (node is ScrollViewer inner && inner.VerticalScrollBarVisibility is ScrollBarVisibility.Auto or ScrollBarVisibility.Visible &&
                (e.Delta > 0 ? inner.VerticalOffset > 0 : inner.VerticalOffset < inner.ScrollableHeight))
                return;
        }
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0 || e.Delta == 0) return;
        var distance = lines < 0 ? ConversationScroll.ViewportHeight : lines * 21d;
        ConversationScroll.ScrollToVerticalOffset(ConversationScroll.VerticalOffset - e.Delta / 120d * distance);
        e.Handled = true;
    }
    private static DependencyObject? ParentOf(DependencyObject node) => node switch
    {
        FrameworkContentElement content => content.Parent,
        Visual or Visual3D => VisualTreeHelper.GetParent(node),
        _ => LogicalTreeHelper.GetParent(node),
    };
    private void ConversationScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource != ConversationScroll) return;
        if (e.ExtentHeightChange == 0 && e.VerticalChange != 0)
            _followingTail = ConversationScroll.ScrollableHeight - ConversationScroll.VerticalOffset <= 4;
        if (_followingTail && e.ExtentHeightChange > 0) ConversationScroll.ScrollToEnd();
        JumpToLatest.Visibility = !_followingTail && ConversationScroll.ScrollableHeight > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void StatusUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is TextBlock status && DataContext is AssistantViewModel { IsBusy: false } &&
            AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.FromElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void OpenSource(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.IsAbsoluteUri && e.Uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
