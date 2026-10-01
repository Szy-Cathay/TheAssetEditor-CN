using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Editors.AnimationFragmentEditor.CampaignAnimBin
{
    public partial class CampaignTableEditorView : UserControl
    {
        public CampaignTableEditorView() => InitializeComponent();
        private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!EntriesGrid.IsKeyboardFocusWithin && EntriesGrid.SelectedItem is { } selected)
                Dispatcher.BeginInvoke(() =>
                {
                    if (EntriesGrid.SelectedItem == selected && EntriesGrid.Items.Contains(selected))
                        EntriesGrid.ScrollIntoView(selected);
                }, DispatcherPriority.Loaded);
        }
        private void ResourcePicker_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ComboBox combo || DataContext is not CampaignTableEditorViewModel vm) return;
            combo.ApplyTemplate();
            if (combo.Template.FindName("PART_EditableTextBox", combo) is not TextBox text) return;
            var candidates = (combo.Tag as string) switch { "Animation" => vm.AnimationFiles, "Meta" => vm.MetaFiles, _ => vm.SoundFiles };
            void Filter() => combo.ItemsSource = candidates.Where(p => string.IsNullOrWhiteSpace(combo.Text) || p.Contains(combo.Text, StringComparison.OrdinalIgnoreCase)).Take(100).ToArray();
            TextChangedEventHandler changed = (_, _) => { if (text.IsKeyboardFocused && combo.IsDropDownOpen) Filter(); };
            TextCompositionEventHandler typing = (_, _) => combo.IsDropDownOpen = true;
            EventHandler opened = (_, _) => Filter();
            text.TextChanged += changed;
            text.PreviewTextInput += typing;
            combo.DropDownOpened += opened;
            combo.Unloaded += (_, _) => { text.TextChanged -= changed; text.PreviewTextInput -= typing; combo.DropDownOpened -= opened; };
            Filter();
        }
    }
}
