using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CommonControls.Editors.BoneMapping.View;
using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using Editors.Shared.Core.Editors.BoneMapping.View;
using NUnit.Framework;
using Shared.Core.Settings;
using static AssetEditorTests.PlaybackAndViewportRefinementTests;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class MeshFitterLayoutTests
{
    [TestCaseSource(typeof(PlaybackAndViewportRefinementTests), nameof(ThemeScales))]
    public void ColumnsAndFields_AlignAndResizeWithoutWindowsOrInput(ThemeType theme, double scale)
    {
        WithTheme(theme, () =>
        {
            var focus = Keyboard.FocusedElement;
            var dummy = new MeshFitterViewModel(null!, null!, null!);
            typeof(MeshFitterViewModel).GetField("_isDisposed", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(dummy, true);
            var window = new MeshFitterWindow(dummy);
            try
            {
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.Resources.MergedDictionaries.Add(window.Resources);
                content.DataContext = new KitbashToolInteractionTests.ToolProbe();
                var host = new Border { Background = (Brush)Application.Current.FindResource("AeBrush.Canvas"), Child = content };
                VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
                Layout(host, 1160, 650);
                var mapping = Descendants<BoneMappingView>(host).Single();
                var metadata = Descendants<BoneMappingMetaDataView>(host).Single();
                var source = (FrameworkElement)mapping.FindName("SourceBonesPanel");
                var target = (FrameworkElement)mapping.FindName("TargetBonesPanel");
                var parameters = (ScrollViewer)window.FindName("ParameterScroll");
                var splitters = Descendants<GridSplitter>(host).ToArray();
                Assert.That(splitters.Length, Is.EqualTo(2));

                foreach (var size in new[] { new Size(1160, 650), new Size(1000, 500) })
                {
                    Layout(host, size.Width, size.Height);
                    Render(host, $"mesh-fitter-aligned-{theme}-{scale:0.00}-{size.Width}", scale);
                    AssertAlignedPanels(host, source, target, parameters);
                    AssertFields(metadata);
                    foreach (var splitter in splitters)
                    {
                        var grid = (Grid)splitter.Parent;
                        var column = Grid.GetColumn(splitter);
                        var originalWidths = grid.ColumnDefinitions.Select(x => x.Width).ToArray();
                        var before = grid.ColumnDefinitions[column - 1].ActualWidth;
                        Assert.That(grid.ColumnDefinitions[column].ActualWidth, Is.EqualTo(1).Within(0.1));
                        Assert.That(splitter.ActualWidth, Is.EqualTo(8));
                        Assert.That(splitter.Style, Is.SameAs(splitter.FindResource("AeVerticalGridSplitterStyle")));

                        Drag(splitter, 24);
                        Layout(host, size.Width, size.Height);
                        Assert.That(grid.ColumnDefinitions[column - 1].ActualWidth, Is.GreaterThan(before));
                        Drag(splitter, 10000);
                        Layout(host, size.Width, size.Height);
                        Assert.That(grid.ColumnDefinitions[column + 1].ActualWidth,
                            Is.EqualTo(grid.ColumnDefinitions[column + 1].MinWidth).Within(0.1));
                        AssertAlignedPanels(host, source, target, parameters);
                        AssertFields(metadata);
                        Drag(splitter, -10000);
                        Layout(host, size.Width, size.Height);
                        Assert.That(grid.ColumnDefinitions[column - 1].ActualWidth,
                            Is.EqualTo(grid.ColumnDefinitions[column - 1].MinWidth).Within(0.1));
                        AssertAlignedPanels(host, source, target, parameters);
                        AssertFields(metadata);
                        for (var index = 0; index < originalWidths.Length; index++)
                            grid.ColumnDefinitions[index].Width = originalWidths[index];
                        Layout(host, size.Width, size.Height);
                    }
                    foreach (var button in Descendants<Button>(host).Where(x => x.Visibility == Visibility.Visible))
                    {
                        var point = button.TranslatePoint(new Point(), host);
                        Assert.That(point.X + button.ActualWidth, Is.LessThanOrEqualTo(size.Width + 0.1));
                        Assert.That(point.Y + button.ActualHeight, Is.LessThanOrEqualTo(size.Height + 0.1));
                    }
                }
                Assert.That(Keyboard.FocusedElement, Is.SameAs(focus));
                Assert.That(window.IsVisible, Is.False);
            }
            finally { window.Close(); }
        });
    }

    private static void AssertAlignedPanels(FrameworkElement host, params FrameworkElement[] panels)
    {
        var top = panels[0].TranslatePoint(new Point(), host).Y;
        var bottom = top + panels[0].ActualHeight;
        foreach (var panel in panels)
        {
            var position = panel.TranslatePoint(new Point(), host);
            Assert.That(position.Y, Is.EqualTo(top).Within(0.1));
            Assert.That(position.Y + panel.ActualHeight, Is.EqualTo(bottom).Within(0.1));
        }
    }

    private static void AssertFields(BoneMappingMetaDataView metadata)
    {
        var rootScale = (TextBox)metadata.FindName("RootScaleInput");
        var left = rootScale.TranslatePoint(new Point(), metadata).X;
        var right = left + rootScale.ActualWidth;
        foreach (var prefix in new[] { "RootScale", "DisplayOffset", "PositionOffset", "RotationOffset", "BoneScale" })
        {
            var label = (TextBlock)metadata.FindName(prefix + "Label");
            var field = (FrameworkElement)(metadata.FindName(prefix + "Input") ?? metadata.FindName(prefix + "Values"));
            Assert.That(label.TranslatePoint(new Point(), metadata).X, Is.EqualTo(0).Within(0.1));
            Assert.That(label.TranslatePoint(new Point(0, label.ActualHeight / 2), metadata).Y,
                Is.EqualTo(field.TranslatePoint(new Point(0, field.ActualHeight / 2), metadata).Y).Within(0.1));
            var textBoxes = field is TextBox textBox ? new[] { textBox } : Descendants<TextBox>(field).ToArray();
            Assert.That(textBoxes[0].TranslatePoint(new Point(), metadata).X, Is.EqualTo(left).Within(0.1));
            Assert.That(textBoxes[^1].TranslatePoint(new Point(textBoxes[^1].ActualWidth, 0), metadata).X,
                Is.EqualTo(right).Within(0.1));
            Assert.That(textBoxes.All(x => x.ActualWidth >= 45 && x.HorizontalContentAlignment == HorizontalAlignment.Left), Is.True);
            Assert.That(textBoxes.Max(x => x.ActualWidth) - textBoxes.Min(x => x.ActualWidth), Is.LessThan(0.1));
        }
        foreach (var name in new[] { "RelativeScaleOption", "BoneActions" })
        {
            var element = (FrameworkElement)metadata.FindName(name);
            Assert.That(element.TranslatePoint(new Point(), metadata).X, Is.EqualTo(left).Within(0.1));
        }
    }

    private static void Drag(GridSplitter splitter, double horizontal)
    {
        splitter.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        splitter.RaiseEvent(new DragDeltaEventArgs(horizontal, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        splitter.RaiseEvent(new DragCompletedEventArgs(horizontal, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
    }
}
