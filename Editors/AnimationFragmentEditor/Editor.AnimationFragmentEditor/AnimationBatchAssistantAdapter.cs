using System.Text.Json.Nodes;
using System.IO;
using CommonControls.Editors.AnimationBatchExporter;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.AnimationFragmentEditor;

public sealed class AnimationBatchAssistantAdapter(IPackFileService files) : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not AnimationBatchExportViewModel vm) yield break;
        JsonNode State() => Node(new { busy = vm.IsBusy.Value, version = vm.SelectedOutputFormat.Value, status = vm.Status.Value,
            sources = vm.PackfileList.Select(p => new { id = ids.Get(p.Container), p.Container.Name, enabled = p.Process.Value }),
            count = vm.Animations.Count, selection = vm.Animations.Where(a => a.IsSelected.Value).Select(a => new { id = ids.Get(a), a.Path }),
            output = files.GetEditablePack() is { } output ? ids.Get(output) : null, written = vm.AssistantWrittenPaths });
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect, Text("DiskRecovery"), State, run, () => vm.IsBusy.Value ? Text("Busy") : null);
        yield return Op("animation_batch.source", "BatchSource", Schema(("container", Str(), true), ("enabled", Bool(), true)), AssistantEffect.Preview, args =>
        { vm.PackfileList.Single(p => ids.Get(p.Container) == args.String("container")).Process.Value = args.Boolean("enabled"); return State(); });
        yield return Op("animation_batch.list", "BatchList", Schema(("query", Str(), false), ("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query, args =>
            Node(vm.Animations.Where(a => a.Path.Contains(args.OptionalString("query"), StringComparison.OrdinalIgnoreCase)).Skip(args.Integer("offset")).Take(100)
                .Select(a => new { id = ids.Get(a), a.Path, a.SourceName, selected = a.IsSelected.Value })));
        yield return Op("animation_batch.select", "Select", Schema(("animations", Array(Str(), 500), true), ("version", Num(5, 8, integer: true), true)), AssistantEffect.Preview, args =>
        {
            var selected = args["animations"]!.AsArray().Select(a => vm.Animations.Single(x => ids.Get(x) == a!.GetValue<string>())).ToHashSet();
            foreach (var item in vm.Animations) item.IsSelected.Value = selected.Contains(item);
            vm.SelectedOutputFormat.Value = (uint)args.Integer("version");
            return State();
        });
        yield return new AssistantOperation("animation_batch.export", ids.Get(editor), Text("BatchExport"), Schema(), AssistantEffect.Disk, Text("DiskRecovery"), State,
            async (_, token) =>
            {
                var output = files.GetEditablePack();
                var errors = await vm.Process(token);
                if (errors == null) throw new OperationCanceledException();
                var paths = vm.AssistantWrittenPaths;
                foreach (var path in paths)
                {
                    var file = files.FindFile(path, output!) ?? throw new InvalidDataException(path);
                    if (path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) AnimationFile.Create(file);
                }
                return Node(new { outcome = errors.Errors.Any(e => e.IsError) ? paths.Count > 0 ? "partial" : "failed" : "completed",
                    paths, diagnostics = errors.Errors, status = vm.Status.Value });
            }, () => vm.CanExport.Value ? null : Text("Unavailable"));
    }
}
