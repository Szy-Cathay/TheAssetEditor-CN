using System.Text.Json.Nodes;
using Editors.Shared.Core.Common.BaseControl;
using Shared.Core.DependencyInjection;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Shared.Core.Common;

public sealed class AssistantPlaybackAdapter(IScopeRepository scopes, IPackFileService files) : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not EditorHostBase host) yield break;
        var player = host.Player;
        JsonNode State() => Node(new
        {
            objects = host.SceneObjects.Select(x => new { id = ids.Get(x.Data), x.Data.Description, model = x.Data.MeshName.Value,
                skeleton = x.Data.SkeletonName.Value, animation = x.Data.AnimationName.Value, clip_id = x.Data.AnimationClip == null ? null : ids.Get(x.Data.AnimationClip),
                visible = x.Data.ShowMesh.Value, skeleton_visible = x.Data.ShowSkeleton.Value }).ToArray(),
            main = player.SelectedMainAnimation == null ? null : ids.Get(player.SelectedMainAnimation.Asset),
            enabled = player.IsEnabled.Value, loop = player.LoopAnimation.Value,
        });
        AssistantOperation Op(string name, string description, JsonObject schema, Func<JsonObject, JsonNode> execute, Func<string?>? unavailable = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(description), schema, AssistantEffect.Preview, Text("NoDataChange"), State, execute, unavailable);
        yield return AssistantOperation.Sync("animation.state", ids.Get(editor), Text("PlaybackState"), Schema(), AssistantEffect.Query, Text("NoDataChange"), State,
            _ => Node(new { playing = player.IsPlaying.Value, frame = player.SelectedAnimationCurrentFrame.Value, frames = player.SelectedAnimationFrameCount.Value,
                seconds = player.PlaybackPositionSeconds, duration_seconds = player.SelectedAnimationMaxTime.Value, fps = player.SelectedAnimationFps.Value }));
        yield return Op("animation.playback", "Playback", Schema(("action", Str("", "play", "pause", "seek_frame", "seek_seconds", "loop"), true),
            ("frame", Num(0, 1000000, "zero-based frame", true), false), ("seconds", Num(0, 86400, "seconds"), false), ("loop", Bool(), false)), args =>
        {
            var action = args.String("action");
            var main = player.SelectedMainAnimation?.Asset.Player;
            if (main?.AnimationClip == null) throw new InvalidOperationException(Text("PreviewNotReady"));
            if (action == "seek_frame" && (args["frame"] == null || args.Integer("frame") >= main.FrameCount)) throw new ArgumentException(Text("InvalidParameters"));
            if (action == "seek_seconds" && (args["seconds"] == null || args.Number("seconds") > main.Duration.TotalSeconds)) throw new ArgumentException(Text("InvalidParameters"));
            player.IsEnabled.Value = true;
            if (action == "play" && !player.IsPlaying.Value || action == "pause" && player.IsPlaying.Value) player.ToggleAnimationPausePlay();
            if (action is "seek_frame" or "seek_seconds")
            {
                if (player.IsPlaying.Value) player.ToggleAnimationPausePlay();
                player.PlaybackPositionSeconds = action == "seek_frame" ? (float)main.AnimationClip.Timebase.GetSampleTime(args.Integer("frame")).TotalSeconds : (float)args.Number("seconds");
            }
            if (action == "loop") { if (args["loop"] == null) throw new ArgumentException(Text("InvalidParameters")); player.LoopAnimation.Value = args.Boolean("loop"); }
            return Node(new { seconds = main.CurrentTime.TotalSeconds, frame = main.CurrentFrame, playing = main.IsPlaying, loop = player.LoopAnimation.Value });
        }, () => player.SelectedMainAnimation?.Asset.AnimationClip != null ? null : Text("PreviewNotReady"));
        yield return Op("scene.load_reference", "LoadReference", Schema(("object", Str(), true), ("kind", Str("", "model", "skeleton", "animation"), true),
            ("container", Str(), true), ("path", Str(), true)), args =>
        {
            var target = host.SceneObjects.SingleOrDefault(x => ids.Get(x.Data) == args.String("object"))?.Data ?? throw new ArgumentException(Text("TargetGone"));
            var container = files.GetAllPackfileContainers().SingleOrDefault(x => ids.Get(x) == args.String("container")) ?? throw new ArgumentException(Text("TargetGone"));
            var file = files.FindFile(args.String("path"), container) ?? throw new ArgumentException(Text("TargetGone"));
            var kind = args.String("kind");
            var extension = System.IO.Path.GetExtension(file.Name).ToLowerInvariant();
            if (kind == "model" ? extension is not (".rigid_model_v2" or ".wsmodel" or ".variantmeshdefinition") : extension != ".anim")
                throw new ArgumentException(Text("InvalidParameters"));
            if (kind != "model") global::Shared.GameFormats.Animation.AnimationFile.Create(file);
            var service = scopes.GetRequiredService<SceneObjectEditor>(editor);
            switch (kind)
            {
                case "model": service.SetMesh(target, file); break;
                case "skeleton": service.SetSkeleton(target, file); break;
                case "animation": host.SceneObjects.Single(x => ReferenceEquals(x.Data, target)).FragAndSlotSelection.LoadAnimation(file); break;
            }
            return State();
        }, () => editor is ISaveableEditor { HasUnsavedChanges: true } ? Text("UnsavedSource") : null);
        yield return Op("scene.visibility", "Visibility", Schema(("object", Str(), true), ("model", Bool(), false), ("skeleton", Bool(), false)), args =>
        {
            var target = host.SceneObjects.SingleOrDefault(x => ids.Get(x.Data) == args.String("object"))?.Data ?? throw new ArgumentException(Text("TargetGone"));
            if (args["model"] != null) target.ShowMesh.Value = args.Boolean("model");
            if (args["skeleton"] != null) target.ShowSkeleton.Value = args.Boolean("skeleton");
            return State();
        });
        yield return Op("scene.reset_camera", "ResetCamera", Schema(), _ => { host.ResetCameraCommand.Execute(null); return Node(new { preview_only = true }); });
    }
}
