using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Xaml.Behaviors;
using NUnit.Framework;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.MathViews;
using Shared.Ui.Common.Behaviors;
using static AssetEditorTests.PlaybackAndViewportRefinementTests;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class NumericDragTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void NumericDrag_PreservesBindingAndKeyboardInputAndCanCancel(ThemeType theme)
    {
        WithTheme(theme, () =>
        {
            var model = new DoubleViewModel(1);
            var text = new TextBox { Width = 160, HorizontalContentAlignment = HorizontalAlignment.Left };
            text.SetBinding(TextBox.TextProperty, new Binding(nameof(model.TextValue))
            {
                Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus
            });
            var drag = new TextBoxInputBehavior { InputMode = TextBoxInputMode.DecimalInput, IsDragEnabled = true, DragStep = 0.1, DragMinimum = 0, DragMaximum = 2 };
            Interaction.GetBehaviors(text).Add(drag);
            var started = 0;
            var cancelled = false;
            text.AddHandler(TextBoxInputBehavior.NumericDragStartedEvent, new RoutedEventHandler((_, _) => started++));
            text.AddHandler(TextBoxInputBehavior.NumericDragCompletedEvent,
                new EventHandler<NumericDragCompletedEventArgs>((_, args) => cancelled = args.Cancelled));
            Layout(text, 160, 28);

            Assert.That(drag.BeginNumericDrag(), Is.True);
            drag.ApplyNumericDrag(5, false);
            Assert.That(model.Value, Is.EqualTo(1.5).Within(0.000001));
            drag.ApplyNumericDrag(10, true);
            Assert.That(model.Value, Is.EqualTo(1.6).Within(0.000001));
            drag.ApplyNumericDrag(200, false);
            Assert.That(model.Value, Is.EqualTo(2));
            drag.FinishNumericDrag(true);
            Assert.That(model.Value, Is.EqualTo(1));
            Assert.That(started, Is.EqualTo(1));
            Assert.That(cancelled, Is.True);
            Assert.That(BindingOperations.IsDataBound(text, TextBox.TextProperty), Is.True);

            text.SetCurrentValue(TextBox.TextProperty, "0.75");
            text.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.That(model.Value, Is.EqualTo(0.75));
            Render(text, $"numeric-drag-{theme}", 1);
        });
    }

    [TestCase("")]
    [TestCase("-")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("模型名称")]
    public void InvalidNumbers_CannotStartDrag(string value)
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var text = new TextBox { Text = value };
            var drag = new TextBoxInputBehavior { InputMode = TextBoxInputMode.DecimalInput, IsDragEnabled = true };
            Interaction.GetBehaviors(text).Add(drag);
            Assert.That(drag.BeginNumericDrag(), Is.False);
            Assert.That(text.Text, Is.EqualTo(value));
        });
    }

    [Test]
    public void DisabledReadOnlyAndUnoptedFields_CannotStartDrag()
    {
        WithTheme(ThemeType.DarkTheme, () =>
        {
            var text = new TextBox { Text = "12" };
            var drag = new TextBoxInputBehavior { InputMode = TextBoxInputMode.DecimalInput };
            Interaction.GetBehaviors(text).Add(drag);
            Assert.That(drag.BeginNumericDrag(), Is.False);
            drag.IsDragEnabled = true;
            text.IsReadOnly = true;
            Assert.That(drag.BeginNumericDrag(), Is.False);
            text.IsReadOnly = false;
            text.IsEnabled = false;
            Assert.That(drag.BeginNumericDrag(), Is.False);
        });
    }
}
