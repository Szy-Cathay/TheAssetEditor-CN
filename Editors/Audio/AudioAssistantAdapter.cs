#nullable enable annotations
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json.Nodes;
using Editors.Audio.AudioExplorer;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Audio;

public sealed class AudioAssistantAdapter : IAssistantEditorAdapter
{
    private static IEnumerable<HircTreeNode> Flatten(IEnumerable<HircTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children)) yield return child;
        }
    }
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not AudioExplorerViewModel vm) yield break;
        JsonNode State() => Node(new { vm.IsLoading, vm.IsExporting, vm.SearchByActionEvent, vm.SearchByDialogueEvent, vm.SearchByHircId, vm.SearchByVOActor,
            languages = vm.SelectedLanguages.Select(x => x.ToString()), selected = vm.SelectedNode == null ? null : ids.Get(vm.SelectedNode),
            selected_event = vm.ExplorerFilter.ExplorerList.SelectedItem == null ? null : ids.Get(vm.ExplorerFilter.ExplorerList.SelectedItem),
            count = vm.ExplorerFilter.ExplorerList.PossibleValues.Count, vm.LoadStatus, readonly_document = true });
        string? Busy() => vm.IsLoading || vm.IsExporting ? Text("Busy") : null;
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, CancellationToken, Task<JsonNode>> run) =>
            new(name, ids.Get(editor), Text(label), schema, effect, Text(effect == AssistantEffect.Disk ? "DiskRecovery" : "NoDataChange"), State, run, Busy);
        yield return Op("audio.search", "AudioSearch", Schema(("query", Str(), true), ("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
            (args, _) => Task.FromResult(Node(vm.ExplorerFilter.ExplorerList.PossibleValues.Where(x => x.DisplayName.Contains(args.String("query"), StringComparison.OrdinalIgnoreCase) || x.Id.ToString().Contains(args.String("query")))
                .Skip(args.Integer("offset")).Take(100).Select(x => new { id = ids.Get(x), x.DisplayName, x.Id, x.PackFile, x.IndexInFile }))));
        yield return Op("audio.filter", "AudioFilter", Schema(("kind", Str("", "action", "dialogue", "hirc", "actor"), true)), AssistantEffect.Preview, async (args, token) =>
        {
            var kind = args.String("kind");
            vm.SearchByActionEvent = kind == "action"; vm.SearchByDialogueEvent = kind == "dialogue";
            vm.SearchByHircId = kind == "hirc"; vm.SearchByVOActor = kind == "actor";
            await vm.WaitAssistantBrowserAsync(token);
            return State();
        });
        yield return Op("audio.select_event", "AudioSelectEvent", Schema(("event", Str(), true)), AssistantEffect.Preview, async (args, token) =>
        {
            vm.ExplorerFilter.ExplorerList.SelectedItem = vm.ExplorerFilter.ExplorerList.PossibleValues.Single(x => ids.Get(x) == args.String("event"));
            await vm.WaitAssistantBrowserAsync(token);
            return State();
        });
        yield return Op("audio.tree", "AudioTree", Schema(("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
            (args, _) => Task.FromResult(Node(Flatten(vm.TreeList).Skip(args.Integer("offset")).Take(100).Select(n => new { id = ids.Get(n), n.DisplayName, n.SourceId,
                n.IsMissingReference, n.MissingHircId, source_bank = n.Hirc?.BnkFilePath, parent = n.Parent == null ? null : ids.Get(n.Parent) }))));
        yield return Op("audio.select_node", "Select", Schema(("node", Str(), true)), AssistantEffect.Preview, (args, _) =>
        { vm.SelectedNode = Flatten(vm.TreeList).Single(n => ids.Get(n) == args.String("node")); return Task.FromResult(State()); });
        yield return Op("audio.playback", "AudioPlayback", Schema(("action", Str("", "play", "stop", "seek"), true), ("seconds", Num(0, 86400, "seconds"), false)), AssistantEffect.Preview,
            async (args, token) =>
            {
                var node = vm.SelectedNode;
                if (args.String("action") == "stop") vm.StopAudio();
                else if (args.String("action") == "seek")
                {
                    if (args["seconds"] == null || args.Number("seconds") > vm.TotalPlaybackSeconds) throw new ArgumentException(Text("InvalidParameters"));
                    vm.PlaybackPositionSeconds = args.Number("seconds");
                }
                else
                {
                    if (node == null || node.IsMissingReference) throw new ArgumentException(Text("MissingReference"));
                    await vm.PlayAudioAsync();
                    if (token.IsCancellationRequested) { vm.StopAudio(); token.ThrowIfCancellationRequested(); }
                    if (!ReferenceEquals(node, vm.SelectedNode) || !vm.IsStopAudioButtonEnabled) throw new InvalidOperationException(Text("NotCompleted"));
                }
                return Node(new { vm.CurrentPlaybackTime, vm.TotalPlaybackTime, vm.IsStopAudioButtonEnabled, vm.LoadStatus });
            });
        yield return Op("audio.export", "AudioExport", Schema(("format", Str("", "wav", "wem"), true), ("scope", Str("", "selected", "branch", "results"), true)), AssistantEffect.Disk,
            async (args, token) =>
            {
                var options = (args.String("scope"), args.String("format")) switch
                {
                    ("selected", "wav") => vm.ExportSelectedAudioWav, ("selected", _) => vm.ExportSelectedAudioWem,
                    ("branch", "wav") => vm.ExportSelectedBranchWav, ("branch", _) => vm.ExportSelectedBranchWem,
                    (_, "wav") => vm.ExportCurrentResultsWav, _ => vm.ExportCurrentResultsWem,
                };
                await vm.ExportAudioAsync(options, token);
                foreach (var path in vm.AssistantExportedPaths)
                {
                    if (args.String("format") == "wav") { using var reader = new NAudio.Wave.WaveFileReader(path); if (reader.Length == 0) throw new InvalidDataException(Text("ReadbackFailed")); }
                    else if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException(Text("ReadbackFailed"));
                }
                return Node(new { outcome = vm.AssistantExportedPaths.Count > 0 && vm.AssistantExportedPaths.Count == vm.AssistantExportExpectedCount ? "completed" :
                    vm.AssistantExportedPaths.Count > 0 ? "partial" : token.IsCancellationRequested ? "cancelled" : "failed",
                    vm.LoadStatus, cancelled = token.IsCancellationRequested, exported = vm.AssistantExportedPaths.Select(p => new { path = p,
                    exists = File.Exists(p), bytes = File.Exists(p) ? new FileInfo(p).Length : 0 }).ToArray(), verified_count = vm.AssistantExportedPaths.Count(p => File.Exists(p) && new FileInfo(p).Length > 0) });
            });
    }
}
