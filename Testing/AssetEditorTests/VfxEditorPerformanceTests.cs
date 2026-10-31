using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
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
public class VfxEditorPerformanceTests
{
    [TestMethod]
    public void ContinuousWritesAndCommandQueriesDoNotScanUnrelatedAuthoredLayers() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var small = Measure(3);
            var large = Measure(600);
            if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_EDIT_VALIDATION_EVIDENCE") is { Length: > 0 } path)
                System.IO.File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
                {
                    Small = new { small.AllocatedBytes, small.ElapsedMs },
                    Large = new { large.AllocatedBytes, large.ElapsedMs },
                }));
            Assert.IsTrue(large.AllocatedBytes <= small.AllocatedBytes * 3 + 16384,
                $"Editing one field must not allocate work for unrelated layers. 3 layers: {small.AllocatedBytes}; 600 layers: {large.AllocatedBytes} bytes.");

            static (long AllocatedBytes, double ElapsedMs) Measure(int count)
            {
                using var context = new Context(count);
                var value = context.Field("LifeTime").Values[0];
                for (var index = 0; index < 5; index++) value.Text = index % 2 == 0 ? "0.75" : "0.5";
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var started = System.Diagnostics.Stopwatch.GetTimestamp();
                for (var index = 0; index < 30; index++)
                {
                    value.Text = index % 2 == 0 ? "1.5" : "0.5";
                    Assert.IsTrue(context.Editor.SaveCommand.CanExecute(null));
                    Assert.IsTrue(context.Editor.SaveAsCommand.CanExecute(null));
                    Assert.IsTrue(context.Editor.AddReferenceCommand.CanExecute(null));
                    Assert.IsTrue(context.Editor.DuplicateLayerCommand.CanExecute(null));
                    Assert.IsTrue(context.Editor.RemoveLayerCommand.CanExecute(null));
                    Assert.IsFalse(context.Editor.HasErrors);
                }
                var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                while (context.Editor.UndoCommand.CanExecute(null)) context.Editor.UndoCommand.Execute(null);
                CollectionAssert.AreEqual(context.Original, context.Document.Write());
                CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
                context.Save.VerifyNoOtherCalls();
                return (bytes, elapsed);
            }
        });

    [DataTestMethod]
    [DataRow("LifeTime", "1.5")]
    [DataRow("ParticleScale", "1.75")]
    public void OrdinaryValuesNotifyGlobalStateOnceWithoutRefreshingEveryLayer(string name, string value) => WithEditor(40, context =>
    {
        var changes = 0;
        context.Editor.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxEditorViewModel.HasErrors)) changes++; };
        var statusChanges = TrackStatusChanges(context.Editor);
        context.Field(name).Values[0].Text = value;
        Assert.AreEqual(1, changes, "A valid write already updates editor state through Apply.");
        Assert.AreEqual(0, statusChanges.Values.Sum(), "Unrelated layer activation labels must not refresh for a numeric edit.");
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        Assert.IsTrue(context.Editor.UndoCommand.CanExecute(null));
        context.Editor.UndoCommand.Execute(null);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        context.Save.VerifyNoOtherCalls();
    });

    [TestMethod]
    public void EnableWriteRefreshesOnlyItsOwningLayerOnce() => WithEditor(40, context =>
    {
        var owner = context.Editor.Layers[0];
        var changes = TrackStatusChanges(context.Editor);
        context.Field("Enable").Values[0].Boolean = false;
        Assert.AreEqual(1, changes[owner]);
        Assert.AreEqual(0, changes.Where(item => !ReferenceEquals(item.Key, owner)).Sum(item => item.Value));
        StringAssert.Contains(owner.Subtitle, context.Editor.Localization.Get("Vfx.Layer.DisabledAtQuality"));
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        context.Editor.UndoCommand.Execute(null);
        Assert.IsFalse(context.Editor.Layers[0].Subtitle.Contains(context.Editor.Localization.Get("Vfx.Layer.DisabledAtQuality"), StringComparison.Ordinal));
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
    });

    [TestMethod]
    public void InvalidTextRecoveryWithoutAnXmlWriteStillRefreshesEditorValidation() => WithEditor(3, context =>
    {
        var field = context.Field("LifeTime");
        var value = field.Values[0];
        var changes = 0;
        context.Editor.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxEditorViewModel.HasErrors)) changes++; };
        value.Text = "-";
        Assert.IsTrue(context.Editor.HasErrors);
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        Assert.IsFalse(context.Editor.SaveCommand.CanExecute(null));
        Assert.IsTrue(context.Editor.UndoCommand.CanExecute(null), "Undo remains available to discard invalid text without changing the document.");
        Assert.AreEqual(1, changes);
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        value.Text = "invalid";
        Assert.IsTrue(context.Editor.HasErrors);
        Assert.AreEqual(1, changes, "Another invalid spelling does not change the editor's validation or source state.");
        value.Text = "0.25";
        Assert.AreEqual(2, changes, "Returning to the unchanged authored value must still clear the prior error.");
        Assert.IsFalse(context.Editor.HasErrors);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsFalse(context.Editor.UndoCommand.CanExecute(null));
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        context.Save.VerifyNoOtherCalls();
    });

    [TestMethod]
    public void ValidationTracksIndependentErrorsOnUnselectedLayers() => WithEditor(3, context =>
    {
        var first = context.Field("LifeTime").Values[0];
        var second = context.Editor.Layers[1].Fields.Single(field => field.Model.Name == "LifeTime").Values[0];
        first.Text = "-";
        second.Text = "invalid";
        first.Text = "0.25";
        Assert.IsTrue(context.Editor.HasErrors, "Recovering one field must not clear another layer's invalid text.");
        Assert.IsFalse(context.Editor.SaveCommand.CanExecute(null));
        second.Text = "0.25";
        Assert.IsFalse(context.Editor.HasErrors);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsTrue(context.Editor.SaveCommand.CanExecute(null));
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        context.Save.VerifyNoOtherCalls();
    });

    [TestMethod]
    public void NumericRangeValidationChangesOnlyOnceForAnAppliedValue() => WithEditor(3, context =>
    {
        var changes = 0;
        context.Editor.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxEditorViewModel.HasErrors)) changes++; };
        context.Field("LifeTime").Values[0].Text = "3";
        Assert.AreEqual(1, changes);
        Assert.IsTrue(context.Editor.HasErrors, "A numeric value may still invalidate the field's min/max range.");
        Assert.IsFalse(context.Editor.SaveCommand.CanExecute(null));
        context.Field("LifeTime").Values[0].Text = "0.5";
        Assert.AreEqual(2, changes);
        Assert.IsFalse(context.Editor.HasErrors);
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        context.Editor.UndoCommand.Execute(null);
        Assert.IsTrue(context.Editor.HasErrors);
        context.Editor.LoadFile(context.File);
        Assert.IsFalse(context.Editor.HasErrors);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
    });

    [TestMethod]
    public void CurvePointWriteNotifiesOnceAndInvalidRecoveryRetainsUndoState() => WithEditor(3, context =>
    {
        var curve = context.Field("curve_alpha").Curve!;
        var value = curve.SelectedPoint!.Value;
        var changes = 0;
        context.Editor.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxEditorViewModel.HasErrors)) changes++; };
        value.Text = "0.5";
        Assert.AreEqual(1, changes);
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        value.Text = "invalid";
        Assert.AreEqual(2, changes);
        Assert.IsTrue(context.Editor.HasErrors);
        value.Text = "0.5";
        Assert.AreEqual(3, changes);
        Assert.IsFalse(context.Editor.HasErrors);
        context.Editor.UndoCommand.Execute(null);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
    });

    [TestMethod]
    public void ColourPickerWriteNotifiesOnceWithoutRefreshingUnrelatedLayers() => WithEditor(3, context =>
    {
        var picker = context.Field("curve_colour").Colours.Single().Picker;
        var changes = 0;
        context.Editor.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxEditorViewModel.HasErrors)) changes++; };
        var statusChanges = TrackStatusChanges(context.Editor);
        picker.PickedColor = Color.FromRgb(120, 160, 200);
        picker.OnHandleColourChanged();
        Assert.AreEqual(1, changes);
        Assert.AreEqual(0, statusChanges.Values.Sum());
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        context.Editor.UndoCommand.Execute(null);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
    });

    [TestMethod]
    public void GraphEditThatClearsInvalidPointTextRefreshesValidationAfterValueSynchronization() => WithEditor(3, context =>
    {
        var curve = context.Field("curve_alpha").Curve!;
        curve.SelectedPoint!.Value.Text = "invalid";
        Assert.IsTrue(context.Editor.HasErrors);
        Assert.IsFalse(context.Editor.SaveCommand.CanExecute(null));
        curve.MoveSelected(0, .75);
        Assert.IsFalse(context.Editor.HasErrors);
        Assert.IsTrue(context.Editor.SaveCommand.CanExecute(null));
        Assert.IsTrue(context.Editor.HasUnsavedChanges);
        context.Editor.UndoCommand.Execute(null);
        Assert.IsFalse(context.Editor.HasErrors);
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
    });

    [TestMethod]
    public void GroupedEffectListKeepsRealizedRowsBoundedWhenScrollingManyAuthoredLayers() => WithEditor(600, context =>
    {
        var view = new VfxEditorView { DataContext = context.Editor };
        try
        {
            Settle(view);
            var list = (ListBox)view.FindName("LayerList");
            Assert.AreEqual(context.Editor.Layers.Count, list.Items.Count);
            Assert.IsTrue(VirtualizingPanel.GetIsVirtualizing(list));
            Assert.IsTrue(VirtualizingPanel.GetIsVirtualizingWhenGrouping(list));
            Assert.AreEqual(VirtualizationMode.Recycling, VirtualizingPanel.GetVirtualizationMode(list));
            Assert.IsTrue(ScrollViewer.GetCanContentScroll(list));
            var firstCount = Descendants(list).OfType<ListBoxItem>().Count();
            Assert.IsTrue(firstCount > 0 && firstCount < 80, $"Initial realized rows: {firstCount} / {list.Items.Count}");
            var target = context.Editor.Layers[450];
            list.ScrollIntoView(target);
            Settle(view);
            Assert.IsTrue(Descendants(list).OfType<ListBoxItem>().Any(item => ReferenceEquals(item.DataContext, target)),
                "A distant authored effect must remain reachable.");
            var scrolledCount = Descendants(list).OfType<ListBoxItem>().Count();
            Assert.IsTrue(scrolledCount > 0 && scrolledCount < 80, $"Scrolled realized rows: {scrolledCount} / {list.Items.Count}");
            var rendered = new RenderTargetBitmap(1200, 700, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(view);
            if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_UI_EVIDENCE") is { Length: > 0 } evidencePath)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(rendered));
                using var stream = System.IO.File.Create(evidencePath);
                encoder.Save(stream);
            }
            Assert.IsFalse(context.Editor.HasUnsavedChanges);
            Assert.IsFalse(context.Editor.UndoCommand.CanExecute(null));
            CollectionAssert.AreEqual(context.Original, context.Document.Write());
            CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        }
        finally { view.DataContext = null; }
    });

    private static Dictionary<VfxLayerViewModel, int> TrackStatusChanges(VfxEditorViewModel editor)
    {
        var changes = editor.Layers.ToDictionary(layer => layer, _ => 0);
        foreach (var layer in editor.Layers)
            layer.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxLayerViewModel.Subtitle)) changes[layer]++; };
        return changes;
    }

    private static void Settle(FrameworkElement view)
    {
        view.Measure(new Size(1200, 700)); view.Arrange(new Rect(0, 0, 1200, 700)); view.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        view.UpdateLayout();
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

    private static void WithEditor(int count, Action<Context> test) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var context = new Context(count);
            test(context);
        });

    private sealed class Context : IDisposable
    {
        public byte[] Original { get; }
        public PackFile File { get; }
        public Mock<IFileSaveService> Save { get; } = new();
        public VfxEditorViewModel Editor { get; }
        public VfxDocument Document => (VfxDocument)typeof(VfxEditorViewModel)
            .GetField("_document", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(Editor)!;

        public Context(int count)
        {
            // DIAG: XML, notifications and WPF container counts only; no renderer or visual-parity sample.
            var emitters = new XElement("emitters");
            for (var index = 0; index < count; index++)
                emitters.Add(new XElement("emitter",
                    new XAttribute("id", "diagnostic_" + index), new XAttribute("type", "continuous_mesh"),
                    new XElement("property", new XAttribute("name", "Enable"), new XAttribute("quality", "0"), new XAttribute("value", "true")),
                    new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("quality", "0"), new XAttribute("min", "0.25"), new XAttribute("max", "2.0")),
                    new XElement("property", new XAttribute("name", "ParticleScale"), new XAttribute("quality", "0"), new XAttribute("value", "float3(1,2,3)"))));
            var alpha = new XElement("property", new XAttribute("name", "curve_alpha"), new XAttribute("type", "curve"),
                "alpha_min,8388608,1;0.0,0.25,linear,linear,-0.05,0.0,0.05,0.0,0;1.0,1.0,linear,linear,-0.05,0.0,0.05,0.0,0:alpha,0;0,0");
            var colour = new XElement("property", new XAttribute("name", "curve_colour"), new XAttribute("type", "curve"),
                "r_min,16711680,0;0.0,0.1,linear,linear,-0.05,0.0,0.05,0.0,1|g_min,16711680,0;0.0,0.2,linear,linear,-0.05,0.0,0.05,0.0,1|b_min,16711680,0;0.0,0.3,linear,linear,-0.05,0.0,0.05,0.0,1:r,0;0,0|g,0;1,1|b,0;2,2");
            var chain = new XElement("behaviour_chain", new XAttribute("id", "diagnostic_0"),
                new XElement("quality", new XAttribute("value", "0"), new XElement("modifier", new XAttribute("fragment", "vs_colour"), alpha, colour)));
            var effect = new XElement("vfx", new XAttribute("revision", "2"), new XAttribute("construction_type", "1"), emitters,
                new XElement("behaviour_chains", chain),
                new XElement("effect_render_stacks"));
            Original = Encoding.UTF8.GetBytes(effect.ToString(SaveOptions.DisableFormatting));
            File = PackFile.CreateFromBytes("editor-performance-diagnostic.xml", Original);
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns("vfx\\editor-performance-diagnostic.xml");
            var localization = new LocalizationManager(); localization.LoadLanguage();
            Editor = new VfxEditorViewModel(files.Object, Save.Object, Mock.Of<IStandardDialogs>(), Mock.Of<IEditorCreator>(), localization);
            Editor.LoadFile(File);
        }

        public VfxFieldViewModel Field(string name) => Editor.Layers[0].Fields.Single(field => field.Model.Name == name);
        public void Dispose() => Editor.Close();
    }
}
