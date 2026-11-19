using System.IO;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;
using Editors.KitbasherEditor.ChildEditors.PhotoStudio;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Components.Rendering;
using Shared.Core.DependencyInjection;
using Shared.Core.ModAssistant;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.ToolCreation;
using Shared.Core.Services;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.KitbasherEditor;

public sealed class PhotoAssistantAdapter(IScopeRepository scopes, IPackFileService files, IEditorManager editors) : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not KitbasherViewModel) yield break;
        var camera = scopes.GetRequiredService<ArcBallCamera>(editor);
        var renderer = scopes.GetRequiredService<RenderEngineComponent>(editor);
        JsonNode State() => Node(new { renderer.CanCapture, yaw_degrees = Microsoft.Xna.Framework.MathHelper.ToDegrees(camera.Yaw),
            pitch_degrees = Microsoft.Xna.Framework.MathHelper.ToDegrees(camera.Pitch), camera.Zoom,
            look_at = new { camera.LookAt.X, camera.LookAt.Y, camera.LookAt.Z } });
        yield return new AssistantOperation("kitbash.photo_export", ids.Get(editor), Text("PhotoExport"), Schema(
            ("output_folder", Str("absolute directory outside projects; generates a unique PNG"), true),
            ("scale", Num(1, 2, "image resolution multiplier: 1 or 2", true), true),
            ("camera_yaw_degrees", Num(-360, 360, "degrees"), false), ("camera_pitch_degrees", Num(-89, 89, "degrees"), false),
            ("camera_zoom", Num(ArcBallCamera.MinZoom, 100000, "game units"), false), ("light_intensity", Num(0, 100), false)),
            AssistantEffect.Disk, Text("PhotoRecovery"), State, async (args, token) =>
            {
                var folder = args.String("output_folder");
                if (!Path.IsPathFullyQualified(folder)) throw new ArgumentException(Text("InvalidParameters"));
                var scope = FolderProjectPathPolicy.ResolveExternalOutputPath(Path.Combine(folder, "capture.png"));
                foreach (var project in files.GetAllPackfileContainers().OfType<FolderProjectContainer>()) FolderProjectPathPolicy.EnsureOutputOutsideProject(project.ProjectRoot, scope);
                using var studio = new PhotoStudioViewModel(renderer, scopes.GetRequiredService<SceneRenderParametersStore>(editor), camera,
                    scopes.GetRequiredService<IStandardDialogs>(editor));
                if (args["camera_yaw_degrees"] != null) studio.CameraYawDegrees = (float)args.Number("camera_yaw_degrees");
                if (args["camera_pitch_degrees"] != null) studio.CameraPitchDegrees = (float)args.Number("camera_pitch_degrees");
                if (args["camera_zoom"] != null) studio.CameraZoom = (float)args.Number("camera_zoom");
                if (args["light_intensity"] != null) studio.LightIntensity = (float)args.Number("light_intensity");
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                renderer.SaveNextFrame(new SaveRenderImageSettings("AECN_" + Guid.NewGuid().ToString("N"), false, args.Integer("scale"), Path.GetDirectoryName(scope)!)
                {
                    CompletionHandler = path => completion.TrySetResult(path), FailureHandler = error => completion.TrySetException(error), CancellationToken = timeout.Token,
                    ValidateOutput = path => FolderProjectPathPolicy.ResolveExternalOutputPath(path),
                });
                editors.SetEditorAsCurrent(editor);
                var output = await completion.Task.WaitAsync(timeout.Token);
                using var stream = File.OpenRead(output);
                var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames.Single();
                return Node(new { output, image.PixelWidth, image.PixelHeight, bytes = stream.Length, preview_only = true, game_verified = false });
            }, () => renderer.CanCapture ? null : Text("PreviewNotReady"));
    }
}
