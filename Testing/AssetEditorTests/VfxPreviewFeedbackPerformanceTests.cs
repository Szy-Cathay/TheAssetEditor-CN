using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Editors.VfxEditor;
using GameWorld.Core.Components;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public sealed class VfxPreviewFeedbackPerformanceTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public void EqualFreshFeedbackKeepsAllDiagnosticsAndTheirSelectionWithoutReplacingBindings() => WithContext(context =>
    {
        var view = new VfxEditorView { DataContext = context.Editor };
        context.Editor.InspectorTabIndex = 2;
        Layout(view);
        var list = (ItemsControl)view.FindName("PreviewDiagnostics");
        if (list is ListBox selection) selection.SelectedIndex = 17;
        var before = context.Editor.ScenePreviewDiagnostics;
        var notifications = 0;
        context.Editor.PropertyChanged += (_, change) => { if (change.PropertyName == nameof(VfxEditorViewModel.ScenePreviewDiagnostics)) notifications++; };
        for (var index = 0; index < 20; index++)
        {
            context.Information = Clone(context.Information);
            context.Diagnostics = context.Diagnostics.Select(diagnostic => diagnostic with { Field = new string(diagnostic.Field.AsSpan()) }).ToArray();
            context.Reloaded();
        }
        Assert.AreSame(before, context.Editor.ScenePreviewDiagnostics, "Equivalent diagnostics must not reset the full bound list every reload.");
        Assert.AreEqual(0, notifications);
        Assert.AreEqual(context.Diagnostics.Count, before.Count);
        if (list is ListBox selected) Assert.AreEqual(17, selected.SelectedIndex);
        CollectionAssert.AreEqual(context.Diagnostics.Select(context.Display).ToArray(), before.ToArray());

        var changed = context.Diagnostics.ToArray();
        changed[17] = changed[17] with { Field = "changed-DIAG" };
        context.Diagnostics = changed;
        context.Reloaded();
        Assert.AreEqual(1, notifications);
        Assert.AreEqual(context.Display(changed[17]), context.Editor.ScenePreviewDiagnostics[17]);
        Assert.AreEqual(changed.Length, context.Editor.ScenePreviewDiagnostics.Count);
    });

    [TestMethod]
    public void EquivalentReadbackDoesNotAllocatePerUnrelatedFieldAndStillAppliesAChangedState() => WithContext(context =>
    {
        var first = context.Editor.Layers.SelectMany(layer => layer.Fields).First();
        var original = context.Information;
        var feedback = Enumerable.Range(0, 20).Select(_ => Clone(original)).ToArray();
        var apply = typeof(VfxEditorViewModel).GetMethod("ApplyPreviewFeedback", Private)!;
        var notifications = 0;
        foreach (var field in context.Editor.Layers.SelectMany(layer => layer.Fields)) field.PropertyChanged += (_, _) => notifications++;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        foreach (var info in feedback) apply.Invoke(context.Editor, [info]);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Assert.IsTrue(allocated < 16384, $"Unchanged readback must reuse existing field mapping. Allocated {allocated} bytes.");
        Assert.AreEqual(0, notifications);
        var changedLayers = original.Layers.Select(layer => layer with
        {
            Fields = layer.Fields.Select(field => field.Key == first.PreviewKey
                ? field with { State = VfxPreviewFieldState.Failed, ReasonKeys = new[] { "Vfx.Preview.MissingResource" } } : field).ToArray(),
        }).ToArray();
        apply.Invoke(context.Editor, [Info(changedLayers)]);
        Assert.AreEqual(VfxPreviewFieldState.Failed, first.PreviewState);
        StringAssert.Contains(first.PreviewStateHint, context.Editor.Localization.Get("Vfx.Preview.MissingResource"));
        Assert.IsTrue(notifications > 0);
    });

    [TestMethod]
    public void DiagnosticsVirtualizeAndKeepEveryChineseMessageReachableAcrossThemesAndDpi() => WithContext(context =>
    {
        var previous = ThemesController.CurrentTheme;
        try
        {
            foreach (var theme in Enum.GetValues<ThemeType>())
            {
                ThemesController.SetTheme(theme);
                var view = new VfxEditorView { DataContext = context.Editor };
                context.Editor.InspectorTabIndex = 2;
                Layout(view);
                var list = (ItemsControl)view.FindName("PreviewDiagnostics");
                Assert.AreEqual(800, list.Items.Count);
                Assert.IsNull(list.ItemContainerGenerator.ContainerFromIndex(799), "Unseen diagnostics must not instantiate every text row.");
                Assert.IsTrue(Descendants(list).OfType<TextBlock>().Count() < 150);
                if (list is ListBox scrolling) scrolling.ScrollIntoView(list.Items[799]);
                else Descendants(view).OfType<ScrollViewer>().Last().ScrollToBottom();
                Layout(view);
                Assert.IsNotNull(list.ItemContainerGenerator.ContainerFromIndex(799));
                var last = context.Editor.ScenePreviewDiagnostics[799];
                Assert.IsTrue(Descendants(list).OfType<TextBlock>().Any(text => text.Text == last));
                if (list is ListBox selection)
                {
                    selection.SelectedItems.Clear();
                    selection.SelectedItems.Add(list.Items[798]);
                    selection.SelectedItems.Add(list.Items[799]);
                    var copy = typeof(VfxEditorView).GetMethod("SelectedDiagnosticText", BindingFlags.Static | BindingFlags.NonPublic)!;
                    Assert.AreEqual(string.Join(Environment.NewLine, context.Editor.ScenePreviewDiagnostics.Skip(798)), copy.Invoke(null, [selection]));
                    Assert.IsTrue(ApplicationCommands.Copy.CanExecute(null, selection));
                    selection.SelectedItems.Clear();
                    Assert.IsFalse(ApplicationCommands.Copy.CanExecute(null, selection));
                }
                foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                {
                    var bitmap = new RenderTargetBitmap((int)(1100 * scale), (int)(760 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bitmap.Render(view);
                    Assert.AreEqual((int)(1100 * scale), bitmap.PixelWidth);
                }
                view.DataContext = null;
            }
        }
        finally { ThemesController.SetTheme(previous); }
    });

    private static void WithContext(Action<Context> test) => WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
    {
        using var context = new Context();
        test(context);
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsFalse(context.Editor.UndoCommand.CanExecute(null));
    });
    private static void Layout(FrameworkElement view)
    {
        var size = new Size(1100, 760);
        view.Measure(size); view.Arrange(new Rect(size)); view.UpdateLayout();
        view.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); view.UpdateLayout();
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
    private static VfxPreviewEditorInfo Info(IEnumerable<VfxPreviewEditorLayerInfo> layers) =>
        (VfxPreviewEditorInfo)typeof(VfxPreviewEditorInfo).GetConstructors(Private).Single().Invoke([layers]);
    private static VfxPreviewEditorInfo Clone(VfxPreviewEditorInfo source) => Info(source.Layers.Select(layer => layer with
    {
        Fields = layer.Fields.Select(field => field with { Key = new string(field.Key.AsSpan()), ReasonKeys = field.ReasonKeys.ToArray() }).ToArray(),
    }));

    private sealed class Context : IDisposable
    {
        public byte[] Original { get; } = Encoding.UTF8.GetBytes("<vfx revision=\"2\"><emitters><emitter id=\"visible\" type=\"continuous_mesh\"><property name=\"Enable\" type=\"bool\" value=\"true\"/><property name=\"ParticleSimulationSpace\" value=\"WORLD_SPACE\"/><property name=\"LifeTime\" min=\"1\" max=\"2\"/><property name=\"SpawnRate\" min=\"4\" max=\"4\"/></emitter></emitters><behaviour_chains><behaviour_chain id=\"visible\"><quality value=\"0\"><modifier fragment=\"ps_texture\"><texture_property name=\"diffuse\" path=\"vfx/DIAG.dds\"/></modifier></quality></behaviour_chain></behaviour_chains></vfx>");
        public PackFile File { get; }
        public VfxEditorViewModel Editor { get; }
        public VfxPreviewEditorInfo Information { get; set; }
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics { get; set; }
        private readonly VfxScenePreview _scene;
        private readonly VfxPreviewComponent _component;
        public Context()
        {
            File = PackFile.CreateFromBytes("feedback-DIAG.xml", Original);
            Information = VfxPreviewInspector.Inspect(new(Original, "vfx/feedback-DIAG.xml", _ => [1, 2, 3])).EditorInfo;
            Diagnostics = Enumerable.Range(0, 800).Select(index => new VfxPreviewDiagnostic("vfx/feedback-DIAG.xml", "效果", "字段" + index, "Vfx.Preview.Unmapped")).ToArray();
            var runtime = new Mock<IVfxPreview>();
            runtime.SetupGet(value => value.EditorInfo).Returns(() => Information);
            runtime.SetupGet(value => value.EmitterCount).Returns(1);
            runtime.SetupGet(value => value.TotalEmitterCount).Returns(1);
            _component = new(Mock.Of<IVfxPreviewFactory>(), Mock.Of<IDeviceResolver>(), null!);
            _scene = new(Mock.Of<IWpfGame>(), Mock.Of<IComponentInserter>(),
                new View3DCoreComponentSet(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!), _component);
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns("vfx/feedback-DIAG.xml");
            var localization = new LocalizationManager(); localization.LoadLanguage();
            Editor = new(files.Object, Mock.Of<IFileSaveService>(), Mock.Of<IStandardDialogs>(), Mock.Of<IEditorCreator>(), localization, _scene);
            Editor.LoadFile(File);
            typeof(VfxPreviewComponent).GetField("_preview", Private)!.SetValue(_component, runtime.Object);
            ((DispatcherTimer)typeof(VfxEditorViewModel).GetField("_scenePreviewTimer", Private)!.GetValue(Editor)!).Stop();
            Reloaded();
        }
        public string Display(VfxPreviewDiagnostic value) => Editor.Localization.GetFormat("Vfx.Preview.Diagnostic", value.Layer, value.Field, Editor.Localization.Get(value.ReasonKey));
        public void Reloaded()
        {
            typeof(VfxPreviewComponent).GetProperty("Diagnostics")!.SetValue(_component, Diagnostics);
            typeof(VfxEditorViewModel).GetMethod("ScenePreviewReloaded", Private)!.Invoke(Editor, null);
        }
        public void Dispose() { Editor.Close(); _scene.Dispose(); _component.Dispose(); }
    }
}
