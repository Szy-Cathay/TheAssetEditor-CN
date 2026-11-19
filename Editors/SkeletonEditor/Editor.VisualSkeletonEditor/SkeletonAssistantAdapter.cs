using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Editor.VisualSkeletonEditor.SkeletonEditor;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editor.VisualSkeletonEditor;

public sealed class SkeletonAssistantAdapter : IAssistantEditorAdapter
{
    private readonly ConditionalWeakTable<SkeletonEditorViewModel, AssistantUndoJournal<GameSkeleton>> _history = new();
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not SkeletonEditorViewModel vm || vm.AssistantSkeleton == null) yield break;
        var history = _history.GetValue(vm, _ => new(() => vm.AssistantSkeleton!.Clone(), vm.RestoreAssistantSkeleton,
            (a, b) => Revision(SkeletonState(a)) == Revision(SkeletonState(b))));
        JsonNode State() => Node(new { vm.SkeletonName, vm.SourceSkeletonName, vm.HasUnsavedChanges,
            revision = Revision(SkeletonState(vm.AssistantSkeleton!)), bones = vm.AssistantSkeleton!.BoneCount,
            selected = vm.SelectedBone?.BoneName, can_undo = history.CanUndo, can_redo = history.CanRedo });
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> action) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect,
                Text(effect == AssistantEffect.Edit ? "GuardedUndo" : "NoDataChange"), State, action);
        yield return Op("skeleton.bones", "SkeletonBones", Schema(("offset", Num(0, 100000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var skeleton = vm.AssistantSkeleton!;
            return Node(new { total = skeleton.BoneCount, bones = Enumerable.Range(0, skeleton.BoneCount).Skip(args.Integer("offset")).Take(100)
                .Select(i => new { name = skeleton.BoneNames[i], parent = skeleton.GetParentBoneIndex(i) is var p && p >= 0 ? skeleton.BoneNames[p] : null,
                    translation = V(skeleton.Translation[i]), rotation_quaternion = skeleton.Rotation[i].ToString(), scale = skeleton.Scale[i] }) });
        });
        yield return Op("skeleton.edit", "SkeletonEdit", Schema(("bone_name", Str(), true),
            ("action", Str(values: ["transform", "rename", "create_child", "duplicate", "delete", "bake_scale"]), true),
            ("name", Str(), false), ("space", Str(values: ["local", "world"]), false),
            ("translation", Vector(), false), ("rotation_degrees", Vector(), false), ("scale", Num(.001, 1000), false)), AssistantEffect.Edit, args =>
        {
            vm.SelectedBone = vm.Bones.Single(b => b.BoneName == args.String("bone_name"));
            var action = args.String("action");
            if (action == "rename" && (string.IsNullOrWhiteSpace(args.String("name")) || vm.Bones.Any(b => b.BoneName == args.String("name"))))
                throw new ArgumentException(Text("InvalidParameters"));
            history.Apply(() =>
            {
                switch (action)
                {
                    case "transform":
                        vm.ShowBonesAsWorldTransform = args.OptionalString("space", "local") == "world";
                        if (args["translation"] is JsonObject translation) vm.SelectedBoneTranslationOffset.Set(Read(translation));
                        if (args["rotation_degrees"] is JsonObject rotation) vm.SelectedBoneRotationOffset.Set(Read(rotation));
                        if (args["scale"] != null) vm.BoneScale = (float)args.Number("scale");
                        break;
                    case "rename": vm.SelectedBoneName = args.String("name"); break;
                    case "create_child": vm.CreateBoneAction(); break;
                    case "duplicate": vm.DuplicateBoneAction(); break;
                    case "delete": vm.DeleteBoneAction(); break;
                    case "bake_scale": vm.BakeSkeletonAction(); break;
                }
            });
            return State();
        });
        yield return Op("skeleton.focus", "Focus", Schema(("bone_name", Str(), true)), AssistantEffect.Preview, args =>
        { vm.SelectedBone = vm.Bones.Single(b => b.BoneName == args.String("bone_name")); vm.FocusSelectedBoneAction(); return State(); });
        yield return Op("skeleton.undo", "Undo", Schema(), AssistantEffect.Edit, _ => { history.Undo(); return State(); });
        yield return Op("skeleton.redo", "Redo", Schema(), AssistantEffect.Edit, _ => { history.Redo(); return State(); });
    }
    private static JsonNode SkeletonState(GameSkeleton s) => Node(new { s.SkeletonName, s.BoneNames,
        translation = s.Translation.Select(V), rotation = s.Rotation.Select(q => new[] { q.X, q.Y, q.Z, q.W }), scale = s.Scale,
        parents = Enumerable.Range(0, s.BoneCount).Select(s.GetParentBoneIndex) });
    private static float[] V(Vector3 v) => [v.X, v.Y, v.Z];
    private static JsonObject Vector() => Schema(("x", Num(-100000, 100000), true), ("y", Num(-100000, 100000), true), ("z", Num(-100000, 100000), true));
    private static Vector3 Read(JsonObject v) => new((float)v.Number("x"), (float)v.Number("y"), (float)v.Number("z"));
}
