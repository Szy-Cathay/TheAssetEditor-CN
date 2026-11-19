using System.Text;
using System.Text.Json.Nodes;
using AssetEditor.ModAssistant;
using AssetEditor.Services;
using CommonControls.Editors.TextEditor;
using System.Windows;
using System.Windows.Threading;
using Moq;
using Editors.AnimationMeta;
using Editors.AnimationMeta.Presentation;
using Editors.CscEditor;
using Editors.CscEditor.Data;
using Editors.CscEditor.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.Events;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.Esf;
using Shared.ByteParsing;
using static Shared.Core.ModAssistant.AssistantJson;

namespace AssetEditorTests;

[TestClass, DoNotParallelize]
public class ModAssistantWorkspaceTests
{
    private sealed class CancellingSource(CancellationTokenSource cancel) : IDataSource
    {
        public long Size => 3;
        public CompressionFormat CompressionFormat => CompressionFormat.None;
        public byte[] ReadData() => [1, 2, 3];
        public byte[] PeekData(int size) => ReadData().Take(size).ToArray();
        public ByteChunk ReadDataAsChunk() => new(ReadData());
        public void CopyTo(Stream destination) { destination.Write(ReadData()); cancel.Cancel(); }
    }
    [TestMethod]
    public async Task Assistant_PackGenerationCancellationPreservesExistingOutputAndRemovesTemporaryWrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-assistant-cancel-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(root, "project"); Directory.CreateDirectory(directory);
        try
        {
            using var project = FolderProjectContainer.Create(directory, new FolderProjectSettings { Name = "隔离测试", GameVersion = GameTypeEnum.ThreeKingdoms });
            var files = new PackFileService(null!) { EnforceGameFilesMustBeLoaded = false, MessageBoxProvider = Mock.Of<ISimpleMessageBox>() };
            files.AddContainer(project, true);
            using var cancellation = new CancellationTokenSource();
            project.FileList["cancel.bin"] = new PackFile("cancel.bin", new CancellingSource(cancellation));
            var output = Path.Combine(root, "output.pack"); byte[] original = [7, 8, 9]; File.WriteAllBytes(output, original);
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => files.GenerateFolderProjectPackAsync(project, output,
                GameInformationDatabase.GetGameById(GameTypeEnum.ThreeKingdoms), cancellation.Token));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(output));
            Assert.AreEqual(0, Directory.GetFiles(root, "*.tmp").Length);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Assistant_RegisteredToolsDiscoverTheirActualScopedAdaptersWithoutOpeningWindows()
    {
        EditorTabDirtyStateTests.WithServices(services =>
        {
            var manager = services.GetRequiredService<IEditorManager>();
            foreach (var kind in new[] { EditorEnums.Kitbash_Editor, EditorEnums.AnimationRetarget_Editor, EditorEnums.MountTool_Editor,
                EditorEnums.VisualSkeletonEditor, EditorEnums.SuperView_Editor, EditorEnums.Audio_Editor, EditorEnums.AudioExplorer_Editor,
                EditorEnums.CampaginAnimation_Editor, EditorEnums.AnimationBatchExporter_Editor }) manager.Create(kind);
            using var scope = services.CreateScope();
            var workspace = scope.ServiceProvider.GetRequiredService<AssistantWorkspace>();
            workspace.BeginTask();
            var operations = workspace.GetOperations().ToArray();
            foreach (var family in new[] { "kitbash.", "retarget.", "mount.", "superview.", "audio_project.", "audio.", "campaign.", "animation_batch." })
                Assert.IsTrue(operations.Any(o => o.Name.StartsWith(family, StringComparison.Ordinal)), family);
            Assert.IsFalse(operations.Any(o => o.Name.StartsWith("skeleton.")), "Unloaded skeleton must not advertise editable bones.");
            foreach (var target in operations.Select(o => o.TargetId).Distinct())
                Assert.IsNotNull(workspace.InspectAsync(target, default).GetAwaiter().GetResult()["revision"]);
        });
    }

    private static byte[] MetaBytes(MetaDataFileParser parser) => parser.GenerateBytes(2, new ParsedMetadataFile
    {
        Version = 2, Attributes = [new Effect_v1 { Name = "EFFECT", Version = 1, VfxName = "diagnostic", BoolUnknown = "true", NodeIndex = -1, EffectStartTime = .25f, EffectEndTime = 1.75f }],
    });

    [TestMethod]
    public void Assistant_MetaRealParserGroupUndoRedoAndUnknownFieldsPreserved()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            new LocalizationManager().LoadLanguage();
            var database = new MetaDataDatabase(); var parser = new MetaDataFileParser(database);
            var vm = new MetaDataEditorViewModel(null!, parser, Mock.Of<IEventHub>());
            vm.LoadFile(PackFile.CreateFromBytes("diagnostic.anm.meta", MetaBytes(parser)));
            try
            {
                var adapter = new MetaAssistantAdapter(parser, database, new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false });
                var ids = new AssistantIds();
                var id = ids.Get(vm.ParsedFile.Attributes.Single());
                ModAssistantDataTests.Run(adapter, vm, ids, "meta.fields.set", new() { ["changes"] = new JsonArray(
                    new JsonObject { ["event"] = id, ["field"] = "EffectStartTime", ["text"] = "0.5" },
                    new JsonObject { ["event"] = id, ["field"] = "EffectEndTime", ["text"] = "2.5" }) });
                Assert.AreEqual(.5f, ((Effect_v1)vm.ParsedFile.Attributes[0]).EffectStartTime);
                Assert.IsTrue(vm.HasUnsavedChanges);
                ModAssistantDataTests.Run(adapter, vm, ids, "meta.undo");
                Assert.AreEqual(.25f, ((Effect_v1)vm.ParsedFile.Attributes[0]).EffectStartTime);
                Assert.IsFalse(vm.HasUnsavedChanges);
                ModAssistantDataTests.Run(adapter, vm, ids, "meta.redo");
                var readback = (Effect_v1)parser.ParseFile(parser.GenerateBytes(2, vm.ParsedFile)).Attributes[0];
                Assert.AreEqual(2.5f, readback.EffectEndTime); Assert.AreEqual("diagnostic", readback.VfxName);
                Assert.AreEqual(-1, readback.NodeIndex); Assert.AreEqual("true", readback.BoolUnknown);
            }
            finally { vm.Close(); }
        });
    }

    [TestMethod]
    public void Assistant_ActualWorkspaceCrossEditorSaveGeneratePackAndChangedProjectGuard()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-assistant-workspace-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            EditorTabDirtyStateTests.WithServices(services =>
            {
                var files = services.GetRequiredService<IPackFileService>();
                Directory.CreateDirectory(Path.Combine(root, "project"));
                using var project = FolderProjectContainer.Create(Path.Combine(root, "project"), new FolderProjectSettings
                { Name = "隔离测试", GameVersion = GameTypeEnum.Warhammer3, OutputPackPath = Path.Combine(root, "output.pack") });
                ((PackFileService)files).AddContainer(project, true);
                var parser = services.GetRequiredService<MetaDataFileParser>();
                files.ApplyFileWrites(project, [new PackFileWrite("sample.xml", Encoding.ASCII.GetBytes("<root value=\"old\"/>")),
                    new PackFileWrite("sample.anm.meta", MetaBytes(parser))], false);
                using var scope = services.CreateScope();
                var workspace = scope.ServiceProvider.GetRequiredService<AssistantWorkspace>();
                workspace.BeginTask();
                JsonNode Execute(string name, string target, JsonObject? args = null)
                {
                    var inspect = workspace.InspectAsync(target, default).GetAwaiter().GetResult();
                    var pending = workspace.ExecuteAsync(Guid.NewGuid().ToString("N"), name, target, inspect.AsObject().String("revision"), args ?? new(), default);
                    if (!pending.IsCompleted)
                    {
                        var frame = new DispatcherFrame(); pending.GetAwaiter().OnCompleted(() => frame.Continue = false); Dispatcher.PushFrame(frame);
                    }
                    var result = pending.GetAwaiter().GetResult();
                    Assert.AreEqual("completed", result["status"]?.GetValue<string>(), name + ": " + result);
                    return result["result"]!;
                }
                string Resource(string query) => Execute("resources.search", "workspace", new() { ["query"] = query })["results"]![0]!["target"]!.GetValue<string>();
                var textId = Execute("resource.open", Resource("sample.xml"), new() { ["editor"] = "XML_Editor" })["id"]!.GetValue<string>();
                var textView = new TextEditorView { DataContext = services.GetRequiredService<IEditorManager>().GetAllEditors().Last() };
                textView.Measure(new Size(640, 360)); textView.Arrange(new Rect(0, 0, 640, 360));
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
                Execute("text.replace", textId, new() { ["old_text"] = "old", ["new_text"] = "new" });
                Assert.IsTrue(Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(project.ProjectRoot, "sample.xml"))).Contains("old"));
                var metaId = Execute("resource.open", Resource("sample.anm.meta"), new() { ["editor"] = "Meta_Editor" })["id"]!.GetValue<string>();
                var eventId = Execute("meta.events", metaId)["events"]![0]!["id"]!.GetValue<string>();
                Execute("meta.fields.set", metaId, new() { ["changes"] = new JsonArray(new JsonObject { ["event"] = eventId, ["field"] = "EffectEndTime", ["text"] = "3" }) });
                Execute("editor.save", textId); Execute("editor.save", metaId);
                var result = Execute("project.generate_pack", "workspace");
                Assert.IsTrue(File.Exists(result["output"]!.GetValue<string>()));
                var pack = services.GetRequiredService<IPackFileContainerLoader>().Load(Path.Combine(root, "output.pack"));
                using (var packStream = File.OpenRead(Path.Combine(root, "output.pack")))
                {
                    Assert.IsTrue(Encoding.ASCII.GetString(((PackedFileSource)pack!.FileList["sample.xml"].DataSource).ReadData(packStream)).Contains("new"));
                    Assert.AreEqual(3f, ((Effect_v1)parser.ParseFile(((PackedFileSource)pack.FileList["sample.anm.meta"].DataSource).ReadData(packStream)).Attributes[0]).EffectEndTime);
                }
                var before = workspace.InspectAsync("workspace", default).GetAwaiter().GetResult();
                Directory.CreateDirectory(Path.Combine(root, "other"));
                using var other = FolderProjectContainer.Create(Path.Combine(root, "other"), new FolderProjectSettings { Name = "其他工程" });
                ((PackFileService)files).AddContainer(other, true);
                var rejected = workspace.ExecuteAsync("stale-project", "project.create_folder", "workspace", before.AsObject().String("revision"), new() { ["path"] = "must-not-exist" }, default).GetAwaiter().GetResult();
                Assert.AreEqual("rejected", rejected["status"]!.GetValue<string>());
                Assert.IsFalse(Directory.Exists(Path.Combine(other.ProjectRoot, "must-not-exist")));
                textView.DataContext = null;
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Assistant_CscActualPropertiesRoundTripAndGroupedUndo()
    {
        EditorTabDirtyStateTests.WithServices(services =>
        {
            using var scope = services.CreateScope();
            var vm = scope.ServiceProvider.GetRequiredService<CscEditorViewModel>();
            vm.LoadFile(PackFile.CreateFromBytes("diagnostic.csc", EmptyScene()));
            try
            {
                var adapter = new CscAssistantAdapter(); var ids = new AssistantIds();
                ModAssistantDataTests.Run(adapter, vm, ids, "csc.add_locator");
                var id = ids.Get(vm.SceneData.Elements.Single());
                ModAssistantDataTests.Run(adapter, vm, ids, "csc.properties.set", new() { ["node"] = id, ["changes"] = new JsonArray(
                    new JsonObject { ["field"] = "begin", ["value_json"] = "2" },
                    new JsonObject { ["field"] = "end", ["value_json"] = "8" }) });
                Assert.AreEqual(2f, vm.SceneData.Elements.Single().Begin);
                ModAssistantDataTests.Run(adapter, vm, ids, "csc.undo"); Assert.AreNotEqual(2f, vm.SceneData.Elements.Single().Begin);
                ModAssistantDataTests.Run(adapter, vm, ids, "csc.redo");
                var readback = CscScene.Load(CscSceneWriter.Write(vm.SceneData));
                Assert.AreEqual(2f, readback.Elements.Single().Begin); Assert.AreEqual(8f, readback.Elements.Single().End);
                Assert.IsTrue(vm.HasUnsavedChanges);
            }
            finally { vm.Close(); }
        });
    }

    private static byte[] EmptyScene()
    {
        var fields = new List<EsfNode>
        {
            EsfNode.Leaf(EsfNodeKind.F32, 20f), EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)), EsfNode.Leaf(EsfNodeKind.F32, 0f),
            EsfNode.Leaf(EsfNodeKind.Ascii, ""), EsfNode.Leaf(EsfNodeKind.F32, 0f), EsfNode.Leaf(EsfNodeKind.Ascii, ""), EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
            EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)), EsfNode.Leaf(EsfNodeKind.Coord3d, new Coord3d(0, 0, 0)), EsfNode.Leaf(EsfNodeKind.F32, 1f),
            EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true), EsfNode.Leaf(EsfNodeKind.U32, 0u, optimized: true),
        };
        using var stream = new MemoryStream();
        EsfWriter.Write(new EsfDocument { Signature = EsfSignature.Caab, CreationDate = 123456, Root = EsfNode.NewRecord("ROOT", 5, EsfRecordFlags.IsRecordNode, [fields]) }, stream);
        return stream.ToArray();
    }
}
