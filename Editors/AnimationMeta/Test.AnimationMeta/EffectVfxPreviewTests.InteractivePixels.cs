using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [TestCase("wh3_main_entity_storm_dragon_attack_impact", .8f, "85C78DD85901788F239C1F25F06A7C76247BD1D57246DC7F45B7B9159D1414A7", 45f)]
    [TestCase("wh_main_heavens_comet_of_casandora_impact", 2.5f, "C40D9D5264274A6934B7B37FEC643E355A5EE1415C669B3B37F0E50C1933FF7B", 180f)]
    public void InteractiveSuperView_CompleteOfficialVfxMatchesSynchronousPacketsAndRgba(
        string name, float age, string originalSha256, float cameraDistance)
    {
        var official = _manyOfficial ??= new ManyOfficialResources();
        official.Origins.Clear(); official.Missing.Clear();
        var path = @"vfx\" + name + ".xml";
        var original = official.Index[path];
        var originalBytes = original.File.DataSource.ReadData();
        Assert.That(Convert.ToHexString(SHA256.HashData(originalBytes)), Is.EqualTo(originalSha256));
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var devices = new Mock<IDeviceResolver>(); devices.SetupGet(value => value.Device).Returns(device);
            var roots = new[] { new GroupNode("synchronous"), new GroupNode("interactive") };
            var results = new List<MetaDataBuildResult>();
            var nodes = new List<VfxPreviewSceneNode>();
            var pumps = 0;
            try
            {
                foreach (var interactive in new[] { false, true })
                {
                    var builder = new MetaDataBuilder(null!, null!, null!, official.Files.Object, null!,
                        new EffekseerVfxPreviewFactory(), devices.Object, new()) { EnableInteractiveVfxPreview = interactive };
                    var source = new Effect_v11
                    {
                        Name = "EFFECT", Version = 11, VfxName = name, StartTime = .25f, EndTime = 4,
                        Position = new(1, 0, -2), Orientation = Quaternion.CreateFromYawPitchRoll(.2f, 0, 0).ToVector4(),
                        Scale = 1.25f, NodeIndex = -1,
                    };
                    var root = roots[interactive ? 1 : 0];
                    var result = builder.Build(null, new() { Version = 2, Attributes = [source] }, null, root, null!, new(), null);
                    results.Add(result);
                    var effect = (EffectMetaInstance)result.Instances.Single();
                    effect.Update(age + source.StartTime);
                    var node = (VfxPreviewSceneNode)root.Children.Single().Children.Single();
                    nodes.Add(node);
                    Assert.That(node.EnableInteractiveReload, Is.EqualTo(interactive));
                    var timer = Stopwatch.StartNew();
                    bool ready;
                    do
                    {
                        ready = node.PrepareFrame(node.Parent.ModelMatrix);
                        pumps++;
                        Assert.That(node.Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False,
                            "A failed runtime is not pending asynchronous preparation.");
                        var preview = InteractiveNodePreview(node);
                        if (preview != null && !preview.HasPendingInteractiveReload
                            && (bool)typeof(VfxPreviewSceneNode).GetField("_currentPrepared", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(node)!) break;
                        Assert.That(timer.Elapsed, Is.LessThan(TimeSpan.FromSeconds(45)), "The explicit interactive host must eventually prepare the exact phase.");
                        Thread.Sleep(1);
                    } while (true);
                    Assert.That(ready, Is.True, string.Join(',', node.Diagnostics.Select(diagnostic => diagnostic.ReasonKey)));
                }
                const int width = 640, height = 480;
                var focus = new Vector3(0, cameraDistance * .12f, 0);
                var eye = focus + Vector3.Normalize(new Vector3(.45f, .45f, 1)) * cameraDistance;
                var parameters = new CommonShaderParameters(Matrix.CreateLookAt(eye, focus, Vector3.Up),
                    Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, (float)width / height, .1f, 1500) * Matrix.CreateScale(-1, 1, 1),
                    eye, focus, 0, 0, 0, 1, Vector3.One, [], ViewportWidth: width, ViewportHeight: height,
                    VfxGroundHeight: 0, VfxWriteGroundDepth: true, MaterialTimeSeconds: age);
                using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24, 4, RenderTargetUsage.DiscardContents);
                Assert.That(target.MultiSampleCount, Is.EqualTo(4), "Keep normal Debug anti-aliasing.");
                var colors = new List<Color[]>();
                var packets = new List<(string Sha256, int Count)>();
                for (var index = 0; index < nodes.Count; index++)
                {
                    device.SetRenderTarget(target);
                    device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, new Color(32, 36, 40), 1, 0);
                    ((IRenderItem)nodes[index]).Draw(device, parameters, RenderingTechnique.Normal);
                    device.SetRenderTarget(null);
                    var pixels = new Color[width * height]; target.GetData(pixels); colors.Add(pixels);
                    packets.Add(InteractiveNodePacketHash(InteractiveNodePreview(nodes[index])!, parameters));
                    var folder = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_RENDER_OUTPUT");
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        Directory.CreateDirectory(folder);
                        using var stream = new FileStream(Path.Combine(folder, name + (index == 0 ? "-synchronous.png" : "-interactive.png")), FileMode.CreateNew);
                        target.SaveAsPng(stream, width, height);
                    }
                }
                var hashes = colors.Select(pixels => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(pixels.AsSpan())))).ToArray();
                ManyEvidence(name + "-interactive-equivalence.json", new
                {
                    Scope = "DIAG META placement and timing; both routes use the same complete, unchanged official VFX/dependencies, quality 0, attachment, phase, camera and 4x MSAA. This proves optimization equivalence, not native-game acceptance.",
                    Source = path, OriginalSha256 = originalSha256, Age = age, Quality = "0", MultiSampleCount = target.MultiSampleCount,
                    Pumps = pumps, Packets = packets.Select(packet => new { packet.Sha256, packet.Count }), RgbaSha256 = hashes,
                    Sources = official.Origins.Values, Missing = official.Missing.ToArray(),
                });
                Assert.Multiple(() =>
                {
                    Assert.That(packets[0].Count, Is.GreaterThan(0));
                    Assert.That(packets[1], Is.EqualTo(packets[0]), "Every complete projected particle packet must match.");
                    Assert.That(colors[1], Is.EqualTo(colors[0]), "Every output RGBA byte must match at the same phase.");
                    Assert.That(colors[0].Count(pixel => pixel != new Color(32, 36, 40)), Is.GreaterThan(20), "The equivalence image must contain the complete effect, not only a matching empty background.");
                    Assert.That(nodes[1].ParticleCount, Is.EqualTo(nodes[0].ParticleCount));
                    Assert.That(nodes.SelectMany(node => node.Diagnostics).Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
                    Assert.That(official.Missing, Is.Empty);
                    Assert.That(original.File.DataSource.ReadData(), Is.EqualTo(originalBytes));
                });
            }
            finally { foreach (var result in results) foreach (var instance in result.Instances) instance.CleanUp(); }
        }
        finally { graphics.Release(); }
    }

    private static IVfxPreview? InteractiveNodePreview(VfxPreviewSceneNode node) =>
        (IVfxPreview?)typeof(VfxPreviewSceneNode).GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(node);

    private static (string Sha256, int Count) InteractiveNodePacketHash(IVfxPreview preview, CommonShaderParameters parameters)
    {
        var simulation = preview.GetType().GetField("_simulation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
        var particles = (Array)simulation.GetType().GetMethod("ProjectedFrame")!.Invoke(simulation, [parameters, true])!;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (particles.Length == 0) return (Convert.ToHexString(hash.GetHashAndReset()), 0);
        var first = particles.GetValue(0)!;
        var renderProperty = first.GetType().GetProperty("Render")!;
        var variantProperty = first.GetType().GetProperty("ModelVariant")!;
        var size = Marshal.SizeOf(renderProperty.GetValue(first)!.GetType());
        var pointer = Marshal.AllocHGlobal(size);
        var bytes = new byte[size];
        try
        {
            foreach (var particle in particles)
            {
                Marshal.StructureToPtr(renderProperty.GetValue(particle)!, pointer, false);
                Marshal.Copy(pointer, bytes, 0, size); hash.AppendData(bytes);
                hash.AppendData(BitConverter.GetBytes((int)variantProperty.GetValue(particle)!));
            }
            return (Convert.ToHexString(hash.GetHashAndReset()), particles.Length);
        }
        finally { Marshal.FreeHGlobal(pointer); }
    }
}
