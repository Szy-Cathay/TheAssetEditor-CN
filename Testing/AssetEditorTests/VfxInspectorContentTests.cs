using System.Reflection;
using System.Text;
using Editors.VfxEditor;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Vfx;

namespace AssetEditorTests;

[TestClass]
public class VfxInspectorContentTests
{
    [TestMethod]
    public void CategoriesContainOnlyFieldsPresentAtTheSelectedPropertyQuality() => WithEditor((editor, _) =>
    {
        editor.SelectedLayer = Layer(editor, "sprite");
        editor.Quality = "0";
        CollectionAssert.AreEqual(new[] { "Common", "Size", "Emission", "Advanced", "All" }, CategoryKeys(editor));
        Assert.IsFalse(editor.Categories.Any(category => category.Key is "Appearance" or "Lifetime" or "Motion" or "Resources"));

        editor.Category = "Size";
        editor.Quality = "1";
        CollectionAssert.AreEqual(new[] { "Common", "Emission", "Resources", "All" }, CategoryKeys(editor));
        Assert.AreEqual("Common", editor.Category, "A missing category must fall back to one the selected quality actually contains.");
        Assert.IsFalse(editor.VisibleFields.Any(field => field.Model.Name == "ParticleScale"));

        editor.Quality = "All";
        CollectionAssert.AreEqual(new[] { "Common", "Size", "Emission", "Resources", "Advanced", "All" }, CategoryKeys(editor));
    });

    [TestMethod]
    public void SwitchingToUnknownOnlyOrEmptyContentDoesNotInventCommonProperties() => WithEditor((editor, _) =>
    {
        editor.SelectedLayer = Layer(editor, "sprite");
        editor.Category = "Size";
        editor.SelectedLayer = Layer(editor, "unknown_only");
        CollectionAssert.AreEqual(new[] { "Advanced", "All" }, CategoryKeys(editor));
        Assert.AreEqual("All", editor.Category);
        Assert.AreEqual("future_payload", editor.VisibleFields.Single().Model.Name);

        editor.Category = "Advanced";
        editor.Search = "future_payload";
        Assert.AreEqual("future_payload", editor.VisibleFields.Single().Model.Name);
        Assert.IsFalse(editor.VisibleFields.Single().HasGuidance);
        editor.Search = "";

        editor.SelectedLayer = Layer(editor, "empty");
        CollectionAssert.AreEqual(new[] { "All" }, CategoryKeys(editor));
        Assert.AreEqual("All", editor.Category);
        Assert.IsTrue(editor.HasNoFields);
    });

    [TestMethod]
    public void ConfigurationEntriesHaveTheirOwnGroupWithoutRemovingRealOrUnknownContent() => WithEditor((editor, context) =>
    {
        var effects = editor.Localization.Get("Vfx.Layer.Group.Effects");
        var configuration = editor.Localization.Get("Vfx.Layer.Group.Configuration");
        Assert.AreNotEqual(effects, configuration);
        foreach (var layer in editor.Layers)
            Assert.AreEqual(layer.CanPreview ? effects : configuration, layer.GroupLabel);
        Assert.AreEqual(4, editor.Layers.Count(layer => layer.CanPreview));
        Assert.AreEqual(2, editor.Layers.Count(layer => !layer.CanPreview));

        var settings = editor.Layers.Single(layer => layer.Model.Kind == "Settings");
        Assert.AreEqual(editor.Localization.Get("Vfx.Layer.Settings"), settings.Label);
        var adjustment = editor.Layers.Single(layer => layer.Model.Kind == "Override");
        Assert.AreEqual(editor.Localization.GetFormat("Vfx.Layer.OverrideLabel", "reference_a", "child_a"), adjustment.Label);
        Assert.IsTrue(adjustment.Fields.Any(field => field.Model.Name == "enable"));
        Assert.IsTrue(Layer(editor, "unknown_only").Fields.Any(field => field.Model.Name == "future_payload"));
        AssertUnchanged(editor, context);
    });

