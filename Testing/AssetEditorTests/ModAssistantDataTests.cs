using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using AssetEditor.ModAssistant;
using CommonControls.Editors.TextEditor;
using Editor.VisualSkeletonEditor;
using Editors.AnimationFragmentEditor;
using Editors.VfxEditor;
using Editors.Audio;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Shared.Core.DependencyInjection;
using Shared.Core.Events;
using Editors.KitbasherEditor;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.SceneNodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Moq;
using Shared.ByteParsing;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.Vfx;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel;
using Shared.Ui.Editors.TextEditor;
using static Shared.Core.ModAssistant.AssistantJson;

namespace AssetEditorTests;

[TestClass, DoNotParallelize]
public class ModAssistantDataTests
{
    [TestMethod]
    public async Task Assistant_AudioWriteGuardFlowsAcrossAwaitAndDoesNotLeakIntoLaterOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-assistant-audio-guard-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "写入保护测试" });
            var files = new PackFileService(null!) { EnforceGameFilesMustBeLoaded = false }; files.AddContainer(project, true);
            var output = new AudioPackOutputService(files, Mock.Of<IFileSaveService>(), Mock.Of<IStandardDialogs>());
            var valid = true; var writes = 0;
            using (output.BeginGuardedWrites(() => { if (!valid) throw new InvalidOperationException("stale"); }, batch => writes += batch.Count))
            {
                await Task.Yield(); valid = false;
                Assert.ThrowsException<InvalidOperationException>(() => output.SaveBatch([new("a.bin", "a.bin", [1])]));
                Assert.IsFalse(File.Exists(Path.Combine(root, "a.bin")));
                valid = true; Assert.IsTrue(output.SaveBatch([new("a.bin", "a.bin", [1])])); Assert.AreEqual(1, writes);
            }
            valid = false; Assert.IsTrue(output.SaveBatch([new("b.bin", "b.bin", [2])])); Assert.AreEqual(1, writes);
            CollectionAssert.AreEqual(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(root, "b.bin")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Assistant_VideoExportPreservesFramesRejectsOverwriteAndHonorsCancellation()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-assistant-video-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            var files = new PackFileService(null!) { EnforceGameFilesMustBeLoaded = false };
            var owner = files.CreateNewPackFileContainer("source", PackFileVersion.PFH5, PackFileCAType.MOD);
            var file = PackFile.CreateFromBytes("sample.ca_vp8", CAVp8ContainerTests.CreateCaVp8WithGap());
            owner.FileList.Add(file.Name, file);
            var adapter = new VideoAssistantAdapter(files);
            var operation = adapter.GetOperations(owner, file, new AssistantIds()).Single(o => o.Name == "video.export");
            var output = Path.Combine(root, "sample.ivf");
            var args = new JsonObject { ["format"] = "ivf", ["output"] = output }; Validate(args, operation.Parameters);
            var result = await operation.Execute(args, default);
            var bytes = File.ReadAllBytes(output);
            Assert.AreEqual("DKIF", Encoding.ASCII.GetString(bytes, 0, 4)); Assert.AreEqual(2u, BitConverter.ToUInt32(bytes, 24));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, bytes[44..47]); CollectionAssert.AreEqual(new byte[] { 4, 5 }, bytes[59..61]);
            Assert.IsFalse(result["decoded_playback_verified"]!.GetValue<bool>());
            await Assert.ThrowsExceptionAsync<IOException>(() => operation.Execute(args, default));
            CollectionAssert.AreEqual(bytes, File.ReadAllBytes(output));
            args["output"] = Path.Combine(root, "cancelled.ivf");
            await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => operation.Execute(args, new CancellationToken(true)));
            Assert.IsFalse(File.Exists(args.String("output")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Assistant_AudioStateChangesUndoRedoAndSaveThroughNativeServices()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-assistant-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            EditorTabDirtyStateTests.WithServices(services =>
            {
                var files = services.GetRequiredService<IPackFileService>();
                using var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "音频测试", GameVersion = Shared.Core.Settings.GameTypeEnum.Warhammer3 });
                files.AddContainer(project); files.SetEditablePack(project);
                var editor = services.GetRequiredService<IEditorManager>().Create(EditorEnums.Audio_Editor);
                var scopes = services.GetRequiredService<IScopeRepository>();
                var state = scopes.GetRequiredService<IAudioEditorStateService>(editor);
                state.AudioProject = AudioProjectFile.CreateNew(Wh3LanguageInformation.GetLanguageAsString(Wh3Language.EnglishUK), "sample");
                state.AudioProjectFileName = "sample.aproj"; state.AudioProjectFilePath = "audio/sample.aproj";
                scopes.GetRequiredService<IEventHub>(editor).Publish(new AudioProjectLoadedEvent(false));
                var adapter = services.GetServices<IAssistantEditorAdapter>().OfType<AudioProjectAssistantAdapter>().Single();
                var ids = new AssistantIds(); var group = state.AudioProject.StateGroups.First().Name;
                Run(adapter, editor, ids, "audio_project.state", new() { ["group"] = group, ["name"] = "assistant_test", ["action"] = "add" });
                Assert.IsTrue(state.AudioProject.GetStateGroup(group).States.Any(s => s.Name == "assistant_test"));
                Run(adapter, editor, ids, "audio_project.undo"); Assert.IsFalse(state.AudioProject.GetStateGroup(group).States.Any(s => s.Name == "assistant_test"));
                Run(adapter, editor, ids, "audio_project.redo"); Assert.IsTrue(((ISaveableEditor)editor).HasUnsavedChanges);
                Assert.IsTrue(((ISaveableEditor)editor).Save());
                var saved = files.FindFile(state.AudioProjectFilePath, project)!;
                var parsed = scopes.GetRequiredService<IAudioProjectFileService>(editor).DeserialiseAudioProject(saved);
                Assert.IsTrue(parsed.GetStateGroup(group).States.Any(s => s.Name == "assistant_test"));
                Assert.IsTrue(File.Exists(Path.Combine(root, "audio", "sample.aproj")));
                var other = AudioProjectFile.CreateNew(state.AudioProject.Language, "other");
                other.GetStateGroup(group).States.Add(new State("second_source"));
                files.ApplyFileWrites(project, [new PackFileWrite("audio/other.aproj", System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(other))], false);
                Run(adapter, editor, ids, "audio_project.merge", new() { ["base_path"] = "audio/sample.aproj", ["other_path"] = "audio/other.aproj",
                    ["name"] = "combined", ["output_folder"] = "merged" });
                var merged = scopes.GetRequiredService<IAudioProjectFileService>(editor).DeserialiseAudioProject(files.FindFile("merged/combined.aproj", project)!);
                Assert.IsTrue(merged.GetStateGroup(group).States.Any(s => s.Name == "assistant_test"));
                Assert.IsTrue(merged.GetStateGroup(group).States.Any(s => s.Name == "second_source"));
                files.UnloadPackContainer(project);
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [TestMethod]
    public void Assistant_KitbashTransformAndBatchMaterialsUseNativeHistoryAndSerialization()
    {
        EditorTabDirtyStateTests.WithServices(services =>
        {
            var editor = (KitbasherViewModel)services.GetRequiredService<IEditorManager>().Create(EditorEnums.Kitbash_Editor);
            var adapter = services.GetServices<IAssistantEditorAdapter>().OfType<KitbashAssistantAdapter>().Single();
            var ids = new AssistantIds();
            Rmv2MeshNode Create(string name)
            {
                var geometry = new MeshObject(Mock.Of<IGraphicsCardGeometry>(), "")
                {
                    VertexArray = [new() { Position = new Vector4(-1, -1, 0, 1) }, new() { Position = new Vector4(1, -1, 0, 1) }, new() { Position = new Vector4(0, 1, 0, 1) }],
                    IndexArray = [0, 1, 2],
                };
                geometry.ChangeVertexType(UiVertexFormat.Static); geometry.BuildBoundingBox();
                return editor.SceneExplorer.SceneManager.RootNode.AddObject(new Rmv2MeshNode(geometry,
                    new WeightedMaterial { ModelName = name }, new GameWorld.Core.Rendering.Materials.Shaders.SpecGloss.DefaultMaterial(null!), null!)
                    { CommonHeader = new RmvCommonHeader() });
            }
            var first = Create("first"); var second = Create("second");
            Run(adapter, editor, ids, "kitbash.select", new() { ["objects"] = new JsonArray(ids.Get(first), ids.Get(second)) });
            Run(adapter, editor, ids, "kitbash.transform", new() { ["mode"] = "Scale", ["x"] = .8, ["y"] = .8, ["z"] = .8, ["space"] = "world" });
            Assert.AreEqual(.8f, first.Geometry.BoundingBox.Max.X, .0001f); Assert.AreEqual(.8f, second.Geometry.BoundingBox.Max.X, .0001f);
            Assert.IsTrue(editor.HasUnsavedChanges);
            Run(adapter, editor, ids, "kitbash.undo"); Assert.AreEqual(1f, first.Geometry.BoundingBox.Max.X); Assert.AreEqual(1f, second.Geometry.BoundingBox.Max.X);
            Run(adapter, editor, ids, "kitbash.redo"); Assert.AreEqual(.8f, first.Geometry.BoundingBox.Max.X, .0001f);
            JsonObject Change(string field, string value) => new() { ["field"] = field, ["value_json"] = value };
            JsonObject Batch(JsonArray changes) => new() { ["objects"] = new JsonArray(ids.Get(first), ids.Get(second)), ["changes"] = changes };
            Assert.ThrowsException<ArgumentException>(() => Run(adapter, editor, ids, "kitbash.materials.set",
                Batch(new JsonArray(Change("use_alpha", "true"), Change("nonexistent", "1")))));
            Assert.IsFalse(first.Material.GetCapability<SpecGlossCapability>().UseAlpha);
            Assert.IsFalse(second.Material.GetCapability<SpecGlossCapability>().UseAlpha);
            Run(adapter, editor, ids, "kitbash.materials.set", Batch(new JsonArray(Change("use_alpha", "true"))));
            foreach (var mesh in new[] { first, second })
            {
                var material = new WeightedMaterial();
                mesh.Material.GetCapability<SpecGlossCapability>().SerializeToRmvMaterial(material);
                var readback = new SpecGlossCapability(); readback.Initialize(null, material);
                Assert.IsTrue(readback.UseAlpha);
            }
            Run(adapter, editor, ids, "kitbash.undo");
            Assert.IsFalse(first.Material.GetCapability<SpecGlossCapability>().UseAlpha);
            Assert.IsFalse(second.Material.GetCapability<SpecGlossCapability>().UseAlpha);
            Run(adapter, editor, ids, "kitbash.redo"); Assert.IsTrue(first.Material.GetCapability<SpecGlossCapability>().UseAlpha);
        });
    }

    internal static JsonNode Run(IAssistantEditorAdapter adapter, IEditorInterface editor, AssistantIds ids, string name, JsonObject? args = null)
    {
        var operation = adapter.GetOperations(editor, ids).Single(o => o.Name == name);
        Assert.IsNull(operation.Unavailable?.Invoke(), name);
        Validate(args ?? new(), operation.Parameters);
        return operation.Execute(args ?? new(), CancellationToken.None).GetAwaiter().GetResult();
    }
    [TestMethod]
    public void Assistant_VfxRangeEditsReadBackAndUndoAsOneWithoutChangingUnknownXml()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var context = new VfxEditorTests.Context(); var editor = context.Editor;
            try
            {
                var adapter = new VfxAssistantAdapter(); var ids = new AssistantIds();
                string Hash() => adapter.GetOperations(editor, ids).First().ReadState()["sha256"]!.GetValue<string>();
                var before = Hash();
                var layer = editor.Layers.Single(l => l.Model.Kind == "Emitter");
                var fields = Run(adapter, editor, ids, "vfx.fields", new() { ["layer"] = ids.Get(layer.Model.Element), ["query"] = "LifeTime" });
                var values = fields["fields"]![0]!["values"]!.AsArray();
                Assert.AreEqual(2, values.Count);
                Run(adapter, editor, ids, "vfx.values.set", new() { ["edits"] = new JsonArray(
                    new JsonObject { ["value_id"] = values[0]!["id"]!.GetValue<string>(), ["text"] = "0.5" },
                    new JsonObject { ["value_id"] = values[1]!["id"]!.GetValue<string>(), ["text"] = "1" }) });
                Assert.IsTrue(editor.HasUnsavedChanges);
                var edited = Hash();
                Run(adapter, editor, ids, "vfx.undo"); Assert.AreEqual(before, Hash());
                Assert.IsFalse(editor.HasUnsavedChanges);
                Run(adapter, editor, ids, "vfx.redo"); Assert.AreEqual(edited, Hash());
                Assert.IsTrue(editor.Save());
                var native = VfxDocument.Read(context.Saved!);
                Assert.IsTrue(Encoding.UTF8.GetString(native.Write()).Contains("<![CDATA[a&b]]>"));
                Assert.IsTrue(Encoding.UTF8.GetString(context.Saved!).Contains("min=\"0.5\" max=\"1\""));
            }
            finally { editor.Close(); }
        });
    }
    [TestMethod]
    public void Assistant_VfxBatchRejectsInvalidSecondFieldBeforeAnyMutation()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var context = new VfxEditorTests.Context(); var editor = context.Editor;
            try
            {
                var ids = new AssistantIds(); var adapter = new VfxAssistantAdapter();
                string Hash() => adapter.GetOperations(editor, ids).First().ReadState()["sha256"]!.GetValue<string>();
                var before = Hash();
                var values = editor.Layers.SelectMany(l => l.Fields).SelectMany(f => f.Values).Where(v => v.IsNumber).Take(2).ToArray();
                Assert.ThrowsException<ArgumentException>(() => Run(adapter, editor, ids, "vfx.values.set", new() { ["edits"] = new JsonArray(
                    new JsonObject { ["value_id"] = ids.Get(values[0]), ["text"] = "0.5" },
                    new JsonObject { ["value_id"] = ids.Get(values[1]), ["text"] = "NaN" }) }));
                Assert.AreEqual(before, Hash()); Assert.IsFalse(editor.HasUnsavedChanges);
            }
            finally { editor.Close(); }
        });
    }
    [TestMethod]
    public void Assistant_SkeletonEditsUndoRedoAndNativeSaveReadback()
    {
        SkeletonDirtyStateTests.WithSkeleton((editor, _, _, save) =>
        {
            var ids = new AssistantIds(); var adapter = new SkeletonAssistantAdapter();
            Run(adapter, editor, ids, "skeleton.edit", new() { ["bone_name"] = "root", ["action"] = "transform", ["translation"] = new JsonObject { ["x"] = 2, ["y"] = 3, ["z"] = 4 } });
            Assert.IsTrue(editor.HasUnsavedChanges);
            var bone = Run(adapter, editor, ids, "skeleton.bones")["bones"]![0]!;
            Assert.AreEqual(2f, bone["translation"]![0]!.GetValue<float>());
            Run(adapter, editor, ids, "skeleton.undo"); Assert.IsFalse(editor.HasUnsavedChanges);
            Run(adapter, editor, ids, "skeleton.redo"); Assert.IsTrue(editor.HasUnsavedChanges);
            byte[]? bytes = null;
            save.Setup(s => s.Save(It.Is<string>(p => p.EndsWith(".anim")), It.IsAny<byte[]>(), false))
                .Returns<string, byte[], bool>((path, data, _) => { bytes = data; return PackFile.CreateFromBytes(path, data); });
            Assert.IsTrue(editor.Save()); Assert.IsNotNull(bytes);
            var parsed = AnimationFile.Create(new ByteChunk(bytes));
            Assert.AreEqual("root", parsed.Bones[0].Name);
            Assert.IsFalse(editor.HasUnsavedChanges);
        });
    }
    [TestMethod]
    public void Assistant_CampaignXmlEditsPreserveMemoryAndDiskBoundary()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            new LocalizationManager().LoadLanguage();
            var original = CampaignAnimationBinLoader.Write(AnimPackWorkflowTests.Campaign(), "sample");
            var file = PackFile.CreateFromBytes("sample.bin", original);
            var vm = AnimPackWorkflowTests.Create(file, "animations/campaign/database/bin/sample.bin", out var saved);
            var ids = new AssistantIds(); var adapter = new AnimPackAssistantAdapter();
            var before = vm.SelectedItemViewModel.Text;
            Run(adapter, vm, ids, "animpack.replace", new() { ["find"] = "humanoid01", ["replace"] = "griffon" });
            Assert.IsTrue(vm.HasUnsavedChanges); CollectionAssert.AreEqual(original, file.DataSource.ReadData());
            Run(adapter, vm, ids, "animpack.undo"); Assert.AreEqual(before, vm.SelectedItemViewModel.Text);
            Run(adapter, vm, ids, "animpack.redo");
            Run(adapter, vm, ids, "animpack.apply_child");
            Assert.IsTrue(vm.HasUnsavedChanges); CollectionAssert.AreEqual(original, file.DataSource.ReadData());
            Assert.IsTrue(vm.Save()); Assert.AreEqual("griffon", CampaignAnimationBinLoader.Load(new ByteChunk(saved())).SkeletonName);
            Assert.IsFalse(vm.HasUnsavedChanges);
        });
    }
    [TestMethod]
    public void Assistant_TextUsesAvalonUndoAndRejectsInvalidXmlBeforeEditing()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            new LocalizationManager().LoadLanguage();
            const string original = "<variantmesh><slot name=\"old\"/></variantmesh>";
            var file = PackFile.CreateFromBytes("sample.variantmeshdefinition", Encoding.ASCII.GetBytes(original));
            var files = new Mock<IPackFileService>(); files.Setup(f => f.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(file.Name);
            var save = new Mock<IFileSaveService>();
            save.Setup(s => s.Save(file.Name, It.IsAny<byte[]>(), false)).Returns<string, byte[], bool>((_, bytes, _) => { file.DataSource = new MemorySource(bytes); return file; });
            var vm = new TextEditorViewModel<DefaultTextConverter>(save.Object, files.Object, new DefaultTextConverter()); vm.LoadFile(file);
            var view = new TextEditorView { DataContext = vm }; view.Measure(new Size(640, 360)); view.Arrange(new Rect(0, 0, 640, 360));
            Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
            var adapter = new TextAssistantAdapter(); var ids = new AssistantIds();
            Run(adapter, vm, ids, "text.replace", new() { ["old_text"] = "old", ["new_text"] = "new" });
            Assert.IsTrue(vm.HasUnsavedChanges); Assert.IsTrue(vm.Text.Contains("new"));
            Assert.ThrowsException<System.Xml.XmlException>(() => Run(adapter, vm, ids, "text.replace", new() { ["old_text"] = "</variantmesh>", ["new_text"] = "" }));
            Run(adapter, vm, ids, "text.undo"); Assert.AreEqual(original, vm.Text); Assert.IsFalse(vm.HasUnsavedChanges);
            Run(adapter, vm, ids, "text.redo"); Assert.IsTrue(vm.Save());
            Assert.IsTrue(Encoding.ASCII.GetString(file.DataSource.ReadData()).Contains("new"));
            view.DataContext = null;
        });
    }
}
