using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.UiCommands
{
    public partial class BlenderShortcutsHelpWindow : WindowHandling.AssetEditorWindow
    {
        public BlenderShortcutsHelpWindow()
        {
            InitializeComponent();
            LoadLocalizedText();
        }

        void LoadLocalizedText()
        {
            var loc = LocalizationManager.Instance;

            Title = loc.Get("BlenderHelp.Title");
            TitleText.Text = loc.Get("BlenderHelp.Title");
            CloseBtn.Content = loc.Get("BlenderHelp.Close");

            // Section 1: Modal Transform
            Header1.Text = loc.Get("BlenderHelp.ModalTransform");
            SetMultilineText(Content1, loc.Get("BlenderHelp.ModalTransform.Content"));

            // Section 2: Navigation
            Header2.Text = loc.Get("BlenderHelp.Navigation");
            SetMultilineText(Content2, loc.Get("BlenderHelp.Navigation.Content"));

            // Section 3: Selection
            Header3.Text = loc.Get("BlenderHelp.Selection");
            SetMultilineText(Content3, loc.Get("BlenderHelp.Selection.Content"));

            // Section 4: Edit Mode
            Header4.Text = loc.Get("BlenderHelp.EditMode");
            SetMultilineText(Content4, loc.Get("BlenderHelp.EditMode.Content"));

            // Section 5: Edit Operations
            Header5.Text = loc.Get("BlenderHelp.EditOperations");
            SetMultilineText(Content5, loc.Get("BlenderHelp.EditOperations.Content"));

            // Section 6: Gizmo Toolbar
            Header6.Text = loc.Get("BlenderHelp.GizmoToolbar");
            SetMultilineText(Content6, loc.Get("BlenderHelp.GizmoToolbar.Content"));

            // Section 7: Proportional Editing
            Header7.Text = loc.Get("BlenderHelp.ProportionalEditing");
            SetMultilineText(Content7, loc.Get("BlenderHelp.ProportionalEditing.Content"));

            // Section 8: Tips
            Header8.Text = loc.Get("BlenderHelp.Tips");
            SetMultilineText(Content8, loc.Get("BlenderHelp.Tips.Content"));
        }

        /// <summary>
        /// Set TextBlock content from a localized string with \n separators,
        /// creating proper Run + LineBreak inlines for WPF rendering.
        /// </summary>
        static void SetMultilineText(TextBlock textBlock, string localizedText)
        {
            textBlock.Inlines.Clear();
            var lines = localizedText.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0)
                    textBlock.Inlines.Add(new LineBreak());
                textBlock.Inlines.Add(new Run(lines[i]));
            }
        }

        void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
