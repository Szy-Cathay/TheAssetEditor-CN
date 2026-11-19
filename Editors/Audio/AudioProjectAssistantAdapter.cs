#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Core.AudioProjectMutation;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Presentation;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.Storage;
using Editors.Audio.AudioProjectConverter;
using Editors.Audio.AudioProjectMerger;
using Editors.Audio.DialogueEventMerger;
using Shared.Core.DependencyInjection;
using Shared.Core.Events;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.ToolCreation;
using Shared.Core.Settings;
using Shared.Core.PackFiles.Utility;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Audio;

public sealed class AudioProjectAssistantAdapter(IScopeRepository scopes, IPackFileService files, IEditorManager editors) : IAssistantEditorAdapter
{
    private readonly ConditionalWeakTable<AudioEditorViewModel, AssistantUndoJournal> _history = new();
    private static IEnumerable<AudioProjectTreeNode> Flatten(IEnumerable<AudioProjectTreeNode> nodes) => nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children)));
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not AudioEditorViewModel vm) yield break;
        var state = scopes.GetRequiredService<IAudioEditorStateService>(editor);
        var events = scopes.GetRequiredService<IEventHub>(editor);
        var parser = scopes.GetRequiredService<IAudioProjectFileService>(editor);
        var settings = scopes.GetRequiredService<ApplicationSettingsService>(editor);
        AudioAssistantWriteSession Writes(System.Threading.CancellationToken token) => new(files,
            scopes.GetRequiredService<IAudioPackOutputService>(editor), parser, settings, token);
        byte[] Capture() => JsonSerializer.SerializeToUtf8Bytes(new { project = state.AudioProject, dirty = vm.HasUnsavedChanges,
            path = state.AudioProjectFilePath, runtime = state.AudioProject?.AudioFiles.Select(f => new { f.Id, f.WemPackFileName, f.WemPackFilePath, f.WemDiskFilePath }) });
        void Refresh(bool dirty)
        {
            var selection = state.SelectedAudioProjectExplorerNode;
            state.StoreSelectedAudioProjectExplorerNode(null);
            state.StoreSelectedViewerRows([]); state.StorePendingEditedViewerRows([]);
            events.Publish(new AudioProjectLoadedEvent(true));
            vm.AudioProjectExplorerViewModel.SelectedNode = Flatten(vm.AudioProjectExplorerViewModel.AudioProjectTree)
                .FirstOrDefault(n => n.Name == selection?.Name && n.Type == selection.Type);
            vm.HasUnsavedChanges = dirty;
        }
        void Restore(byte[] bytes)
        {
            var snapshot = JsonNode.Parse(bytes)!.AsObject();
            if (snapshot.String("path") != state.AudioProjectFilePath) throw new InvalidOperationException(Text("StaleTarget"));
            var project = parser.DeserialiseAudioProject(PackFile.CreateFromBytes("restore.aproj", JsonSerializer.SerializeToUtf8Bytes(snapshot["project"])));
            foreach (var runtime in snapshot["runtime"]!.AsArray().Cast<JsonObject>())
            {
                var file = project.AudioFiles.Single(f => f.Id == runtime["Id"]!.GetValue<uint>());
                file.WemPackFileName = runtime["WemPackFileName"]?.GetValue<string>(); file.WemPackFilePath = runtime["WemPackFilePath"]?.GetValue<string>(); file.WemDiskFilePath = runtime["WemDiskFilePath"]?.GetValue<string>();
            }
            state.StoreAudioProject(project); Refresh(snapshot.Boolean("dirty"));
        }
        var journal = _history.GetValue(vm, _ => new(Capture, Restore));
        JsonNode State() => Node(new { loaded = vm.IsAudioProjectLoaded, vm.IsBusy, vm.HasUnsavedChanges, state.AudioProjectFilePath,
            revision = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Capture())), selected = state.SelectedAudioProjectExplorerNode?.Name,
            selected_audio = state.AudioFiles.Select(f => f.WavPackFilePath), settings = state.HircSettings,
            vm.CompileStatus, compile_succeeded = vm.AssistantCompileSucceeded });
        string? Unavailable() => !vm.CanEditAudioProject ? Text(vm.IsBusy ? "Busy" : "PreviewNotReady") : null;
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect, Text("GuardedUndo"), State, run, Unavailable);
        void Edit(System.Action action) => journal.Apply(() => { action(); Refresh(true); });
        yield return new AssistantOperation("audio_project.open", ids.Get(editor), Text("AudioProjectOpen"), Schema(), AssistantEffect.Preview, Text("DiskRecovery"), State,
            async (_, token) => { var previous = state.AudioProject; await vm.LoadAudioProject(token); token.ThrowIfCancellationRequested(); if (!vm.IsAudioProjectLoaded || ReferenceEquals(previous, state.AudioProject)) throw new OperationCanceledException(); return State(); },
            () => vm.IsBusy ? Text("Busy") : null);
        yield return AssistantOperation.Sync("audio_project.new", ids.Get(editor), Text("AudioProjectNew"), Schema(), AssistantEffect.Preview, Text("DiskRecovery"), State,
            _ => { var previous = state.AudioProject; vm.NewAudioProject(); if (ReferenceEquals(previous, state.AudioProject)) throw new OperationCanceledException(); return State(); }, () => vm.IsBusy ? Text("Busy") : null);
        yield return Op("audio_project.tree", "AudioProjectTree", Schema(("query", Str(), false), ("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
            args => Node(Flatten(vm.AudioProjectExplorerViewModel.AudioProjectTree).Where(n => n.Name.Contains(args.OptionalString("query"), StringComparison.OrdinalIgnoreCase))
                .Skip(args.Integer("offset")).Take(100).Select(n => new { id = ids.Get(n), n.Name, type = n.Type.ToString() })));
        yield return Op("audio_project.select", "Select", Schema(("node", Str(), true)), AssistantEffect.Preview, args =>
        { vm.AudioProjectExplorerViewModel.SelectedNode = Flatten(vm.AudioProjectExplorerViewModel.AudioProjectTree).Single(n => ids.Get(n) == args.String("node")); return State(); });
        yield return Op("audio_project.read", "AudioProjectRead", Schema(("offset", Num(0, 10000000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var json = JsonSerializer.Serialize(state.AudioProject); var offset = Math.Min(args.Integer("offset"), json.Length);
            return Node(new { length = json.Length, offset, content = json.Substring(offset, Math.Min(16000, json.Length - offset)), source = "unsaved_editor" });
        });
        yield return Op("audio_project.audio_files", "AudioProjectFiles", Schema(("paths", Array(Str(), 100), true)), AssistantEffect.Preview, args =>
        {
            var paths = args["paths"]!.AsArray().Select(p => p!.GetValue<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var nodes = paths.Select(path =>
            {
                var file = files.FindFile(path) ?? throw new FileNotFoundException(path);
                if (!path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(Text("InvalidParameters"));
                using var stream = new MemoryStream(file.DataSource.ReadData());
                using var reader = new NAudio.Wave.WaveFileReader(stream);
                return new AudioFilesTreeNode { FileName = Path.GetFileName(path), FilePath = path, Type = AudioFilesTreeNodeType.WavFile };
            }).ToArray();
            vm.SettingsViewModel.SetAudioFilesViaDrop(nodes);
            return State();
        });
        yield return Op("audio_project.state", "AudioStateEdit", Schema(("group", Str(), true), ("name", Str(), true), ("action", Str("", "add", "remove"), true)), AssistantEffect.Edit, args =>
        {
            var group = state.AudioProject.StateGroups.Single(g => g.Name == args.String("group"));
            var name = args.String("name"); var add = args.String("action") == "add";
            if (string.IsNullOrWhiteSpace(name) || group.States.Any(s => s.Name == name) == add) throw new ArgumentException(Text("InvalidParameters"));
            var service = scopes.GetRequiredService<IStateService>(editor);
            if (!add) service.ValidateStateCanBeRemoved(group.Name, name);
            Edit(() => { if (add) service.AddState(group.Name, name); else service.RemoveState(group.Name, name); });
            return State();
        });
        yield return Op("audio_project.settings_targets", "AudioSettingsTargets", Schema(("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var targets = state.AudioProject.SoundBanks.SelectMany(bank =>
                bank.Sounds.Where(sound => sound.HircSettings != null).Select(sound => new { bank = bank.Id.ToString(), id = sound.Id.ToString(),
                    kind = "sound", name = sound.Name, settings = sound.HircSettings, children = 1 })
                .Concat(bank.RandomSequenceContainers.Select(container => new { bank = bank.Id.ToString(), id = container.Id.ToString(),
                    kind = "container", name = container.Name, settings = container.HircSettings, children = container.Children.Count }))).ToArray();
            return Node(new { total = targets.Length, targets = targets.Skip(args.Integer("offset")).Take(100), source = "unsaved_editor" });
        });
        yield return Op("audio_project.prepare_settings", "AudioPrepareSettings", Schema(("settings", AudioHircSettingsPatch.Schema(), true)), AssistantEffect.Preview, args =>
        {
            if (state.AudioFiles.Count == 0 || state.HircSettings == null || vm.SettingsViewModel.ShowSettingsFromAudioProjectViewer)
                throw new InvalidOperationException(Text("AudioSelectFilesFirst"));
            var before = Node(state.HircSettings);
            var value = AudioHircSettingsPatch.Apply(state.HircSettings, args["settings"]!.AsObject(), state.AudioFiles.Count > 1);
            vm.SettingsViewModel.ApplySettings(value);
            return Node(new { before, after = state.HircSettings, scope = "settings_for_next_add", state = State() });
        });
        yield return Op("audio_project.edit_settings", "AudioEditSettings", Schema(
            ("targets", Array(Schema(("bank", Str(), true), ("id", Str(), true), ("kind", Str("", "sound", "container"), true)), 100), true),
            ("settings", AudioHircSettingsPatch.Schema(), true)), AssistantEffect.Edit, args =>
        {
            var targets = args["targets"]!.AsArray().Cast<JsonObject>().ToArray();
            if (targets.Select(t => t.String("bank") + "/" + t.String("kind") + "/" + t.String("id")).Distinct().Count() != targets.Length)
                throw new ArgumentException(Text("InvalidParameters"));
            var changes = targets.Select(target =>
            {
                var bank = state.AudioProject.SoundBanks.Single(b => b.Id.ToString() == target.String("bank"));
                var container = target.String("kind") == "container";
                var sound = container ? null : bank.Sounds.Single(s => s.Id.ToString() == target.String("id") && s.HircSettings != null);
                var group = container ? bank.RandomSequenceContainers.Single(c => c.Id.ToString() == target.String("id")) : null;
                var before = container ? group!.HircSettings : sound!.HircSettings;
                var after = AudioHircSettingsPatch.Apply(before, args["settings"]!.AsObject(), container);
                return (target, sound, group, before, after);
            }).ToArray();
            Edit(() =>
            {
                foreach (var change in changes)
                    if (change.group != null) change.group.HircSettings = change.after;
                    else change.sound!.HircSettings = change.after;
            });
            return Node(new { changes = changes.Select(c => new { c.target, c.before, c.after }), state = State() });
        });
        yield return Op("audio_project.action_event", "AudioActionEdit", Schema(("type", Str(), true), ("name", Str(), true), ("action", Str("", "add", "remove"), true)), AssistantEffect.Edit, args =>
        {
            var type = args.String("type"); var name = args.String("name"); var add = args.String("action") == "add";
            if (!Flatten(vm.AudioProjectExplorerViewModel.AudioProjectTree).Any(n => n.IsActionEvent() && n.Name == type)) throw new ArgumentException(Text("InvalidParameters"));
            var existing = state.AudioProject.SoundBanks.SelectMany(b => b.ActionEvents).Any(e => e.Name == name);
            if (existing == add || !new[] { "Play_", "Pause_", "Resume_", "Stop_" }.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))) throw new ArgumentException(Text("InvalidParameters"));
            if (add && name.StartsWith("Play_", StringComparison.Ordinal) && (state.AudioFiles.Count == 0 || state.HircSettings == null)) throw new ArgumentException(Text("AudioSelectFilesFirst"));
            var service = scopes.GetRequiredService<IActionEventService>(editor);
            Edit(() =>
            {
                if (!add) service.RemoveActionEvent(type, name);
                else if (name.StartsWith("Play_", StringComparison.Ordinal)) service.AddPlayActionEvent(type, name, state.AudioFiles.ToList(), state.HircSettings);
                else service.AddPauseResumeStopActionEvent(type, name);
            });
            return State();
        });
        yield return Op("audio_project.dialogue", "AudioDialogueEdit", Schema(("event", Str(), true), ("action", Str("", "add", "remove"), true),
            ("path_name", Str(), false), ("states", Array(Schema(("group", Str(), true), ("state", Str(), true)), 32), false)), AssistantEffect.Edit, args =>
        {
            var name = args.String("event");
            if (!Flatten(vm.AudioProjectExplorerViewModel.AudioProjectTree).Any(n => n.IsDialogueEvent() && n.Name == name)) throw new ArgumentException(Text("InvalidParameters"));
            var service = scopes.GetRequiredService<IDialogueEventService>(editor);
            var add = args.String("action") == "add";
            var pairs = (args["states"] as JsonArray ?? []).Cast<JsonObject>().Select(p => new KeyValuePair<string, string>(p.String("group"), p.String("state"))).ToList();
            if (add)
            {
                var repository = scopes.GetRequiredService<IAudioRepository>(editor);
                if (state.AudioFiles.Count == 0 || state.HircSettings == null || !repository.StateGroupsByDialogueEvent[name].SequenceEqual(pairs.Select(p => p.Key)))
                    throw new ArgumentException(Text("InvalidParameters"));
                foreach (var pair in pairs)
                    if (!state.AudioProject.StateGroups.Any(g => g.Name == pair.Key && g.States.Any(s => s.Name == pair.Value))) throw new ArgumentException(Text("InvalidParameters"));
            }
            Edit(() => { if (add) service.AddStatePath(name, state.AudioFiles.ToList(), state.HircSettings, pairs);
                else if (!service.RemoveStatePath(name, args.String("path_name"))) throw new InvalidOperationException(Text("NotCompleted")); });
            return State();
        });
        yield return Op("audio_project.undo", "Undo", Schema(), AssistantEffect.Edit, _ => { journal.Undo(); return State(); });
        yield return Op("audio_project.redo", "Redo", Schema(), AssistantEffect.Edit, _ => { journal.Redo(); return State(); });
        yield return new AssistantOperation("audio_project.merge", ids.Get(editor), Text("AudioMerge"), Schema(("base_path", Str(), true),
            ("other_path", Str(), true), ("name", Str(), true), ("output_folder", Str("project-relative directory"), true)), AssistantEffect.Disk, Text("DiskRecovery"), State,
            (args, token) =>
            {
                foreach (var key in new[] { "base_path", "other_path" })
                {
                    var source = files.FindFile(args.String(key)) ?? throw new FileNotFoundException(Text("MissingReference"));
                    if (source.DataSource.Size > 32 * 1024 * 1024) throw new InvalidOperationException(Text("ResourceTooLarge"));
                    parser.DeserialiseAudioProject(source);
                    if (editors.GetAllEditors().OfType<AudioEditorViewModel>().Any(open => open.HasUnsavedChanges &&
                        args.String(key).Equals(scopes.GetRequiredService<IAudioEditorStateService>(open).AudioProjectFilePath, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException(Text("UnsavedSource"));
                }
                var project = files.GetEditablePack() as FolderProjectContainer ?? throw new InvalidOperationException(Text("NoProject"));
                FolderProjectPathPolicy.ResolveFilePath(project.ProjectRoot, Path.Combine(args.String("output_folder"), args.String("name") + ".aproj"));
                using var writes = Writes(token);
                var merge = scopes.GetRequiredService<AudioProjectMergerViewModel>(editor); var success = false;
                merge.BaseAudioProjectPath = args.String("base_path"); merge.MergingAudioProjectPath = args.String("other_path");
                merge.MergedAudioProjectName = args.String("name"); merge.OutputDirectoryPath = args.String("output_folder");
                merge.SetCloseAction(() => success = true); merge.MergeAudioProjects();
                return System.Threading.Tasks.Task.FromResult(writes.Result(success, ""));
            }, () => vm.IsBusy ? Text("Busy") : null);
        yield return new AssistantOperation("audio_project.convert", ids.Get(editor), Text("AudioConvert"), Schema(("name", Str(), true),
            ("output_folder", Str("project-relative directory"), true), ("wem_directory", Str("absolute source directory"), true),
            ("bnk_directory", Str("absolute source directory"), true), ("voice_actors", Str(), true), ("soundbanks_xml", Str("optional absolute Wwise SoundbanksInfo.xml"), false)),
            AssistantEffect.Disk, Text("DiskRecovery"), State, async (args, token) =>
            {
                foreach (var key in new[] { "wem_directory", "bnk_directory" })
                    if (!Path.IsPathFullyQualified(args.String(key)) || !Directory.Exists(args.String(key))) throw new ArgumentException(Text("InvalidParameters"));
                var project = files.GetEditablePack() as FolderProjectContainer ?? throw new InvalidOperationException(Text("NoProject"));
                FolderProjectPathPolicy.ResolveFilePath(project.ProjectRoot, Path.Combine(args.String("output_folder"), args.String("name") + ".aproj"));
                using var writes = Writes(token);
                var converter = scopes.GetRequiredService<AudioProjectConverterViewModel>(editor); var success = false;
                converter.SetCloseAction(() => success = true);
                converter.AudioProjectName = args.String("name"); converter.OutputDirectoryPath = args.String("output_folder");
                converter.WemsDirectoryPath = args.String("wem_directory"); converter.BnksDirectoryPath = args.String("bnk_directory");
                converter.VOActorSubstring = args.String("voice_actors"); converter.IsUsingWwiseProject = args["soundbanks_xml"] != null;
                converter.SoundbanksInfoXmlPath = args.OptionalString("soundbanks_xml");
                await converter.InitializeAsync(token); token.ThrowIfCancellationRequested();
                await converter.ProcessAudioProjectConversionAsync(token);
                return writes.Result(success, converter.Status);
            }, () => vm.IsBusy ? Text("Busy") : null);
        yield return new AssistantOperation("audio_project.merge_dialogue_banks", ids.Get(editor), Text("AudioMergeBanks"),
            Schema(("banks", Array(Str("exact Pack resource path"), 100), true), ("suffix", Str(), true)), AssistantEffect.Disk, Text("DiskRecovery"), State,
            async (args, token) =>
            {
                using var writes = Writes(token);
                var merge = scopes.GetRequiredService<DialogueEventMergerViewModel>(editor); var success = false;
                merge.SetCloseAction(() => success = true);
                await merge.InitializeAsync(token); token.ThrowIfCancellationRequested();
                var requested = args["banks"]!.AsArray().Select(p => p!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (requested.Any(p => !merge.ModdedSoundBanks.Any(b => b.FilePath.Equals(p, StringComparison.OrdinalIgnoreCase)))) throw new ArgumentException(Text("MissingReference"));
                foreach (var bank in merge.ModdedSoundBanks) bank.IsChecked = requested.Contains(bank.FilePath);
                merge.SoundBankSuffix = args.String("suffix");
                await merge.GenerateMergedDialogueEventSoundBankAsync(token);
                return writes.Result(success, merge.LoadStatus);
            }, () => vm.IsBusy ? Text("Busy") : null);
        yield return new AssistantOperation("audio_project.compile", ids.Get(editor), Text("AudioCompile"), Schema(("target", Str(values: vm.CompileTargets.Select(t => t.DisplayName).ToArray()), true)),
            AssistantEffect.Destructive, Text("DiskRecovery"), State, async (args, token) =>
            {
                using var writes = Writes(token);
                var option = vm.CompileTargets.Single(t => t.DisplayName == args.String("target"));
                await vm.CompileAudioProject(option.Target, token);
                return writes.Result(vm.AssistantCompileSucceeded == true, vm.CompileStatus);
            }, Unavailable);
    }
}
