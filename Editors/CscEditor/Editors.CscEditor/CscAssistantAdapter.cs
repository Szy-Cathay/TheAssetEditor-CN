using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Windows.Input;
using Editors.CscEditor.Data;
using Editors.CscEditor.ViewModels;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.CscEditor;

public sealed class CscAssistantAdapter : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not CscEditorViewModel vm) yield break;
        JsonNode State() => Node(new { vm.HasUnsavedChanges, vm.ManifestEditable, vm.CanUndo, vm.CanRedo,
            sha256 = Convert.ToHexString(SHA256.HashData(vm.AssistantBytes())),
            selected = vm.SelectedElement == null ? null : ids.Get(vm.SelectedElement.Element),
            node_count = vm.SceneData?.Elements.Count ?? 0 });
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run, Func<string?>? available = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect, Text(effect == AssistantEffect.Edit ? "EditorUndo" : "NoDataChange"), State, run,
                () => vm.SceneData == null ? Text("TargetGone") : available?.Invoke());
        CscElementViewModel Find(string id) => vm.SceneData!.Elements.Select(vm.FindViewModel).SingleOrDefault(n => n != null && ids.Get(n.Element) == id)
            ?? throw new ArgumentException(Text("TargetGone"));
        yield return Op("csc.nodes", "CscNodes", Schema(("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query, args =>
            Node(vm.SceneData!.Elements.Skip(args.Integer("offset")).Take(50).Select(e => new { id = ids.Get(e), e.Id, kind = e.Kind.ToString(), e.AssetPath, e.Begin, e.End,
                parent = e.Parent == null ? null : ids.Get(e.Parent), editable = vm.FindViewModel(e)?.IsEditable, properties = Properties(vm.FindViewModel(e)!).Select(p => new { p.Name, schema = p.Schema, value = p.Read() }) })));
        yield return Op("csc.select", "Select", Schema(("node", Str(), true)), AssistantEffect.Preview, args =>
        { vm.SelectedElement = Find(args.String("node")); return State(); });
        yield return Op("csc.properties.set", "CscSet", Schema(("node", Str(), true), ("changes", Array(Schema(("field", Str(), true), ("value_json", Str(), true)), 60), true)), AssistantEffect.Edit, args =>
        {
            var target = Find(args.String("node"));
            if (!target.IsEditable) throw new ArgumentException(target.ReadOnlyReason);
            var properties = Properties(target).ToDictionary(p => p.Name);
            var changes = ((JsonArray)args["changes"]!).Cast<JsonObject>().Select(c => (Property: properties.GetValueOrDefault(c.String("field"))
                ?? throw new ArgumentException(Text("InvalidParameters")), Value: JsonNode.Parse(c.String("value_json")))).ToArray();
            if (changes.Select(c => c.Property.Name).Distinct().Count() != changes.Length) throw new ArgumentException(Text("InvalidParameters"));
            foreach (var change in changes) Validate(change.Value, change.Property.Schema);
            var begin = changes.FirstOrDefault(c => c.Property.Name == "begin").Value?.GetValue<float>() ?? target.Begin;
            var end = changes.FirstOrDefault(c => c.Property.Name == "end").Value?.GetValue<float>() ?? target.End;
            if (end < begin) throw new ArgumentException(Text("InvalidParameters"));
            vm.ApplyAssistantGroup(() => { foreach (var change in changes) change.Property.Write(change.Value!); });
            return Node(new { state = State(), changed = changes.Select(c => new { c.Property.Name, value = c.Property.Read() }) });
        });
        yield return Op("csc.reparent", "CscReparent", Schema(("node", Str(), true), ("parent", Str("empty means scene root"), true)), AssistantEffect.Edit, args =>
        {
            var child = Find(args.String("node"));
            var parent = args.String("parent").Length == 0 ? null : Find(args.String("parent"));
            if (!child.IsEditable || parent is { IsEditable: false }) throw new ArgumentException(Text("ReadOnly"));
            vm.ReparentElement(child, parent);
            if (!ReferenceEquals(child.Element.Parent, parent?.Element)) throw new InvalidOperationException(Text("NotCompleted"));
            return State();
        });
        AssistantOperation Command(string name, string label, ICommand command, AssistantEffect effect = AssistantEffect.Edit) =>
            Op(name, label, Schema(), effect, _ => { command.Execute(null); return State(); }, () => command.CanExecute(null) ? null : Text("Unavailable"));
        yield return Op("csc.undo", "Undo", Schema(), AssistantEffect.Edit, _ => { if (!vm.Undo()) throw new InvalidOperationException(Text("NoUndo")); return State(); });
        yield return Op("csc.redo", "Redo", Schema(), AssistantEffect.Edit, _ => { if (!vm.Redo()) throw new InvalidOperationException(Text("NoRedo")); return State(); });
        yield return Command("csc.delete", "DeleteNode", vm.DeleteSelectedCommand);
        yield return Command("csc.detach", "DetachNode", vm.DetachSelectedCommand);
        yield return Command("csc.add_model", "AddModel", vm.AddModelCommand);
        yield return Command("csc.add_variant", "AddVariant", vm.AddVariantModelCommand);
        yield return Command("csc.add_vfx", "AddVfx", vm.AddVfxCommand);
        yield return Command("csc.add_sfx", "AddSfx", vm.AddSfxCommand);
        yield return Command("csc.add_point_light", "AddLight", vm.AddPointLightCommand);
        yield return Command("csc.add_spot_light", "AddLight", vm.AddSpotLightCommand);
        yield return Command("csc.add_camera", "AddCamera", vm.AddCameraCommand);
        yield return Command("csc.add_locator", "AddGroup", vm.AddLocatorCommand);
        yield return Command("csc.add_scene", "AddScene", vm.AddCompositeSceneCommand);
        yield return Command("csc.add_prefab", "AddPrefab", vm.AddPrefabCommand);
        yield return Command("csc.add_animation", "AddAnimation", vm.AddAnimationCommand);
        yield return Command("csc.focus", "Focus", vm.FocusSelectedCommand, AssistantEffect.Preview);
        yield return Command("csc.look_through_camera", "LookCamera", vm.LookThroughCameraCommand, AssistantEffect.Preview);
        yield return Op("csc.playback", "Playback", Schema(("playing", Bool(), false), ("seconds", Num(0, 86400, "seconds"), false), ("loop", Bool(), false)), AssistantEffect.Preview, args =>
        {
            if (args["seconds"] != null && args.Number("seconds") > vm.Duration) throw new ArgumentException(Text("InvalidParameters"));
            if (args["seconds"] != null) { vm.Playback.IsPlaying = false; vm.CurrentTime = args.Number("seconds"); }
            if (args["playing"] != null) vm.Playback.IsPlaying = args.Boolean("playing");
            if (args["loop"] != null) vm.Playback.Loop = args.Boolean("loop");
            return Node(new { vm.CurrentTime, vm.Duration, vm.IsPlaying, vm.Playback.Loop });
        });
        yield return Op("csc.curves", "CscCurves", Schema(), AssistantEffect.Query, _ => Node(vm.CurveSeriesList.Select(c => new { id = ids.Get(c.Channel), c.Name,
            c.Channel.Header, keys = c.Channel.Keyframes.Select(k => new { id = ids.Get(k), k.Time, k.Value, k.ModeIn, k.ModeOut }) })));
        yield return Op("csc.curve_key", "CscCurveKey", Schema(("channel", Str(), true), ("action", Str("", "set", "add", "delete"), true),
            ("key", Str(), false), ("seconds", Num(0, 86400, "seconds"), false), ("value", Num(-1000000, 1000000, "native channel units; rotation uses radians"), false)), AssistantEffect.Edit, args =>
        {
            if (vm.SelectedElement?.IsEditable != true) throw new ArgumentException(Text("ReadOnly"));
            var channel = vm.CurveSeriesList.Single(c => ids.Get(c.Channel) == args.String("channel")).Channel;
            var action = args.String("action");
            if (action != "set" && vm.CurveStructureLocked) throw new ArgumentException(Text("ReadOnly"));
            var key = action == "add" ? null : channel.Keyframes.Single(k => ids.Get(k) == args.String("key"));
            if (action != "delete" && (args["seconds"] == null || args["value"] == null)) throw new ArgumentException(Text("InvalidParameters"));
            if (action != "delete" && channel.Keyframes.Any(k => !ReferenceEquals(k, key) && Math.Abs(k.Time - args.Number("seconds")) < 0.000001)) throw new ArgumentException(Text("InvalidParameters"));
            vm.ApplyAssistantGroup(() =>
            {
                if (action == "delete") channel.Keyframes.Remove(key!);
                else if (action == "add") channel.AddKeyframe((float)args.Number("seconds"), (float)args.Number("value"));
                else { key!.Time = (float)args.Number("seconds"); key.Value = (float)args.Number("value"); channel.SortKeyframes(); }
                vm.OnCurvesModified();
                vm.RedrawCurves?.Invoke();
            });
            return State();
        });
    }

    private sealed record Property(string Name, JsonObject Schema, Func<JsonNode> Read, Action<JsonNode> Write);
    private static IEnumerable<Property> Properties(CscElementViewModel vm)
    {
        Property Number(string name, Func<float> get, Action<float> set, double min = -1000000, double max = 1000000, string unit = "native") =>
            new(name, Num(min, max, unit), () => Node(get()), v => set(v.GetValue<float>()));
        yield return Number("begin", () => vm.Begin, v => vm.Begin = v, 0, 86400, "seconds");
        yield return Number("end", () => vm.End, v => vm.End = v, 0, 86400, "seconds");
        yield return new("timing", Str("", "duration", "infinite", "pulse"), () => Node(vm.TimingMode), v => vm.TimingMode = v.GetValue<string>());
        yield return new("anchor", Str("", "free", "map_zero_y_to_terrain", "map_zero_y_to_terrain_and_align", "align_to_terrain"), () => Node(vm.AnchorMode), v => vm.AnchorMode = v.GetValue<string>());
        if (vm.Element.Position != null)
        {
            yield return Number("position.x", () => vm.PositionX, v => vm.PositionX = v);
            yield return Number("position.y", () => vm.PositionY, v => vm.PositionY = v);
            yield return Number("position.z", () => vm.PositionZ, v => vm.PositionZ = v);
        }
        if (vm.Element.Rotation != null)
        {
            yield return Number("rotation.x", () => vm.RotationXDegrees, v => vm.RotationXDegrees = v, unit: "degrees; offsets existing curve");
            yield return Number("rotation.y", () => vm.RotationYDegrees, v => vm.RotationYDegrees = v, unit: "degrees; offsets existing curve");
            yield return Number("rotation.z", () => vm.RotationZDegrees, v => vm.RotationZDegrees = v, unit: "degrees; offsets existing curve");
        }
        if (vm.Element.Scale != null) yield return Number("scale", () => vm.ScaleValue, v => vm.ScaleValue = v, .0001, 10000);
        if (vm.HasWeight) yield return Number("weight", () => vm.WeightValue, v => vm.WeightValue = v, 0, 1);
        if (vm.IsLight)
        {
            yield return Number("colour.r", () => vm.LightColourR, v => vm.LightColourR = v, 0, 1);
            yield return Number("colour.g", () => vm.LightColourG, v => vm.LightColourG = v, 0, 1);
            yield return Number("colour.b", () => vm.LightColourB, v => vm.LightColourB = v, 0, 1);
            yield return Number("light.intensity", () => vm.LightIntensity, v => vm.LightIntensity = v, 0);
            yield return Number("light.range", () => vm.LightRange, v => vm.LightRange = v, 0);
        }
        if (vm.HasCameraFov) yield return Number("camera.fov", () => vm.CameraFov, v => vm.CameraFov = v, .001, 179);
        if (vm.HasCameraNear) yield return Number("camera.near", () => vm.CameraNear, v => vm.CameraNear = v, .0001);
        if (vm.HasCameraFar) yield return Number("camera.far", () => vm.CameraFar, v => vm.CameraFar = v, .0001);
        if (vm.HasCameraRoll) yield return Number("camera.roll", () => vm.CameraRollDegrees, v => vm.CameraRollDegrees = v, unit: "degrees");
        if (vm.HasPeriod)
        {
            yield return Number("period.speed", () => vm.PeriodSpeedMultiplier, v => vm.PeriodSpeedMultiplier = v, .0001, 1000);
            yield return Number("period.offset", () => vm.PeriodTimeOffset, v => vm.PeriodTimeOffset = v, unit: "seconds");
        }
    }
}
