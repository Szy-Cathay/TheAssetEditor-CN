using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.VfxEditor;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Vfx;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class VfxSliderLiveBindingTests
{
    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CreatingEitherSliderTemplatePreservesPreciseTextBytesAndHistory(bool vector) => WithEditor(context =>
    {
        var value = context.Value(vector);
        var original = value.Text;
        var host = CreateTemplate(value, vector);
        var slider = Descendants(host).OfType<Slider>().Single();
        Assert.AreEqual(double.Parse(original, CultureInfo.InvariantCulture), slider.Value);
        slider.SetCurrentValue(RangeBase.ValueProperty, slider.Value);
        Settle(host);
        Assert.AreEqual(original, value.Text, "Opening an editor or transferring the same number must not reformat its source text.");
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsFalse(context.Editor.UndoCommand.CanExecute(null));
        Assert.IsFalse(context.Editor.RedoCommand.CanExecute(null));
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        context.Save.VerifyNoOtherCalls();
    });

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void BothSliderTemplatesWriteDuringMovementAndPreserveUndoRedo(bool vector) => WithEditor(context =>
    {
        var value = context.Value(vector);
        var original = value.Text;
        var host = CreateTemplate(value, vector);
        var slider = Descendants(host).OfType<Slider>().Single();
        Assert.IsNotNull(BindingOperations.GetBindingExpression(slider, RangeBase.ValueProperty));
        // SliderScrubbing uses this same DP operation while the button is still held.
        slider.SetCurrentValue(RangeBase.ValueProperty, 1.75);
        Settle(host);
        Assert.AreEqual("1.75", value.Text, "The source must change without MouseUp, KeyUp or an explicit UpdateSource call.");
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        Assert.IsTrue(context.Editor.UndoCommand.CanExecute(null));
        Assert.AreEqual(vector ? "float3(1.75,-2.3456789012345678,3.4567890123456789)" : "1.75",
            context.Field(vector).Model.Attributes.Single().Value);
        context.Editor.UndoCommand.Execute(null);
        host.Content = context.Value(vector);
        Settle(host);
        Assert.AreEqual(original, context.Value(vector).Text);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsTrue(context.Editor.RedoCommand.CanExecute(null), "Rebinding an unchanged value must not create a new edit and clear Redo.");
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        context.Editor.RedoCommand.Execute(null);
        host.Content = context.Value(vector);
        Settle(host);
        Assert.AreEqual("1.75", context.Value(vector).Text);
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        context.Save.VerifyNoOtherCalls();
    });

    [DataTestMethod]
    [DataRow(false, "123.45678901234567")]
    [DataRow(true, "123.45678901234567")]
    [DataRow(false, "-123.45678901234567")]
    [DataRow(true, "-123.45678901234567")]
    public void TypedValuesOutsideTheExistingSliderRangeAreNotClippedOrReformatted(bool vector, string typed) => WithEditor(context =>
    {
        var value = context.Value(vector);
        var host = CreateTemplate(value, vector);
        var slider = Descendants(host).OfType<Slider>().Single();
        var text = Descendants(host).OfType<TextBox>().Single(box =>
            BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path.Path == nameof(VfxValueViewModel.Text));
        var number = double.Parse(typed, CultureInfo.InvariantCulture);
        Assert.IsTrue(number > slider.Maximum || number < slider.Minimum);
        text.SetCurrentValue(TextBox.TextProperty, typed);
        Settle(host);
        Assert.AreEqual(typed, value.Text,
            "The range only limits pointer adjustment; a source-to-target clamp must not be written back to valid text input.");
        Assert.IsFalse(value.HasError);
        Assert.AreEqual(number, value.Number);
        Assert.AreEqual(vector ? $"float3({typed},-2.3456789012345678,3.4567890123456789)" : typed,
            context.Field(vector).Model.Attributes.Single().Value);
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        context.Editor.UndoCommand.Execute(null);
        host.Content = context.Value(vector);
        Settle(host);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsTrue(context.Editor.RedoCommand.CanExecute(null));
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        context.Editor.RedoCommand.Execute(null);
        host.Content = context.Value(vector);
        Settle(host);
        Assert.AreEqual(typed, context.Value(vector).Text,
            "A replacement view model must transfer its newly computed range without clipping the restored value.");
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        context.Save.VerifyNoOtherCalls();
    });

    [TestMethod]
    public void IntegerSliderKeepsTheExistingRoundedWriteAndUndoBehavior() => WithEditor(context =>
    {
        var field = context.Editor.Layers.Single(layer => layer.Model.Kind == "Emitter").Fields.Single(item => item.Model.Name == "SpawnRate");
        var host = CreateTemplate(field.Values.Single(), false);
        var slider = Descendants(host).OfType<Slider>().Single();
        slider.SetCurrentValue(RangeBase.ValueProperty, 3.6);
        Settle(host);
        Assert.AreEqual("4", field.Values.Single().Text);
        Assert.IsFalse(context.Editor.HasErrors);
        context.Editor.UndoCommand.Execute(null);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
    });

    private static ContentControl CreateTemplate(VfxValueViewModel value, bool vector)
    {
        var view = new VfxEditorView();
        var key = vector ? (object)"VectorValue" : new DataTemplateKey(typeof(VfxValueViewModel));
        var host = new ContentControl { Content = value, ContentTemplate = (DataTemplate)view.Resources[key] };
        host.Resources.MergedDictionaries.Add(view.Resources);
        Settle(host);
        return host;
    }

    private static void Settle(FrameworkElement host)
    {
        host.ApplyTemplate();
        host.Measure(new Size(500, 400));
        host.Arrange(new Rect(0, 0, 500, 400));
        host.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        host.UpdateLayout();
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void WithEditor(Action<Context> test) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var context = new Context();
            test(context);
        });

    private sealed class Context : IDisposable
    {
        // DIAG: binding and source-preservation controls, with no renderer or visual-parity claim.
        private const string Source = """
            <vfx revision="2" construction_type="1"><emitters><emitter id="diagnostic" type="continuous_mesh">
              <property name="LifeTime" type="float" value="1.2345678901234567"/>
              <property name="ParticleScale" value="float3(1.2345678901234567,-2.3456789012345678,3.4567890123456789)"/>
              <property name="SpawnRate" type="int" value="3"/>
            </emitter></emitters><behaviour_chains/><effect_render_stacks/></vfx>
            """;

        public byte[] Original { get; } = Encoding.UTF8.GetBytes(Source);
        public PackFile File { get; }
        public Mock<IFileSaveService> Save { get; } = new();
        public VfxEditorViewModel Editor { get; }
        public VfxDocument Document => (VfxDocument)typeof(VfxEditorViewModel)
            .GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Editor)!;

        public Context()
        {
            File = PackFile.CreateFromBytes("slider-binding-diagnostic.xml", Original);
            var files = new Mock<IPackFileService>();
            files.Setup(value => value.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns("vfx\\slider-binding-diagnostic.xml");
            var localization = new LocalizationManager();
            localization.LoadLanguage();
            Editor = new VfxEditorViewModel(files.Object, Save.Object, Mock.Of<IStandardDialogs>(),
                Mock.Of<IEditorCreator>(), localization);
            Editor.LoadFile(File);
        }

        public VfxFieldViewModel Field(bool vector) => Editor.Layers.Single(layer => layer.Model.Kind == "Emitter")
            .Fields.Single(field => field.Model.Name == (vector ? "ParticleScale" : "LifeTime"));
        public VfxValueViewModel Value(bool vector) => Field(vector).Values[0];
        public void Dispose() => Editor.Close();
    }
}
