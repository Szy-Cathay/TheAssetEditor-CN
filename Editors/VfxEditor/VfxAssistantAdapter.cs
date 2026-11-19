using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using Shared.Core.PackFiles;
using GameWorld.Core.Utility;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.VfxEditor;

public sealed class VfxAssistantAdapter(IPackFileService? files = null) : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not VfxEditorViewModel vm) yield break;
        JsonNode State() => Node(new
        {
            sha256 = Convert.ToHexString(SHA256.HashData(vm.AssistantDocumentBytes)), vm.HasDocument, vm.HasUnsavedChanges, vm.HasErrors,
            selected_layer = vm.SelectedLayer == null ? null : ids.Get(vm.SelectedLayer.Model.Element),
            layers = vm.Layers.Select(layer => new { id = ids.Get(layer.Model.Element), layer.Label, layer.Model.Kind, layer.Model.Reference, layer.IsIncludedInPreview }).ToArray(),
            vm.PreviewQuality, vm.ScenePreviewStatus, diagnostics = vm.ScenePreviewDiagnostics.Take(30).ToArray(),
        });
        AssistantOperation Op(string name, string description, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> execute, Func<string?>? unavailable = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(description), schema, effect, Text("EditorRecovery"), State, execute, unavailable);
        VfxLayerViewModel Layer(string id) => vm.Layers.SingleOrDefault(x => ids.Get(x.Model.Element) == id) ?? throw new ArgumentException(Text("TargetGone"));
        IEnumerable<VfxValueViewModel> Values(VfxFieldViewModel field) => field.Values.Concat(field.Curve?.Channels.SelectMany(channel => channel.Points.SelectMany(point => new[] { point.Time, point.Value })) ?? []);
        yield return Op("vfx.fields", "VfxFields", Schema(("layer", Str(), true), ("query", Str(), false), ("offset", Num(0, 100000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var fields = Layer(args.String("layer")).Fields.Where(field => field.SearchText.Contains(args["query"]?.GetValue<string>() ?? "", StringComparison.OrdinalIgnoreCase)).ToArray();
            return Node(new { total = fields.Length, fields = fields.Skip(args.Integer("offset")).Take(25).Select(field => new
            {
                id = ids.Get(field.Model.Element), field.Label, field.Model.Name, field.Context, field.Description, field.AdjustmentHint,
                field.IsOpaque, field.RawValue, field.Error, field.PreviewStateText,
                values = field.Values.Select(value => new { id = ids.Get(value), value.Label, value.Text, value.IsNumber, value.IsBoolean, choices = value.Choices }).ToArray(),
                channels = field.Curve?.Channels.Select(channel => new { channel.Model.Name, points = channel.Points.Select(point => new { time_id = ids.Get(point.Time), value_id = ids.Get(point.Value), time = point.Time.Text, value = point.Value.Text }).ToArray() }).ToArray(),
            }).ToArray() });
        });
        yield return Op("vfx.values.set", "VfxSet", Schema(("edits", Array(Schema(("value_id", Str(), true), ("text", Str(), true))), true)), AssistantEffect.Edit, args =>
        {
            var values = vm.Layers.SelectMany(layer => layer.Fields).SelectMany(Values).ToDictionary(value => ids.Get(value));
            var edits = args["edits"]!.AsArray().Select(node =>
            {
                var item = node!.AsObject();
                if (!values.TryGetValue(item.String("value_id"), out var value)) throw new ArgumentException(Text("TargetGone"));
                var text = item.String("text");
                if (!value.Accepts(text)) throw new ArgumentException(Text("InvalidParameters"));
                return (Value: value, Before: value.Text, After: text);
            }).ToArray();
            if (edits.Select(x => x.Value).Distinct().Count() != edits.Length) throw new ArgumentException(Text("InvalidParameters"));
            if (files != null)
                foreach (var field in vm.Layers.SelectMany(layer => layer.Fields).Where(f => f.CanBrowse))
                    foreach (var edit in edits.Where(e => field.Values.Contains(e.Value) && e.After.Length > 0 && e.After != e.Before))
                        if ((field.Model.Name is "diffuse" or "normal" ? TexturePathResolver.FindTextureFile(files, edit.After, out _) : files.FindFile(edit.After)) == null)
                            throw new ArgumentException(Text("MissingReference") + ": " + edit.After);
            vm.ApplyAssistantGroup(() => { foreach (var edit in edits) edit.Value.Text = edit.After; });
            return Node(new { changes = edits.Select(x => new { id = ids.Get(x.Value), before = x.Before, after = x.Value.Text }).ToArray(), saved = false });
        }, () => !vm.HasDocument || vm.HasErrors ? Text("InvalidDocument") : null);
        yield return Op("vfx.select", "Select", Schema(("layer", Str(), true)), AssistantEffect.Preview, args => { vm.SelectedLayer = Layer(args.String("layer")); return State(); });
        yield return Op("vfx.preview_quality", "VfxPreviewQuality", Schema(("quality", Str("", vm.PreviewQualities.Select(q => q.Key).ToArray()), true)), AssistantEffect.Preview,
            args => { vm.PreviewQuality = args.String("quality"); return State(); });
        yield return Op("vfx.preview_layers", "VfxPreviewLayers", Schema(("layers", new JsonObject { ["type"] = "array", ["items"] = Str(), ["maxItems"] = 100 }, true)), AssistantEffect.Preview, args =>
        {
            var layers = args["layers"]!.AsArray().Select(x => Layer(x!.GetValue<string>())).ToHashSet();
            foreach (var layer in vm.Layers.Where(x => x.CanPreview)) layer.IsIncludedInPreview = layers.Contains(layer);
            return State();
        });
        foreach (var entry in new[] { ("undo", "Undo", vm.UndoCommand), ("redo", "Redo", vm.RedoCommand), ("duplicate_layer", "DuplicateLayer", vm.DuplicateLayerCommand), ("remove_layer", "RemoveLayer", vm.RemoveLayerCommand) })
            yield return Op("vfx." + entry.Item1, entry.Item2, Schema(), AssistantEffect.Edit, _ => { entry.Item3.Execute(null); return State(); },
                () => entry.Item3.CanExecute(null) ? null : Text("Unavailable"));
        yield return Op("vfx.playback", "Playback", Schema(("action", Str("", "play", "pause", "restart", "seek", "loop"), true),
            ("seconds", Num(0, 600, "seconds"), false), ("loop", Bool(), false)), AssistantEffect.Preview, args =>
        {
            var player = vm.Playback;
            var action = args.String("action");
            if (action == "play" && !player.IsPlaying.Value || action == "pause" && player.IsPlaying.Value) player.ToggleAnimationPausePlay();
            if (action == "restart") player.SetAnimationFirstFrame();
            if (action == "seek")
            {
                if (args["seconds"] == null || args.Number("seconds") > player.SelectedAnimationMaxTime.Value) throw new ArgumentException(Text("InvalidParameters"));
                player.PlaybackPositionSeconds = (float)args.Number("seconds");
            }
            if (action == "loop") { if (args["loop"] == null) throw new ArgumentException(Text("InvalidParameters")); player.LoopAnimation.Value = args.Boolean("loop"); }
            return Node(new { seconds = player.PlaybackPositionSeconds, playing = player.IsPlaying.Value, loop = player.LoopAnimation.Value, vm.ScenePreviewStatus });
        }, () => vm.Playback.SelectedAnimationMaxTime.Value > 0 ? null : Text("PreviewNotReady"));
    }
}
