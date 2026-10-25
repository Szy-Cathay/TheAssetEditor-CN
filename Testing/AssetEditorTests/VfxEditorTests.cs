using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Editors.VfxEditor;
using GameWorld.Core.Vfx;
using GameWorld.Core.Rendering;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Vfx;

namespace AssetEditorTests;

[TestClass]
public class VfxEditorTests
{
    [TestMethod]
    public void PreviewQuality_IsIndependentOfFieldFiltersAndLayerSelectionWithoutEditingFiles() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var store = new VfxPreviewDocumentStore();
            var context = new Context(store);
            var editor = context.Editor;
            var original = context.File.DataSource.ReadData();
            var refresh = typeof(VfxEditorViewModel).GetMethod("RefreshScenePreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            try
            {
                editor.PreviewQuality = "1";
                editor.Quality = "2";
                editor.SelectedLayer = editor.Layers.Single(layer => layer.Model.Kind == "Reference");
                Assert.AreEqual("All", editor.Quality);
                refresh.Invoke(editor, [null, EventArgs.Empty]);
                Assert.IsTrue(store.TryGet(context.File, out var snapshot));
                Assert.AreEqual("1", snapshot!.Quality, "The whole composition must retain the chosen preview quality.");
                editor.PreviewQuality = "2";
                editor.Quality = "All";
                refresh.Invoke(editor, [null, EventArgs.Empty]);
                Assert.IsTrue(store.TryGet(context.File, out snapshot));
                Assert.AreEqual("2", snapshot!.Quality);
                Assert.IsFalse(editor.HasUnsavedChanges);
                Assert.IsFalse(editor.UndoCommand.CanExecute(null));
                CollectionAssert.AreEqual(original, context.File.DataSource.ReadData());
                context.Save.VerifyNoOtherCalls();

            }
            finally { editor.Close(); }
        });

    [TestMethod]
    public void WeatherSelection_UsesSharedReadOnlyStateWithoutSavingOrDirtyingTheDocument() => WithEditor((_, _) =>
    {
        var weather = new GameWeatherStore(); var context = new Context(weather: weather); var editor = context.Editor;
        var xml = "<environment_file><lighting use_custom_ambient_cube_colours='true' ambient_cube_hdr_scaler='1' ambient_scale='1' sun_direction_azimuth='90' sun_direction_elevation='30' sun_colour_scale='20000'>"
            + "<sun_colour r='1' g='1' b='1'/>" + string.Concat(new[] { "left", "right", "top", "bottom", "front", "back" }.Select(face => $"<ambient_cube_{face} r='0.5' g='0.5' b='0.5'/>")) + "</lighting></environment_file>";
        var file = PackFile.CreateFromBytes("control.environment", Encoding.UTF8.GetBytes(xml));
        context.Files.Setup(x => x.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns("weather/control.environment");
        context.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>())).Returns(new BrowseDialogResultFile(true, file));
        var before = context.File.DataSource.ReadData();
        try
        {
            editor.SelectWeatherCommand.Execute(null);
            Assert.AreEqual("weather/control.environment", weather.Current!.SourcePath);
            Assert.IsTrue(editor.WeatherStatus.Contains("weather/control.environment", StringComparison.Ordinal));
            Assert.IsFalse(editor.HasUnsavedChanges);
            context.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>())).Returns(new BrowseDialogResultFile(false, null!));
            editor.SelectWeatherCommand.Execute(null);
            Assert.IsNotNull(weather.Current);
            var invalid = PackFile.CreateFromBytes("broken.environment", Encoding.UTF8.GetBytes("<broken/>"));
            context.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>())).Returns(new BrowseDialogResultFile(true, invalid));
            editor.SelectWeatherCommand.Execute(null);
            Assert.AreEqual("weather/control.environment", weather.Current.SourcePath);
            editor.ClearWeatherCommand.Execute(null);
            Assert.IsNull(weather.Current);
            Assert.AreEqual(LocalizationManager.Instance.Get("Vfx.Preview.WeatherNone"), editor.WeatherStatus);
            Assert.IsFalse(editor.HasUnsavedChanges); Assert.IsNull(context.Saved);
            CollectionAssert.AreEqual(before, context.File.DataSource.ReadData());
            context.Save.VerifyNoOtherCalls();
        }
        finally { editor.Close(); }
    });

    [TestMethod]
    public void ScenePreviewPanel_RendersFourThemesWithoutSavingOrEnablingModelEditing() => WithEditor((editor, context) =>
    {
        var previous = ThemesController.CurrentTheme;
        try
        {
            var diagnosticKeys = new[] { "Vfx.Preview.InitialMotionUncalibrated", "Vfx.Preview.DragCandidate", "Vfx.Preview.LightingCandidate", "Vfx.Preview.ConstantForceDampingCandidate", "Vfx.Preview.UvTransformCandidate", "Vfx.Preview.AnalyticCurveCalibrated", "Vfx.Preview.BezierCandidate", "Vfx.Preview.SoulPanningMaterialCandidate", "Vfx.Preview.ProjectedDecalCandidate", "Vfx.Preview.FlatGroundReceiver", "Vfx.Preview.FixedSpeedAtlasCandidate", "Vfx.Preview.FlatTerrainCandidate", "Vfx.Preview.FlatTerrainDepthUnavailable", "Vfx.Preview.TerrainDecalBasisUnsupported" };
            var diagnostics = diagnosticKeys.Select(key => LocalizationManager.Instance.Get(key)).ToArray();
            for (var index = 0; index < diagnostics.Length; ++index)
                Assert.AreNotEqual(diagnosticKeys[index], diagnostics[index], "Preview limitations must load their Chinese text.");
            typeof(VfxEditorViewModel).GetProperty(nameof(VfxEditorViewModel.ScenePreviewDiagnostics))!.SetValue(editor, diagnostics);
            foreach (var theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight })
            {
                ThemesController.SetTheme(theme);
                var view = new VfxEditorView { DataContext = editor };
                view.Measure(new Size(1440, 900));
                view.Arrange(new Rect(0, 0, 1440, 900));
                view.UpdateLayout();
                var panel = (Grid)view.FindName("ScenePreviewPanel");
                Assert.IsTrue(panel.ActualWidth > 240, theme.ToString());
                ((TabControl)view.FindName("Inspector")).SelectedIndex = 2;
                var limitations = (ItemsControl)view.FindName("PreviewDiagnostics");
                view.UpdateLayout();
                foreach (var diagnostic in diagnostics)
                    Assert.IsTrue(Descendants(limitations).OfType<TextBlock>().Any(text => text.Text == diagnostic && text.ActualHeight > 0), theme.ToString());
                var bitmap = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(view);
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, $"vfx-scene-panel-{theme}.png"));
                    encoder.Save(stream);
                }
                Assert.IsFalse(editor.HasUnsavedChanges);
                context.Save.Verify(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()), Times.Never);
                view.DataContext = null;
            }
        }
        finally { editor.Close(); ThemesController.SetTheme(previous); }
    });

    private const string Curve = "alpha_min,8388608,1;0.0,0.0,linear,linear,-0.05,0.0,0.05,0.0,0;0.5,1.0,spline,spline,-0.2,0.3,0.2,0.3,0;1.0,0.0,linear,linear,-0.05,0.0,0.05,0.0,0:alpha,0;0,0";
    private const string UnorderedCurve = "alpha_min,8388608,1;0.0,0.0,linear,log3,-0.05,0.0,0.05,0.0,0;1.0,0.0,sine,linear,-0.05,0.0,0.05,0.0,0;0.15,1.0,sine,log3,-0.05,0.0,0.05,0.0,0:alpha,1;0,0";
    private static string Constant(string name, double value) => $"{name},16711680,0;0.0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,-0.05,0.0,0.05,0.0,1";
    internal static string Sample => $$"""
        <shader_library file="fx/particle_vfx_library.hlsl"/>
        <vfx revision="2" construction_type="1" camera_following="false">
          <!-- must survive -->
          <emitters><emitter id="water_trail" type="continuous_mesh">
            <property name="EmissionRateCurve" quality="0">{{Constant("x_min", 60)}}|{{Constant("x_max", 90)}}:x,1;0,1</property>
            <ranged_property name="LifeTime" quality="0" min="0.25" max="2.0"/>
            <property name="InfiniteLife" quality="0" value="false"/>
            <property name="SpawnSpacing" quality="0" value="0.075"/>
            <property name="BoundingBoxMax" quality="0" x="1.0" y="2.0" z="3.0"/>
            <future unknown="keep"><payload><![CDATA[a&b]]></payload></future>
          </emitter></emitters>
          <behaviour_chains><behaviour_chain id="water_trail">
            <quality value="0"><modifier fragment="vs_colour">
              <property name="curve_colour" type="curve">{{Constant("r_min", 0.3)}}|{{Constant("g_min", 0.72)}}|{{Constant("b_min", 0.91)}}:r,0;0,0|g,0;1,1|b,0;2,2</property>
              <property name="curve_alpha" type="curve">{{Curve}}</property>
              <property name="alpha_START_offset" type="float2" value="float2(0.0,0.0)"/>
              <property name="cell" type="int" value="2"/>
            </modifier></quality>
            <quality value="2"><modifier fragment="vs_colour"><property name="curve_alpha" type="curve">{{Curve}}</property></modifier></quality>
          </behaviour_chain></behaviour_chains>
          <effect_render_stacks><effect_render_stack id="water_trail" future="keep"/></effect_render_stacks>
        </vfx>
        <vfx_references><vfx inst_name="splash" vfx_ref="water_library" scale_min="1.0" scale_max="1.0">
          <emitter name="droplets" enable="true" tint_min_r="0.3" tint_min_g="0.7" tint_min_b="1.0" tint_min_a="0.8" tint_max_r="0.4" tint_max_g="0.8" tint_max_b="1.0" tint_max_a="1.0"/>
        </vfx></vfx_references>
        <editor_data><effect_components><component id="water_trail"/><component id="splash"/></effect_components></editor_data>
        """;

    [TestMethod]
    public void Composition_AddsIndependentReferencesAndRestoresExactOriginalBytes()
    {
        var document = VfxDocument.CreateComposition();
        var original = document.Write();
        var source = VfxDocument.Read(Encoding.UTF8.GetBytes(Sample));
        var first = document.AddReference("water_trail", source);
        first.Apply();
        var second = document.AddReference("water_trail", source);
        second.Apply();
        Assert.AreEqual(2, document.Layers.Count(x => x.Kind == "Reference"));
        Assert.AreNotEqual(first.Selection.Attribute("inst_name")!.Value, second.Selection.Attribute("inst_name")!.Value);
        first.Selection.SetAttributeValue("pos_x", "5");
        Assert.AreEqual("0.00", second.Selection.Attribute("pos_x")!.Value);
        Assert.AreEqual(1, first.Selection.Elements("emitter").Count());
        Assert.AreEqual(2, document.Root.Elements("editor_data").Elements("effect_components").Elements("component").Count());
        Assert.IsTrue(XNode.DeepEquals(document.Root, VfxDocument.Read(document.Write()).Root));
        second.Restore();
        first.Restore();
        CollectionAssert.AreEqual(original, document.Write());
    }

    [TestMethod]
    public void BrowseVfx_FiltersPathsWithoutRepeatedContainerSearches() => WithEditor((editor, context) =>
    {
        var unrelated = PackFile.CreateFromASCII("water_trail.xml", "<ordinary/>");
        var source = new PackFileContainer("source");
        context.Files.Setup(x => x.GetAllPackfileContainers()).Returns([source]);
        context.Files.Setup(x => x.GetFileEntriesSnapshot(source)).Returns([
            new("VFX/water_trail.xml", context.File), new("materials/water_trail.xml", unrelated)]);
        BrowseDialogFilter? filter = null;
        context.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<BrowseDialogFilter>()))
            .Callback<List<string>, BrowseDialogFilter>((_, value) => filter = value)
            .Returns(new BrowseDialogResultFile(false, null!));
        context.Files.Invocations.Clear();
        editor.OpenCommand.Execute(null);
        Assert.IsNotNull(filter);
        Assert.IsTrue(filter.Matches(context.File));
        Assert.IsFalse(filter.Matches(unrelated));
        context.Files.Verify(x => x.GetFullPath(It.IsAny<PackFile>(), It.IsAny<PackFileContainer?>()), Times.Never);
    });

    [TestMethod]
    public void Composition_DuplicateAndRemovePreserveLinkedBlocksAndFieldUndo() => WithEditor((editor, context) =>
    {
        Field(editor, "SpawnSpacing").Values[0].Text = "0.5";
        editor.DuplicateLayerCommand.Execute(null);
        var duplicate = editor.SelectedLayer!;
        Assert.AreEqual("water_trail_copy", duplicate.Model.Name);
        Assert.AreEqual("0.5", duplicate.Fields.Single(x => x.Model.Name == "SpawnSpacing").RawValue);
        duplicate.Fields.Single(x => x.Model.Name == "SpawnSpacing").Values[0].Text = "0.75";
        Assert.AreEqual("0.5", Field(editor, "SpawnSpacing").RawValue);
        editor.RemoveLayerCommand.Execute(null);
        Assert.IsTrue(editor.Save());
        Assert.AreEqual(1, VfxDocument.Read(context.Saved!).Layers.Count(x => x.Kind == "Emitter"));
        editor.UndoCommand.Execute(null);
        Assert.AreEqual("0.75", editor.Layers.Single(x => x.Model.Name == "water_trail_copy").Fields.Single(x => x.Model.Name == "SpawnSpacing").RawValue);
        editor.UndoCommand.Execute(null);
        Assert.IsTrue(editor.Save());
        var saved = VfxDocument.Read(context.Saved!);
        foreach (var name in new[] { "emitter", "behaviour_chain", "effect_render_stack", "component" })
            Assert.IsTrue(saved.Root.Descendants(name).Any(x => (string?)x.Attribute("id") == "water_trail_copy"), name);
        editor.UndoCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        Assert.IsTrue(editor.Save());
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(Sample), context.Saved!);
        for (var index = 0; index < 4; index++) editor.RedoCommand.Execute(null);
        Assert.AreEqual(1, editor.Layers.Count(x => x.Model.Kind == "Emitter"));
    });

    [TestMethod]
    public void Composition_RemovingReferenceIncludesItsOverridesAndMetadata()
    {
        var original = Encoding.UTF8.GetBytes(Sample);
        var document = VfxDocument.Read(original);
        var layer = document.Layers.Single(x => x.Kind == "Reference");
        var edit = document.RemoveLayer(layer);
        edit.Apply();
        Assert.IsFalse(document.Layers.Any(x => x.Kind is "Reference" or "Override"));
        Assert.IsFalse(document.Root.Descendants("component").Any(x => (string?)x.Attribute("id") == layer.Name));
        edit.Restore();
        CollectionAssert.AreEqual(original, document.Write());
    }

    [TestMethod]
    public void Composition_AddAndSaveRejectCyclesAndCancelPreservesNewDocument() => WithEditor((editor, context) =>
    {
        context.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<BrowseDialogFilter>()))
            .Returns(new BrowseDialogResultFile(true, context.File));
        editor.AddReferenceCommand.Execute(null);
        Assert.IsFalse(editor.HasUnsavedChanges);
        context.Dialogs.Verify(x => x.ShowDialogBox(It.Is<string>(text => text.Contains("循环引用")), It.IsAny<string>(), UiMessageBoxIcon.Warning), Times.Once);

        var fresh = new VfxEditorViewModel(context.Files.Object, context.Save.Object, context.Dialogs.Object, Mock.Of<IEditorCreator>(), editor.Localization);
        fresh.NewComposition();
        fresh.AddReferenceCommand.Execute(null);
        Assert.AreEqual(1, fresh.Layers.Count(x => x.Model.Kind == "Reference"));
        Assert.IsTrue(fresh.VisibleFields.Any(x => x.Model.Name == "reference_position"));
        context.Dialogs.Setup(x => x.DisplaySaveDialog(context.Files.Object, It.IsAny<List<string>>()))
            .Returns(new SaveDialogResult(false, null, null));
        Assert.IsFalse(fresh.Save());
        Assert.IsTrue(fresh.HasUnsavedChanges);
        context.Dialogs.Setup(x => x.DisplaySaveDialog(context.Files.Object, It.IsAny<List<string>>()))
            .Returns(new SaveDialogResult(true, null, "vfx\\water_trail.xml"));
        Assert.IsFalse(fresh.Save());
        Assert.IsNull(context.Saved);
        context.Dialogs.Setup(x => x.DisplaySaveDialog(context.Files.Object, It.IsAny<List<string>>()))
            .Returns(new SaveDialogResult(true, null, "vfx\\combined.xml"));
        Assert.IsTrue(fresh.Save());
        Assert.IsFalse(fresh.HasUnsavedChanges);
        Assert.AreEqual("water_trail", VfxDocument.Read(context.Saved!).Layers.Single(x => x.Kind == "Reference").Reference);
    });

    [TestMethod]
    public void Composition_SaveAsCannotOverwriteAnExternallyChangedSource() => WithEditor((editor, context) =>
    {
        context.File.DataSource = new MemorySource(Encoding.UTF8.GetBytes(Sample + "<!-- external change -->"));
        context.Dialogs.Setup(x => x.DisplaySaveDialog(context.Files.Object, It.IsAny<List<string>>()))
            .Returns(new SaveDialogResult(true, context.File, "vfx\\water_trail.xml"));
        editor.SaveAsCommand.Execute(null);
        Assert.IsNull(context.Saved);
        context.Dialogs.Verify(x => x.ShowDialogBox(editor.Localization.Get("Vfx.FileChanged"),
            It.IsAny<string>(), UiMessageBoxIcon.Warning), Times.Once);
    });

    [TestMethod]
    public void Composition_RejectsIndirectCyclesAndInvalidPlacementRange() => WithEditor((editor, context) =>
    {
        var source = PackFile.CreateFromBytes("other.xml", Encoding.UTF8.GetBytes(Sample.Replace("vfx_ref=\"water_library\"", "vfx_ref=\"water_trail\"")));
        context.Files.Setup(x => x.GetFullPath(source, It.IsAny<PackFileContainer?>())).Returns("vfx\\other.xml");
        context.Dialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<BrowseDialogFilter>()))
            .Returns(new BrowseDialogResultFile(true, source));
        editor.AddReferenceCommand.Execute(null);
        Assert.IsFalse(editor.HasUnsavedChanges);
        var scale = editor.Layers.Single(x => x.Model.Kind == "Reference").Fields.Single(x => x.Model.Name == "reference_scale");
        scale.Values[0].Text = "2";
        Assert.IsTrue(editor.HasErrors);
        Assert.IsFalse(editor.AddReferenceCommand.CanExecute(null));
        Assert.IsFalse(editor.Save());
        scale.Values[1].Text = "2";
        Assert.IsFalse(editor.HasErrors);
    });

    [TestMethod]
    public void GuidedNavigation_StartsWithCommonEditsAndSearchFindsAdvancedProperties() => WithEditor((editor, _) =>
    {
        Assert.AreEqual("Common", editor.Category);
        Assert.AreEqual("curve_colour", editor.SelectedField!.Model.Name);
        Assert.IsTrue(editor.VisibleFields.All(x => x.IsCommon));
        Assert.IsFalse(editor.VisibleFields.Contains(Field(editor, "BoundingBoxMax")));
        Assert.IsTrue(editor.SelectedField.HasColours);
        Assert.IsFalse(editor.SelectedField.ShowCurve);

        editor.Search = "显示边界";
        Assert.IsTrue(editor.VisibleFields.Contains(Field(editor, "BoundingBoxMax")), "Search must not silently hide advanced matches.");
        editor.Search = "淡入淡出";
        Assert.AreEqual("curve_alpha", editor.SelectedField!.Model.Name);
        Assert.AreEqual("0", editor.SelectedField.Model.Quality);
        Assert.IsTrue(editor.SelectedField.Curve!.ChannelHint.Contains("下限不是动画起始值"));
        editor.Search = "";
        Assert.IsTrue(editor.VisibleFields.All(x => x.IsCommon));
        Assert.IsFalse(editor.HasUnsavedChanges);
    });

    [TestMethod]
    public void ChineseChoices_PreserveUnknownModesAndSaveOriginalGameValues() => WithEditor((editor, context) =>
    {
        var text = Sample.Replace("<future unknown=", "<property name=\"orientation_mode\" quality=\"0\" type=\"list\" value=\"future_mode\"/><future unknown=");
        context.File.DataSource = new MemorySource(Encoding.UTF8.GetBytes(text));
        editor.LoadFile(context.File);
        var field = Field(editor, "orientation_mode");
        var value = field.Values.Single();
        Assert.IsTrue(value.IsChoice);
        Assert.IsFalse(value.IsInput);
        Assert.AreEqual("future_mode", value.Text);
        Assert.IsTrue(value.DisplayText.Contains("原文件的其他模式"));
        Assert.IsFalse(editor.HasUnsavedChanges);
        value.Text = "Horizontally-oriented";
        Assert.AreEqual("水平放置", value.DisplayText);
        Assert.AreEqual("Horizontally-oriented", field.Model.Attributes[0].Value);
        Assert.IsTrue(editor.Save());
        var saved = VfxDocument.Read(context.Saved!);
        Assert.AreEqual("Horizontally-oriented", saved.Layers[0].Fields.Single(x => x.Name == "orientation_mode").RawValue);
        editor.UndoCommand.Execute(null);
        Assert.AreEqual("future_mode", Field(editor, "orientation_mode").Values.Single().Text);
    });

    [TestMethod]
    public void UnknownProperties_AreExplicitAndNeverPresentedAsCommonAdjustments() => WithEditor((editor, _) =>
    {
        var field = Field(editor, "payload");
        Assert.IsFalse(field.HasGuidance);
        Assert.IsFalse(field.IsCommon);
        Assert.AreEqual(field.Model.Name, field.Label);
        Assert.IsTrue(field.ToolTip.Contains("尚未确认"));
        Assert.IsTrue(field.Description.Contains("尚未确认"));
        Assert.IsTrue(field.AdjustmentHint.Contains("不提供猜测"));
        editor.Category = "Advanced";
        Assert.IsTrue(editor.VisibleFields.Contains(field));
        Assert.IsFalse(editor.HasUnsavedChanges);
    });

    [TestMethod]
    public void RealVfxGuidance_CoversActualPropertyNamesWithoutGenericHelp() => WithEditor((editor, _) =>
    {
        var directory = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_SAMPLES");
        if (string.IsNullOrWhiteSpace(directory)) Assert.Inconclusive("Set ASSETEDITOR_VFX_SAMPLES to validate local VFX guidance.");
        var fields = Directory.GetFiles(directory!, "*.xml", SearchOption.AllDirectories)
            .SelectMany(file => VfxDocument.Read(File.ReadAllBytes(file)).Layers.SelectMany(layer => layer.Fields)).ToArray();
        var names = fields.Select(field => field.Name).Distinct(StringComparer.Ordinal).ToArray();
        var missing = names.Where(name => editor.Localization.GetOrDefault("Vfx.Field." + name, "").Length == 0
            || editor.Localization.GetOrDefault("Vfx.Help." + name, "").Length == 0
            || editor.Localization.GetOrDefault("Vfx.Tune." + name, "").Length == 0).ToArray();
        Assert.AreEqual(0, missing.Length, "Missing guidance: " + string.Join(", ", missing));
        var components = fields.Where(field => field.Attributes.Count > 1).SelectMany(field => field.Attributes.Select(attribute => attribute.Name.LocalName))
            .Distinct(StringComparer.Ordinal).Where(name => editor.Localization.GetOrDefault("Vfx.Component." + name, "").Length == 0).ToArray();
        Assert.AreEqual(0, components.Length, "Missing component labels: " + string.Join(", ", components));
        Console.WriteLine($"Validated Chinese purpose and adjustment guidance for {names.Length} distinct real VFX attributes.");
    });

    [TestMethod]
    public void FragmentRoundTrip_PreservesExactBytesEncodingCommentsAndUnknownData()
    {
        foreach (var revision in new[] { "1", "2" })
        foreach (var encoding in new Encoding[] { new UTF8Encoding(false), new UTF8Encoding(true), new UnicodeEncoding(false, true), new UnicodeEncoding(true, true) })
        {
            var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(Sample.Replace("revision=\"2\"", $"revision=\"{revision}\""))).ToArray();
            var doc = VfxDocument.Read(bytes);
            CollectionAssert.AreEqual(bytes, doc.Write());
            Assert.AreEqual(4, doc.Layers.Count);
            var field = doc.Layers[0].Fields.Single(x => x.Name == "SpawnSpacing");
            field.Attributes[0].Value = "0.125";
            var reloaded = VfxDocument.Read(doc.Write());
            Assert.AreEqual("0.125", reloaded.Layers[0].Fields.Single(x => x.Name == "SpawnSpacing").RawValue);
            Assert.IsTrue(doc.Write().AsSpan().StartsWith(encoding.GetPreamble()));
            var before = VfxDocument.Read(bytes);
            before.Layers[0].Fields.Single(x => x.Name == "SpawnSpacing").Attributes[0].Value = "0.125";
            Assert.IsTrue(XNode.DeepEquals(before.Root, reloaded.Root));
        }
    }

    [TestMethod]
    public void XmlDeclarationAndDtd_HaveExplicitHandling()
    {
        var bytes = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Sample);
        CollectionAssert.AreEqual(bytes, VfxDocument.Read(bytes).Write());
        Assert.ThrowsException<System.Xml.XmlException>(() => VfxDocument.Read(Encoding.UTF8.GetBytes("<!DOCTYPE vfx [<!ENTITY bad SYSTEM 'file:///secret'>]>" + Sample)));
        Assert.ThrowsException<InvalidDataException>(() => VfxDocument.Read(Encoding.UTF8.GetBytes("<ordinary/>")));
    }

    [TestMethod]
    public void CurveEditing_ChangesOnlySelectedTokensAndRejectsCrossingOrNonfiniteNumbers()
    {
        Assert.IsTrue(VfxCurve.TryParse(Curve, out var curve));
        Assert.AreEqual(Curve, curve!.ToString());
        curve.Channels[0].MovePoint(1, 0.6, 0.8);
        var output = curve.ToString();
        StringAssert.Contains(output, "spline,spline,-0.2,0.3,0.2,0.3,0");
        StringAssert.EndsWith(output, ":alpha,0;0,0");
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => curve.Channels[0].MovePoint(1, 1, 2));
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => curve.Channels[0].MovePoint(1, 0.5, double.NaN));
        Assert.IsFalse(VfxCurve.TryParse("unknown:future", out _));
    }

    [TestMethod]
    public void CurveEditing_EmptyAuthoredChannelsSurviveSavingAndUndo() => WithEditor((editor, context) =>
    {
        var text = Constant("x_min", 0.15) + "|" + Constant("x_max", 0.2)
            + "|z_min,4210816,1|z_max,4210943,1:x,1;0,1|z,0;2,3";
        Assert.IsTrue(VfxCurve.TryParse(text, out var curve));
        Assert.AreEqual(text, curve!.ToString());
        Assert.AreEqual(0, curve.Channels[2].Points.Count);
        context.File.DataSource = new MemorySource(Encoding.UTF8.GetBytes(Sample.Replace(Curve, text)));
        editor.LoadFile(context.File);
        var field = Field(editor, "curve_alpha");
        Assert.IsFalse(field.IsOpaque);
        field.Curve!.SelectedChannel = field.Curve.Channels[2];
        Assert.IsNull(field.Curve.SelectedPoint);
        field.Curve.SelectedChannel = field.Curve.Channels[0];
        field.Curve.SelectedPoint!.Value.Text = "0.25";
        Assert.IsTrue(editor.Save());
        Assert.AreEqual(text.Replace("0.0,0.15,linear", "0.0,0.25,linear"),
            VfxDocument.Read(context.Saved!).Layers[0].Fields.First(x => x.Name == "curve_alpha").RawValue);
        editor.UndoCommand.Execute(null);
        Assert.IsTrue(editor.Save());
        CollectionAssert.AreEqual(context.File.DataSource.ReadData(), context.Saved!);
    });

    [TestMethod]
    public void CurveEditing_UnorderedKeysRemainEditableWithoutReorderingStoredData()
    {
        Assert.IsTrue(VfxCurve.TryParse(UnorderedCurve, out var curve));
        Assert.AreEqual(UnorderedCurve, curve!.ToString());
        var channel = curve.Channels[0];
        CollectionAssert.AreEqual(new[] { 0.0, 0.15, 1.0 }, channel.Points.Select(x => x.Time).ToArray());
        channel.MovePoint(1, 0.25, 0.75);
        Assert.AreEqual(UnorderedCurve.Replace("0.15,1.0,sine,log3", "0.25,0.75,sine,log3"), curve.ToString());
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => channel.MovePoint(1, 1.1, 0.75));
    }

    [TestMethod]
    public void CurveEditing_CoincidentKeysPreserveEveryKeyAndAllowValueChanges()
    {
        var text = UnorderedCurve.Replace("0.15,1.0,sine,log3", "0.0,1.0,sine,log3");
        Assert.IsTrue(VfxCurve.TryParse(text, out var curve));
        Assert.AreEqual(text, curve!.ToString());
        var channel = curve.Channels[0];
        Assert.AreEqual(3, channel.Points.Count);
        channel.MovePoint(1, 0, 0.75);
        Assert.AreEqual(text.Replace("0.0,1.0,sine,log3", "0.0,0.75,sine,log3"), curve.ToString());
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => channel.MovePoint(0, 0.1, 0.5));
    }

    [TestMethod]
    public void UnorderedCurve_EditorSavesSelectedKeyAndUndoRestoresOriginalBytes() => WithEditor((editor, context) =>
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Sample.Replace(Curve, UnorderedCurve))).ToArray();
        context.File.DataSource = new MemorySource(bytes);
        editor.LoadFile(context.File);
        var field = Field(editor, "curve_alpha");
        Assert.IsFalse(field.IsOpaque);
        field.Curve!.SelectedPoint = field.Curve.SelectedChannel!.Points[1];
        field.Curve.SelectedPoint.Value.Text = "0.75";
        Assert.IsFalse(editor.HasErrors);
        Assert.IsTrue(editor.Save());
        Assert.AreEqual(UnorderedCurve.Replace("0.15,1.0,sine,log3", "0.15,0.75,sine,log3"),
            VfxDocument.Read(context.Saved!).Layers[0].Fields.First(x => x.Name == "curve_alpha").RawValue);
        editor.UndoCommand.Execute(null);
        Assert.IsTrue(editor.Save());
        CollectionAssert.AreEqual(bytes, context.Saved!);
    });

    [TestMethod]
    public void InvalidXml_ReportsFileAndLocationWithoutDiscardingCurrentDocument() => WithEditor((editor, context) =>
    {
        var invalidFile = PackFile.CreateFromBytes("broken.xml", Encoding.UTF8.GetBytes("<vfx revision=\"2\">\n<emitter name=\"a\" name=\"b\"/>\n</vfx>"));
        editor.LoadFile(invalidFile);
        Assert.AreSame(context.File, editor.CurrentFile);
        Assert.IsTrue(editor.HasDocument);
        Assert.IsFalse(editor.HasUnsavedChanges);
        context.Dialogs.Verify(x => x.ShowDialogBox(It.Is<string>(message => message.Contains("broken.xml") && message.Contains("2")),
            It.IsAny<string>(), UiMessageBoxIcon.Warning), Times.Once);
    });

    [TestMethod]
    public void OpeningAndColourPickerInitialization_DoNotDirtyOrQuantizeFile() => WithEditor((editor, _) =>
    {
        Assert.IsFalse(editor.HasUnsavedChanges);
        var color = Field(editor, "curve_colour").Colours.Single();
        color.Picker.OnHandleColourChanged();
        Assert.IsFalse(editor.HasUnsavedChanges, "Displaying an 8-bit swatch must not quantize source float values.");
        Assert.AreEqual("VFX 可视化编辑器", editor.Localization.Get("Vfx.Title"));
    });

    [TestMethod]
    public void UndoRedo_BranchingCurvesAndSavepointRestoreExactState() => WithEditor((editor, context) =>
    {
        var field = Field(editor, "curve_alpha");
        field.Curve!.SelectedPoint = field.Curve.SelectedChannel!.Points[1];
        field.Curve.MoveSelected(0.6, 0.8);
        editor.UndoCommand.Execute(null);
        Assert.IsFalse(editor.HasUnsavedChanges);
        Assert.AreEqual(editor.Localization.Get("Vfx.Status.Ready"), editor.Status);
        Assert.AreEqual(Curve, Field(editor, "curve_alpha").RawValue);
        editor.RedoCommand.Execute(null);
        Assert.AreEqual(editor.Localization.Get("Vfx.Status.Modified"), editor.Status);
        Assert.IsTrue(editor.Save());
        Assert.IsFalse(editor.HasUnsavedChanges);
        var saved = context.Saved!;
        field = Field(editor, "curve_alpha");
        field.Curve!.SelectedPoint = field.Curve.SelectedChannel!.Points[2];
        field.Curve.MoveSelected(1.1, 0.2);
        editor.UndoCommand.Execute(null);
        Assert.IsFalse(editor.HasUnsavedChanges);
        field = Field(editor, "curve_alpha");
        field.Curve!.SelectedPoint = field.Curve.SelectedChannel!.Points[0];
        field.Curve.MoveSelected(0.1, 0.1);
        editor.UndoCommand.Execute(null);
        Assert.IsFalse(editor.HasUnsavedChanges);
        Assert.IsTrue(editor.Save());
        CollectionAssert.AreEqual(saved, context.Saved!);
    });

    [TestMethod]
    public void KeyboardShortcuts_UndoRedoAndSaveUseTheDocumentCommandsWithoutSystemInput() => WithEditor((editor, context) =>
    {
        var view = new VfxEditorView { DataContext = editor };
        var original = Field(editor, "LifeTime").Values[0].Text;
        Field(editor, "LifeTime").Values[0].Text = "1.5";
        Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control));
        Assert.AreEqual(original, Field(editor, "LifeTime").Values[0].Text);
        Assert.IsFalse(editor.HasUnsavedChanges);
        Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.AreEqual("1.5", Field(editor, "LifeTime").Values[0].Text);
        Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control));
        Assert.IsTrue(Shortcut(view, Key.Y, ModifierKeys.Control));
        Assert.AreEqual("1.5", Field(editor, "LifeTime").Values[0].Text);
        Assert.IsTrue(Shortcut(view, Key.S, ModifierKeys.Control));
        Assert.IsNotNull(context.Saved);
        Assert.IsFalse(editor.HasUnsavedChanges);
        view.DataContext = null;
    });

    [DataTestMethod]
    [DataRow(Key.Z, ModifierKeys.None)]
    [DataRow(Key.Z, ModifierKeys.Control | ModifierKeys.Alt)]
    [DataRow(Key.Y, ModifierKeys.Control | ModifierKeys.Shift)]
    [DataRow(Key.S, ModifierKeys.Control | ModifierKeys.Shift)]
    [DataRow(Key.G, ModifierKeys.Control)]
    public void KeyboardShortcuts_UnsupportedCombinationsDoNotConsumeOrEdit(Key key, ModifierKeys modifiers) => WithEditor((editor, context) =>
    {
        var view = new VfxEditorView { DataContext = editor };
        Field(editor, "LifeTime").Values[0].Text = "1.5";
        Assert.IsFalse(Shortcut(view, key, modifiers));
        Assert.AreEqual("1.5", Field(editor, "LifeTime").Values[0].Text);
        Assert.IsTrue(editor.HasUnsavedChanges);
        Assert.IsNull(context.Saved);
        view.DataContext = null;
    });

    [TestMethod]
    public void KeyboardShortcuts_SupportedEmptyHistoryIsConsumedButMissingEditorIsNot() => WithEditor((editor, _) =>
    {
        var view = new VfxEditorView { DataContext = editor };
        Assert.IsFalse(editor.UndoCommand.CanExecute(null));
        Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control));
        Assert.IsTrue(Shortcut(view, Key.Y, ModifierKeys.Control));
        Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
        Assert.IsFalse(editor.HasUnsavedChanges);
        view.DataContext = null;
        Assert.IsFalse(Shortcut(view, Key.Z, ModifierKeys.Control));
    });

    [TestMethod]
    public void PreviewTransforms_UndoRedoAndResetPreserveDocumentAndPlaybackAndDetachOnClose() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var scene = CreateTestScene(true, out var interaction);
            var context = new Context(scenePreview: scene); var editor = context.Editor;
            var view = new VfxEditorView { DataContext = editor };
            try
            {
                editor.PreviewAllCommand.Execute(null); RefreshScene(editor);
                scene.Preview.Update(new Microsoft.Xna.Framework.GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(1.25)));
                scene.Preview.IsPlaying = false;
                var original = DocumentBytes(editor); var beforeTime = scene.Preview.CurrentTime;
                var translated = Microsoft.Xna.Framework.Matrix.CreateTranslation(2, 3, 4);
                CommitPreviewTranslation(interaction!, new(2, 3, 4));
                Assert.AreEqual(translated, interaction!.Target.World);
                Assert.IsTrue(editor.UndoCommand.CanExecute(null));
                Assert.IsFalse(editor.HasUnsavedChanges);
                Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control));
                Assert.AreEqual(Microsoft.Xna.Framework.Matrix.Identity, interaction.Target.World);
                Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control | ModifierKeys.Shift));
                Assert.AreEqual(translated, interaction.Target.World);
                editor.ResetEmitterCommand.Execute(null);
                Assert.AreEqual(Microsoft.Xna.Framework.Matrix.Identity, interaction.Target.World);
                editor.UndoCommand.Execute(null);
                Assert.AreEqual(translated, interaction.Target.World);
                editor.RedoCommand.Execute(null);
                Assert.AreEqual(Microsoft.Xna.Framework.Matrix.Identity, interaction.Target.World);
                Assert.AreEqual(beforeTime, scene.Preview.CurrentTime);
                Assert.IsFalse(scene.Preview.IsPlaying);
                Assert.IsFalse(PreviewTimer(editor).IsEnabled);
                Assert.IsNull(typeof(VfxPreviewComponent).GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scene.Preview));
                Assert.IsFalse(editor.HasUnsavedChanges);
                CollectionAssert.AreEqual(original, DocumentBytes(editor));
                CollectionAssert.AreEqual(original, context.File.DataSource.ReadData());
                context.Save.VerifyNoOtherCalls();
                editor.LoadFile(context.File);
                Assert.IsFalse(editor.UndoCommand.CanExecute(null));
                Assert.IsFalse(editor.RedoCommand.CanExecute(null));
                editor.Close();
                CommitPreviewTranslation(interaction, new(7, 8, 9));
                Assert.IsFalse(editor.UndoCommand.CanExecute(null), "Closed editors must not record later scene events.");
            }
            finally { view.DataContext = null; editor.Close(); scene.Dispose(); }
        });

    [TestMethod]
    public void PreviewTransforms_InterleaveWithDocumentHistoryAndSavepointsWithoutDirtyingOrLosingOrder() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var scene = CreateTestScene(true, out var interaction);
            var context = new Context(scenePreview: scene); var editor = context.Editor;
            var original = Field(editor, "LifeTime").Values[0].Text;
            try
            {
                CommitPreviewTranslation(interaction!, new(1, 0, 0));
                Assert.IsFalse(editor.HasUnsavedChanges);
                Field(editor, "LifeTime").Values[0].Text = "1.5";
                CommitPreviewTranslation(interaction!, new(1, 0, 0));
                Assert.IsTrue(editor.Save());
                var saved = context.Saved!;
                editor.UndoCommand.Execute(null);
                Assert.AreEqual(new Microsoft.Xna.Framework.Vector3(1, 0, 0), interaction!.Target.Position);
                Assert.AreEqual("1.5", Field(editor, "LifeTime").Values[0].Text);
                Assert.IsFalse(editor.HasUnsavedChanges);
                editor.UndoCommand.Execute(null);
                Assert.AreEqual(original, Field(editor, "LifeTime").Values[0].Text);
                Assert.IsTrue(editor.HasUnsavedChanges);
                editor.UndoCommand.Execute(null);
                Assert.AreEqual(Microsoft.Xna.Framework.Vector3.Zero, interaction.Target.Position);
                Assert.IsTrue(editor.HasUnsavedChanges);
                for (var index = 0; index < 3; index++) editor.RedoCommand.Execute(null);
                Assert.AreEqual(new Microsoft.Xna.Framework.Vector3(2, 0, 0), interaction.Target.Position);
                Assert.IsFalse(editor.HasUnsavedChanges);
                CollectionAssert.AreEqual(saved, DocumentBytes(editor));
                editor.UndoCommand.Execute(null);
                CommitPreviewTranslation(interaction, new(5, 0, 0));
                Assert.IsFalse(editor.RedoCommand.CanExecute(null), "A new preview gesture must branch the same chronological history.");
                Assert.IsFalse(editor.HasUnsavedChanges);
                editor.UndoCommand.Execute(null);
                Assert.AreEqual(new Microsoft.Xna.Framework.Vector3(1, 0, 0), interaction.Target.Position);
                editor.UndoCommand.Execute(null);
                Assert.AreEqual(original, Field(editor, "LifeTime").Values[0].Text);
                Assert.IsTrue(editor.HasUnsavedChanges);
                editor.UndoCommand.Execute(null);
                Assert.AreEqual(Microsoft.Xna.Framework.Vector3.Zero, interaction.Target.Position);
            }
            finally { editor.Close(); scene.Dispose(); }
        });

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PreviewTransforms_UndoCancelsTheActiveGestureBeforeConsumingACommittedStep(bool boxSelection) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var scene = CreateTestScene(true, out var interaction);
            var context = new Context(scenePreview: scene); var editor = context.Editor;
            var view = new VfxEditorView { DataContext = editor };
            try
            {
                interaction!.Select();
                CommitPreviewTranslation(interaction, new(1, 0, 0));
                if (boxSelection)
                {
                    interaction.Select(false);
                    typeof(VfxEmitterInteractionComponent).GetField("_selectionBeforeBox", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(interaction, true);
                    typeof(VfxEmitterInteractionComponent).GetField("_isBoxSelecting", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(interaction, true);
                }
                else
                {
                    interaction.BeginTransform();
                    interaction.ApplyTransform(GameWorld.Core.Components.Gizmo.ModalPreviewReplacement.Translate(new(5, 0, 0), GameWorld.Core.Components.Gizmo.PivotType.ObjectCenter));
                }
                Assert.IsTrue(interaction.HasActiveInteraction);
                Assert.IsFalse(editor.RedoCommand.CanExecute(null));
                Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control));
                Assert.IsFalse(interaction.HasActiveInteraction);
                Assert.IsTrue(interaction.IsSelected);
                Assert.AreEqual(new Microsoft.Xna.Framework.Vector3(1, 0, 0), interaction.Target.Position);
                Assert.IsTrue(editor.UndoCommand.CanExecute(null), "Cancelling an active operation must preserve the preceding committed step.");
                Assert.IsTrue(Shortcut(view, Key.Z, ModifierKeys.Control));
                Assert.AreEqual(Microsoft.Xna.Framework.Vector3.Zero, interaction.Target.Position);
                Assert.IsTrue(Shortcut(view, Key.Y, ModifierKeys.Control));
                Assert.AreEqual(new Microsoft.Xna.Framework.Vector3(1, 0, 0), interaction.Target.Position);
                Assert.IsFalse(editor.HasUnsavedChanges);
                context.Save.VerifyNoOtherCalls();
            }
            finally { view.DataContext = null; editor.Close(); scene.Dispose(); }
        });

    private static bool Shortcut(VfxEditorView view, Key key, ModifierKeys modifiers) => (bool)typeof(VfxEditorView)
        .GetMethod("TryExecuteShortcut", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(view, [key, modifiers])!;

    private static byte[] DocumentBytes(VfxEditorViewModel editor) => ((VfxDocument)typeof(VfxEditorViewModel)
        .GetField("_document", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!).Write();

    private static DispatcherTimer PreviewTimer(VfxEditorViewModel editor) => (DispatcherTimer)typeof(VfxEditorViewModel)
        .GetField("_scenePreviewTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;

    private static void CommitPreviewTranslation(VfxEmitterInteractionComponent interaction, Microsoft.Xna.Framework.Vector3 offset)
    {
        interaction.BeginTransform();
        interaction.ApplyTransform(GameWorld.Core.Components.Gizmo.ModalPreviewReplacement.Translate(offset, GameWorld.Core.Components.Gizmo.PivotType.ObjectCenter));
        typeof(VfxEmitterInteractionComponent).GetMethod("EndTransform", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(interaction, null);
    }

    [TestMethod]
    public void InvalidHiddenValuesAndRanges_BlockSaveWithoutDiscardingEdits() => WithEditor((editor, context) =>
    {
        Field(editor, "cell").Values[0].Text = "2.5";
        Assert.IsTrue(editor.HasErrors);
        Assert.IsTrue(editor.HasUnsavedChanges);
        editor.SelectedLayer = editor.Layers[1];
        Assert.IsFalse(editor.Save());
        Assert.IsNull(context.Saved);
        editor.UndoCommand.Execute(null);
        Assert.IsFalse(editor.HasErrors);
        Field(editor, "LifeTime").Values[0].Text = "3";
        Assert.IsFalse(editor.Save());
        Field(editor, "LifeTime").Values[1].Text = "4";
        Assert.IsTrue(editor.Save());
    });

    [TestMethod]
    public void SaveFailureAndCancel_KeepDirtyAndReferenceNeverOverwritesOtherProjectFile() => WithEditor((editor, context) =>
    {
        Field(editor, "SpawnSpacing").Values[0].Text = "0.1";
        context.Save.Setup(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), false)).Throws(new IOException("expected"));
        Assert.IsFalse(editor.Save());
        Assert.IsTrue(editor.HasUnsavedChanges);
        context.Files.Setup(x => x.GetEditablePack()).Returns(new PackFileContainer("different-project"));
        context.Dialogs.Setup(x => x.DisplaySaveDialog(context.Files.Object, It.IsAny<List<string>>()))
            .Returns(new SaveDialogResult(false, null, null));
        Assert.IsFalse(editor.Save());
        Assert.IsTrue(editor.HasUnsavedChanges);
        context.Dialogs.Verify(x => x.DisplaySaveDialog(context.Files.Object, It.IsAny<List<string>>()), Times.Once);
        context.Save.Verify(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), false), Times.Once);
    });

    [TestMethod]
    public void Save_RejectsExternalFileChangesAndKeepsQualityVariantsSeparate() => WithEditor((editor, context) =>
    {
        var alpha = Field(editor, "curve_alpha");
        alpha.Curve!.SelectedPoint = alpha.Curve.SelectedChannel!.Points[1];
        alpha.Curve.MoveSelected(0.6, 0.7);
        Assert.AreEqual(Curve, editor.Layers[0].Fields.Single(x => x.Model.Name == "curve_alpha" && x.Model.Quality == "2").RawValue);
        context.File.DataSource = new MemorySource(Encoding.UTF8.GetBytes(Sample + "<!-- external -->"));
        Assert.IsFalse(editor.Save());
        Assert.IsTrue(editor.HasUnsavedChanges);
        Assert.IsNull(context.Saved);
    });

    [TestMethod]
    public void VisualEditor_RendersAllThemesAndScaledLayoutsWithoutChangingData()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                var context = new Context();
                var editor = context.Editor;
                editor.SelectedField = Field(editor, "curve_alpha");
                foreach (var theme in Enum.GetValues<ThemeType>())
                {
                    ThemesController.SetTheme(theme);
                    var view = new VfxEditorView { DataContext = editor };
                    view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    view.Measure(new Size(1140, 800));
                    view.Arrange(new Rect(0, 0, 1140, 800));
                    view.UpdateLayout();
                    var graph = Descendants(view).OfType<VfxCurveGraph>().Single();
                    Assert.IsTrue(graph.ActualWidth > 200);
                    Assert.AreEqual(((SolidColorBrush)view.FindResource("AeBrush.Canvas")).Color, ((SolidColorBrush)view.Background).Color, theme.ToString());
                    var sample = graph.TransformToAncestor(view).Transform(new Point(5, 5));
                    var surface = ((SolidColorBrush)view.FindResource("AeBrush.Surface1")).Color;
                    foreach (var scale in new[] { 1.0, 1.25, 1.5 })
                    {
                        var bitmap = new RenderTargetBitmap((int)(1140 * scale), (int)(800 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        bitmap.Render(view);
                        var pixel = new byte[4];
                        bitmap.CopyPixels(new Int32Rect((int)(sample.X * scale), (int)(sample.Y * scale), 1, 1), pixel, 4, 0);
                        Assert.AreEqual(surface, Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]), $"{theme}, {scale}");
                        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                        if (!string.IsNullOrWhiteSpace(output) && (theme is ThemeType.DarkTheme or ThemeType.LightTheme or ThemeType.HighContrastDark or ThemeType.HighContrastLight))
                        {
                            Directory.CreateDirectory(output);
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmap));
                            using var stream = File.Create(Path.Combine(output, $"vfx-{theme}-{scale.ToString(CultureInfo.InvariantCulture)}.png"));
                            encoder.Save(stream);
                        }
                    }
                    Assert.IsFalse(editor.HasUnsavedChanges);
                    view.DataContext = null;
                }
                editor.Close();
            }
            finally
            {
                ThemesController.SetTheme(previous);
            }
        });
    }

    [TestMethod]
    public void Workspace_UsesCompactViewportMenusAndRightInspectorWithoutEditingTheDocument() => WithEditor((editor, context) =>
    {
        var original = context.File.DataSource.ReadData();
        var view = new VfxEditorView { DataContext = editor };
        view.Measure(new Size(1140, 800)); view.Arrange(new Rect(0, 0, 1140, 800)); view.UpdateLayout();
        var workspace = (Grid)view.FindName("Workspace");
        var scene = (Grid)view.FindName("ScenePreviewPanel");
        var inspector = (TabControl)view.FindName("Inspector");
        Assert.AreEqual(3, workspace.ColumnDefinitions.Count, "There must be no extra layer column beside the global file tree.");
        Assert.AreEqual(0, Grid.GetColumn(scene)); Assert.AreEqual(2, Grid.GetColumn(inspector));
        var toolbar = (WrapPanel)view.FindName("SceneToolbar");
        Assert.IsTrue(toolbar.ActualHeight > 0 && toolbar.ActualHeight <= 28, $"Compact menu row: {toolbar.ActualHeight}");
        Assert.IsTrue(Descendants(toolbar).OfType<Menu>().Any());
        var menu = Descendants(toolbar).OfType<Menu>().Single();
        CollectionAssert.AreEqual(new[] { editor.Localization.Get("Vfx.Menu.File"), editor.Localization.Get("Vfx.Menu.Edit"), editor.Localization.Get("Vfx.Menu.View") },
            menu.Items.Cast<MenuItem>().Select(item => item.Header.ToString()).ToArray());
        Assert.IsFalse(menu.Items.Cast<MenuItem>().Any(item => Equals(item.Header, editor.Localization.Get("Vfx.Menu.Transform"))));
        Assert.IsFalse(Descendants(view).OfType<TextBlock>().Any(text => text.Text == editor.Localization.Get("Vfx.WelcomeHint")));
        Assert.AreEqual("透明度曲线", Field(editor, "curve_alpha").Label);
        var field = Field(editor, "curve_alpha");
        Assert.IsTrue(field.ToolTip.Contains(field.Description)); Assert.IsTrue(field.ToolTip.Contains(field.AdjustmentHint));
        Assert.IsTrue(field.ToolTip.Contains(field.Model.Name));
        var source = Descendants(view).OfType<ComboBox>().Single(x => ReferenceEquals(x.ItemsSource, editor.Layers));
        source.SelectedItem = editor.Layers.Single(x => x.Model.Kind == "Reference");
        Assert.AreSame(source.SelectedItem, editor.SelectedLayer);
        inspector.SelectedIndex = 1; view.UpdateLayout();
        Assert.IsTrue(((Grid)view.FindName("LayerPanel")).ActualWidth >= 360);
        Assert.AreSame(editor.Layers, ((ListBox)view.FindName("LayerList")).ItemsSource);
        CollectionAssert.AreEqual(original, context.File.DataSource.ReadData());
        Assert.IsFalse(editor.HasUnsavedChanges); Assert.IsFalse(editor.UndoCommand.CanExecute(null));
        context.Save.Verify(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()), Times.Never);
        view.DataContext = null;
    });

    [TestMethod]
    public void InlineParameterEditor_KeepsEditsWhenSwitchingSearchingAndUndoing() => WithEditor((editor, context) =>
    {
        var view = new VfxEditorView { DataContext = editor };
        var fields = (ListBox)view.FindName("FieldList");
        var lifetime = Field(editor, "LifeTime");
        editor.SelectedField = lifetime;
        Layout();
        fields.ScrollIntoView(lifetime);
        Layout();
        var input = Descendants(view).OfType<TextBox>().Single(x => ReferenceEquals(x.DataContext, lifetime.Values[0]));
        input.Text = "1.5";
        Assert.AreEqual("1.5", lifetime.Values[0].Text);
        Assert.IsTrue(editor.HasUnsavedChanges);
        var transparency = Field(editor, "curve_alpha");
        fields.ScrollIntoView(transparency);
        Layout();
        var header = Descendants(fields).OfType<Expander>().Single(x => ReferenceEquals(x.Header, transparency));
        header.SetCurrentValue(Expander.IsExpandedProperty, true);
        Layout();
        Assert.AreSame(transparency, editor.SelectedField);
        Assert.AreEqual("1.5", lifetime.Values[0].Text);
        editor.Search = "no-matching-parameter";
        Layout();
        Assert.IsTrue(editor.HasNoFields);
        editor.Search = "";
        Layout();
        Assert.IsTrue(editor.UndoCommand.CanExecute(null));
        editor.UndoCommand.Execute(null);
        Layout();
        Assert.AreEqual("0.25", Field(editor, "LifeTime").Values[0].Text);
        Assert.IsFalse(editor.HasUnsavedChanges);
        Assert.IsNull(context.Saved);
        view.DataContext = null;

        void Layout()
        {
            view.Measure(new Size(760, 640));
            view.Arrange(new Rect(0, 0, 760, 640));
            view.UpdateLayout();
            view.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    });

    [TestMethod]
    public void ReferencePresentation_ShowsSourceNameAndAlignedAxesWithoutChangingData() => WithEditor((editor, context) =>
    {
        Assert.IsTrue(Field(editor, "BoundingBoxMax").IsVector);
        var document = VfxDocument.CreateComposition();
        document.AddReference("animation_hu1e_test", VfxDocument.Read(Encoding.UTF8.GetBytes(Sample))).Apply();
        var bytes = document.Write();
        context.File.DataSource = new MemorySource(bytes);
        editor.LoadFile(context.File);
        var reference = editor.Layers.Single(x => x.Model.Kind == "Reference");
        Assert.AreEqual("animation_hu1e_test", reference.Label);
        editor.SelectedLayer = reference;
        Assert.IsTrue(editor.SelectedField!.IsVector);
        CollectionAssert.AreEqual(new[] { "X 轴", "Y 轴", "Z 轴" }, editor.SelectedField.Values.Select(x => x.Label).ToArray());
        Assert.IsFalse(editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(bytes, context.File.DataSource.ReadData());
    });

    [TestMethod]
    public void ValueEditors_RenderWithoutWritingToTheSource() => WithEditor((editor, _) =>
    {
        editor.Category = "All";
        foreach (var name in new[] { "curve_colour", "InfiniteLife", "LifeTime", "alpha_START_offset", "payload" })
        {
            editor.SelectedField = Field(editor, name);
            var view = new VfxEditorView { DataContext = editor };
            view.Measure(new Size(960, 700));
            view.Arrange(new Rect(0, 0, 960, 700));
            view.UpdateLayout();
            ((ListBox)view.FindName("FieldList")).ScrollIntoView(editor.SelectedField);
            view.UpdateLayout();
            var bitmap = new RenderTargetBitmap(960, 700, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            Assert.IsFalse(editor.HasUnsavedChanges, name);
            Assert.IsFalse(editor.HasErrors, name);
            view.DataContext = null;
        }
    });

    [TestMethod]
    public void SmallValueCurves_UseTheGraphHeightInsteadOfCollapsingAtZero() => WithEditor((editor, context) =>
    {
        context.File.DataSource = new MemorySource(Encoding.UTF8.GetBytes(Sample.Replace("0.5,1.0,spline", "0.5,0.02,spline")));
        editor.LoadFile(context.File);
        var graph = new VfxCurveGraph { DataContext = Field(editor, "curve_alpha").Curve };
        graph.Measure(new Size(640, 240));
        graph.Arrange(new Rect(0, 0, 640, 240));
        graph.UpdateLayout();
        var bitmap = new RenderTargetBitmap(640, 240, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var pixels = new byte[640 * 240 * 4];
        bitmap.CopyPixels(pixels, 640 * 4, 0);
        var accent = ((SolidColorBrush)graph.FindResource("AeBrush.Accent")).Color;
        var rows = Enumerable.Range(0, 240).Where(y => Enumerable.Range(64, 553).Any(x =>
        {
            var offset = (y * 640 + x) * 4;
            return pixels[offset] == accent.B && pixels[offset + 1] == accent.G && pixels[offset + 2] == accent.R;
        })).ToArray();
        Assert.IsTrue(rows.Length > 0);
        Assert.IsTrue(rows.Max() - rows.Min() > 120, "A small nonzero value must remain easy to see and drag.");
        Assert.IsFalse(editor.HasUnsavedChanges);
    });

    [TestMethod]
    public void SaveToRealFolderProject_WritesReadableVfxAndCanReopen()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"ae-vfx-save-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(root);
            try
            {
                using var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "VFX test" });
                var files = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false, MessageBoxProvider = Mock.Of<ISimpleMessageBox>() };
                files.AddEditableFolderProject(project);
                var source = PackFile.CreateFromBytes("water_trail.xml", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Sample)).ToArray());
                files.AddFilesToPack(project, [new NewPackFileEntry("vfx", source)]);
                var file = files.FindFile("vfx\\water_trail.xml", project)!;
                var dialogs = Mock.Of<IStandardDialogs>();
                var editor = new VfxEditorViewModel(files, new FileSaveService(files, dialogs), dialogs, Mock.Of<IEditorCreator>(), LocalizationManager.Instance);
                editor.LoadFile(file);
                Field(editor, "SpawnSpacing").Values[0].Text = "0.2";
                Assert.IsTrue(editor.Save());
                Assert.IsFalse(editor.HasUnsavedChanges);
                var bytes = File.ReadAllBytes(Path.Combine(root, "vfx", "water_trail.xml"));
                Assert.IsTrue(bytes.AsSpan().StartsWith(Encoding.Unicode.GetPreamble()));
                Assert.AreEqual("0.2", VfxDocument.Read(bytes).Layers[0].Fields.Single(x => x.Name == "SpawnSpacing").RawValue);
                editor.LoadFile(file);
                Assert.AreEqual("0.2", Field(editor, "SpawnSpacing").Values[0].Text);
                var compositionDialogs = new Mock<IStandardDialogs>();
                compositionDialogs.Setup(x => x.DisplayBrowseDialog(It.IsAny<List<string>>(), It.IsAny<BrowseDialogFilter>()))
                    .Returns(new BrowseDialogResultFile(true, file));
                compositionDialogs.Setup(x => x.DisplaySaveDialog(files, It.IsAny<List<string>>()))
                    .Returns(new SaveDialogResult(true, null, "vfx\\combined.xml"));
                var composition = new VfxEditorViewModel(files, new FileSaveService(files, compositionDialogs.Object),
                    compositionDialogs.Object, Mock.Of<IEditorCreator>(), LocalizationManager.Instance);
                composition.NewComposition();
                composition.AddReferenceCommand.Execute(null);
                composition.DuplicateLayerCommand.Execute(null);
                composition.SelectedLayer!.Fields.Single(x => x.Model.Name == "reference_position").Values[1].Text = "3";
                Assert.IsTrue(composition.Save());
                Assert.AreSame(files.FindFile("vfx\\combined.xml", project), composition.CurrentFile);
                Assert.AreEqual("vfx\\combined.xml", composition.FilePath);
                Assert.IsTrue(composition.Save(), "A newly saved composition must remain attached for subsequent saves.");
                var combined = VfxDocument.Read(File.ReadAllBytes(Path.Combine(root, "vfx", "combined.xml")));
                Assert.AreEqual(2, combined.Layers.Count(x => x.Kind == "Reference"));
                Assert.AreEqual("3", combined.Layers.Last(x => x.Kind == "Reference").Element.Attribute("pos_y")!.Value);
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.Combine(root, "vfx", "water_trail.xml")));
                composition.LoadFile(files.FindFile("vfx\\combined.xml", project)!);
                Assert.AreEqual(2, composition.Layers.Count(x => x.Model.Kind == "Reference"));
                Assert.IsFalse(composition.HasUnsavedChanges);
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [TestMethod]
    public void RealVfxSamples_RoundTripAndEditWithoutLosingOtherNodes()
    {
        var directory = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_SAMPLES");
        if (string.IsNullOrWhiteSpace(directory)) Assert.Inconclusive("Set ASSETEDITOR_VFX_SAMPLES to validate locally available VFX files.");
        var files = Directory.GetFiles(directory!, "*.xml", SearchOption.AllDirectories);
        Assert.IsTrue(files.Length > 0);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            var document = VfxDocument.Read(bytes);
            CollectionAssert.AreEqual(bytes, document.Write(), file);
            foreach (var field in document.Layers.SelectMany(x => x.Fields).Where(x => x.IsText
                && ((string?)x.Element.Attribute("type") == "curve" || x.RawValue.Contains(':'))))
            {
                Assert.IsTrue(VfxCurve.TryParse(field.RawValue, out var curve), $"{file}: {field.Name}");
                Assert.AreEqual(field.RawValue, curve!.ToString(), $"{file}: {field.Name}");
                var channel = curve.Channels.FirstOrDefault(x => x.Points.Count != 0);
                if (channel != null)
                {
                    channel.MovePoint(0, channel.Points[0].Time, channel.Points[0].Value + 0.125);
                    field.Element.Value = curve.ToString();
                }
            }
            var roundTrip = VfxDocument.Read(document.Write());
            Assert.IsTrue(XNode.DeepEquals(document.Root, roundTrip.Root), file);
        }
        Console.WriteLine($"Validated {files.Length} real VFX XML files.");
    }

    [TestMethod]
    public void LiveEdits_ArePublishedWhileRenderWorkRemainsQueued() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var store = new VfxPreviewDocumentStore();
            var context = new Context(store);
            var editor = context.Editor;
            var original = context.File.DataSource.ReadData();
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            var deadline = new DispatcherTimer(DispatcherPriority.Send) { Interval = TimeSpan.FromSeconds(2) };
            DispatcherOperation? queuedRender = null;
            var rendering = true;
            var renderCount = 0;
            var published = false;
            void Render()
            {
                renderCount++;
                if (rendering) queuedRender = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(Render));
            }
            void Published(object file)
            {
                if (!ReferenceEquals(file, context.File)) return;
                published = true;
                frame.Continue = false;
            }
            deadline.Tick += (_, _) => frame.Continue = false;
            store.Changed += Published;
            try
            {
                Field(editor, "LifeTime").Values[0].Text = "1.5";
                queuedRender = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(Render));
                deadline.Start();
                Dispatcher.PushFrame(frame);
                Assert.IsTrue(renderCount > 0);
                Assert.IsTrue(published, "Continuous rendering must not starve the debounced live preview update.");
                Assert.IsTrue(store.TryGet(context.File, out var snapshot));
                StringAssert.Contains(Encoding.UTF8.GetString(snapshot!.GameBytes), "min=\"1.5\"");
                CollectionAssert.AreEqual(original, context.File.DataSource.ReadData());
                Assert.IsNull(context.Saved);
            }
            finally
            {
                rendering = false;
                deadline.Stop();
                queuedRender?.Abort();
                store.Changed -= Published;
                editor.Close();
            }
        });

    [TestMethod]
    public void UnsavedEdits_ArePublishedForSharedScenesWithoutAnEditorRuntime() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var store = new VfxPreviewDocumentStore();
            var context = new Context(store);
            var editor = context.Editor;
            var original = context.File.DataSource.ReadData();
            var refresh = typeof(VfxEditorViewModel).GetMethod("RefreshScenePreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            void Refresh() => refresh.Invoke(editor, [null, EventArgs.Empty]);
            try
            {
                Assert.IsNull(editor.PreviewWorld);
                Field(editor, "LifeTime").Values[0].Text = "1.5";
                Refresh();
                Assert.IsTrue(store.TryGet(context.File, out var snapshot));
                Assert.IsNotNull(snapshot);
                StringAssert.Contains(Encoding.UTF8.GetString(snapshot.GameBytes), "min=\"1.5\"");
                Assert.IsTrue(editor.HasUnsavedChanges);
                Assert.IsTrue(editor.UndoCommand.CanExecute(null));
                Field(editor, "LifeTime").Values[0].Text = "invalid";
                Refresh();
                Assert.IsTrue(store.TryGet(context.File, out snapshot));
                Assert.IsNull(snapshot);
                editor.UndoCommand.Execute(null);
                Refresh();
                Assert.IsTrue(store.TryGet(context.File, out snapshot));
                Assert.IsNotNull(snapshot);
                CollectionAssert.AreEqual(original, context.File.DataSource.ReadData());
                context.Save.Verify(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()), Times.Never);

            }
            finally { editor.Close(); }
            Assert.IsFalse(store.TryGet(context.File, out _));
        });

    [TestMethod]
    public void ReferencedUnsavedEdits_RefreshTheEditorsOwnSceneAndNeverFallBackToInvalidSavedData() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var store = new VfxPreviewDocumentStore();
            var context = new Context(store, scenePreview: CreateTestScene());
            var editor = context.Editor;
            var owner = new object();
            const string path = @"vfx\child.xml";
            var original = Encoding.UTF8.GetBytes("original child bytes");
            var child = PackFile.CreateFromBytes("child.xml", original);
            var container = context.Files.Object.GetAllPackfileContainers().Single();
            container.FileList.Add(path, child);
            context.Files.Setup(x => x.FindFile(path, container)).Returns(child);
            var refresh = typeof(VfxEditorViewModel).GetMethod("RefreshScenePreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var timer = (System.Windows.Threading.DispatcherTimer)typeof(VfxEditorViewModel)
                .GetField("_scenePreviewTimer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(editor)!;
            void Refresh() => refresh.Invoke(editor, [null, EventArgs.Empty]);
            try
            {
                    Refresh();
                Assert.IsFalse(timer.IsEnabled, "Publishing the root snapshot must not schedule its own reload forever.");
                Assert.IsTrue(store.TryGet(context.File, out var root));
                CollectionAssert.AreEqual(original, root!.Resolve(path)!);
                var edited = Encoding.UTF8.GetBytes("unsaved child bytes");
                store.Publish(owner, child, new(edited, path, _ => null));
                Assert.IsTrue(timer.IsEnabled, "Only a dependency actually resolved by this preview should schedule a reload.");
                Refresh();
                Assert.IsTrue(store.TryGet(context.File, out root));
                CollectionAssert.AreEqual(edited, root!.Resolve(path)!);
                var unrelatedOwner = new object();
                store.Publish(unrelatedOwner, new object(), null);
                Assert.IsFalse(timer.IsEnabled);
                store.Remove(unrelatedOwner);
                store.Publish(owner, child, null);
                Assert.IsTrue(timer.IsEnabled);
                Refresh();
                Assert.IsTrue(store.TryGet(context.File, out root));
                Assert.ThrowsException<InvalidDataException>(() => root!.Resolve(path));
                store.Publish(owner, child, new(edited, path, _ => null));
                Assert.IsTrue(timer.IsEnabled);
                Refresh();
                Assert.IsTrue(store.TryGet(context.File, out root));
                CollectionAssert.AreEqual(edited, root!.Resolve(path)!);
                CollectionAssert.AreEqual(original, child.DataSource.ReadData());
                Assert.IsFalse(editor.HasUnsavedChanges);
                Assert.IsNull(context.Saved);

            }
            finally { editor.Close(); store.Remove(owner); }
            Assert.IsFalse(timer.IsEnabled);
        });


    [TestMethod]
    public void PreviewScope_DefaultsAreStructuralAndNeverUseTheFileName() => WithEditor((editor, context) =>
    {
        Assert.IsFalse(editor.HasPreviewSelection, "A multi-root source requires an explicit scope.");
        editor.PreviewAllCommand.Execute(null);
        Assert.AreEqual(2, editor.Layers.Count(layer => layer.IsIncludedInPreview));
        var composition = PackFile.CreateFromBytes("arbitrary-source-name.xml", Encoding.UTF8.GetBytes(Sample.Replace("construction_type=\"1\"", "construction_type=\"2\"")));
        editor.LoadFile(composition);
        Assert.AreEqual(2, editor.Layers.Count(layer => layer.IsIncludedInPreview));
        var single = VfxDocument.Read(Encoding.UTF8.GetBytes(Sample));
        single.Root.Element("vfx_references")!.Remove();
        editor.LoadFile(PackFile.CreateFromBytes("arbitrary-multi-effect-name.xml", single.Write()));
        Assert.AreEqual(1, editor.Layers.Count(layer => layer.IsIncludedInPreview));
        Assert.IsFalse(editor.HasUnsavedChanges);
        context.Save.VerifyNoOtherCalls();
    });

    [TestMethod]
    public void PreviewScope_OnlyCombinationAndEmptyAffectOwnSceneButNotSavedOrSharedBytes() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var store = new VfxPreviewDocumentStore();
            var scene = CreateTestScene();
            var context = new Context(store, scenePreview: scene);
            var editor = context.Editor;
            var original = context.File.DataSource.ReadData();
            var emitter = editor.Layers.Single(layer => layer.Model.Kind == "Emitter");
            var reference = editor.Layers.Single(layer => layer.Model.Kind == "Reference");
            try
            {
                emitter.PreviewOnlyCommand.Execute(null); RefreshScene(editor);
                var local = PendingScene(scene);
                CollectionAssert.AreEqual(new[] { emitter.Model.Name }, local.RootSelection!.EmitterIds.ToArray());
                Assert.AreEqual(0, local.RootSelection.ReferenceNames.Count);
                editor.SelectedLayer = reference; editor.Quality = "All"; editor.PreviewQuality = "1";
                Assert.IsTrue(emitter.IsIncludedInPreview); Assert.IsFalse(reference.IsIncludedInPreview);
                reference.IsIncludedInPreview = true; RefreshScene(editor);
                Assert.IsNull(PendingScene(scene).RootSelection, "Selecting all preserves the complete original contract.");
                editor.PreviewNoneCommand.Execute(null); RefreshScene(editor);
                Assert.AreEqual(0, PendingScene(scene).RootSelection!.EmitterIds.Count);
                Assert.AreEqual(0, PendingScene(scene).RootSelection!.ReferenceNames.Count);
                Assert.IsTrue(store.TryGet(context.File, out var shared));
                Assert.IsNull(shared!.RootSelection, "Local choices must not contaminate SuperView snapshots.");
                CollectionAssert.AreEqual(original, shared.GameBytes);
                CollectionAssert.AreEqual(original, context.File.DataSource.ReadData());
                Assert.IsFalse(editor.HasUnsavedChanges); Assert.IsFalse(editor.UndoCommand.CanExecute(null));
                Field(editor, "LifeTime").Values[0].Text = "1.5"; RefreshScene(editor);
                Assert.IsTrue(store.TryGet(context.File, out shared));
                StringAssert.Contains(Encoding.UTF8.GetString(shared!.GameBytes), "min=\"1.5\"");
                Assert.IsNull(shared.RootSelection); Assert.IsTrue(editor.HasUnsavedChanges);
                editor.UndoCommand.Execute(null); Assert.IsFalse(editor.HasUnsavedChanges);
                context.Save.VerifyNoOtherCalls();
            }
            finally { editor.Close(); scene.Dispose(); }
        });

    [TestMethod]
    public void InvalidEdits_RestoreThePriorPlayingPausedOrCollapsedState() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            foreach (var (playing, expanded, collapseDuringError) in new[]
                     { (true, true, false), (false, true, false), (true, false, false), (true, true, true), (false, true, true) })
            {
                var scene = CreateTestScene();
                var context = new Context(scenePreview: scene);
                var editor = context.Editor;
                try
                {
                    editor.PreviewAllCommand.Execute(null); RefreshScene(editor);
                    scene.Preview.Update(new Microsoft.Xna.Framework.GameTime());
                    scene.Preview.IsPlaying = playing;
                    editor.Playback.IsEnabled.Value = expanded;
                    var expected = scene.Preview.IsPlaying;
                    Field(editor, "LifeTime").Values[0].Text = "invalid";
                    RefreshScene(editor); RefreshScene(editor);
                    Assert.IsFalse(scene.Preview.IsLoaded); Assert.IsFalse(scene.Preview.IsPlaying);
                    if (collapseDuringError) editor.Playback.IsEnabled.Value = false;
                    editor.UndoCommand.Execute(null); RefreshScene(editor);
                    scene.Preview.Update(new Microsoft.Xna.Framework.GameTime());
                    Assert.IsTrue(scene.Preview.IsLoaded);
                    Assert.AreEqual(expected && !collapseDuringError, scene.Preview.IsPlaying);
                    Assert.AreEqual(expanded && !collapseDuringError, editor.Playback.IsEnabled.Value);
                    editor.Playback.IsEnabled.Value = true;
                    Assert.AreEqual(playing, scene.Preview.IsPlaying, "An invalid edit must not replace the user's playback intent.");
                    Assert.IsFalse(editor.HasUnsavedChanges);
                }
                finally { editor.Close(); scene.Dispose(); }
            }
        });

    [TestMethod]
    public void LoadedPreview_RendersTheSharedSecondsPlayerAcrossThemesWithoutOpeningAWindow() =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var scene = CreateTestScene();
            var context = new Context(scenePreview: scene);
            var editor = context.Editor;
            var previous = ThemesController.CurrentTheme;
            try
            {
                editor.PreviewAllCommand.Execute(null); RefreshScene(editor);
                scene.Preview.Update(new Microsoft.Xna.Framework.GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(2.5)));
                foreach (var theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight })
                {
                    ThemesController.SetTheme(theme);
                    var view = new VfxEditorView { DataContext = editor };
                    view.Measure(new Size(1440, 900)); view.Arrange(new Rect(0, 0, 1440, 900)); view.UpdateLayout();
                    var player = (AnimationEditor.Common.AnimationPlayer.AnimationPlayerView)view.FindName("PlaybackPanel");
                    Assert.AreSame(editor.Playback, player.DataContext);
                    Assert.IsFalse(player.ShowFrameInformation);
                    Assert.IsTrue(((FrameworkElement)player.FindName("PlaybackCommandRow")).ActualHeight > 0);
                    var slider = Descendants(player).OfType<Slider>().Single();
                    Assert.AreEqual(8d, slider.Maximum); Assert.AreEqual(2.5d, slider.Value);
                    var buttons = Descendants(player).OfType<Button>().Where(button => button.Visibility == Visibility.Visible).ToArray();
                    Assert.IsTrue(buttons.Any(button => System.Windows.Automation.AutomationProperties.GetName(button) == "跳到开始"));
                    Assert.IsTrue(buttons.Any(button => System.Windows.Automation.AutomationProperties.GetName(button) == "跳到末尾"));
                    Assert.IsFalse(buttons.Any(button => System.Windows.Automation.AutomationProperties.GetName(button) == editor.Localization.Get("SharedAnimPlayer.NextFrame")));
                    var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        Directory.CreateDirectory(output);
                        var bitmap = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
                        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                        using var stream = File.Create(Path.Combine(output, $"vfx-loaded-seconds-player-{theme}.png")); encoder.Save(stream);
                    }
                    Assert.IsFalse(editor.HasUnsavedChanges);
                    view.DataContext = null;
                }
            }
            finally { editor.Close(); scene.Dispose(); ThemesController.SetTheme(previous); }
        });

    private static void RefreshScene(VfxEditorViewModel editor) => typeof(VfxEditorViewModel)
        .GetMethod("RefreshScenePreview", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(editor, [null, EventArgs.Empty]);

    private static VfxPreviewSnapshot PendingScene(VfxScenePreview scene) => (VfxPreviewSnapshot)typeof(VfxPreviewComponent)
        .GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scene.Preview)!;

    private static VfxScenePreview CreateTestScene() => CreateTestScene(false, out _);

    private static VfxScenePreview CreateTestScene(bool withInteraction, out VfxEmitterInteractionComponent? interaction)
    {
        var preview = new Mock<IVfxPreview>();
        preview.SetupGet(value => value.Diagnostics).Returns(Array.Empty<VfxPreviewDiagnostic>());
        preview.SetupGet(value => value.CycleDuration).Returns(8);
        preview.SetupGet(value => value.EmitterCount).Returns(1);
        preview.SetupGet(value => value.TotalEmitterCount).Returns(1);
        var factory = new Mock<IVfxPreviewFactory>();
        factory.Setup(value => value.Create(It.IsAny<Microsoft.Xna.Framework.Graphics.GraphicsDevice>())).Returns(preview.Object);
        var component = new VfxPreviewComponent(factory.Object, Mock.Of<GameWorld.Core.Utility.IDeviceResolver>(), null!);
        var core = new GameWorld.Core.Components.View3DCoreComponentSet(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var mouse = new Mock<GameWorld.Core.Components.Input.IMouseComponent>(); mouse.SetupProperty(value => value.MouseOwner);
        interaction = withInteraction ? new VfxEmitterInteractionComponent(mouse.Object, Mock.Of<GameWorld.Core.Components.Input.IKeyboardComponent>(), null!,
            Mock.Of<GameWorld.Core.Utility.IDeviceResolver>(), null!, component) : null;
        return new VfxScenePreview(Mock.Of<IWpfGame>(), Mock.Of<GameWorld.Core.Components.IComponentInserter>(), core, component, interaction);
    }

    private static VfxFieldViewModel Field(VfxEditorViewModel editor, string name) =>
        editor.Layers[0].Fields.First(x => x.Model.Name == name);

    private static void WithEditor(Action<VfxEditorViewModel, Context> test) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var context = new Context();
            test(context.Editor, context);
        });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class Context
    {
        public Mock<IPackFileService> Files { get; } = new();
        public Mock<IFileSaveService> Save { get; } = new();
        public Mock<IStandardDialogs> Dialogs { get; } = new();
        public PackFile File { get; } = PackFile.CreateFromBytes("water_trail.xml", Encoding.UTF8.GetBytes(Sample));
        public byte[]? Saved { get; private set; }
        public VfxEditorViewModel Editor { get; }

        public Context(VfxPreviewDocumentStore? livePreviews = null, GameWeatherStore? weather = null, VfxScenePreview? scenePreview = null)
        {
            var container = new PackFileContainer("project");
            container.FileList.Add("vfx\\water_trail.xml", File);
            Files.Setup(x => x.GetAllPackfileContainers()).Returns([container]);
            Files.Setup(x => x.GetFileEntriesSnapshot(container)).Returns(() => container.FileList.ToArray());
            Files.Setup(x => x.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns("vfx\\water_trail.xml");
            Files.Setup(x => x.GetPackFileContainer(File)).Returns(container);
            Files.Setup(x => x.GetEditablePack()).Returns(container);
            Save.Setup(x => x.Save(It.IsAny<string>(), It.IsAny<byte[]>(), false)).Returns((string path, byte[] data, bool prompt) =>
            {
                Saved = data;
                File.DataSource = new MemorySource(data);
                return File;
            });
            var localization = new LocalizationManager();
            localization.LoadLanguage();
            Editor = new VfxEditorViewModel(Files.Object, Save.Object, Dialogs.Object, Mock.Of<IEditorCreator>(), localization, scenePreview: scenePreview, livePreviews: livePreviews, weather: weather);
            Editor.LoadFile(File);
        }
    }
}
