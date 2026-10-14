// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CommonControls.Editors.AnimationBatchExporter
{
    /// <summary>
    /// Interaction logic for AnimationBatchExportView.xaml
    /// </summary>
    public partial class AnimationBatchExportView : UserControl
    {
        private AnimationBatchExportViewModel? _selectionModel;

        public AnimationBatchExportView()
        {
            InitializeComponent();
        }

        private void ViewLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AnimationBatchExportViewModel model)
                model.RefreshWorkspace();
        }

        private void AnimationMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not AnimationBatchExportViewModel model ||
                FindAnimationRow(e.OriginalSource as DependencyObject) is not { DataContext: AnimationBatchExportViewModel.AnimationListItem item } row)
                return;

            EndAnimationSelection();
            if (!model.BeginAnimationSelection(item, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
                return;
            e.Handled = true;
            row.Focus();
            _selectionModel = model;
            if (!Mouse.Capture(AnimationsList, CaptureMode.SubTree))
                EndAnimationSelection();
        }

        private void AnimationMouseMove(object sender, MouseEventArgs e)
        {
            if (_selectionModel == null)
                return;
            var point = e.GetPosition(AnimationsList);
            if (e.LeftButton != MouseButtonState.Pressed ||
                point.X < 0 || point.Y < 0 || point.X >= AnimationsList.ActualWidth || point.Y >= AnimationsList.ActualHeight)
            {
                EndAnimationSelection();
                return;
            }
            var row = FindAnimationRow(AnimationsList.InputHitTest(point) as DependencyObject);
            if (row?.DataContext is AnimationBatchExportViewModel.AnimationListItem item &&
                !_selectionModel.ContinueAnimationSelection(item))
                EndAnimationSelection();
            e.Handled = true;
        }

        private void AnimationMouseUp(object sender, MouseButtonEventArgs e) => EndAnimationSelection();

        private void AnimationMouseLeave(object sender, MouseEventArgs e) => EndAnimationSelection();

        private void AnimationLostMouseCapture(object sender, MouseEventArgs e) => EndAnimationSelection();

        private void ViewUnloaded(object sender, RoutedEventArgs e) => EndAnimationSelection();

        private void ViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            (e.OldValue as AnimationBatchExportViewModel)?.EndAnimationSelection();
            EndAnimationSelection();
        }

        private void AnimationKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Space || DataContext is not AnimationBatchExportViewModel model ||
                FindAnimationRow(e.OriginalSource as DependencyObject)?.DataContext is not AnimationBatchExportViewModel.AnimationListItem item)
                return;
            e.Handled = true;
            if (e.IsRepeat)
                return;
            EndAnimationSelection();
            model.BeginAnimationSelection(item, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            model.EndAnimationSelection();
        }

        private ListBoxItem? FindAnimationRow(DependencyObject? source)
        {
            return source == null ? null : ItemsControl.ContainerFromElement(AnimationsList, source) as ListBoxItem;
        }

        private void EndAnimationSelection()
        {
            (_selectionModel ?? DataContext as AnimationBatchExportViewModel)?.EndAnimationSelection();
            _selectionModel = null;
            if (AnimationsList != null && ReferenceEquals(Mouse.Captured, AnimationsList))
                AnimationsList.ReleaseMouseCapture();
        }

        private async void ExportClicked(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AnimationBatchExportViewModel model)
                return;
            var errors = await model.Process();
            await ProgressHost.CompleteAsync();
            if (errors != null)
                model.ShowResults(errors);
        }
    }
}
