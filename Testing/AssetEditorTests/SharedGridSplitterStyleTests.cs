using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class SharedGridSplitterStyleTests
{
    [TestCase("AeVerticalGridSplitterStyle", "⁞")]
    [TestCase("AeHorizontalGridSplitterStyle", "⋯")]
    public void GripStyle_LoadsExpectedTemplate(string resourceKey, string grip)
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

            var template = style.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == Control.TemplateProperty)
                .Value as ControlTemplate;
            NUnitAssert.That(template, Is.Not.Null);
            var root = template!.LoadContent() as Grid;
            NUnitAssert.That(root, Is.Not.Null);
            var button = root!.Children.OfType<Button>().Single();
            var overlay = root.Children.OfType<Rectangle>().Single();

            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(button.Content, Is.EqualTo(grip));
                NUnitAssert.That(
                    (overlay.Fill as SolidColorBrush)?.Color.A,
                    Is.Zero);
            });
        });
    }
}
