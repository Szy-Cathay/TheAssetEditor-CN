using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Editors.VfxEditor;
using GameWorld.Core.Components;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Vfx;
using Size = System.Windows.Size;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class VfxEditorGuidedEditingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestMethod]
    public void EditingALayerSelectsItsParametersWithoutChangingThePreviewCombination() => WithEditor(context =>
    {
        var included = context.Editor.Layers.Select(layer => layer.IsIncludedInPreview).ToArray();
        var target = context.Editor.Layers.Single(layer => layer.Model.Name == "second" && layer.CanPreview);
        target.EditLayerCommand.Execute(null);
        Assert.AreSame(target, context.Editor.SelectedLayer);
        Assert.AreEqual(1, context.Editor.InspectorTabIndex);
        CollectionAssert.AreEqual(included, context.Editor.Layers.Select(layer => layer.IsIncludedInPreview).ToArray());
        StringAssert.Contains(context.Editor.SelectedLayerSource, "guided-DIAG.xml");
        StringAssert.Contains(context.Editor.SelectedLayerScope, "second");
        var adjustment = context.Editor.Layers.First(layer => layer.Model.Kind == "Override");
        adjustment.EditLayerCommand.Execute(null);
        StringAssert.Contains(context.Editor.SelectedLayerScope, adjustment.Model.Type);
        StringAssert.Contains(context.Editor.SelectedLayerScope, adjustment.Model.Name);
        StringAssert.Contains(context.Editor.SelectedLayerReference, "child");
        AssertPreserved(context);
    });

    [TestMethod]
    public void IdentificationIsIdempotentAndDoesNotReloadSeekOrChangeSelection() => WithEditor(context =>
    {
        var target = context.Editor.Layers.Single(layer => layer.Model.Name == "second" && layer.CanPreview);
        var selection = context.Editor.SelectedLayer;
        var included = context.Editor.Layers.Select(layer => layer.IsIncludedInPreview).ToArray();
        Call(context.Editor, "BeginIdentification", target);
        Call(context.Editor, "BeginIdentification", target);
        context.Runtime.Verify(runtime => runtime.Identify(target.Identity), Times.Once);
        Call(context.Editor, "EndIdentification");
        Call(context.Editor, "EndIdentification");
        context.Runtime.Verify(runtime => runtime.Identify(null), Times.Once);
        Assert.AreSame(selection, context.Editor.SelectedLayer);
        CollectionAssert.AreEqual(included, context.Editor.Layers.Select(layer => layer.IsIncludedInPreview).ToArray());
        Assert.IsFalse(Timer(context.Editor).IsEnabled);
        AssertPreserved(context);
    }, scene: true);

    [TestMethod]
    public void EditorSnapshotCapturesResourceLookupOnTheOwnerAndLeavesOnlyTheReadForTheWorker() => WithEditor(context =>
    {
        var owner = Environment.CurrentManagedThreadId;
        var container = new PackFileContainer("resource-capture-DIAG");
        var dependency = PackFile.CreateFromBytes("texture.dds", [4, 5, 6]);
        var lookups = 0;
        context.Files.Setup(files => files.GetEditablePack()).Returns(container);
        context.Files.Setup(files => files.FindFile(It.IsAny<string>(), container)).Returns(() =>
        {
            Assert.AreEqual(owner, Environment.CurrentManagedThreadId);
            lookups++; return dependency;
        });
        VfxPreviewSnapshot? snapshot = null;
        context.Runtime.Setup(runtime => runtime.BeginInteractiveReload(It.IsAny<VfxPreviewSnapshot>()))
            .Callback<VfxPreviewSnapshot>(value => snapshot = value).Returns(false);
        context.Runtime.Setup(runtime => runtime.Reload(It.IsAny<VfxPreviewSnapshot>()))
            .Callback<VfxPreviewSnapshot>(value => snapshot = value);
        context.PublishAndPump();
        var playbackCalls = context.Runtime.Invocations.Count(invocation => invocation.Method.Name is nameof(IVfxPreview.Reload) or nameof(IVfxPreview.Seek));
        Assert.IsNotNull(snapshot);
        Assert.IsNotNull(snapshot.CaptureResourceRead);
        var read = snapshot.CaptureResourceRead("vfx/texture.dds");
        Assert.IsNotNull(read);
        Assert.AreEqual(1, lookups);
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, Task.Run(read).GetAwaiter().GetResult());
        Assert.AreEqual(1, lookups, "Captured worker invocation cannot touch the editor's Pack lookup.");
        CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, snapshot.Resolve("vfx/texture.dds"));
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsFalse(context.Editor.UndoCommand.CanExecute(null));
        Assert.IsFalse(context.Editor.RedoCommand.CanExecute(null));
        context.Save.VerifyNoOtherCalls();
        Assert.AreEqual(playbackCalls, context.Runtime.Invocations.Count(invocation => invocation.Method.Name is nameof(IVfxPreview.Reload) or nameof(IVfxPreview.Seek)),
            "Capturing or executing resource reads cannot add playback operations after the explicitly requested publication.");
    }, scene: true);

    [TestMethod]
    public void UnloadedLayersAndOverridesCannotRequestAnEmptyIdentification() => WithEditor(context =>
    {
        var target = context.Editor.Layers.Single(layer => layer.Model.Name == "second" && layer.CanPreview);
        target.IsIncludedInPreview = false;
        Timer(context.Editor).Stop();
        context.Runtime.Invocations.Clear();
        Call(context.Editor, "BeginIdentification", target);
        Call(context.Editor, "BeginIdentification", context.Editor.Layers.First(layer => layer.Model.Kind == "Override"));
        Assert.IsFalse(target.CanIdentify);
        Assert.AreEqual(context.Editor.Localization.Get("Vfx.Layer.IdentifyUnavailable"), target.IdentificationHint);
        context.Runtime.Verify(runtime => runtime.Identify(It.IsAny<VfxPreviewLayerIdentity?>()), Times.Never);
        AssertPreserved(context);
    }, scene: true);

    [DataTestMethod]
    [DataRow("IdentifyMouseLeave")]
    [DataRow("IdentifyLostMouseCapture")]
    [DataRow("EditorUnloaded")]
    [DataRow("DataContext")]
    public void DetachedViewCancellationRestoresIdentificationWithoutDesktopInput(string cancellation) => WithEditor(context =>
    {
        // No Show, Focus, CaptureMouse or real input. Only disconnected view handlers are exercised.
        var view = new VfxEditorView { DataContext = context.Editor };
        var target = context.Editor.Layers.First(layer => layer.CanPreview);
        Call(context.Editor, "BeginIdentification", target);
        typeof(VfxEditorView).GetField("_identificationEditor", Private)!.SetValue(view, context.Editor);
        typeof(VfxEditorView).GetField("_identificationButton", Private)!.SetValue(view, new Button());
        if (cancellation == "DataContext") view.DataContext = null;
        else if (cancellation == "EditorUnloaded") Call(view, cancellation, view, new RoutedEventArgs());
        else Call(view, cancellation, view, new MouseEventArgs(Mouse.PrimaryDevice, 0));
        Call(view, "CancelIdentification");
        context.Runtime.Verify(runtime => runtime.Identify(null), Times.Once);
        AssertPreserved(context);
        view.DataContext = null;
    }, scene: true);

    [TestMethod]
    public void ClosingAnIdentifyingEditorRestoresTheFilterBeforeItsSceneStops() => WithEditor(context =>
    {
        var target = context.Editor.Layers.First(layer => layer.CanPreview);
        Call(context.Editor, "BeginIdentification", target);
        context.Editor.Close();
        context.Runtime.Verify(runtime => runtime.Identify(null), Times.Once);
        Call(context.Editor, "BeginIdentification", target);
        context.Runtime.Verify(runtime => runtime.Identify(target.Identity), Times.Once);
        AssertPreserved(context);
    }, scene: true);

    [TestMethod]
    public void DuplicateLayerNamesHaveDifferentIdentificationOccurrences() => WithEditor(context =>
    {
        var document = VfxDocument.Read(context.Original);
        var emitters = document.Root.Element("vfx")!.Element("emitters")!.Elements("emitter").ToArray();
        emitters[1].SetAttributeValue("id", "sprite");
        context.Editor.LoadFile(PackFile.CreateFromBytes("duplicates-DIAG.xml", document.Write()));
        var duplicates = context.Editor.Layers.Where(layer => layer.Model.Kind == "Emitter" && layer.Model.Name == "sprite").ToArray();
        CollectionAssert.AreEqual(new[] { 0, 1 }, duplicates.Select(layer => layer.Identity.Occurrence).ToArray());
        Assert.AreNotEqual(duplicates[0].Identity, duplicates[1].Identity);
    });

    [TestMethod]
    public void ActualConversionFeedbackMovesUnreadFieldsOutOfCommonAndKeepsSearchAndBytes() => WithEditor(context =>
    {
        context.Editor.SelectedLayer = context.Editor.Layers.First(layer => layer.Model.Name == "sprite");
        var unread = context.Editor.SelectedLayer.Fields.Single(field => field.Model.Name == "EmitterPosition");
        Assert.IsTrue(unread.HasGuidance);
        Assert.IsTrue(context.Editor.VisibleFields.Contains(unread), "Before feedback, existing common guidance remains available with an explicit waiting status.");
        Assert.AreEqual(VfxPreviewFieldState.Unknown, unread.PreviewState);
        var feedback = context.Feedback();
        var fieldNotifications = 0;
        foreach (var field in context.Editor.SelectedLayer.Fields)
            field.PropertyChanged += (_, _) => fieldNotifications++;
        ApplyFeedback(context.Editor, feedback);
        Assert.AreEqual(VfxPreviewFieldState.NotConnected, unread.PreviewState);
        Assert.IsFalse(unread.IsCommon);
        Assert.AreEqual("Advanced", unread.Category);
        Assert.IsFalse(context.Editor.VisibleFields.Contains(unread));
        Assert.IsTrue(context.Editor.VisibleFields.Any(field => field.Model.Name == "ParticleScale" && field.IsPreviewRead));
        var count = fieldNotifications;
        ApplyFeedback(context.Editor, feedback);
        ApplyFeedback(context.Editor, context.Feedback());
        Assert.AreEqual(count, fieldNotifications, "Repeated equivalent feedback must not notify fields or rebuild controls.");
        context.Editor.Search = "EmitterPosition";
        Assert.IsTrue(context.Editor.VisibleFields.Contains(unread));
        context.Editor.Search = "";
        context.Editor.Category = "All";
        Assert.IsTrue(context.Editor.VisibleFields.Contains(unread));
        AssertPreserved(context);
    });

    [TestMethod]
    public void UnselectedFeedbackIsVisibleWithoutDeletingAuthoredFields() => WithEditor(context =>
    {
        var selected = context.Editor.SelectedLayer!;
        var originalCount = selected.Fields.Count;
        var info = VfxPreviewInspector.Inspect(new(context.Original, "vfx/guided-DIAG.xml", context.Resolve, "0", new([], []))).EditorInfo;
        ApplyFeedback(context.Editor, info);
        Assert.AreEqual(originalCount, selected.Fields.Count);
        Assert.IsTrue(selected.Fields.All(field => field.PreviewState == VfxPreviewFieldState.Unselected));
        context.Editor.Category = "All";
        Assert.IsTrue(context.Editor.VisibleFields.Count > 0);
        AssertPreserved(context);
    });

    [TestMethod]
    public void PreviewQualityAndScopeChangesImmediatelyInvalidateOlderFieldFeedback() => WithEditor(context =>
    {
        var field = context.Editor.SelectedLayer!.Fields.First(item => item.Model.Name == "ParticleScale");
        ApplyFeedback(context.Editor, context.Feedback());
        Assert.IsTrue(field.IsPreviewRead);
        context.Editor.PreviewQuality = "1";
        Assert.AreEqual(VfxPreviewFieldState.Unknown, field.PreviewState);
        Assert.IsTrue(field.IsCommon, "Pending feedback keeps existing common fields accessible without marking them connected.");
        ApplyFeedback(context.Editor, context.Feedback());
        Assert.IsTrue(field.IsPreviewRead);
        context.Editor.SelectedLayer.IsIncludedInPreview = false;
        Assert.AreEqual(VfxPreviewFieldState.Unknown, field.PreviewState);
        AssertPreserved(context);
    });

    [TestMethod]
    public void FeedbackRefreshKeepsExistingInlineControlsWhenRelatedFieldsChange() => WithEditor(context =>
    {
        ApplyFeedback(context.Editor, context.Feedback());
        var scale = context.Editor.SelectedLayer!.Fields.Single(field => field.Model.Name == "ParticleScale");
        context.Editor.SelectedField = scale;
        context.Editor.InspectorTabIndex = 1;
        var view = new VfxEditorView { DataContext = context.Editor };
        try
        {
            void Layout()
            {
                view.Measure(new Size(1280, 850));
                view.Arrange(new Rect(0, 0, 1280, 850));
                view.UpdateLayout();
                view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                view.UpdateLayout();
            }
            Layout();
            var graph = Descendants(view).OfType<VfxCurveGraph>().Single();
            context.Editor.PreviewQuality = "1";
            Layout();
            Assert.AreSame(graph, Descendants(view).OfType<VfxCurveGraph>().Single(),
                "Pending feedback must not destroy the current inline editor when other common fields are added.");
            ApplyFeedback(context.Editor, context.Feedback());
            Layout();
            Assert.AreSame(graph, Descendants(view).OfType<VfxCurveGraph>().Single(),
                "New feedback must retain the inline editor when unrelated unread fields leave Common.");
        }
        finally { view.DataContext = null; }
        AssertPreserved(context);
    });

    [TestMethod]
    public void GuidedControlsRenderThemesAndDpiWithoutMutation() => WithEditor(context =>
    {
        var previous = ThemesController.CurrentTheme;
        try
        {
            ApplyFeedback(context.Editor, context.Feedback());
            foreach (var theme in Enum.GetValues<ThemeType>())
            {
                ThemesController.SetTheme(theme);
                var view = new VfxEditorView { DataContext = context.Editor };
                view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                foreach (var tab in new[] { 0, 1 })
                {
                    context.Editor.InspectorTabIndex = tab;
                    view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    view.Measure(new Size(1280, 850));
                    view.Arrange(new Rect(0, 0, 1280, 850));
                    view.UpdateLayout();
                    foreach (var scale in new[] { 1.0, 1.5 })
                    {
                        var bitmap = new RenderTargetBitmap((int)(1280 * scale), (int)(850 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(view);
                        Assert.IsTrue(bitmap.PixelWidth > 0);
                        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GUIDED_UI_EVIDENCE") is { Length: > 0 } evidenceDirectory)
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = System.IO.File.Create(System.IO.Path.Combine(evidenceDirectory, $"guided-{theme}-tab{tab}-{scale * 100:0}.png"));
                            encoder.Save(stream);
                        }
                    }
                    if (tab == 0)
                    {
                        var buttons = Descendants(view).OfType<Button>().Where(button => button.IsVisible || button.Visibility == Visibility.Visible).ToArray();
                        Assert.IsTrue(buttons.Any(button => button.DataContext is VfxLayerViewModel layer && layer.CanPreview
                            && ReferenceEquals(button.Command, layer.PreviewOnlyCommand)), "Persistent single-layer preview remains reachable beside identification.");
                        Assert.IsTrue(buttons.Any(button => System.Windows.Automation.AutomationProperties.GetName(button) == context.Editor.Localization.Get("Vfx.Layer.Identify")));
                        Assert.IsTrue(buttons.Any(button => button.DataContext is VfxLayerViewModel layer && ReferenceEquals(button.Command, layer.EditLayerCommand)));
                    }
                    else Assert.IsTrue(Descendants(view).OfType<TextBlock>().Any(text => text.Text == context.Editor.SelectedLayerScope));
                }
                view.DataContext = null;
            }
        }
        finally { ThemesController.SetTheme(previous); }
        AssertPreserved(context);
    });

    private static void ApplyFeedback(VfxEditorViewModel editor, VfxPreviewEditorInfo info) => Call(editor, "ApplyPreviewFeedback", info);
    private static object? Call(object target, string method, params object?[] args) => target.GetType().GetMethod(method, Private)!.Invoke(target, args);
    private static System.Windows.Threading.DispatcherTimer Timer(VfxEditorViewModel editor) => (System.Windows.Threading.DispatcherTimer)typeof(VfxEditorViewModel).GetField("_scenePreviewTimer", Private)!.GetValue(editor)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void AssertPreserved(Context context)
    {
        CollectionAssert.AreEqual(context.Original, context.Document.Write());
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        Assert.IsFalse(context.Editor.HasUnsavedChanges);
        Assert.IsFalse(context.Editor.UndoCommand.CanExecute(null));
        Assert.IsFalse(context.Editor.RedoCommand.CanExecute(null));
        context.Save.VerifyNoOtherCalls();
        Assert.AreEqual(0, context.Runtime.Invocations.Count(invocation => invocation.Method.Name is nameof(IVfxPreview.Reload) or nameof(IVfxPreview.Seek)));
    }
    private static void WithEditor(Action<Context> test, bool scene = false) => WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
    {
        using var context = new Context(scene);
        test(context);
    });

    private sealed class Context : IDisposable
    {
        public byte[] Original { get; }
        public PackFile File { get; }
        public Mock<IFileSaveService> Save { get; } = new();
        public Mock<IPackFileService> Files { get; } = new();
        public Mock<IVfxPreview> Runtime { get; } = new();
        public VfxEditorViewModel Editor { get; }
        public VfxDocument Document => (VfxDocument)typeof(VfxEditorViewModel).GetField("_document", Private)!.GetValue(Editor)!;
        private readonly VfxScenePreview? _scene;
        private readonly VfxPreviewComponent? _component;
        private readonly byte[] _child;
        public Context(bool scene)
        {
            // DIAG: real converter readback and disconnected WPF controls; no native/GPU or visual-parity sample.
            const string scale = "x_min,0,0;0,1,linear,linear,0,0,0,0,1|y_min,0,0;0,1,linear,linear,0,0,0,0,1|z_min,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2";
            var source = $"""
                <vfx revision="2" construction_type="2"><emitters><emitter id="sprite" type="continuous_mesh"><property name="ParticleScale">{scale}</property></emitter>
                <emitter id="second" type="continuous_mesh"><property name="ParticleScale">{scale}</property></emitter></emitters>
                <behaviour_chains><behaviour_chain id="sprite"><quality value="0"><modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/texture.dds"/></modifier>
                <modifier fragment="unknown-DIAG"><property name="EmitterPosition" value="float3(0,0,0)"/><future_payload keep="original"/></modifier></quality></behaviour_chain>
                <behaviour_chain id="second"><quality value="0"><modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/texture.dds"/></modifier></quality></behaviour_chain></behaviour_chains></vfx>
                """;
            _child = Encoding.UTF8.GetBytes(source);
            var document = VfxDocument.Read(_child);
            document.AddReference("child", VfxDocument.Read(_child)).Apply();
            Original = document.Write();
            File = PackFile.CreateFromBytes("guided-DIAG.xml", Original);
            var files = Files;
            files.Setup(service => service.GetAllPackfileContainers()).Returns([]);
            files.Setup(service => service.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns("vfx\\guided-DIAG.xml");
            var localization = new LocalizationManager(); localization.LoadLanguage();
            if (scene)
            {
                Runtime.SetupGet(runtime => runtime.Diagnostics).Returns(Array.Empty<VfxPreviewDiagnostic>());
                Runtime.SetupGet(runtime => runtime.CycleDuration).Returns(8);
                Runtime.SetupGet(runtime => runtime.EmitterCount).Returns(3);
                Runtime.SetupGet(runtime => runtime.TotalEmitterCount).Returns(3);
                var factory = new Mock<IVfxPreviewFactory>();
                factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(Runtime.Object);
                _component = new(factory.Object, Mock.Of<IDeviceResolver>(), null!);
                _scene = new(Mock.Of<IWpfGame>(), Mock.Of<IComponentInserter>(),
                    new View3DCoreComponentSet(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!), _component);
            }
            Editor = new(files.Object, Save.Object, Mock.Of<IStandardDialogs>(), Mock.Of<IEditorCreator>(), localization, _scene);
            Editor.LoadFile(File);
            if (_component != null)
            {
                Call(Editor, "RefreshScenePreview", null, EventArgs.Empty);
                _component.Update(new GameTime());
            }
            Runtime.Invocations.Clear();
        }
        public byte[] Resolve(string path) => path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? _child : [1, 2, 3];
        public void PublishAndPump() { Call(Editor, "RefreshScenePreview", null, EventArgs.Empty); _component?.Update(new GameTime()); }
        public VfxPreviewEditorInfo Feedback() => VfxPreviewInspector.Inspect(new(Original, "vfx/guided-DIAG.xml", Resolve)).EditorInfo;
        public void Dispose() { Editor.Close(); _scene?.Dispose(); _component?.Dispose(); }
    }
}
