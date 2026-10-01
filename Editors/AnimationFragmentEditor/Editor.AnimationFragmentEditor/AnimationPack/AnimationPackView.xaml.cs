// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;

namespace CommonControls.Editors.AnimationPack
{
    /// <summary>
    /// Interaction logic for AnimationPackView.xaml
    /// </summary>
    public partial class AnimationPackView : UserControl
    {
        private bool _isHandlingFileSelection;
        private IAnimationPackFile? _displayedFile;

        public AnimationPackView()
        {
            InitializeComponent();
            DataContextChanged += (_, e) =>
            {
                if (e.OldValue is AnimPackViewModel previous) previous.CommitPendingEdits = null;
                if (e.NewValue is AnimPackViewModel current) current.CommitPendingEdits = CommitPendingEdits;
                _displayedFile = (e.NewValue as AnimPackViewModel)?.AnimationPackItems.SelectedItem;
                EditorScrollViewer.ScrollToTop();
            };
        }

        private void FileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var list = (ListView)sender;
            if (_isHandlingFileSelection || DataContext is not AnimPackViewModel editor) return;
            _isHandlingFileSelection = true;
            try
            {
                if (!ReferenceEquals(list.SelectedItem, editor.AnimationPackItems.SelectedItem))
                {
                    editor.AnimationPackItems.SelectedItem = (IAnimationPackFile?)list.SelectedItem;
                    list.GetBindingExpression(ListView.SelectedItemProperty)?.UpdateTarget();
                }
                if (!ReferenceEquals(_displayedFile, editor.AnimationPackItems.SelectedItem))
                {
                    _displayedFile = editor.AnimationPackItems.SelectedItem;
                    EditorScrollViewer.ScrollToTop();
                }
            }
            finally { _isHandlingFileSelection = false; }
        }

        private bool CommitPendingEdits()
        {
            bool valid = true;
            void Visit(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    Visit(VisualTreeHelper.GetChild(parent, i));
                if (parent is TextBox { IsReadOnly: false, IsVisible: true } text)
                    text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (parent is ComboBox { IsEditable: true, IsVisible: true } combo)
                    combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                if (parent is DataGrid { IsVisible: true } grid)
                    valid &= grid.CommitEdit(DataGridEditingUnit.Cell, true) && grid.CommitEdit(DataGridEditingUnit.Row, true);
                if (Validation.GetHasError(parent)) valid = false;
            }
            Visit(this);
            return valid;
        }

    }
}
