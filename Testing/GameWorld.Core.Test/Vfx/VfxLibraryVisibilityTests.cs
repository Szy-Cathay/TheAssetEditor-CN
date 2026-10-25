using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxLibraryVisibilityTests
{
    [TestCase("ParticleScale")]
    [TestCase("ParticleInitialScale")]
    public void LegacyScalarScale_KeepsNativeDefaultsOnOmittedAxes(string field)
    {
        var curve = new VfxPreviewCurve(Constant("x", .25f), field, 1);
        curve.RequireChannels("x", "y", "z");
        Assert.That(curve.Vector(.4, new(.2f, .4f, .8f)), Is.EqualTo(new Vector3(.25f, 1, 1)));
    }

    [Test]
    public void MissingParticleScale_UsesNativeUnitSizeWithoutEditingSource()
    {
        var doc = VfxDocument.Read(Source(""));
        doc.Root.Descendants("emitter").Single().Elements().Where(n => (string)n.Attribute("name")! == "ParticleScale").Remove();
        var bytes = doc.Write(); var conversion = Convert(bytes);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(conversion.Emitters[0].Scale.Vector(.5, Vector3.One), Is.EqualTo(Vector3.One));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void MissingEmissionRate_MatchesNativeSteadyStateCountForTwoSecondParticles()
    {
        var doc = VfxDocument.Read(Source("")); var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Elements().Single(n => (string)n.Attribute("name")! == "EmissionRateCurve").Remove();
        emitter.Elements().Single(n => (string)n.Attribute("name")! == "ParticleLifeTime").Value = Constant("x", 2);
        var bytes = doc.Write(); var conversion = Convert(bytes); var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(5, Matrix.Identity);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(simulation.ParticleCount, Is.EqualTo(20));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void OmittedEmitterDuration_KeepsEmittingAsInTheNativeDelayedControl()
    {
        var doc = VfxDocument.Read(Source(""));
        doc.Root.Descendants("emitter").Single().Elements().Single(n => (string)n.Attribute("name")! == "InfiniteLife")
            .SetAttributeValue("value", "false");
        var bytes = doc.Write(); var conversion = Convert(bytes);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(45, Matrix.Identity);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "LifeTime" && d.ReasonKey == "Vfx.Preview.DefaultEmitterDurationCandidate"), Is.True);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [Test]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    [TestCase(false, true, false)]
    public void ReferenceLifetime_MatchesNativeFiniteAndInfiniteControls(bool childInfinite, bool referenceInfinite, bool visibleLate)
    {
        var child = VfxDocument.Read(Source("")); var emitter = child.Root.Descendants("emitter").Single();
        emitter.Elements().Single(n => (string)n.Attribute("name")! == "InfiniteLife").SetAttributeValue("value", childInfinite ? "true" : "false");
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", ".3"), new XAttribute("max", ".3")));
        emitter.Elements().Single(n => (string)n.Attribute("name")! == "EmissionRateCurve").Value = Constant("x", 20);
        emitter.Elements().Single(n => (string)n.Attribute("name")! == "ParticleLifeTime").Value = Constant("x", .4f);
        var childBytes = child.Write(); var parent = VfxDocument.CreateComposition();
        var edit = parent.AddReference("child", child); edit.Apply();
        edit.Selection.SetAttributeValue("infinite_life", referenceInfinite ? "true" : "false");
        var bytes = parent.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/parent.xml", p => p == @"vfx\child.xml" ? childBytes : [1]));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(2, Matrix.Identity);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        Assert.That(simulation.ParticleCount > 0, Is.EqualTo(visibleLate));
        Assert.That(parent.Write(), Is.EqualTo(bytes)); Assert.That(child.Write(), Is.EqualTo(childBytes));
    }

    [Test]
    public void GradientAndVertexColour_KeepTheirOwnModifierParameterScopes()
    {
        var bytes = Source($"""
            <modifier fragment="vs_colour"><property name="curve_colour" type="curve">{Rgb(.5f)}</property></modifier>
            <modifier fragment="ps_gradient"><property name="curve_colour" type="curve">{Rgb(1)}</property>
            <property name="curve_alpha" type="curve">{Constant("alpha", 1)}</property><property name="alpha_mapping" value="true"/></modifier>
            <modifier fragment="vs_vertex_alpha"><property name="curve_alpha" type="curve">{Constant("alpha", .75f)}</property></modifier>
            """);
        var result = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.False);
            Assert.That(result.Emitters.Select(e => e.Render.GradientIndex), Has.All.GreaterThanOrEqualTo(0));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
        if (result.Emitters.Count == 1)
        {
            Assert.That(result.Emitters[0].Colour.Sample("r", .5, .5f), Is.EqualTo(.5f));
            Assert.That(result.Emitters[0].Alpha.Sample("alpha", .5, .5f), Is.EqualTo(.75f));
        }
    }

    [TestCase("ps_texture")]
    [TestCase("ps_normal")]
    public void ShadingNormal_UsesTheSingleNativeBindingInsteadOfAnUnrelatedModifier(string fragment)
    {
        var doc = VfxDocument.Read(Source("""
            <modifier fragment="ps_lighting"><property name="emissive" type="float" value="0.5"/></modifier>
            <modifier fragment="ps_normal"/>
            <modifier fragment="vs_colour"><texture_property name="normal" path="vfx/unrelated-normal.dds"/></modifier>
            """));
        doc.Root.Descendants("modifier").Single(n => (string)n.Attribute("fragment")! == fragment)
            .Add(new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("path", "vfx/shading-normal.dds")));
        var bytes = doc.Write(); var resolved = new List<string>();
        byte[] normal = [4, 3, 2, 1];
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/normal-binding.xml", path =>
        {
            resolved.Add(path); return path == @"vfx\shading-normal.dds" ? normal : [1];
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var render = conversion.Emitters[0].Render;
        Assert.That(render.NormalIndex, Is.GreaterThanOrEqualTo(0));
        Assert.That(conversion.TexturePaths[render.NormalIndex], Is.EqualTo("vfx/shading-normal.dds"));
        Assert.That(conversion.TextureUsages[render.NormalIndex], Is.EqualTo(VfxTextureUsage.ShadingNormal));
        Assert.That(conversion.Textures[render.NormalIndex], Is.EqualTo(normal));
        Assert.That(resolved, Does.Not.Contain(@"vfx\unrelated-normal.dds"));
        Assert.That(conversion.Diagnostics.Any(item => item.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.False);
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("vfx/shading-normal.dds")]
    [TestCase("vfx/other-normal.dds")]
    public void ShadingNormal_RejectsTwoNativeBindingsWithoutChoosingOrRetainingResources(string texturePath)
    {
        var doc = VfxDocument.Read(Source("""
            <modifier fragment="ps_lighting"><property name="emissive" type="float" value="0.5"/></modifier>
            <modifier fragment="ps_normal"><texture_property name="normal" path="vfx/shading-normal.dds"/></modifier>
            """));
        doc.Root.Descendants("modifier").Single(n => (string)n.Attribute("fragment")! == "ps_texture")
            .Add(new XElement("texture_property", new XAttribute("name", "normal"), new XAttribute("path", texturePath)));
        var bytes = doc.Write(); var resolved = new List<string>();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, "vfx/ambiguous-normal.xml", path => { resolved.Add(path); return [1]; }));
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Is.Empty);
            Assert.That(conversion.Diagnostics.Any(item => item.Field == "normal" && item.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
            Assert.That(conversion.Textures, Is.Empty);
            Assert.That(resolved.Any(path => path.EndsWith("normal.dds", StringComparison.Ordinal)), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void TranslationModifiers_DoNotTreatTheirEqualPivotsAsConflictingProperties()
    {
        var bytes = Source("""
            <modifier fragment="vs_translation"><property name="pivot_x" value="0.5"/><property name="pivot_y" value="0.5"/></modifier>
            <modifier fragment="vs_translation_velocity_v2"><property name="pivot_x" value="0.5"/><property name="pivot_y" value="0.5"/>
            <property name="orientation_mode" value="Horizontally-oriented"/></modifier>
            """);
        var result = Convert(bytes);
        Assert.That(result.Emitters, Has.Count.EqualTo(1));
    }

    [Test]
    public void EmitterRotation_DirectsVelocityWithoutFlatteningTheDefaultHorizontalParticlePlane()
    {
        var doc = VfxDocument.Read(Source("""
            <modifier fragment="vs_translation_velocity_v2"><property name="orientation_mode" value="Horizontally-oriented"/></modifier>
            """));
        var emitter = doc.Root.Descendants("emitter").Single();
        var orientation = Constant("x", 90).Split(':')[0] + "|" + Constant("y", 0).Split(':')[0] + "|" + Constant("z", 0).Split(':')[0]
            + ":x,0;0,0|y,0;1,1|z,0;2,2";
        emitter.Add(new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "EmitterOrientation"), orientation),
            new System.Xml.Linq.XElement("property", new System.Xml.Linq.XAttribute("name", "ParticleInitialSpeed"), Constant("x", 1)));
        var simulation = new TotalWarVfxSimulation(Convert(doc.Write())); simulation.Seek(.1f, Matrix.Identity);
        var camera = new GameWorld.Core.Rendering.CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 10, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var particle = simulation.Frame(camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Cross(particle.Transform.Right, particle.Transform.Up).Length(), Is.EqualTo(1).Within(.00001));
            Assert.That(particle.Transform.Translation.Z, Is.EqualTo(.1f).Within(.00001));
            Assert.That(particle.Transform.Translation.Y, Is.EqualTo(0).Within(.00001));
        });
    }

    [Test]
    public void ConflictingPropertiesInsideOneModifier_AreStillDiagnosed()
    {
        var result = Convert(Source($"""
            <modifier fragment="vs_colour"><property name="curve_colour">{Rgb(1)}</property><property name="curve_colour">{Rgb(.2f)}</property></modifier>
            """));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(d => d.Field == "curve_colour" && d.ReasonKey == "Vfx.Preview.AmbiguousField"), Is.True);
    }

    [TestCase(0f, 1.35f)]
    [TestCase(12f, 13.35f)]
    public void FinitePreviewCycle_CoversDelayedEmissionAndTheParticleTailWithoutEightSecondBlankGaps(float start, float expected)
    {
        var doc = VfxDocument.Read(Source(""));
        var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Elements().Single(n => (string)n.Attribute("name")! == "InfiniteLife").SetAttributeValue("value", "false");
        emitter.Add(new System.Xml.Linq.XElement("ranged_property", new System.Xml.Linq.XAttribute("name", "LifeTime"),
            new System.Xml.Linq.XAttribute("min", ".1"), new System.Xml.Linq.XAttribute("max", ".1")));
        emitter.Add(new System.Xml.Linq.XElement("ranged_property", new System.Xml.Linq.XAttribute("name", "StartTime"),
            new System.Xml.Linq.XAttribute("min", start), new System.Xml.Linq.XAttribute("max", start)));
        var simulation = new TotalWarVfxSimulation(Convert(doc.Write()));
        Assert.That(simulation.CycleDuration, Is.EqualTo(expected).Within(.00001));
    }

    [Test]
    public void InitialFraming_UsesPreviewGeometryWithoutChangingTheActualSimulation()
    {
        var conversion = Convert(Source(""));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.2f, Matrix.Identity);
        var time = simulation.Time; var count = simulation.ParticleCount;
        var bounds = VfxPreviewBounds.Estimate(conversion);
        Assert.Multiple(() =>
        {
            Assert.That(bounds, Is.Not.Null);
            Assert.That(bounds!.Value.Max.X - bounds.Value.Min.X, Is.GreaterThan(1));
            Assert.That(simulation.Time, Is.EqualTo(time));
            Assert.That(simulation.ParticleCount, Is.EqualTo(count));
        });
    }

    private static VfxConversion Convert(byte[] bytes) => TotalWarVfxConverter.Convert(new(bytes, "vfx/library-test.xml", _ => [1]));
    internal static string Constant(string channel, float value) => FormattableString.Invariant($"{channel}_min,0,0;0,{value},linear,linear,0,0,0,0,1:{channel},0;0,0");
    private static string Rgb(float value) => string.Join('|', new[] { "r", "g", "b" }.Select(c => Constant(c, value).Split(':')[0])) + ":r,0;0,0|g,0;1,1|b,0;2,2";
    internal static byte[] Source(string modifiers) => Encoding.UTF8.GetBytes($"""
        <vfx revision="2"><emitters><emitter id="visible" type="continuous_mesh">
        <property name="EmissionRateCurve">{Constant("x", 1)}</property><property name="ParticleLifeTime">{Constant("x", 1)}</property>
        <property name="ParticleScale">{string.Join('|', new[]{"x","y","z"}.Select(c=>Constant(c,1).Split(':')[0]))}:x,0;0,0|y,0;1,1|z,0;2,2</property>
        <property name="InfiniteLife" value="true"/></emitter></emitters>
        <behaviour_chains><behaviour_chain id="visible"><quality value="0">{modifiers}
        <modifier fragment="ps_texture"><texture_property name="diffuse" path="vfx/texture.dds"/></modifier>
        </quality></behaviour_chain></behaviour_chains></vfx>
        """);
}

