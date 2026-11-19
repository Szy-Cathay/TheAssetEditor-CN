using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using AssetEditor.ModAssistant;
using Editors.Audio;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Events.AudioFilesExplorer;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Wwise;
using Editors.ImportExport;
using Editors.Reports;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.DependencyInjection;
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
using Shared.GameFormats.Dat;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Bkhd;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;
using static Shared.Core.ModAssistant.AssistantJson;
using HircSettings = Editors.Audio.Shared.AudioProject.Models.HircSettings;
using AudioAction = Editors.Audio.Shared.AudioProject.Models.Action;

namespace AssetEditorTests;

[TestClass, DoNotParallelize]
public class ModAssistantExtensionTests
{
    private static T Await<T>(Task<T> task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame(); task.GetAwaiter().OnCompleted(() => frame.Continue = false); Dispatcher.PushFrame(frame);
        }
        return task.GetAwaiter().GetResult();
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AssetEditor.CN.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    [TestMethod]
    public void AudioAdvancedSettings_ValidateModeUnitsAndEnumsBeforeChangingSource()
    {
        var source = HircSettings.CreateRecommendedRandomSequenceContainerSettings(3);
        foreach (var patch in new[] { new JsonObject { ["number_of_loops"] = 0 }, new JsonObject { ["transition_duration"] = 3601 },
            new JsonObject { ["random_type"] = "unknown" }, new JsonObject { ["playlist_end"] = "Restart" },
            new JsonObject { ["transition_type"] = "Delay" }, new JsonObject { ["arbitrary_field"] = 3 } })
            Assert.ThrowsException<ArgumentException>(() => AudioHircSettingsPatch.Apply(source, patch, true));
        Assert.AreEqual(PlayMode.Step, source.PlayMode);
        var changed = AudioHircSettingsPatch.Apply(source, new()
        {
            ["play_mode"] = "Continuous",
            ["looping_type"] = "FiniteLooping",
            ["number_of_loops"] = 3,
            ["transition_type"] = "Delay",
            ["transition_duration"] = .25
        }, true);
        Assert.AreEqual(.25m, changed.TransitionDuration); Assert.AreEqual(3u, changed.NumberOfLoops);
        Assert.AreEqual(source.RepetitionInterval, changed.RepetitionInterval);
        Assert.ThrowsException<ArgumentException>(() => AudioHircSettingsPatch.Apply(source, new() { ["container_type"] = "Sequence" }, false));
    }

