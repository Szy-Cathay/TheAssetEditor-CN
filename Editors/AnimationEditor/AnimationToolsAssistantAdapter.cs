using System.Text.Json.Nodes;
using AnimationEditor.CampaignAnimationCreator;
using AnimationEditor.CampaignAnimationCreator.Commands;
using AnimationEditor.MountAnimationCreator;
using Editors.Shared.Core.Common;
using Microsoft.Xna.Framework;
using Shared.ByteParsing;
using Shared.Core.DependencyInjection;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.Core.PackFiles.Models;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.AnimationVisualEditors;

public sealed class AnimationToolsAssistantAdapter(IPackFileService files) : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is CampaignAnimationCreatorViewModel campaign)
        {
            JsonNode State() => Node(new { bones = campaign.ModelBoneList.PossibleValues.Select(b => b.BoneName), selected = campaign.ModelBoneList.SelectedItem?.BoneName,
                clip = campaign.SceneObjects.FirstOrDefault()?.Data.AnimationClip is { } clip ? ids.Get(clip) : null });
            yield return AssistantOperation.Sync("campaign.convert", ids.Get(editor), Text("CampaignConvert"), Schema(("bone", Str(), true)), AssistantEffect.Preview, Text("ReloadPreview"), State, args =>
            {
                campaign.ModelBoneList.SelectedItem = campaign.ModelBoneList.PossibleValues.Single(b => b.BoneName == args.String("bone"));
                var scene = campaign.SceneObjects.First().Data; var oldClip = scene.AnimationClip;
                campaign.Convert();
                if (scene.AnimationClip == null || ReferenceEquals(oldClip, scene.AnimationClip)) throw new InvalidOperationException(Text("NotCompleted"));
                AnimationFile.Create(new ByteChunk(AnimationFile.ConvertToBytes(scene.AnimationClip.ConvertToFileFormat(scene.Skeleton))));
                return State();
            });
            yield return AssistantOperation.Sync("campaign.save", ids.Get(editor), Text("CampaignSave"), Schema(), AssistantEffect.Disk, Text("DiskRecovery"), State, _ =>
            {
                campaign.SaveAnimation();
                var saved = campaign.AssistantSavedFile;
                if (saved == null) throw new OperationCanceledException();
                var read = AnimationFile.Create(saved);
                return Node(new { path = files.GetFullPath(saved), version = read.Header.Version, bytes = saved.DataSource.Size });
            });
        }
        if (editor is not MountAnimationCreatorViewModel vm) yield break;
        JsonNode MountState() => Node(new { can_generate = vm.CanPreview.Value, can_save = vm.CanSave.Value, can_add = vm.CanAddToFragment.Value,
            can_batch = vm.CanBatchProcess.Value, status = vm.Status.Value, attachment = vm.SelectedVertexesText.Value,
            bone = vm.SelectedRiderBone.SelectedItem?.BoneName, saved = vm.AssistantLastSavedPath,
            mount_table = vm.MountLinkController.AnimationSetForMount.SelectedItem?.FullPath,
            rider_table = vm.MountLinkController.AnimationSetForRider.SelectedItem?.FullPath,
            output_table = vm.ActiveOutputFragment.SelectedItem?.FullPath,
            mount_slot = vm.MountLinkController.SelectedMountTag.SelectedItem?.SlotName,
            rider_slot = vm.MountLinkController.SelectedRiderTag.SelectedItem?.SlotName,
            output_slot = vm.ActiveFragmentSlot.SelectedItem?.SlotName,
            settings = new { fit = vm.AnimationSettings.FitAnimation, loops = vm.AnimationSettings.LoopCounter.Value, scale = vm.AnimationSettings.Scale.Value,
                translation = V(vm.AnimationSettings.Translation.GetAsVector3()), rotation_degrees = V(vm.AnimationSettings.Rotation.GetAsVector3()),
                vm.AnimationSettings.KeepRiderRotation, vm.AnimationSettings.IsRootNodeAnimation },
            generated = vm.AssistantGeneratedClip == null ? null : ids.Get(vm.AssistantGeneratedClip),
            scenes = vm.SceneObjects.Select(s => new { id = ids.Get(s.Data), s.Data.Description, mesh = s.Data.MeshName.Value, skeleton = s.Data.SkeletonName.Value, animation = s.Data.AnimationName.Value }) });
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run, Func<string?>? unavailable = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect, Text(effect == AssistantEffect.Disk ? "DiskRecovery" : "ReloadPreview"), MountState, run, unavailable);
        yield return Op("mount.attachment_choices", "MountChoices", Schema(), AssistantEffect.Query, _ => Node(new
        {
            meshes = vm.AssistantMountMeshes.Select(m => new { id = ids.Get(m), m.Name, vertices = m.Geometry.VertexCount(), weights = m.Geometry.WeightCount }),
            rider_bones = vm.SelectedRiderBone.PossibleValues.Select(b => b.BoneName)
        }));
        yield return Op("mount.attach", "MountAttach", Schema(("mesh", Str(), true), ("vertex", Num(0, 10000000, "zero-based vertex", true), true), ("rider_bone", Str(), true)), AssistantEffect.Preview, args =>
        {
            var mesh = vm.AssistantMountMeshes.Single(m => ids.Get(m) == args.String("mesh"));
            var bone = vm.SelectedRiderBone.PossibleValues.Single(b => b.BoneName == args.String("rider_bone"));
            if (args.Integer("vertex") >= mesh.Geometry.VertexCount()) throw new ArgumentException(Text("InvalidParameters"));
            vm.SelectedRiderBone.SelectedItem = bone; vm.SetAttachment(mesh, [args.Integer("vertex")]); return MountState();
        });
        yield return Op("mount.settings", "MountSettings", Schema(("scale", Num(.001, 1000), false), ("loops", Num(1, 1000, integer: true), false),
            ("fit", Bool(), false), ("keep_rotation", Bool(), false), ("root_motion", Bool(), false), ("translation", Vector(), false), ("rotation_degrees", Vector(), false)), AssistantEffect.Preview, args =>
        {
            var s = vm.AnimationSettings;
            if (args["scale"] != null) s.Scale.Value = args.Number("scale");
            if (args["loops"] != null) s.LoopCounter.Value = args.Integer("loops");
            if (args["fit"] != null) s.FitAnimation = args.Boolean("fit");
            if (args["keep_rotation"] != null) s.KeepRiderRotation = args.Boolean("keep_rotation");
            if (args["root_motion"] != null) s.IsRootNodeAnimation = args.Boolean("root_motion");
            if (args["translation"] is JsonObject t) s.Translation.Set(Read(t));
            if (args["rotation_degrees"] is JsonObject r) s.Rotation.Set(Read(r));
            return MountState();
        });
        yield return Op("mount.fragments", "MountFragments", Schema(("side", Str("", "mount", "rider", "output"), true), ("offset", Num(0, 100000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var tables = args.String("side") switch { "mount" => vm.MountLinkController.AnimationSetForMount, "rider" => vm.MountLinkController.AnimationSetForRider, _ => vm.ActiveOutputFragment };
            var slots = args.String("side") switch { "mount" => vm.MountLinkController.SelectedMountTag, "rider" => vm.MountLinkController.SelectedRiderTag, _ => vm.ActiveFragmentSlot };
            return Node(new { tables = tables.PossibleValues.Skip(args.Integer("offset")).Take(100).Select(t => new { id = ids.Get(t), t.FullPath, t.Name }),
                slots = slots.PossibleValues.Skip(args.Integer("offset")).Take(100).Select(s => new { id = ids.Get(s), s.SlotName, s.AnimationFile }) });
        });
        yield return Op("mount.select_fragment", "Select", Schema(("side", Str("", "mount", "rider", "output"), true), ("table", Str(), false), ("slot", Str(), false)), AssistantEffect.Preview, args =>
        {
            var tables = args.String("side") switch { "mount" => vm.MountLinkController.AnimationSetForMount, "rider" => vm.MountLinkController.AnimationSetForRider, _ => vm.ActiveOutputFragment };
            var slots = args.String("side") switch { "mount" => vm.MountLinkController.SelectedMountTag, "rider" => vm.MountLinkController.SelectedRiderTag, _ => vm.ActiveFragmentSlot };
            if (args["table"] != null) tables.SelectedItem = tables.PossibleValues.Single(t => ids.Get(t) == args.String("table"));
            if (args["slot"] != null) slots.SelectedItem = slots.PossibleValues.Single(s => ids.Get(s) == args.String("slot"));
            return MountState();
        });
        yield return Op("mount.generate", "MountGenerate", Schema(), AssistantEffect.Preview, _ =>
        {
            var before = vm.AssistantGeneratedClip; vm.CreateMountAnimationAction();
            if (ReferenceEquals(before, vm.AssistantGeneratedClip) || vm.AssistantGeneratedClip == null || vm.AssistantGeneratedSkeleton == null) throw new InvalidOperationException(Text("NotCompleted"));
            AnimationFile.Create(new ByteChunk(AnimationFile.ConvertToBytes(vm.AssistantGeneratedClip.ConvertToFileFormat(vm.AssistantGeneratedSkeleton))));
            return MountState();
        }, () => vm.CanPreview.Value ? null : Text("PreviewNotReady"));
        yield return Op("mount.save", "MountSave", Schema(("prefix", Str(), true), ("version", Num(5, 7, integer: true), true), ("add_to_fragment", Bool(), false)), AssistantEffect.Disk, args =>
        {
            if (args.String("prefix").IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || args.Boolean("add_to_fragment") && !vm.CanAddToFragment.Value) throw new ArgumentException(Text("InvalidParameters"));
            vm.SavePrefixText.Value = args.String("prefix"); vm.SelectedAnimationOutputFormat.Value = (uint)args.Integer("version"); vm.EnsureUniqeFileName.Value = true;
            if (args.Boolean("add_to_fragment")) vm.AddAnimationToFragment(); else vm.SaveCurrentAnimationAction();
            var path = vm.AssistantLastSavedPath ?? throw new OperationCanceledException();
            var saved = files.FindFile(path, files.GetEditablePack()) ?? throw new InvalidOperationException(Text("NotCompleted"));
            var read = AnimationFile.Create(saved);
            return Node(new { path, version = read.Header.Version, bytes = saved.DataSource.Size, fragment_updated = args.Boolean("add_to_fragment") });
        }, () => vm.CanSave.Value ? null : Text("PreviewNotReady"));
        yield return new AssistantOperation("mount.batch_export", ids.Get(editor), Text("MountBatch"), Schema(("pack_name", Str(), true), ("fragment_name", Str(), true),
            ("prefix", Str(), true), ("version", Num(5, 7, integer: true), true)), AssistantEffect.Disk, Text("DiskRecovery"), MountState, async (args, token) =>
            {
                foreach (var key in new[] { "pack_name", "fragment_name", "prefix" })
                    if (args.String(key).IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || key != "prefix" && string.IsNullOrWhiteSpace(args.String(key)))
                        throw new ArgumentException(Text("InvalidParameters"));
                vm.SelectedAnimationOutputFormat.Value = (uint)args.Integer("version"); vm.EnsureUniqeFileName.Value = true;
                var expected = MountState();
                var project = files.GetEditablePack() as FolderProjectContainer ?? throw new InvalidOperationException(Text("NoProject"));
                var revision = project.CurrentRevision;
                var result = await vm.ProcessAssistantBatch(new() { AnimPackName = args.String("pack_name"), FragmentName = args.String("fragment_name"), SavePrefix = args.String("prefix") },
                    () => JsonNode.DeepEquals(expected, MountState()) && project.CurrentRevision == revision, token);
                foreach (var path in result.Paths)
                {
                    var file = files.FindFile(path, project) ?? throw new InvalidOperationException(Text("ReadbackFailed"));
                    if (path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase)) AnimationFile.Create(file);
                    if (path.EndsWith(".animpack", StringComparison.OrdinalIgnoreCase)) AnimationPackSerializer.Load(file, files);
                }
                return Node(new { outcome = result.Success ? "completed" : "failed", outputs = result.Paths, diagnostics = result.Diagnostics.Errors, overwrite = false });
            }, () => vm.CanBatchProcess.Value ? null : Text("PreviewNotReady"));
    }
    private static float[] V(Vector3 v) => [v.X, v.Y, v.Z];
    private static JsonObject Vector() => Schema(("x", Num(-100000, 100000), true), ("y", Num(-100000, 100000), true), ("z", Num(-100000, 100000), true));
    private static Vector3 Read(JsonObject v) => new((float)v.Number("x"), (float)v.Number("y"), (float)v.Number("z"));
}
