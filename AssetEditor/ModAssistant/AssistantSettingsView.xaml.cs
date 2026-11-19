using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Data;

namespace AssetEditor.ModAssistant;

public partial class AssistantSettingsView : UserControl
{
    public AssistantSettingsView() => InitializeComponent();
    private void StatusUpdated(object sender, DataTransferEventArgs e)
    {
        if (sender is TextBlock status && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            UIElementAutomationPeer.FromElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }
    private void SaveKey(object sender, RoutedEventArgs e)
    {
        if (DataContext is AssistantSettingsViewModel settings) settings.SaveKey(KeyBox.Password);
        KeyBox.Clear();
    }
}