[NonParallelizable]
public class VfxGradientPixelTests
{
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void Gradient_LooksUpFilteredTextureRedAndIndependentAlphaBeforeCoverage(bool mapAlpha, bool premultiplied)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new global::Test.TestingUtility.Shared.GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            var direct = (SharpDX.Direct3D11.Device)device.Handle;
            using var preview = EffekseerNative.AecnVfxCreate(direct.NativePointer, direct.ImmediateContext.NativePointer);
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, [0, 0, 0, 128, 255, 255, 255, 128], 8, 2, 1, 0));
            var pixels = new byte[256 * 4];
            for (var i = 0; i < 256; i++)
            {
                pixels[i * 4] = (byte)MathF.Round(i * i / 255f);
                pixels[i * 4 + 1] = (byte)(255 - i);
                pixels[i * 4 + 3] = (byte)MathF.Round(255 - i * .5f);
            }
            EffekseerNative.Check(EffekseerNative.AecnVfxTextureRgba(preview, pixels, pixels.Length, 256, 1, 0));
            var layer = EffekseerLayer.Create(); layer.GradientIndex = 1; layer.GradientAlphaMapping = mapAlpha ? 1 : 0;
            layer.MaterialMode = premultiplied ? 1 : 0;
            EffekseerNative.Check(EffekseerNative.AecnVfxConfigure(preview, [layer], 1));
            using var target = new Microsoft.Xna.Framework.Graphics.RenderTarget2D(device, 65, 65, false,
                Microsoft.Xna.Framework.Graphics.SurfaceFormat.Vector4, Microsoft.Xna.Framework.Graphics.DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(Color.Transparent); device.DepthStencilState = Microsoft.Xna.Framework.Graphics.DepthStencilState.Default;
            var view = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up);
            var lens = Matrix.CreateOrthographic(2, 2, .1f, 100) * Matrix.CreateScale(-1, 1, 1);
            var particle = new EffekseerParticle { Transform = Matrix.CreateScale(2), Colour = Vector4.One, UV = new(0, 0, 1, 1) };
            EffekseerNative.Check(EffekseerNative.AecnVfxDraw(preview, Values(view), Values(lens), [particle], 1));
            device.SetRenderTarget(null); var output = new Vector4[65 * 65]; target.GetData(output);
            var sourceAlpha = MathF.Pow((128f / 255 + .055f) / 1.055f, 2.4f);
            var alpha = mapAlpha ? 1 - sourceAlpha * .5f : sourceAlpha;
            var expected = new Vector3(.25f, .5f, 0);
            expected = premultiplied ? Vector3.Min(expected, new(alpha)) : expected * alpha;
            Assert.That(Vector3.Distance(new(output[32 * 65 + 32].X, output[32 * 65 + 32].Y, output[32 * 65 + 32].Z), expected), Is.LessThan(.009f),
                "Map the filtered red value, not particle age or a precoloured texture; preserve independent alpha mapping.");
        }
        finally { graphics.Release(); }
    }

    private static float[] Values(Matrix m) => [m.M11,m.M12,m.M13,m.M14,m.M21,m.M22,m.M23,m.M24,m.M31,m.M32,m.M33,m.M34,m.M41,m.M42,m.M43,m.M44];
}