    [TestMethod]
    public void AudioAdvancedSettings_ActualProjectBatchUndoRedoAndSavedReadbackPreserveIds()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-audio-settings-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            EditorTabDirtyStateTests.WithServices(services =>
            {
                var files = services.GetRequiredService<IPackFileService>();
                using var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "音频参数测试" });
                ((PackFileService)files).AddContainer(project, true);
                var editor = services.GetRequiredService<IEditorManager>().Create(EditorEnums.Audio_Editor);
                var scopes = services.GetRequiredService<IScopeRepository>();
                var state = scopes.GetRequiredService<IAudioEditorStateService>(editor);
                state.AudioProject = AudioProjectFile.CreateNew("english(uk)", "settings");
                state.AudioProjectFileName = "settings.aproj"; state.AudioProjectFilePath = "audio/settings.aproj";
                var bank = state.AudioProject.SoundBanks.First(b => b.GameSoundBank == Wh3SoundBank.BattleIndividualMagic);
                var sound = Sound.CreateTargetSound(Guid.NewGuid(), 42, 7, 8, 771, "english(uk)", HircSettings.CreateDefaultSoundSettings());
                var container = new RandomSequenceContainer(Guid.NewGuid(), 43, 9, 10, HircSettings.CreateRecommendedRandomSequenceContainerSettings(2), [42, 44]);
                bank.Sounds.Add(sound); bank.RandomSequenceContainers.Add(container);
                bank.ActionEvents.Add(new ActionEvent(50, "Play_settings", [AudioAction.CreatePlay(51, AkBkHircType.Sound, sound.Id, bank.Id)], Wh3ActionEventType.BattleAbilities));
                state.AudioProject.AudioFiles.Add(new AudioFile(Guid.NewGuid(), 771, "settings.wav", "audio/settings.wav"));
                scopes.GetRequiredService<IEventHub>(editor).Publish(new AudioProjectLoadedEvent(false));
                var adapter = services.GetServices<IAssistantEditorAdapter>().OfType<AudioProjectAssistantAdapter>().Single(); var ids = new AssistantIds();
                JsonObject Target(uint id, string kind) => new() { ["bank"] = bank.Id.ToString(), ["id"] = id.ToString(), ["kind"] = kind };
                JsonNode Run(string name, JsonObject? args = null) => ModAssistantDataTests.Run(adapter, editor, ids, "audio_project." + name, args);
                Assert.AreEqual(2, Run("settings_targets")["total"]!.GetValue<int>());
                var invalid = new JsonObject { ["targets"] = new JsonArray(Target(43, "container"), Target(42, "sound")), ["settings"] = new JsonObject { ["container_type"] = "Sequence" } };
                Assert.ThrowsException<ArgumentException>(() => Run("edit_settings", invalid));
                Assert.AreEqual(ContainerType.Random, container.HircSettings.ContainerType);
                Run("edit_settings", new()
                {
                    ["targets"] = new JsonArray(Target(43, "container")),
                    ["settings"] = new JsonObject
                    {
                        ["play_mode"] = "Continuous",
                        ["container_type"] = "Sequence",
                        ["playlist_end"] = "PlayInReverseOrder",
                        ["always_reset_playlist"] = true,
                        ["looping_type"] = "FiniteLooping",
                        ["number_of_loops"] = 3,
                        ["transition_type"] = "Delay",
                        ["transition_duration"] = .5
                    }
                });
                Assert.AreEqual(.5m, container.HircSettings.TransitionDuration);
                Run("undo"); Assert.AreEqual(PlayMode.Step, state.AudioProject.SoundBanks.Single(b => b.Id == bank.Id).RandomSequenceContainers.Single().HircSettings.PlayMode);
                Run("redo");
                Run("edit_settings", new() { ["targets"] = new JsonArray(Target(42, "sound")), ["settings"] = new JsonObject { ["looping_type"] = "FiniteLooping", ["number_of_loops"] = 4 } });
                Assert.IsTrue(((ISaveableEditor)editor).Save());
                var saved = scopes.GetRequiredService<IAudioProjectFileService>(editor).DeserialiseAudioProject(files.FindFile(state.AudioProjectFilePath, project)!);
                var readBank = saved.SoundBanks.Single(b => b.Id == bank.Id);
                Assert.AreEqual(4u, readBank.Sounds.Single().HircSettings.NumberOfLoops);
                Assert.AreEqual(sound.Guid, readBank.Sounds.Single().Guid); Assert.AreEqual(7u, readBank.Sounds.Single().OverrideBusId);
                Assert.AreEqual(.5m, readBank.RandomSequenceContainers.Single().HircSettings.TransitionDuration);
                CollectionAssert.AreEqual(new uint[] { 42, 44 }, readBank.RandomSequenceContainers.Single().Children.ToArray());
                Assert.IsTrue(File.Exists(Path.Combine(root, "audio", "settings.aproj")));
                var model = (Editors.Audio.AudioEditor.Presentation.AudioEditorViewModel)editor;
                scopes.GetRequiredService<IEventHub>(editor).Publish(new AudioFilesChangedEvent([
                    new AudioFile(Guid.NewGuid(), 900, "one.wav", "audio/one.wav"), new AudioFile(Guid.NewGuid(), 901, "two.wav", "audio/two.wav")], false, false, false));
                Run("prepare_settings", new()
                {
                    ["settings"] = new JsonObject
                    {
                        ["container_type"] = "Random",
                        ["random_type"] = "Shuffle",
                        ["enable_repetition_interval"] = true,
                        ["repetition_interval"] = 2
                    }
                });
                Assert.AreEqual(RandomType.Shuffle, state.HircSettings.RandomType);
                Assert.AreEqual(2u, state.HircSettings.RepetitionInterval);
                Assert.IsFalse(model.HasUnsavedChanges, "Preparing the next add must not mark the saved project dirty.");
                model.SettingsViewModel.RandomType = RandomType.Standard;
                Assert.AreEqual(RandomType.Standard, state.HircSettings.RandomType, "Native UI and assistant share the same settings state.");
                files.UnloadPackContainer(project);
            });
        }
        finally { DeleteTemporaryDirectory(root); }
    }

    [TestMethod]
    public void Reports_OriginalTemplatesProduceBoundedVerifiedFilesWithNativeParsers()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-reports-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            EditorTabDirtyStateTests.WithServices(services =>
            {
                var files = services.GetRequiredService<IPackFileService>();
                var owner = files.CreateNewPackFileContainer("isolated-reports", PackFileVersion.PFH5, PackFileCAType.MOD);
                void Add(string path, byte[] bytes) => owner.FileList.Add(path.Replace('/', '\\').ToLowerInvariant(), PackFile.CreateFromBytes(Path.GetFileName(path), bytes));
                var parser = services.GetRequiredService<MetaDataFileParser>();
                Add("test.meta", parser.GenerateBytes(2, new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes = [new Effect_v1 {
                    Name = "EFFECT", Version = 1, VfxName = "report_sample", BoolUnknown = "true", NodeIndex = -1, EffectEndTime = 2 }]
                }));
                var dat = new SoundDatFile(); dat.Unknown.Add("Play_report_sample"); Add("test.dat", DatFileParser.WriteData(dat));
                Add("text.xml", Encoding.ASCII.GetBytes("Report Sample"));
                Add("bad.meta", [1, 2, 3]);
                var repository = RepositoryRoot();
                var modelDirectory = Path.Combine(repository, "Data", "Karl_and_celestialgeneral_Pack");
                var model = Directory.GetFiles(modelDirectory, "*.rigid_model_v2", SearchOption.AllDirectories).First();
                var ws = Path.ChangeExtension(model, ".wsmodel");
                Add("test.rigid_model_v2", File.ReadAllBytes(model)); Add("test.wsmodel", File.ReadAllBytes(ws));
                foreach (var material in new Shared.GameFormats.WsModel.WsModelFile(owner.FileList["test.wsmodel"]).MaterialList)
                    if (!owner.FileList.ContainsKey(material.MaterialPath.Replace('/', '\\').ToLowerInvariant())) Add(material.MaterialPath, File.ReadAllBytes(Path.Combine(modelDirectory, material.MaterialPath)));
                Add("test.anim", File.ReadAllBytes(Path.Combine(repository, "Data", "AnimationRetarget", "animations", "skeletons", "humanoid01.anim")));
                var animations = new AnimationPackFileDatabase("test.animpack");
                animations.AddFile(new AnimationBinWh3("animations/database/battle/bin/test.bin") { Name = "report_table", SkeletonName = "humanoid01" });
                Add("test.animpack", AnimationPackSerializer.ConvertToBytes(animations));
                Add("test.bnk", NativeReportBank());
                var ids = new AssistantIds();
                var adapter = services.GetServices<IAssistantWorkspaceAdapter>().OfType<ReportTemplateAssistantAdapter>().Single();
                var operation = adapter.GetOperations(ids).Single(o => o.Name == "reports.generate");
                JsonObject Args(string template, params string[] paths) => new()
                {
                    ["template"] = template,
                    ["output"] = Path.Combine(root, template),
                    ["inputs"] = new JsonArray(paths.Select(path => (JsonNode)new JsonObject { ["container"] = ids.Get(owner), ["path"] = path }).ToArray())
                };
                var samples = new Dictionary<string, string[]>
                {
                    ["file_list"] = ["text.xml", "test.dat"],
                    ["rmv2"] = ["test.rigid_model_v2"],
                    ["materials"] = ["test.wsmodel"],
                    ["meta_table"] = ["test.meta"],
                    ["meta_json"] = ["test.meta", "test.anim", "test.animpack"],
                    ["dat"] = ["test.dat"],
                    ["dialogue_info"] = ["test.bnk", "test.dat"],
                    ["event_names"] = ["test.bnk", "test.dat"],
                    ["deep_search"] = ["text.xml"]
                };
                foreach (var sample in samples)
                {
                    var args = Args(sample.Key, sample.Value); if (sample.Key == "deep_search") args["search"] = "report";
                    Validate(args, operation.Parameters); var result = Await(operation.Execute(args, default));
                    Assert.AreEqual("completed", result["outcome"]!.GetValue<string>(), sample.Key + ": " + result);
                    Assert.IsTrue(result["outputs"]!.AsArray().Count > 0, sample.Key); Assert.IsTrue(result["readback_verified"]!.GetValue<bool>());
                    foreach (var output in result["outputs"]!.AsArray()) Assert.IsTrue(File.Exists(output!["path"]!.GetValue<string>()));
                }
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "file_list", "FileList.csv")), "text.xml");
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "dat", "test.dat.txt")), "Play_report_sample");
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "meta_json", "test.meta.json")), "report_sample");
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "meta_json", "animations", "database", "battle", "bin", "test.bin.xml")), "report_table");
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "deep_search", "DeepSearch.txt")), "text.xml");
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "event_names", "test.bnk.txt")), "Play_report_sample");
                StringAssert.Contains(File.ReadAllText(Path.Combine(root, "dialogue_info", "test.bnk.txt")), "1234 : 5678");
                Assert.ThrowsException<ArgumentException>(() => Await(operation.Execute(Args("file_list", "text.xml"), default)));
                var partial = Args("meta_json", "test.meta", "bad.meta"); partial["output"] = Path.Combine(root, "partial");
                var partialResult = Await(operation.Execute(partial, default)); Assert.AreEqual("partial", partialResult["outcome"]!.GetValue<string>());
                Assert.IsTrue(File.Exists(Path.Combine(root, "partial", "test.meta.json"))); Assert.IsTrue(partialResult["diagnostics"]!.AsArray().Count > 0);
                var cancelled = Args("file_list", "text.xml"); cancelled["output"] = Path.Combine(root, "cancelled");
                Assert.ThrowsException<OperationCanceledException>(() => Await(operation.Execute(cancelled, new CancellationToken(true))));
                Assert.IsFalse(Directory.Exists(Path.Combine(root, "cancelled")));
                files.UnloadPackContainer(owner);
            });
        }
        finally { DeleteTemporaryDirectory(root); }
    }

    [TestMethod]
    public void ReportOutputs_RejectOverwriteAndEscapeAndKeepVerifiedPartialWorkOnCancel()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-report-paths-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            using var cancel = new CancellationTokenSource();
            var context = new ReportExecutionContext([], Path.Combine(root, "output") + Path.DirectorySeparatorChar, cancel.Token);
            var first = Path.Combine(context.OutputDirectory, "first.txt");
            ReportExecutionContext.WriteText(context, first, "verified");
            Assert.ThrowsException<IOException>(() => ReportExecutionContext.WriteText(context, first, "overwrite"));
            Assert.ThrowsException<IOException>(() => ReportExecutionContext.WriteText(context, Path.Combine(root, "escape.txt"), "escape"));
            Assert.AreEqual("verified", File.ReadAllText(first)); Assert.IsFalse(File.Exists(Path.Combine(root, "escape.txt")));
            cancel.Cancel();
            Assert.ThrowsException<OperationCanceledException>(() => ReportExecutionContext.WriteText(context, Path.Combine(context.OutputDirectory, "second.txt"), "cancelled"));
            CollectionAssert.AreEqual(new[] { first }, context.Outputs.ToArray()); Assert.IsTrue(context.VerifiedOutputs.Contains(first));
            Assert.IsFalse(File.Exists(Path.Combine(context.OutputDirectory, "second.txt")));
        }
        finally { DeleteTemporaryDirectory(root); }
    }

    [TestMethod]
    public void HistoryMenus_ActualRestorePointDeleteAndDiscardUseCoordinatorAndPreserveOtherFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-history-assistant-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            EditorTabDirtyStateTests.WithServices(services =>
            {
                var files = services.GetRequiredService<IPackFileService>();
                var history = services.GetRequiredService<IFolderProjectHistoryService>();
                var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "历史测试", GameVersion = GameTypeEnum.Warhammer3 });
                ((PackFileService)files).AddContainer(project, true); history.Initialize(root);
                files.ApplyFileWrites(project, [new("a.txt", [1]), new("b.txt", [2])], false);
                var point = history.CreateRestorePoint(root, "第一次修改");
                files.ApplyFileWrites(project, [new("a.txt", [3])], true); var next = history.CreateRestorePoint(root, "第二次修改");
                using var scope = services.CreateScope(); var workspace = scope.ServiceProvider.GetRequiredService<AssistantWorkspace>(); workspace.BeginTask();
                JsonNode Execute(string name, JsonObject args)
                {
                    var operation = workspace.GetOperations().Single(o => o.Name == name && o.TargetId == "workspace");
                    Validate(args, operation.Parameters);
                    return Await(operation.Execute(args, default));
                }
                Execute("project.delete_restore_point", new() { ["id"] = point.Id });
                Assert.IsFalse(history.GetRestorePoints(root).Any(p => p.Id == point.Id));
                CollectionAssert.AreEqual(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(root, "a.txt")));
                files.ApplyFileWrites((FolderProjectContainer)files.GetEditablePack(), [new("a.txt", [7]), new("b.txt", [8])], true);
                Execute("project.discard_changes", new() { ["paths"] = new JsonArray("a.txt") });
                CollectionAssert.AreEqual(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(root, "a.txt")));
                CollectionAssert.AreEqual(new byte[] { 8 }, File.ReadAllBytes(Path.Combine(root, "b.txt")));
                Assert.AreSame(project, files.GetEditablePack());
                Assert.AreEqual("第二次修改", history.GetRestorePoints(root).First().Description);
                Assert.IsTrue(history.GetStatus(root).UnrecordedChanges.Any(c => c.Path == "b.txt"));
                var secondPage = Execute("project.history", new() { ["offset"] = 1, ["limit"] = 1 });
                Assert.IsTrue(secondPage["restore_points"]![0]!["IsInitial"]!.GetValue<bool>());
                Execute("project.discard_all", new() { ["root"] = root });
                CollectionAssert.AreEqual(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(root, "b.txt")));
                Assert.IsTrue(history.GetStatus(root).IsClean);
                Assert.AreNotSame(project, files.GetEditablePack());
                var initial = history.GetRestorePoints(root).Single(p => p.IsInitial);
                Assert.ThrowsException<FolderProjectHistoryException>(() => Execute("project.delete_restore_point", new() { ["id"] = initial.Id }));
                files.UnloadPackContainer(files.GetEditablePack());
            });
        }
        finally { DeleteTemporaryDirectory(root); }
    }

    [TestMethod]
    public void HistoryRecovery_BindsAbnormalProjectAndReopensItThroughActualCoordinator()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-history-recovery-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            EditorTabDirtyStateTests.WithServices(services =>
            {
                var files = services.GetRequiredService<IPackFileService>();
                using (var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "异常历史测试", GameVersion = GameTypeEnum.Warhammer3 })) { }
                var history = services.GetRequiredService<IFolderProjectHistoryService>(); history.Initialize(root);
                File.WriteAllText(Path.Combine(root, "keep.txt"), "keep"); history.CreateRestorePoint(root, "已记录");
                using (var repository = new LibGit2Sharp.Repository(root)) LibGit2Sharp.Commands.Checkout(repository, repository.Head.Tip);
                File.WriteAllText(Path.Combine(root, "keep.txt"), "unrecorded");
                Assert.IsTrue(history.GetDisplayStatus(root).CanRecover);
                using var scope = services.CreateScope(); var workspace = scope.ServiceProvider.GetRequiredService<AssistantWorkspace>(); workspace.BeginTask();
                JsonNode Execute(string name, JsonObject args, string request)
                {
                    var inspect = Await(workspace.InspectAsync("workspace", default));
                    return Await(workspace.ExecuteAsync(request, name, "workspace", inspect.AsObject().String("revision"), args, default));
                }
                var status = Execute("project.recovery_status", new() { ["root"] = root }, "inspect-recovery");
                Assert.AreEqual("completed", status["status"]!.GetValue<string>(), status.ToJsonString());
                var recover = Execute("project.recover_history", new() { ["root"] = root }, "recover");
                Assert.AreEqual("completed", recover["status"]!.GetValue<string>(), recover.ToJsonString());
                Assert.IsFalse(history.GetDisplayStatus(root).CanRecover);
                Assert.AreEqual("unrecorded", File.ReadAllText(Path.Combine(root, "keep.txt")));
                Assert.IsInstanceOfType(files.GetEditablePack(), typeof(FolderProjectContainer));
                var followup = Execute("resources.search", new() { ["query"] = "keep.txt" }, "after-recovery");
                Assert.AreEqual("completed", followup["status"]!.GetValue<string>());
                files.UnloadPackContainer(files.GetEditablePack()!);
            }, registrations =>
            {
                var dialogs = new Mock<IStandardDialogs>();
                dialogs.Setup(d => d.ShowYesNoBox(It.IsAny<string>(), It.IsAny<string>())).Returns(ShowMessageBoxResult.OK);
                registrations.AddScoped(_ => dialogs.Object);
            });
        }
        finally { DeleteTemporaryDirectory(root); }
    }

    [TestMethod]
    public void FbxBoundary_DiscoveryAndImportRejectUnsupportedFormatWithoutWriting()
    {
        EditorTabDirtyStateTests.WithServices(services =>
        {
            var adapter = services.GetServices<IAssistantWorkspaceAdapter>().OfType<ImportExportAssistantAdapter>().Single();
            var ids = new AssistantIds(); var formats = adapter.GetOperations(ids).Single(o => o.Name == "convert.formats");
            Assert.IsFalse(Await(formats.Execute(new(), default))["fbx_supported"]!.GetValue<bool>());
            Assert.IsFalse(Editors.ImportExport.Misc.FileExtensionHelper.IsGltfFile("model.fbx"));
            var root = Path.Combine(Path.GetTempPath(), "AECN-fbx-boundary-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
            try
            {
                using var project = FolderProjectContainer.Create(root, new FolderProjectSettings { Name = "格式边界" });
                var files = services.GetRequiredService<IPackFileService>(); ((PackFileService)files).AddContainer(project, true);
                var source = Path.Combine(root, "model.fbx"); File.WriteAllText(source, "invalid FBX fixture");
                var operation = adapter.GetOperations(ids).Single(o => o.Name == "convert.import_gltf");
                Assert.ThrowsException<ArgumentException>(() => Await(operation.Execute(new() { ["source"] = source, ["folder"] = "output" }, default)));
                Assert.IsFalse(Directory.Exists(Path.Combine(root, "output"))); files.UnloadPackContainer(project);
            }
            finally { DeleteTemporaryDirectory(root); }
        });
    }

    private static byte[] NativeReportBank()
    {
        var action = new CAkEvent_V136 { Id = WwiseHash.Compute("Play_report_sample"), HircType = AkBkHircType.Event };
        var dialogue = new CAkDialogueEvent_V136
        {
            Id = 1234,
            HircType = AkBkHircType.Dialogue_Event,
            TreeDepth = 1,
            TreeDataSize = 12,
            Arguments = [new AkGameSync_V136 { GroupId = 5678 }],
            AkDecisionTree = new AkDecisionTree_V136 { Nodes = [new AkDecisionTree_V136.Node_V136 { AudioNodeId = 42 }] }
        };
        action.UpdateSectionSize(); dialogue.UpdateSectionSize();
        var items = new HircItem[] { action, dialogue };
        var hirc = new HircChunk
        {
            NumHircItems = 2,
            HircItems = items.ToList(),
            ChunkHeader = new() { Tag = "HIRC", ChunkSize = 4 + (uint)items.Sum(item => item.WriteData().Length) }
        };
        var header = new BkhdChunk { ChunkHeader = new() { Tag = "BKHD", ChunkSize = 20 }, AkBankHeader = new() { BankGeneratorVersion = (uint)GameBnkVersion.Warhammer3, SoundBankId = 1 } };
        return BkhdChunk.WriteData(header).Concat(HircChunk.WriteData(hirc, (uint)GameBnkVersion.Warhammer3)).ToArray();
    }

    private static void DeleteTemporaryDirectory(string root)
    {
        Assert.IsTrue(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase));
        foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
        Directory.Delete(root, true);
    }
}
