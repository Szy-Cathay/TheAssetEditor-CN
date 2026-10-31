using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Editors.CscEditor.Data;
using Editors.CscEditor.ViewModels;
using Editors.CscEditor.Views;
using KitbasherEditor.ViewModels.MenuBarViews;
using KitbasherEditor.Views;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Esf;
using Assert = NUnit.Framework.Assert;
using LayoutChecks = AssetEditorTests.SuperViewWorkspaceLayoutTests;

namespace AssetEditorTests;

[NonParallelizable]
public class FeedbackEditorUiTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void KitbashTransformSpace_UsesRealViewModelAndKeepsCompactToolbar(ThemeType theme)
    {
        LayoutChecks.WithTheme(theme, () =>
        {
            foreach (var dpi in new[] { 96, 120, 144 })
            {
                var model = UiModelKitbashFamilyGallery.CreateMenuBarModel();
                var transform = new TransformToolViewModel(null!, null!, null!, Mock.Of<IEventHub>());
                model.TransformTool = transform;
                var view = new MenuBarView { DataContext = model };
                var width = 1440d * 96 / dpi;
                NaturalToolbarLayout(view, width);
                var selector = (ComboBox)view.FindName("TransformSpaceSelector");
                var row = (FrameworkElement)((FrameworkElement)((FrameworkElement)selector.Parent).Parent).Parent;
                var visibleHeight = row.ActualHeight;
                Assert.Multiple(() =>
                {
                    Assert.That(selector.IsEnabled, Is.True);
                    Assert.That(selector.Items.Cast<ComboBoxItem>().Select(item => item.Content), Is.EqualTo(new[] { "世界", "局部" }));
                    Assert.That(selector.ActualHeight, Is.EqualTo(24).Within(1));
                    Assert.That(selector.SelectedIndex, Is.EqualTo(0));
                });
                LayoutChecks.AssertInside(selector, row);
                selector.SetCurrentValue(ComboBox.SelectedIndexProperty, 1);
                Assert.That(transform.SelectedTransformSpaceIndex, Is.EqualTo(1));
                selector.SetCurrentValue(ComboBox.SelectedIndexProperty, 0);
                Assert.That(transform.SelectedTransformSpaceIndex, Is.EqualTo(0));
                selector.Visibility = Visibility.Collapsed;
                NaturalToolbarLayout(view, width);
                Assert.That(visibleHeight, Is.LessThanOrEqualTo(row.ActualHeight + 1));
                selector.Visibility = Visibility.Visible;
                NaturalToolbarLayout(view, width);
                LayoutChecks.Snapshot(view, $"kitbash-transform-space-{theme}-{dpi}.png", dpi);
            }
        });
    }

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void CscCameraFields_ShowOnlyAuthoredParametersAndKeepSaveReachable(ThemeType theme)
    {
        LayoutChecks.WithTheme(theme, () =>
        {
            foreach (var dpi in new[] { 96, 120, 144 })
            foreach (var version in new[] { 1, 2, 100 })
            {
                var element = version == 100
                    ? CscElementFactory.Create(new CscScene(), CscElementKind.Camera)
                    : CscScene.Load(CreateDiagnosticStaticCamera(version)).Elements.Single();
                var elementViewModel = new CscElementViewModel(element, (_, _) => { }, LocalizationManager.Instance);
                var model = new CameraViewProbe(elementViewModel);
                var view = new CscEditorView { DataContext = model };
                var width = 1440d * 96 / dpi;
                var height = 900d * 96 / dpi;
                LayoutChecks.Layout(view, width, height);
                var fields = LayoutChecks.Descendants<TextBox>(view)
                    .Where(input => BindingOperations.GetBinding(input, TextBox.TextProperty)?.Path?.Path?.StartsWith("Camera", StringComparison.Ordinal) == true)
                    .ToDictionary(input => BindingOperations.GetBinding(input, TextBox.TextProperty)!.Path.Path);
                Assert.Multiple(() =>
                {
                    Assert.That(fields["CameraFov"].Visibility, Is.EqualTo(Visibility.Visible));
                    Assert.That(fields["CameraRollDegrees"].Visibility, Is.EqualTo(version == 100 ? Visibility.Visible : Visibility.Collapsed));
                    Assert.That(fields["CameraNear"].Visibility, Is.EqualTo(version == 1 ? Visibility.Collapsed : Visibility.Visible));
                    Assert.That(fields["CameraFar"].Visibility, Is.EqualTo(version == 1 ? Visibility.Collapsed : Visibility.Visible));
                    Assert.That(fields["CameraFov"].DataContext, Is.SameAs(elementViewModel));
                });
                if (version != 100)
                {
                    Assert.That(element.IsStaticTransform, Is.True);
                    Assert.That(element.TypeGroups, Is.Empty, "固定值相机不能伪造动画通道");
                    Assert.That(((CurveEditorControl)view.FindName("CurveEditor")).SeriesSource, Is.SameAs(model.CurveSeriesList));
                    Assert.That(model.CurveSeriesList, Is.Empty);
                }
                var details = LayoutChecks.Descendants<ScrollViewer>(view).Single(scroll => scroll.MaxHeight == 420);
                details.ScrollToEnd();
                LayoutChecks.Layout(view, width, height);
                var save = (Button)view.FindName("SaveButton");
                LayoutChecks.AssertInside(save, view);
                Assert.That(save.IsEnabled, Is.True);
                LayoutChecks.Snapshot(view, $"csc-camera-v{version}-{theme}-{dpi}.png", dpi);
            }
        });
    }

    private static void NaturalToolbarLayout(FrameworkElement view, double width)
    {
        view.Height = double.NaN;
        view.Width = width;
        view.Measure(new Size(width, double.PositiveInfinity));
        LayoutChecks.Layout(view, width, view.DesiredSize.Height);
    }

    // A format fixture for conditional UI checks, not a game visual reference.
    private static byte[] CreateDiagnosticStaticCamera(int version)
    {
        var transform = new List<EsfNode>
        {
            EsfNode.Leaf(EsfNodeKind.U32, 43u), EsfNode.Leaf(EsfNodeKind.Ascii, "[Camera]"),
            EsfNode.Leaf(EsfNodeKind.F32, 0f), EsfNode.Leaf(EsfNodeKind.F32, 10f),
            EsfNode.Leaf(EsfNodeKind.Ascii, "duration"), EsfNode.Leaf(EsfNodeKind.Ascii, "free"),
            EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(2, 3, 4)),
            EsfNode.Leaf(EsfNodeKind.F32, 0f), EsfNode.Leaf(EsfNodeKind.F32, 0f),
            EsfNode.Leaf(EsfNodeKind.F32, 0f), EsfNode.Leaf(EsfNodeKind.F32, 1f),
            EsfNode.Leaf(EsfNodeKind.F32, 1f), EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)),
            EsfNode.Leaf(EsfNodeKind.F32, 0f), EsfNode.Leaf(EsfNodeKind.F32, 0f),
            EsfNode.Leaf(EsfNodeKind.F32, 0f), EsfNode.Leaf(EsfNodeKind.F32, 1f),
        };
        var camera = new List<EsfNode> { EsfNode.Leaf(EsfNodeKind.F32, 30f) };
        if (version == 2)
            camera.AddRange([EsfNode.Leaf(EsfNodeKind.F32, 0.5f), EsfNode.Leaf(EsfNodeKind.F32, 20000f),
                EsfNode.Leaf(EsfNodeKind.Bool, false), EsfNode.Leaf(EsfNodeKind.F32, 1f),
                EsfNode.Leaf(EsfNodeKind.F32, 1f), EsfNode.Leaf(EsfNodeKind.F32, 0.2f)]);
        var fields = new List<EsfNode>
        {
            EsfNode.Leaf(EsfNodeKind.F32, 10f), EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)),
            EsfNode.Leaf(EsfNodeKind.F32, 0f), EsfNode.Leaf(EsfNodeKind.Ascii, ""),
            EsfNode.Leaf(EsfNodeKind.U32, 1u), EsfNode.Leaf(EsfNodeKind.Ascii, "camera"),
            EsfNode.NewRecord("ELEMENT", 5, EsfRecordFlags.IsRecordNode, [transform]),
            EsfNode.NewRecord("CAMERA_ELEMENT", (byte)version, EsfRecordFlags.IsRecordNode, [camera]),
            EsfNode.Leaf(EsfNodeKind.U32, 1u), EsfNode.Leaf(EsfNodeKind.U32, uint.MaxValue),
            EsfNode.Leaf(EsfNodeKind.U32, 1u), EsfNode.Leaf(EsfNodeKind.U32, 43u),
            EsfNode.NewRecord("ATTACH_TO_PARAMETERS", 0, EsfRecordFlags.IsRecordNode,
                [[EsfNode.Leaf(EsfNodeKind.U32, uint.MaxValue)]]),
        };
        using var stream = new MemoryStream();
        EsfWriter.Write(new EsfDocument { Signature = EsfSignature.Caab,
            Root = EsfNode.NewRecord("ROOT", 3, EsfRecordFlags.IsRecordNode, [fields]) }, stream);
        return stream.ToArray();
    }

    public sealed class CameraViewProbe(CscElementViewModel selectedElement)
    {
        public CscElementViewModel SelectedElement { get; } = selectedElement;
        public object[] SceneRootItems => [];
        public object? Scene => null;
        public bool IsElementSelected => true;
        public bool IsSceneRootSelected => false;
        public bool HasSelection => true;
        public bool CanUndo => false;
        public bool CanRedo => false;
        public bool IsLookingThroughCamera { get; set; }
        public bool ManifestEditable => false;
        public bool CurveStructureLocked => true;
        public bool IsPlaying { get; set; }
        public bool HasUnsavedChanges => false;
        public double Duration => 10;
        public double CurrentTime { get; set; }
        public ObservableCollection<CurveSeries> CurveSeriesList { get; } = [];
        public CurveSeries? SelectedCurveSeries { get; set; }
        public string DisplayName => "诊断相机.csc";
        public string StatusText => "就绪";
    }
}
