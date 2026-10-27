#nullable enable
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxOfficialTrailCameraDiagnosticTests
{
    private readonly VfxOfficialDistortionPixelTests _official = new();

    [OneTimeSetUp]
    public void RequireCurrentOfficialResources() => _official.LoadCurrentOfficialResourcesOnly();

    [TestCase(@"vfx\wh_dlc03_beasts_amber_spear_trail.xml", .35f)]
    [TestCase(@"vfx\wh3_dlc25_sorcerer_of_nurgle_death_trail.xml", .35f)]
    [TestCase(@"vfx\wh2_dlc14_stealth_smoke_trail.xml", .35f)]
    public void CompleteOfficialTrail_RecordsGeometryAndPixelsAcrossVelocityRelativeCameras(string path, float time)
    {
        var source = Read(path) ?? throw new FileNotFoundException(path);
        var sourceHash = Convert.ToHexStringLower(SHA256.HashData(source));
        var snapshot = new VfxPreviewSnapshot(source, path, Read, "0");
        var conversion = TotalWarVfxConverter.Convert(snapshot);
        Assert.That(conversion.Emitters, Is.Not.Empty, JsonSerializer.Serialize(conversion.Diagnostics));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(time, Attachment(time), Attachment);
        var initial = simulation.Frame(Camera(new(0, 4, 12), Vector3.Zero, Vector3.Up));
        var target = initial.Where(p => conversion.Emitters[p.Layer].Kind == 0)
            .OrderByDescending(p => Vector3.Cross(p.Transform.Right, p.Transform.Up).LengthSquared()).FirstOrDefault();
        Assert.That(initial.Any(p => conversion.Emitters[p.Layer].Kind == 0), Is.True,
            "The complete source must contain a live ordinary textured layer before collecting camera data.");
        var centre = target.Transform.Translation;
        var velocity = WorldVelocity(simulation, target.Layer);
        var direction = velocity.LengthSquared() > 0 ? Vector3.Normalize(velocity) : Vector3.Forward;
        var helperUp = Math.Abs(Vector3.Dot(direction, Vector3.Up)) < .9f ? Vector3.Up : Vector3.Right;
        var side = Vector3.Normalize(Vector3.Cross(helperUp, direction));
        var cameras = new[]
        {
            (Name: "side", Direction: side),
            (Name: "near-velocity", Direction: Vector3.Normalize(direction + side * .002f)),
            (Name: "along-velocity", Direction: direction),
        };
        var rows = new List<object>();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var preview = new EffekseerVfxPreview(graphics.GraphicsDevice);
            preview.Reload(snapshot);
            preview.Seek(time, Attachment(time), Attachment);
            foreach (var cameraCase in cameras)
            {
                var eye = centre + cameraCase.Direction * 12;
                var up = Math.Abs(Vector3.Dot(cameraCase.Direction, Vector3.Up)) < .9f ? Vector3.Up : Vector3.Right;
                var camera = Camera(eye, centre, up);
                var frame = simulation.Frame(camera);
                Assert.That(frame, Is.EqualTo(simulation.Frame(camera)), "Changing only the camera must not advance or mutate particle simulation.");
                var layers = frame.GroupBy(p => p.Layer).Select(group =>
                {
                    var emitter = conversion.Emitters[group.Key];
                    var metrics = group.Select(p =>
                    {
                        var cross = Vector3.Cross(p.Transform.Right, p.Transform.Up);
                        var area = cross.Length();
                        var toCamera = eye - p.Transform.Translation;
                        var cosine = area > 0 && toCamera.LengthSquared() > 0
                            ? Math.Abs(Vector3.Dot(cross / area, Vector3.Normalize(toCamera))) : 0;
                        Assert.That(float.IsFinite(area) && float.IsFinite(cosine), Is.True);
                        return new { Area = area, CameraCosine = cosine };
                    }).ToArray();
                    return new
                    {
                        emitter.Name, emitter.Kind, emitter.Alignment, emitter.TrailMode, emitter.SpawnSpacing,
                        Particles = metrics.Length, MinimumArea = metrics.Min(p => p.Area), MaximumArea = metrics.Max(p => p.Area),
                        MinimumCameraCosine = metrics.Min(p => p.CameraCosine), MaximumCameraCosine = metrics.Max(p => p.CameraCosine),
                    };
                }).ToArray();
                var device = graphics.GraphicsDevice;
                using var image = new RenderTarget2D(device, 192, 192, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
                device.SetRenderTarget(image);
                device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                device.DepthStencilState = DepthStencilState.Default;
                preview.Draw(camera);
                device.SetRenderTarget(null);
                var pixels = new Vector4[192 * 192];
                image.GetData(pixels);
                Assert.That(pixels.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)), Is.True);
                var raw = new byte[pixels.Length * 16];
                for (var index = 0; index < pixels.Length; index++)
                {
                    BitConverter.GetBytes(pixels[index].X).CopyTo(raw, index * 16);
                    BitConverter.GetBytes(pixels[index].Y).CopyTo(raw, index * 16 + 4);
                    BitConverter.GetBytes(pixels[index].Z).CopyTo(raw, index * 16 + 8);
                    BitConverter.GetBytes(pixels[index].W).CopyTo(raw, index * 16 + 12);
                }
                rows.Add(new
                {
                    cameraCase.Name, Eye = Values(eye), Target = Values(centre), Up = Values(up),
                    View = Values(camera.View), Projection = Values(camera.Projection), Layers = layers,
                    NonBlackPixels = pixels.Count(p => p.X + p.Y + p.Z > .001f),
                    ImageSha256 = Convert.ToHexStringLower(SHA256.HashData(raw)),
                });
            }
        }
        finally { graphics.Release(); }
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(source)), Is.EqualTo(sourceHash));
        var report = new
        {
            Path = path, Quality = "0", SourceSha256 = sourceHash, Time = time,
            TargetLayer = conversion.Emitters[target.Layer].Name, TargetVelocity = Values(velocity), Cameras = rows,
            Scope = "Complete unchanged official source and dependencies. Camera-only geometry/pixel diagnostic, not a native visual-equivalence assertion.",
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        TestContext.Progress.WriteLine(json);
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, Path.GetFileNameWithoutExtension(path.Replace('\\', '/')) + "-camera.json"), json);
        }
    }

    private byte[]? Read(string path) => (byte[]?)typeof(VfxOfficialDistortionPixelTests)
        .GetMethod("Read", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_official, [path]);

    private static Matrix Attachment(float time) => Matrix.CreateTranslation(time * 6, 0, 0);

    private static CommonShaderParameters Camera(Vector3 eye, Vector3 target, Vector3 up) => new(
        Matrix.CreateLookAt(eye, target, up), Matrix.CreateOrthographic(20, 20, .01f, 200),
        eye, target, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    private static Vector3 WorldVelocity(TotalWarVfxSimulation simulation, int layer)
    {
        var particles = ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(simulation)!).Cast<object>();
        foreach (var particle in particles)
        {
            var type = particle.GetType();
            var emitter = type.GetField("Emitter")!.GetValue(particle)!;
            if ((int)emitter.GetType().GetField("DefinitionIndex")!.GetValue(emitter)! != layer) continue;
            return Vector3.TransformNormal((Vector3)type.GetField("Velocity")!.GetValue(particle)!,
                (Matrix)type.GetField("Root")!.GetValue(particle)!);
        }
        return Vector3.Zero;
    }

    private static float[] Values(Vector3 value) => [value.X, value.Y, value.Z];
    private static float[] Values(Matrix value) => [value.M11, value.M12, value.M13, value.M14, value.M21, value.M22, value.M23, value.M24,
        value.M31, value.M32, value.M33, value.M34, value.M41, value.M42, value.M43, value.M44];
}
