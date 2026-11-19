using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.Saving;
using Microsoft.Xna.Framework;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.AnimatioReTarget;

public sealed class RetargetAssistantAdapter(IPackFileService files) : IAssistantEditorAdapter
{
    private readonly ConditionalWeakTable<AnimationRetargetEditor, AssistantUndoJournal> _histories = new();
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not AnimationRetargetEditor vm) yield break;
        var source = vm.SourceModel.Data;
        var target = vm.TargetModel.Data;
        var generated = vm.SceneObjects.Single(x => x.Data.Id == AnimationRetargetIds.Generated).Data;
        JsonNode SettingsState() => Node(new { source = source.SkeletonName.Value, target = target.SkeletonName.Value,
            vm.Settings.SkeletonScale, vm.Settings.AnimationSpeedMult, vm.Settings.ApplyRelativeScale, vm.Settings.ZeroUnmappedBones,
            bones = vm.BoneManager.FlatBoneList.Select(b => new { b.BoneIndex, b.BoneName, b.MappedIndex, b.HasMapping, b.BoneLengthMult,
                rotation = V(b.RotationOffset.GetAsVector3()), translation = V(b.TranslationOffset.GetAsVector3()), b.FreezeRotation, b.FreezeTranslation,
                b.ApplyRotation, b.ApplyTranslation, b.ForceSnapToWorld }) });
        void Restore(byte[] bytes)
        {
            var state = JsonNode.Parse(bytes)!.AsObject();
            if (state.String("source") != source.SkeletonName.Value || state.String("target") != target.SkeletonName.Value) throw new InvalidOperationException(Text("StaleTarget"));
            vm.Settings.SkeletonScale = (float)state.Number("SkeletonScale"); vm.Settings.AnimationSpeedMult = (float)state.Number("AnimationSpeedMult");
            vm.Settings.ApplyRelativeScale = state.Boolean("ApplyRelativeScale"); vm.Settings.ZeroUnmappedBones = state.Boolean("ZeroUnmappedBones");
            foreach (var item in state["bones"]!.AsArray().Cast<JsonObject>())
            {
                var b = vm.BoneManager.FlatBoneList.Single(x => x.BoneIndex == item.Integer("BoneIndex"));
                b.MappedIndex = item.Integer("MappedIndex"); b.HasMapping = item.Boolean("HasMapping"); b.BoneLengthMult = (float)item.Number("BoneLengthMult");
                b.RotationOffset.Set(ReadV(item["rotation"]!)); b.TranslationOffset.Set(ReadV(item["translation"]!));
                b.FreezeRotation = item.Boolean("FreezeRotation"); b.FreezeTranslation = item.Boolean("FreezeTranslation");
                b.ApplyRotation = item.Boolean("ApplyRotation"); b.ApplyTranslation = item.Boolean("ApplyTranslation"); b.ForceSnapToWorld = item.Boolean("ForceSnapToWorld");
            }
            vm.BoneManager.ApplyAssistantMappings(vm.BoneManager.FlatBoneList.Where(b => b.HasMapping).ToDictionary(b => b.BoneIndex, b => b.MappedIndex));
        }
        var history = _histories.GetValue(vm, _ => new(() => Encoding.UTF8.GetBytes(SettingsState().ToJsonString()), Restore));
        JsonNode State() => Node(new { settings = SettingsState(), source_bones = source.Skeleton?.BoneNames,
            generated = generated.AnimationClip == null ? null : ids.Get(generated.AnimationClip),
            generated_frames = generated.Player.FrameCount, vm.SaveManager.Settings.SavePrefix, vm.SaveManager.Settings.AnimationFormat,
            can_generate = vm.CanUpdateAnimation(out var error), generation_error = error });
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run, Func<string?>? unavailable = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect, Text(effect == AssistantEffect.Edit ? "GuardedUndo" : effect == AssistantEffect.Disk ? "DiskRecovery" : "NoDataChange"), State, run, unavailable);
        yield return Op("retarget.settings", "RetargetSettings", Schema(("scale", Num(.001, 1000), false), ("speed", Num(.001, 1000), false),
            ("relative_scale", Bool(), false), ("zero_unmapped", Bool(), false)), AssistantEffect.Edit, args =>
        {
            history.Apply(() =>
            {
                if (args["scale"] != null) vm.Settings.SkeletonScale = (float)args.Number("scale");
                if (args["speed"] != null) vm.Settings.AnimationSpeedMult = (float)args.Number("speed");
                if (args["relative_scale"] != null) vm.Settings.ApplyRelativeScale = args.Boolean("relative_scale");
                if (args["zero_unmapped"] != null) vm.Settings.ZeroUnmappedBones = args.Boolean("zero_unmapped");
            });
            return State();
        });
        yield return Op("retarget.mapping", "RetargetMapping", Schema(("mappings", Array(Schema(("target_bone", Str(), true), ("source_bone", Str("empty means unmapped"), true)), 512), true)), AssistantEffect.Edit, args =>
        {
            if (source.Skeleton == null || target.Skeleton == null) throw new InvalidOperationException(Text("PreviewNotReady"));
            var mappings = ((JsonArray)args["mappings"]!).Cast<JsonObject>().Select(item =>
            {
                var bone = vm.BoneManager.FlatBoneList.Single(x => x.BoneName == item.String("target_bone"));
                var sourceName = item.String("source_bone");
                var index = sourceName.Length == 0 ? -1 : source.Skeleton.BoneNames.IndexOf(sourceName);
                if (index < 0 && sourceName.Length > 0) throw new ArgumentException(Text("TargetGone"));
                return (bone, index);
            }).ToArray();
            if (mappings.Select(m => m.bone).Distinct().Count() != mappings.Length) throw new ArgumentException(Text("InvalidParameters"));
            var merged = vm.BoneManager.FlatBoneList.Where(b => b.HasMapping).ToDictionary(b => b.BoneIndex, b => b.MappedIndex);
            foreach (var (bone, index) in mappings) { if (index >= 0) merged[bone.BoneIndex] = index; else merged.Remove(bone.BoneIndex); }
            history.Apply(() => vm.BoneManager.ApplyAssistantMappings(merged));
            return State();
        });
        var vectorSchema = Schema(("x", Num(-100000, 100000), true), ("y", Num(-100000, 100000), true), ("z", Num(-100000, 100000), true));
        yield return Op("retarget.bone_settings", "RetargetBoneSettings", Schema(("bone", Str(), true), ("translation", vectorSchema, false),
            ("rotation", (JsonObject)vectorSchema.DeepClone(), false), ("length_multiplier", Num(.001, 1000), false),
            ("freeze_rotation", Bool(), false), ("freeze_translation", Bool(), false), ("apply_rotation", Bool(), false),
            ("apply_translation", Bool(), false), ("snap_to_world", Bool(), false)), AssistantEffect.Edit, args =>
        {
            var bone = vm.BoneManager.FlatBoneList.Single(b => b.BoneName == args.String("bone"));
            Vector3 Read(JsonObject v) => new((float)v.Number("x"), (float)v.Number("y"), (float)v.Number("z"));
            history.Apply(() =>
            {
                if (args["translation"] is JsonObject translation) bone.TranslationOffset.Set(Read(translation));
                if (args["rotation"] is JsonObject rotation) bone.RotationOffset.Set(Read(rotation));
                if (args["length_multiplier"] != null) bone.BoneLengthMult = (float)args.Number("length_multiplier");
                if (args["freeze_rotation"] != null) bone.FreezeRotation = args.Boolean("freeze_rotation");
                if (args["freeze_translation"] != null) bone.FreezeTranslation = args.Boolean("freeze_translation");
                if (args["apply_rotation"] != null) bone.ApplyRotation = args.Boolean("apply_rotation");
                if (args["apply_translation"] != null) bone.ApplyTranslation = args.Boolean("apply_translation");
                if (args["snap_to_world"] != null) bone.ForceSnapToWorld = args.Boolean("snap_to_world");
            });
            return State();
        });
        yield return Op("retarget.automap", "RetargetAutomap", Schema(), AssistantEffect.Edit, _ => { history.Apply(vm.BoneManager.ApplyDefaultMapping); return State(); },
            () => source.Skeleton != null && target.Skeleton != null ? null : Text("PreviewNotReady"));
        yield return Op("retarget.undo_settings", "Undo", Schema(), AssistantEffect.Edit, _ => { history.Undo(); return State(); });
        yield return Op("retarget.redo_settings", "Redo", Schema(), AssistantEffect.Edit, _ => { history.Redo(); return State(); });
        yield return Op("retarget.generate", "RetargetGenerate", Schema(), AssistantEffect.Preview, _ =>
        {
            var before = generated.AnimationClip;
            vm.UpdateAnimation();
            if (ReferenceEquals(before, generated.AnimationClip)) throw new InvalidOperationException(Text("NotCompleted"));
            var native = generated.AnimationClip.ConvertToFileFormat(generated.Skeleton);
            AnimationFile.Create(new global::Shared.ByteParsing.ByteChunk(AnimationFile.ConvertToBytes(native)));
            return State();
        }, () => vm.CanUpdateAnimation(out var error) ? null : error);
        yield return Op("retarget.save_generated", "RetargetSave", Schema(("prefix", Str(), true), ("version", Num(5, 7, integer: true), true)), AssistantEffect.Disk, args =>
        {
            var prefix = args.String("prefix");
            if (prefix.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) throw new ArgumentException(Text("InvalidParameters"));
            vm.SaveManager.Settings.SavePrefix = prefix;
            vm.SaveManager.Settings.AnimationFormat = (uint)args.Integer("version");
            var saved = vm.SaveManager.SaveAnimation(true) ?? throw new OperationCanceledException();
            var readback = AnimationFile.Create(saved);
            return Node(new { saved = files.GetFullPath(saved), version = readback.Header.Version, bytes = saved.DataSource.Size, generated_unchanged = true });
        }, () => generated.AnimationClip == null ? Text("PreviewNotReady") : null);
        yield return Op("retarget.batch_candidates", "RetargetBatchCandidates", Schema(("query", Str(), false), ("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
            args => Node(vm.SaveManager.GetBatchCandidates().Where(a => a.AnimationFile.Contains(args.OptionalString("query"), StringComparison.OrdinalIgnoreCase))
                .Skip(args.Integer("offset")).Take(100).Select(a => new { container = ids.Get(a.Container), path = a.AnimationFile })));
        yield return new AssistantOperation("retarget.batch_export", ids.Get(editor), Text("RetargetBatchExport"), Schema(("animations", Array(Schema(
            ("container", Str(), true), ("path", Str(), true)), 100), true), ("path_mode", Str("", "SourcePath", "SelectedFolder"), true), ("folder", Str(), false)),
            AssistantEffect.Disk, Text("DiskRecovery"), State, async (args, token) =>
            {
                var candidates = vm.SaveManager.GetBatchCandidates();
                var selected = args["animations"]!.AsArray().Cast<JsonObject>().Select(a => candidates.Single(c => ids.Get(c.Container) == a.String("container") && c.AnimationFile == a.String("path"))).ToArray();
                if (selected.Distinct().Count() != selected.Length) throw new ArgumentException(Text("InvalidParameters"));
                var project = files.GetEditablePack() ?? throw new InvalidOperationException(Text("NoProject"));
                var expected = SettingsState();
                var result = await vm.SaveManager.ExecuteBatchAsync(new(selected, Enum.Parse<BatchRetargetPathMode>(args.String("path_mode")), args.OptionalString("folder"), false), null, token,
                    () => JsonNode.DeepEquals(expected, SettingsState()));
                foreach (var item in result.Items.Where(i => i.Status == BatchAnimationRetargetItemStatus.Success))
                {
                    if (!project.FileList.TryGetValue(item.OutputPath!.ToLowerInvariant(), out var file)) throw new InvalidOperationException(Text("ReadbackFailed"));
                    AnimationFile.Create(file);
                }
                return Node(new { outcome = result.SuccessCount == selected.Length ? "completed" : result.SuccessCount > 0 ? "partial" : token.IsCancellationRequested ? "cancelled" : "failed", result.Items,
                    result.SuccessCount, result.SkippedCount, result.FailureCount, result.NotProcessedCount, overwrite = false });
            }, () => vm.SaveManager.CanBatchRetarget(out var error) ? null : error);
    }
    private static float[] V(Vector3 v) => [v.X, v.Y, v.Z];
    private static Vector3 ReadV(JsonNode v) => new(v[0]!.GetValue<float>(), v[1]!.GetValue<float>(), v[2]!.GetValue<float>());
}
