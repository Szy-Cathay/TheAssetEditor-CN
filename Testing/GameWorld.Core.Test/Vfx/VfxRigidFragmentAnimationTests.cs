#nullable enable
using GameWorld.Core.Vfx;
using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxRigidFragmentAnimationTests
{
    [Test]
    public void NativePixels_MoveOnlyTheIndependentlyAnimatedFragment()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var model = Model(2); var clip = Clip(); var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(VfxModelAnimationTests.Source(life: 2), "vfx/rigid.xml", path => path.Replace('\\', '/') switch
            {
                "vfx/model-0.rigid_model_v2" => model, "animations/animated.anim" => clip,
                _ when path.EndsWith(".dds") => VfxModelVariantPixelTests.Dds(Color.White), _ => null,
            }));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.ReasonKey)));
            using var target = new RenderTarget2D(device, 256, 128, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var camera = new Vector3(2, 1, 10); var parameters = new CommonShaderParameters(Matrix.CreateLookAt(camera, new(2, 1, 0), Vector3.Up),
                Matrix.CreateOrthographic(8, 4, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
            var first = Positions(.1f); var second = Positions(.6f);
            Assert.That(second.Right - first.Right, Is.InRange(12f, 20f));
            Assert.That(second.Left, Is.EqualTo(first.Left).Within(.5f));

            (double Left, double Right) Positions(float time)
            {
                preview.Seek(time, Matrix.Identity); device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
                preview.Draw(parameters); device.SetRenderTarget(null); var pixels = new Color[256 * 128]; target.GetData(pixels);
                var visible = pixels.Select((colour, index) => (Colour: colour, X: index % 256)).Where(p => p.Colour.R > 128).ToArray();
                Assert.That(visible.Length, Is.GreaterThan(100));
                return (visible.Where(p => p.X < 120).Average(p => p.X), visible.Where(p => p.X >= 128).Average(p => p.X));
            }
        }
        finally { graphics.Release(); }
    }

    [Test]
    public void IndexedRigidFragments_DoNotRequireABindSkeletonOrUniqueBoneNames()
    {
        var source = VfxModelAnimationTests.Source(life: 2);
        var clip = Clip(); var model = Model(2);
        var conversion = Convert(source, model, clip);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.ReasonKey)));
        Assert.That(conversion.Models, Has.Count.EqualTo(2));
        Assert.That(conversion.Models[0].Vertices[0].Position.X, Is.EqualTo(2).Within(.001), "Rigid tracks are absolute transforms, not skinning deltas.");
        Assert.That(conversion.Models[1].Vertices[0].Position.Y, Is.EqualTo(1).Within(.001));
        Assert.That(conversion.Models[0].AnimationVertices![60 * 3].Position.X, Is.EqualTo(3).Within(.001));
        Assert.That(conversion.Models[1].AnimationVertices![60 * 3].Position.Y, Is.EqualTo(1).Within(.001), "Repeated names must not bind both pieces to the first matrix.");
        Assert.That(clip, Is.EqualTo(Clip()));
        Assert.That(model, Is.EqualTo(Model(2)));
    }

    [Test]
    public void FortyTwoAnimatedParts_ShareOneParticleAndRemainWithinTheExistingPoseBudget()
    {
        var conversion = Convert(VfxModelAnimationTests.Source(life: 2), Model(42), Clip());
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Models, Has.Count.EqualTo(42));
        Assert.That(conversion.Emitters[0].RenderLayerCount, Is.EqualTo(42));
        Assert.That(conversion.Models.All(m => m.AnimationFrameCount == 120), Is.True);
    }

    [TestCase(-1)]
    [TestCase(2)]
    public void InvalidRigidMatrixIndex_IsDiagnosedBeforeAnyPartialEmitterIsPublished(int matrixIndex)
    {
        var conversion = Convert(VfxModelAnimationTests.Source(life: 2), Model(2, matrixIndex), Clip());
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Models, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/MatrixIndex" && d.ReasonKey == "Vfx.Preview.GeometryUnsupported"), Is.True);
    }

    [Test]
    public void UncalibratedRigidHierarchy_RemainsAnExplicitDiagnostic()
    {
        var clip = AnimationFile.Create(new PackFile("clip", new MemorySource(Clip())));
        clip.Bones[1].ParentId = 0;
        var conversion = Convert(VfxModelAnimationTests.Source(life: 2), Model(2), AnimationFile.ConvertToBytes(clip));
        Assert.That(conversion.Emitters, Is.Empty);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData/Hierarchy" && d.ReasonKey == "Vfx.Preview.GeometryUnsupported"), Is.True);
    }

    private static VfxConversion Convert(byte[] source, byte[] model, byte[] clip) => TotalWarVfxConverter.Convert(new(source, "vfx/rigid.xml", path => path.Replace('\\', '/') switch
    {
        "vfx/model-0.rigid_model_v2" => model,
        "animations/animated.anim" => clip,
        _ when path.EndsWith(".dds") => VfxModelVariantPixelTests.Dds(Color.White),
        _ => null,
    }));

    internal static byte[] Model(int count, int? matrixIndex = null)
    {
        var file = ModelFactory.Create().Load(VfxModelVariantTests.Model(0)); var header = file.Header; header.SkeletonName = "building"; file.Header = header;
        file.ModelList[0] = Enumerable.Range(0, count).Select(index =>
        {
            var part = ModelFactory.Create().Load(VfxModelVariantTests.Model(0)).ModelList[0][0];
            ((WeightedMaterial)part.Material).MatrixIndex = matrixIndex ?? index % 2;
            return part;
        }).ToArray();
        file.RecalculateOffsets(); return ModelFactory.Create().Save(file);
    }

    internal static byte[] Clip()
    {
        var clip = AnimationFile.Create(new PackFile("clip", new MemorySource(VfxModelAnimationTests.Animation(false))));
        clip.Header.SkeletonName = "building"; clip.Header.FrameRate = 1;
        foreach (var bone in clip.Bones) { bone.Name = "fragment"; bone.ParentId = -1; }
        clip.AnimationParts[0].DynamicFrames[1].Transforms[0] = new RmvVector3(4, 0, 0);
        return AnimationFile.ConvertToBytes(clip);
    }
}
