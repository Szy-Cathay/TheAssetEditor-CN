using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests
{

    public class SharedGridSplitterStyleTests
    {
        [TestCase("AeVerticalGridSplitterStyle", Orientation.Vertical)]
        [TestCase("AeHorizontalGridSplitterStyle", Orientation.Horizontal)]
        public void GripStyle_LoadsCenteredVectorTemplate(
            string resourceKey,
            Orientation orientation)
        {
            WpfTestApplicationHost.Invoke(_ =>
            {
                ResourceDictionary? resources = null;
                Exception? loadError = null;
                try
                {
                    resources = new ResourceDictionary
                    {
                        Source = new Uri(
                            "pack://application:,,,/Shared.Ui;component/Common/Styles/GridSplitterStyles.xaml"),
                    };
                }
                catch (Exception exception)
                {
                    loadError = exception;
                }

                NUnitAssert.That(loadError, Is.Null, loadError?.ToString());
                NUnitAssert.That(resources, Is.Not.Null);
                var style = resources![resourceKey] as Style;
                NUnitAssert.That(style, Is.Not.Null);
                NUnitAssert.That(style!.TargetType, Is.EqualTo(typeof(GridSplitter)));

                var setters = style.Setters
                    .OfType<Setter>()
                    .ToDictionary(setter => setter.Property);

                var template = setters[Control.TemplateProperty]
                    .Value as ControlTemplate;
                NUnitAssert.That(template, Is.Not.Null);
                var root = template!.LoadContent() as Border;
                NUnitAssert.That(root, Is.Not.Null);
                var grip = root!.Child as StackPanel;

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(grip, Is.Not.Null);
                    NUnitAssert.That(grip!.Orientation, Is.EqualTo(orientation));
                    NUnitAssert.That(grip.Children.OfType<Ellipse>().Count(), Is.EqualTo(3));
                    NUnitAssert.That(root.HorizontalAlignment, Is.EqualTo(HorizontalAlignment.Stretch));
                    NUnitAssert.That(root.VerticalAlignment, Is.EqualTo(VerticalAlignment.Stretch));
                    NUnitAssert.That(
                        setters[GridSplitter.ResizeBehaviorProperty].Value,
                        Is.EqualTo(GridResizeBehavior.PreviousAndNext));
                    NUnitAssert.That(
                        setters[GridSplitter.ResizeDirectionProperty].Value,
                        Is.EqualTo(
                            orientation == Orientation.Vertical
                                ? GridResizeDirection.Columns
                                : GridResizeDirection.Rows));
                    NUnitAssert.That(
                        setters[
                            orientation == Orientation.Vertical
                                ? FrameworkElement.WidthProperty
                                : FrameworkElement.HeightProperty].Value,
                        Is.EqualTo(5d));
                });
            });
        }
    }
}
