using CommonControls;
﻿using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Shared.Core.ToolCreation;

namespace CommonControls.BaseDialogs.ToolSelector
{
    /// <summary>
    /// Interaction logic for ToolSelectorWindow.xaml
    /// </summary>
    public partial class ToolSelectorWindow : Window
    {
        public ToolSelectorWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
        }

        private void PossibleTool_MouseDoubleClick(
            object sender,
            MouseButtonEventArgs e)
        {
            if (sender is not ListViewItem item ||
                item.Content is not EditorEnums selectedEditor ||
                selectedEditor == EditorEnums.None)
            {
                return;
            }

            PossibleTools.SelectedItem = selectedEditor;
            ConfirmSelection();
            e.Handled = true;
        }

        private void Button_Click(object sender, RoutedEventArgs e) =>
            ConfirmSelection();

        private void ConfirmSelection()
        {
            DialogResult = true;
            Close();
        }
    }
}
