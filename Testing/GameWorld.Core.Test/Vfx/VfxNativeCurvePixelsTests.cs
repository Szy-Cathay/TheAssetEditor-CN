#nullable enable
using System.Globalization;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxNativeCurvePixelsTests
{
    [Test]
    public void OriginalQuadraticModelAlpha_MatchesIndependentNativeTablePixelsAndChangesCoverage()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering is opt-in.");
        var source = VfxNativeCurveBindingTests.ModelAlphaSource(VfxNativeCurveBindingTests.OriginalAlpha());
        var table = VfxNativeCurveBindingTests.AlphaTable();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var actual = new EffekseerVfxPreview(graphics.GraphicsDevice);
            actual.Reload(new(source, "DIAG/original-q-alpha-field.xml", Resolve));
            Assert.That(actual.EmitterCount, Is.EqualTo(1), string.Join(';', actual.Diagnostics.Select(d => d.ReasonKey)));
            var stages = new List<(Color[] Actual, Color[] Reference)>();
            foreach (var time in new[] { .1f, .5f, .75f })
            {
                // Expected values were produced by official Utility75ed0, not by a product curve evaluator.
                var index = time * 127;
                var lower = (int)index;
                var alpha = table[lower] + (table[lower + 1] - table[lower]) * (index - lower);
                var constant = VfxNativeCurveBindingTests.ModelAlphaSource(VfxLibraryVisibilityTests.Constant("alpha", alpha));
                using var reference = new EffekseerVfxPreview(graphics.GraphicsDevice);
                reference.Reload(new(constant, "DIAG/native-q-alpha-reference.xml", Resolve));
                var pixels = Render(graphics.GraphicsDevice, actual, time);
                var expected = Render(graphics.GraphicsDevice, reference, time);
                Assert.That(pixels.Zip(expected).Max(pair => Difference(pair.First, pair.Second)), Is.LessThanOrEqualTo(3),
                    $"Native float table alpha={alpha.ToString("R", CultureInfo.InvariantCulture)} at {time}s must reach GPU coverage.");
                Assert.That(pixels.Count(pixel => pixel.R > 3), Is.GreaterThan(20));
                stages.Add((pixels, expected));
            }
            for (var index = 1; index < stages.Count; ++index)
            {
                var previous = stages[index - 1]; var current = stages[index];
                var expectedChanges = previous.Reference.Zip(current.Reference).Select(pair => Difference(pair.First, pair.Second) > 3).ToArray();
                var actualChanges = previous.Actual.Zip(current.Actual).Select(pair => Difference(pair.First, pair.Second) > 3).ToArray();
                Assert.That(expectedChanges.Count(changed => changed), Is.GreaterThan(20), "Independent native-table controls must predict visible phase changes.");
                Assert.That(actualChanges, Is.EqualTo(expectedChanges), "The phase must change exactly the coverage pixels predicted by the native-table controls.");
                Assert.That(Math.Sign(previous.Actual.Sum(pixel => (long)pixel.R) - current.Actual.Sum(pixel => (long)pixel.R)),
                    Is.EqualTo(Math.Sign(previous.Reference.Sum(pixel => (long)pixel.R) - current.Reference.Sum(pixel => (long)pixel.R))),
                    "Compare coverage direction with the independent GPU control, without assuming linear output-energy ratios.");
            }
            TestContext.Out.WriteLine("DIAG: unchanged official realm_shooting_stars alpha field in isolated rigid geometry, independent official-code float-table reference; not complete-effect/game visual acceptance.");

        }
        finally { graphics.Release(); }
    }

    [Test]
    public void OriginalCoincidentModelScale_MatchesIndependentNativeTableGeometryPixels()
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native offscreen rendering is opt-in.");
        var item = VfxNativeCurveBoundaryTests.Fixture().GetProperty("Cases").EnumerateArray()
            .Single(item => item.GetProperty("Name").GetString()!.Contains("meteor_arc", StringComparison.Ordinal));
        var source = VfxDocument.Read(VfxModelVariantTests.Source(1));
        VfxParticleEventMotionTests.Set(source.Root.Descendants("emitter").Single(), "ParticleScale", item.GetProperty("Source").GetString()!);
        var bytes = source.Write();
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            using var actual = new EffekseerVfxPreview(graphics.GraphicsDevice);
            actual.Reload(new(bytes, "DIAG/original-coincident-scale-field.xml", Resolve));
            Assert.That(actual.EmitterCount, Is.EqualTo(1), string.Join(';', actual.Diagnostics.Select(d => d.ReasonKey)));
            foreach (var time in new[] { .1f, .5f, .7f })
            {
                var scales = new float[3];
                for (var axis = 0; axis < 3; ++axis)
                {
                    var position = time * 127;
                    var lower = (int)position;
                    var bounds = new float[2];
                    for (var bound = 0; bound < 2; ++bound)
                    {
                        var table = item.GetProperty("Channels")[axis * 2 + bound].GetProperty("Baked");
                        bounds[bound] = table[lower].GetSingle() + (table[lower + 1].GetSingle() - table[lower].GetSingle()) * (position - lower);
                    }
                    Assert.That(bounds[0], Is.EqualTo(bounds[1]), "This phase has equal original bounds, so the independent pixel oracle does not need the game's RNG.");
                    scales[axis] = bounds[0];
                }
                var referenceDocument = VfxDocument.Read(VfxModelVariantTests.Source(1));
                VfxParticleEventMotionTests.Set(referenceDocument.Root.Descendants("emitter").Single(), "ParticleScale",
                    VfxParticleEventMotionTests.VectorCurve(new(scales[0], scales[1], scales[2])));
                using var reference = new EffekseerVfxPreview(graphics.GraphicsDevice);
                reference.Reload(new(referenceDocument.Write(), "DIAG/native-coincident-scale-reference.xml", Resolve));
                var pixels = Render(graphics.GraphicsDevice, actual, time);
                var expected = Render(graphics.GraphicsDevice, reference, time);
                Assert.That(pixels.Count(pixel => pixel.R > 3), Is.GreaterThan(20));
                Assert.That(pixels.Zip(expected).Max(pair => Difference(pair.First, pair.Second)), Is.LessThanOrEqualTo(3),
                    "Coincident-key selection and the rigid 128-sample scale table must reach actual GPU geometry.");
            }
            Assert.That(source.Write(), Is.EqualTo(bytes));
            TestContext.Out.WriteLine("DIAG: unchanged official Meteor ParticleScale field in isolated rigid geometry, independent official-code baked-scale pixel reference; complete originals are tested separately.");
        }
        finally { graphics.Release(); }
    }

    private static Color[] Render(GraphicsDevice device, EffekseerVfxPreview preview, float time)
    {
        using var target = new RenderTarget2D(device, 160, 100, false, SurfaceFormat.Color, DepthFormat.Depth24);
        preview.Seek(time, Matrix.Identity);
        device.SetRenderTarget(target);
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
        var eye = new Vector3(0, 0, 10);
        preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up),
            Matrix.CreateOrthographic(3, 1.875f, .1f, 100), eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
        device.SetRenderTarget(null);
        var pixels = new Color[160 * 100];
        target.GetData(pixels);
        return pixels;
    }

    private static byte[]? Resolve(string path) => path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
        ? VfxModelVariantPixelTests.Dds(Color.White) : VfxModelVariantTests.Resolve(path);
    private static int Difference(Color a, Color b) => Math.Max(Math.Max(Math.Abs(a.R - b.R), Math.Abs(a.G - b.G)),
        Math.Max(Math.Abs(a.B - b.B), Math.Abs(a.A - b.A)));
}