    [TestMethod]
    public void DisabledMarkerFollowsPreviewQualityAndKeepsTheLayerEditable() => WithEditor((editor, context) =>
    {
        var layer = Layer(editor, "sprite");
        var marker = editor.Localization.Get("Vfx.Layer.DisabledAtQuality");
        var subtitleChanges = 0;
        layer.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxLayerViewModel.Subtitle)) subtitleChanges++; };
        Assert.IsFalse(layer.Subtitle.Contains(marker, StringComparison.Ordinal));
        editor.PreviewQuality = "1";
        Assert.IsTrue(layer.Subtitle.Contains(marker, StringComparison.Ordinal));
        Assert.IsTrue(subtitleChanges > 0, "Bindings must see a preview-quality status change without reopening the document.");
        Assert.IsTrue(layer.CanPreview);
        Assert.IsTrue(editor.Layers.Contains(layer), "An authored disabled layer must remain available for re-enabling.");
        editor.SelectedLayer = layer;
        editor.Quality = "0";
        Assert.IsTrue(layer.Subtitle.Contains(marker, StringComparison.Ordinal), "The property filter must not replace the actual preview quality.");
        editor.PreviewQuality = "2";
        Assert.IsFalse(layer.Subtitle.Contains(marker, StringComparison.Ordinal), "Missing preview-quality overrides inherit the authored quality-zero setting.");
        AssertUnchanged(editor, context);
    });

    [TestMethod]
    public void EditingAndUndoingEnableRefreshesTheStatusWithoutDiscardingDisabledConfiguration() => WithEditor((editor, context) =>
    {
        editor.PreviewQuality = "1";
        var layer = Layer(editor, "sprite");
        var marker = editor.Localization.Get("Vfx.Layer.DisabledAtQuality");
        var field = layer.Fields.Single(item => item.Model.Name == "Enable" && item.Model.Quality == "1");
        var subtitleChanges = 0;
        layer.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(VfxLayerViewModel.Subtitle)) subtitleChanges++; };
        Assert.IsTrue(layer.Subtitle.Contains(marker, StringComparison.Ordinal));
        field.Values.Single().Boolean = true;
        Assert.IsFalse(layer.Subtitle.Contains(marker, StringComparison.Ordinal));
        Assert.IsTrue(subtitleChanges > 0);
        Assert.IsTrue(editor.HasUnsavedChanges);
        Assert.IsTrue(editor.UndoCommand.CanExecute(null));
        editor.UndoCommand.Execute(null);
        Assert.IsTrue(Layer(editor, "sprite").Subtitle.Contains(marker, StringComparison.Ordinal));
        Assert.IsFalse(editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, Document(editor).Write());
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        context.Save.VerifyNoOtherCalls();
    });

    [TestMethod]
    public void UndoAndRedoKeepTheCategoryThatExistsAtTheRetainedPropertyQuality() => WithEditor((editor, context) =>
    {
        editor.SelectedLayer = Layer(editor, "sprite");
        editor.Quality = "1";
        editor.Category = "Resources";
        editor.SelectedField = editor.VisibleFields.Single();
        editor.SelectedField.Values.Single().Text = "changed-diagnostic.dds";
        Assert.IsTrue(editor.HasUnsavedChanges);
        editor.UndoCommand.Execute(null);
        Assert.AreEqual("1", editor.Quality);
        Assert.AreEqual("Resources", editor.Category, "The intermediate quality-zero rebuild must not discard a category available at the final quality.");
        Assert.AreEqual("diffuse", editor.SelectedField!.Model.Name);
        Assert.AreEqual("diagnostic.dds", editor.SelectedField.Values.Single().Text);
        Assert.IsFalse(editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, Document(editor).Write());

        editor.RedoCommand.Execute(null);
        Assert.AreEqual("1", editor.Quality);
        Assert.AreEqual("Resources", editor.Category);
        Assert.AreEqual("changed-diagnostic.dds", editor.SelectedField!.Values.Single().Text);
        Assert.IsTrue(editor.HasUnsavedChanges);
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        context.Save.VerifyNoOtherCalls();
    });

    [TestMethod]
    public void InspectingCategoriesGroupsAndDisabledStatesPreservesBytesAndHistory() => WithEditor((editor, context) =>
    {
        foreach (var layer in editor.Layers)
        {
            editor.SelectedLayer = layer;
            foreach (var quality in editor.Qualities.Select(choice => choice.Key).ToArray())
            {
                editor.Quality = quality;
                foreach (var category in editor.Categories.Select(choice => choice.Key).ToArray())
                    editor.Category = category;
            }
        }
        foreach (var quality in editor.PreviewQualities.Select(choice => choice.Key)) editor.PreviewQuality = quality;
        AssertUnchanged(editor, context);
    });

    private static string[] CategoryKeys(VfxEditorViewModel editor) => editor.Categories.Select(choice => choice.Key).ToArray();
    private static VfxLayerViewModel Layer(VfxEditorViewModel editor, string name) => editor.Layers.Single(layer => layer.Model.Name == name);
    private static VfxDocument Document(VfxEditorViewModel editor) => (VfxDocument)typeof(VfxEditorViewModel)
        .GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor)!;

    private static void AssertUnchanged(VfxEditorViewModel editor, Context context)
    {
        Assert.IsFalse(editor.HasUnsavedChanges);
        Assert.IsFalse(editor.UndoCommand.CanExecute(null));
        Assert.IsFalse(editor.RedoCommand.CanExecute(null));
        CollectionAssert.AreEqual(context.Original, Document(editor).Write());
        CollectionAssert.AreEqual(context.Original, context.File.DataSource.ReadData());
        context.Save.VerifyNoOtherCalls();
    }

    private static void WithEditor(Action<VfxEditorViewModel, Context> test) =>
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var context = new Context();
            try { test(context.Editor, context); }
            finally { context.Editor.Close(); }
        });

    private sealed class Context
    {
        // DIAG: source-structure controls only; these do not represent a visual or native-parity sample.
        private const string Source = """
            <shader_library file="fx/particle_vfx_library.hlsl"/>
            <vfx revision="2" construction_type="1" camera_following="false">
              <emitters>
                <emitter id="sprite" type="continuous_mesh">
                  <property name="Enable" quality="0" value="true"/>
                  <property name="Enable" quality="1" value="false"/>
                  <property name="ParticleScale" quality="0" value="float3(1,1,1)"/>
                  <future_payload quality="0" keep="unchanged"/>
                </emitter>
                <emitter id="unknown_only" type="continuous_mesh"><future_payload quality="0" keep="unknown"/></emitter>
                <emitter id="empty" type="continuous_mesh"/>
              </emitters>
              <behaviour_chains><behaviour_chain id="sprite"><quality value="1"><modifier fragment="ps_texture"><property name="diffuse" path="diagnostic.dds"/></modifier></quality></behaviour_chain></behaviour_chains>
              <effect_render_stacks><effect_render_stack id="sprite"/></effect_render_stacks>
            </vfx>
            <vfx_references><vfx inst_name="reference_a" vfx_ref="diagnostic_reference"><emitter name="child_a" enable="false"/></vfx></vfx_references>
            <editor_data><effect_components><component id="sprite"/></effect_components></editor_data>
            """;

        public byte[] Original { get; } = Encoding.UTF8.GetBytes(Source);
        public PackFile File { get; }
        public Mock<IFileSaveService> Save { get; } = new();
        public VfxEditorViewModel Editor { get; }

        public Context()
        {
            File = PackFile.CreateFromBytes("inspector-diagnostic.xml", Original);
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns("vfx\\inspector-diagnostic.xml");
            var localization = new LocalizationManager();
            localization.LoadLanguage();
            Editor = new VfxEditorViewModel(files.Object, Save.Object, Mock.Of<IStandardDialogs>(), Mock.Of<IEditorCreator>(), localization);
            Editor.LoadFile(File);
        }
    }
}
