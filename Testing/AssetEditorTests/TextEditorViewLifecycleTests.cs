using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommonControls.Editors.TextEditor;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace AssetEditorTests;

public class TextEditorViewLifecycleTests
{
    [NUnit.Framework.Test]
    public void SyntaxSelector_UsesThemeStyleAndThemeAwareColorizer()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                var view = new TextEditorView();
                var window = new Window
                {
                    Content = view,
                    Width = 900,
                    Height = 600,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();

                    var selector = FindVisualDescendants<ComboBox>(view)
                        .Single(comboBox =>
                            comboBox.Name == "highlightingComboBox");
                    var editor = FindVisualDescendants<TextEditor>(view)
                        .Single();
                    selector.SelectedItem = HighlightingManager.Instance
                        .GetDefinition("XML");
                    editor.Text =
                        "<SLOT name=\"value\" probability=\"0.0\" />";
                    window.UpdateLayout();
                    editor.TextArea.TextView.Redraw();
                    window.UpdateLayout();
                    editor.TextArea.TextView.EnsureVisualLines();

                    var colorizers = editor.TextArea.TextView
                        .LineTransformers
                        .OfType<HighlightingColorizer>()
                        .ToArray();
                    var foregroundBrushes = editor.TextArea.TextView.VisualLines
                        .SelectMany(line => line.Elements)
                        .Select(element =>
                            element.TextRunProperties.ForegroundBrush)
                        .Where(brush => brush != null)
                        .ToArray();
                    NUnit.Framework.Assert.Multiple(() =>
                    {
                        NUnit.Framework.Assert.That(
                            selector.Style,
                            NUnit.Framework.Is.SameAs(
                                Application.Current.FindResource(
                                    "AeInput.ComboBox")));
                        NUnit.Framework.Assert.That(
                            colorizers.Select(colorizer =>
                                colorizer.GetType().Name),
                            NUnit.Framework.Does.Contain(
                                "ThemeAwareHighlightingColorizer"));
                        NUnit.Framework.Assert.That(
                            colorizers.Any(colorizer =>
                                colorizer.GetType() ==
                                typeof(HighlightingColorizer)),
                            NUnit.Framework.Is.False);
                        NUnit.Framework.Assert.That(
                            foregroundBrushes,
                            NUnit.Framework.Does.Contain(
                                Application.Current.FindResource(
                                    "AeBrush.Accent")));
                        NUnit.Framework.Assert.That(
                            foregroundBrushes,
                            NUnit.Framework.Does.Contain(
                                Application.Current.FindResource(
                                    "AeBrush.Success")));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
    }

    [NUnit.Framework.Test]
    public void FoldingTimer_FollowsLoadedAndUnloadedLifecycle()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () =>
            {
                TextEditorView? view = null;
                try
                {
                    view = new TextEditorView();
                    NUnit.Framework.Assert.That(
                        view.IsFoldingTimerEnabled,
                        NUnit.Framework.Is.False);

                    view.RaiseEvent(new RoutedEventArgs(
                        FrameworkElement.LoadedEvent));
                    NUnit.Framework.Assert.That(
                        view.IsFoldingTimerEnabled,
                        NUnit.Framework.Is.True);

                    view.RaiseEvent(new RoutedEventArgs(
                        FrameworkElement.LoadedEvent));
                    NUnit.Framework.Assert.That(
                        view.IsFoldingTimerEnabled,
                        NUnit.Framework.Is.True);

                    view.RaiseEvent(new RoutedEventArgs(
                        FrameworkElement.UnloadedEvent));
                    NUnit.Framework.Assert.That(
                        view.IsFoldingTimerEnabled,
                        NUnit.Framework.Is.False);

                    view.RaiseEvent(new RoutedEventArgs(
                        FrameworkElement.LoadedEvent));
                    NUnit.Framework.Assert.That(
                        view.IsFoldingTimerEnabled,
                        NUnit.Framework.Is.True);
                }
                finally
                {
                    view?.RaiseEvent(new RoutedEventArgs(
                        FrameworkElement.UnloadedEvent));
                }
            });
    }

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }
}
