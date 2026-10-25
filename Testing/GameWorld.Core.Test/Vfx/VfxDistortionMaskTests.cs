#nullable enable
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxDistortionMaskTests
{
    [Test]
    public void Distortion_KeepsDiffuseOpacitySeparateFromItsNormalTexture()
    {
        var source = Source();
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var emitter = conversion.Emitters.Single();
        var render = emitter.Render;
        Assert.Multiple(() =>
        {
            Assert.That(render.DistortionMaskIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(render.DistortionMaskIndex, Is.Not.EqualTo(render.TextureIndex));
            Assert.That(conversion.TexturePaths[render.TextureIndex], Is.EqualTo("vfx/normal.dds"));
            Assert.That(conversion.TextureUsages[render.TextureIndex], Is.EqualTo(VfxTextureUsage.DistortionNormal));
            Assert.That(conversion.Textures[render.TextureIndex], Is.EqualTo(Normal));
            Assert.That(conversion.TexturePaths[render.DistortionMaskIndex], Is.EqualTo("vfx/mask.dds"));
            Assert.That(conversion.TextureUsages[render.DistortionMaskIndex], Is.EqualTo(VfxTextureUsage.Diffuse));
            Assert.That(conversion.Textures[render.DistortionMaskIndex], Is.EqualTo(Mask));
            Assert.That(emitter.PremultipliedAlpha, Is.False);
            Assert.That(render.Blend, Is.EqualTo(1), "Captured background is coverage, not additive emission.");
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.DistortionCandidate"), Is.True);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [Test]
    public void SharedTexturePath_StillKeepsNormalAndDiffuseSamplingRolesDistinct()
    {
        var source = Source(maskPath: "vfx/normal.dds");
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var render = conversion.Emitters.Single().Render;
        Assert.Multiple(() =>
        {
            Assert.That(render.DistortionMaskIndex, Is.Not.EqualTo(render.TextureIndex));
            Assert.That(conversion.TexturePaths[render.DistortionMaskIndex], Is.EqualTo(conversion.TexturePaths[render.TextureIndex]));
            Assert.That(conversion.Textures[render.DistortionMaskIndex], Is.EqualTo(conversion.Textures[render.TextureIndex]));
            Assert.That(conversion.TextureUsages[render.DistortionMaskIndex], Is.EqualTo(VfxTextureUsage.Diffuse));
            Assert.That(conversion.TextureUsages[render.TextureIndex], Is.EqualTo(VfxTextureUsage.DistortionNormal));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DistortionGradient_PreservesTheIndependentAlphaMappingSwitch(bool mapAlpha)
    {
        var source = Source(gradient: true, mapAlpha: mapAlpha);
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var render = conversion.Emitters.Single().Render;
        Assert.Multiple(() =>
        {
            Assert.That(render.GradientIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(render.GradientIndex, Is.Not.EqualTo(render.DistortionMaskIndex));
            Assert.That(render.GradientAlphaMapping, Is.EqualTo(mapAlpha ? 1 : 0));
            Assert.That(conversion.TextureUsages[render.GradientIndex], Is.EqualTo(VfxTextureUsage.GradientValue));
            Assert.That(conversion.Textures[render.GradientIndex].Where((_, index) => index % 4 == 3), Has.All.EqualTo((byte)128));
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.False);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.GradientCandidate"), Is.True);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.DistortionCandidate"), Is.True);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DistortionCrush_ReachesEachParticleWithoutRequiringPremultipliedColour(bool premultiplied, bool sharp)
    {
        var source = Source(crush: true, premultiplied: premultiplied, sharp: sharp);
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        simulation.Seek(3, Matrix.Identity);
        var frame = simulation.Frame(camera);
        Assert.That(frame, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(frame[0].AlphaCrush.X, Is.EqualTo(.5f).Within(.005f));
            Assert.That(frame[0].AlphaCrush.Y, Is.EqualTo(sharp ? 2 : 1));
            Assert.That(conversion.Emitters.Single().PremultipliedAlpha, Is.False);
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "ps_alpha_crush" && d.ReasonKey == "Vfx.Preview.Unmapped"), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
        simulation.Seek(2.5f, Matrix.Identity);
        simulation.Advance(.5f, Matrix.Identity);
        Assert.That(simulation.Frame(camera), Is.EqualTo(frame));
    }

    [TestCase(3f, 1f)]
    [TestCase(1f, 3f)]
    [TestCase(3f, 2f)]
    public void DistortionOpacity_PreservesValuesAboveOneUntilTheTextureMaskIsSampled(float initialAlpha, float vertexAlpha)
    {
        var document = VfxDocument.Read(Source());
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleInitialAlpha"), Constant("x", initialAlpha)));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"),
            new XElement("property", new XAttribute("name", "curve_alpha"), Constant("alpha", vertexAlpha))));
        var source = document.Write();
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        simulation.Seek(3, Matrix.Identity);
        Assert.That(simulation.Frame(camera).Single().Colour.W, Is.EqualTo(initialAlpha * vertexAlpha).Within(.0001f));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void MissingDiffuseMask_DisablesOnlyItsDependentLayerAndRollsBackResources()
    {
        var document = VfxDocument.Read(Source(maskPath: "vfx/missing.dds"));
        var ordinary = VfxDocument.Read(Source(distortion: false, id: "ordinary"));
        document.Root.Element("vfx")!.Element("emitters")!.Add(new XElement(ordinary.Root.Descendants("emitter").Single()));
        document.Root.Element("vfx")!.Element("behaviour_chains")!.Add(new XElement(ordinary.Root.Descendants("behaviour_chain").Single()));
        var source = document.Write();
        var conversion = Convert(source);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.TotalEmitterCount, Is.EqualTo(2));
            Assert.That(conversion.Emitters.Select(e => e.Name), Is.EqualTo(new[] { "ordinary" }));
            Assert.That(conversion.Diagnostics.Any(d => d.Layer == "distortion" && d.Field == @"vfx\missing.dds"
                && d.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
            Assert.That(conversion.Textures, Has.Count.EqualTo(1), "Failed mask loading must not leave a normal texture in a later layer's resource indexes.");
            Assert.That(conversion.Textures.Single(), Is.EqualTo(Mask));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [Test]
    public void DistortionWithoutAnyTexture_DisablesOnlyThatLayerBeforeNativeConfiguration()
    {
        var document = VfxDocument.Read(Source());
        foreach (var texture in document.Root.Descendants("texture_property")) texture.SetAttributeValue("path", "");
        var ordinary = VfxDocument.Read(Source(distortion: false, id: "ordinary"));
        document.Root.Element("vfx")!.Element("emitters")!.Add(new XElement(ordinary.Root.Descendants("emitter").Single()));
        document.Root.Element("vfx")!.Element("behaviour_chains")!.Add(new XElement(ordinary.Root.Descendants("behaviour_chain").Single()));
        var source = document.Write();
        var conversion = Convert(source);
        Assert.Multiple(() =>
        {
            Assert.That(conversion.TotalEmitterCount, Is.EqualTo(2));
            Assert.That(conversion.Emitters.Select(emitter => emitter.Name), Is.EqualTo(new[] { "ordinary" }));
            Assert.That(conversion.Diagnostics.Any(d => d.Layer == "distortion" && d.Field == "ps_texture"
                && d.ReasonKey == "Vfx.Preview.MaterialUnsupported"), Is.True);
            Assert.That(conversion.Textures, Has.Count.EqualTo(1));
            Assert.That(conversion.Textures.Single(), Is.EqualTo(Mask));
            Assert.That(conversion.TextureUsages.Single(), Is.EqualTo(VfxTextureUsage.Diffuse));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedReference_RebasesMaskAlongsideNormalAndGradientResources(bool hasNormal)
    {
        var childDocument = VfxDocument.Read(Source(gradient: true));
        if (!hasNormal) childDocument.Root.Descendants("texture_property").Single(p => (string?)p.Attribute("name") == "normal").Remove();
        var child = childDocument.Write();
        var middle = VfxDocument.Read(Source(distortion: false, id: "middle"));
        middle.AddReference("child", VfxDocument.Read(child)).Apply();
        var middleBytes = middle.Write();
        var parent = VfxDocument.Read(Source(distortion: false, id: "parent"));
        parent.AddReference("middle", middle).Apply();
        var parentBytes = parent.Write();
        var conversion = TotalWarVfxConverter.Convert(new(parentBytes, "vfx/parent.xml", path => path switch
        {
            @"vfx\middle.xml" => middleBytes,
            @"vfx\child.xml" => child,
            _ => Resolve(path),
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(3));
        var render = conversion.Emitters.Single(e => e.Name == "distortion").Render;
        Assert.Multiple(() =>
        {
            Assert.That(render.TextureIndex, hasNormal ? Is.GreaterThanOrEqualTo(2) : Is.EqualTo(-1));
            Assert.That(render.DistortionMaskIndex, Is.GreaterThanOrEqualTo(2));
            Assert.That(render.GradientIndex, Is.GreaterThanOrEqualTo(2));
            Assert.That(new[] { render.TextureIndex, render.DistortionMaskIndex, render.GradientIndex }.Distinct().ToArray(), Has.Length.EqualTo(3));
            if (hasNormal) Assert.That(conversion.Textures[render.TextureIndex], Is.EqualTo(Normal));
            Assert.That(conversion.Textures[render.DistortionMaskIndex], Is.EqualTo(Mask));
            Assert.That(conversion.TextureUsages[render.DistortionMaskIndex], Is.EqualTo(VfxTextureUsage.Diffuse));
            Assert.That(conversion.TextureUsages[render.GradientIndex], Is.EqualTo(VfxTextureUsage.GradientValue));
            Assert.That(parent.Write(), Is.EqualTo(parentBytes));
            Assert.That(middle.Write(), Is.EqualTo(middleBytes));
            Assert.That(VfxDocument.Read(child).Write(), Is.EqualTo(child));
        });
    }

    [Test]
    public void DistortionWithoutNormal_KeepsOnlyItsRealDiffuseAndGradientResources()
    {
        var document = VfxDocument.Read(Source(gradient: true));
        document.Root.Descendants("texture_property").Single(p => (string?)p.Attribute("name") == "normal").Remove();
        var source = document.Write();
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var render = conversion.Emitters.Single().Render;
        Assert.Multiple(() =>
        {
            Assert.That(render.TextureIndex, Is.EqualTo(-1), "The geometric basis path must not upload a fabricated normal.");
            Assert.That(render.DistortionMaskIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(render.GradientIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(conversion.Textures, Has.Count.EqualTo(2));
            Assert.That(conversion.TextureUsages, Does.Not.Contain(VfxTextureUsage.DistortionNormal));
            Assert.That(conversion.Textures[render.DistortionMaskIndex], Is.EqualTo(Mask));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void DistortionWithoutDiffuse_KeepsItsRealNormalAndRawVertexAlpha(bool emptyField, bool gradient)
    {
        var document = VfxDocument.Read(Source(gradient: gradient));
        var diffuse = document.Root.Descendants("texture_property").Single(p => (string?)p.Attribute("name") == "diffuse");
        if (emptyField) diffuse.SetAttributeValue("path", ""); else diffuse.Remove();
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleInitialAlpha"), Constant("x", 2)));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"),
            new XElement("property", new XAttribute("name", "curve_alpha"), Constant("alpha", .75f))));
        var source = document.Write();
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var render = conversion.Emitters.Single().Render;
        var simulation = new TotalWarVfxSimulation(conversion);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        simulation.Seek(3, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(render.DistortionMaskIndex, Is.EqualTo(-1), "An omitted diffuse uses raw particle alpha, without a replacement mask.");
            Assert.That(render.TextureIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(conversion.Textures[render.TextureIndex], Is.EqualTo(Normal));
            Assert.That(conversion.TextureUsages, Does.Not.Contain(VfxTextureUsage.Diffuse));
            Assert.That(conversion.Textures, Has.Count.EqualTo(gradient ? 2 : 1));
            Assert.That(render.GradientIndex, gradient ? Is.GreaterThanOrEqualTo(0) : Is.EqualTo(-1));
            Assert.That(render.GradientAlphaMapping, Is.EqualTo(gradient ? 1 : 0));
            Assert.That(simulation.Frame(camera).Single().Colour.W, Is.EqualTo(1.5f).Within(.0001f));
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [Test]
    public void OrdinaryColourTexture_DoesNotAcquireAnExtraDistortionMask()
    {
        var conversion = Convert(Source(distortion: false, premultiplied: true));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var emitter = conversion.Emitters.Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Render.Distortion, Is.Zero);
            Assert.That(emitter.Render.DistortionMaskIndex, Is.EqualTo(-1));
            Assert.That(emitter.PremultipliedAlpha, Is.True);
            Assert.That(conversion.Textures, Has.Count.EqualTo(1));
            Assert.That(conversion.Textures[emitter.Render.TextureIndex], Is.EqualTo(Mask));
            Assert.That(conversion.TextureUsages[emitter.Render.TextureIndex], Is.EqualTo(VfxTextureUsage.Diffuse));
        });
    }

    [TestCase(0f, 0f)]
    [TestCase(0f, .25f)]
    [TestCase(.75f, 2f)]
    public void DistortionSoftDepth_RetainsTheEnabledModifierAndAuthoredScaleStrength(float factor, float strength)
    {
        var document = VfxDocument.Read(Source());
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_soft_edge"),
            new XElement("property", new XAttribute("name", "soft_factor"), new XAttribute("value", factor.ToString(CultureInfo.InvariantCulture))),
            new XElement("property", new XAttribute("name", "scale_strength"), new XAttribute("value", strength.ToString(CultureInfo.InvariantCulture)))));
        var source = document.Write();
        var conversion = Convert(source);
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var render = conversion.Emitters.Single().Render;
        Assert.Multiple(() =>
        {
            Assert.That(render.DistortionSoftDepth, Is.EqualTo(1), "A zero soft factor still enables native depth sampling and its .001 denominator.");
            Assert.That(render.SoftDistance, Is.EqualTo(factor));
            Assert.That(render.DistortionSoftScaleStrength, Is.EqualTo(strength));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "scale_strength" && d.ReasonKey == "Vfx.Preview.Unmapped"), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase(.25f, 2f, .25f)]
    [TestCase(2f, .125f, .125f)]
    [TestCase(-.5f, 2f, -.5f)]
    [TestCase(2f, -.25f, -.25f)]
    [TestCase(-.5f, -2f, -2f)]
    public void DistortionSoftDepth_PassesTheSignedMinimumParticleScale(float x, float y, float expected)
    {
        var document = VfxDocument.Read(Source());
        var scale = $"{Constant("x", x).Split(':')[0]}|{Constant("y", y).Split(':')[0]}|{Constant("z", 3).Split(':')[0]}:x,0;0,0|y,0;1,1|z,0;2,2";
        document.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "ParticleScale"), scale));
        var source = document.Write();
        var simulation = new TotalWarVfxSimulation(Convert(source));
        simulation.Seek(3, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        var frame = simulation.Frame(camera);
        Assert.That(frame, Has.Length.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(frame[0].ParticleSize, Is.EqualTo(expected).Within(.00001f), "Native takes signed min(X,Y), then saturates in the pixel shader; absolute scale changes negative-scale depth semantics.");
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [Test]
    public void DistortionSoftDepth_AppendsItsAbiFieldsAndKeepsAbsentModifierDisabled()
    {
        var layer = EffekseerLayer.Create();
        Assert.Multiple(() =>
        {
            Assert.That(Marshal.SizeOf<EffekseerLayer>(), Is.EqualTo(132));
            Assert.That(Marshal.OffsetOf<EffekseerLayer>(nameof(EffekseerLayer.DistortionMaskIndex)).ToInt32(), Is.EqualTo(116));
            Assert.That(Marshal.OffsetOf<EffekseerLayer>(nameof(EffekseerLayer.DistortionSoftScaleStrength)).ToInt32(), Is.EqualTo(120));
            Assert.That(Marshal.OffsetOf<EffekseerLayer>(nameof(EffekseerLayer.DistortionSoftDepth)).ToInt32(), Is.EqualTo(124));
            Assert.That(Marshal.OffsetOf<EffekseerLayer>(nameof(EffekseerLayer.NativeSoftDepth)).ToInt32(), Is.EqualTo(128));
            Assert.That(layer.DistortionSoftScaleStrength, Is.EqualTo(1));
            Assert.That(layer.DistortionSoftDepth, Is.Zero);
            Assert.That(Convert(Source()).Emitters.Single().Render.DistortionSoftDepth, Is.Zero);
        });
    }

    [TestCase(false, 2f, false)]
    [TestCase(true, .5f, true)]
    [TestCase(true, 2f, false)]
    public void OrdinarySoftDepth_EnablesNativeCoverageOnlyForSupportedPremultipliedSprites(bool premultiplied, float strength, bool supported)
    {
        var document = VfxDocument.Read(Source(distortion: false, premultiplied: premultiplied));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_soft_edge"),
            new XElement("property", new XAttribute("name", "soft_factor"), new XAttribute("value", ".75")),
            new XElement("property", new XAttribute("name", "scale_strength"), new XAttribute("value", strength.ToString(CultureInfo.InvariantCulture)))));
        var conversion = Convert(document.Write());
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().Render.DistortionSoftDepth, Is.Zero);
            Assert.That(conversion.Emitters.Single().Render.SoftDistance, Is.EqualTo(.75f));
            Assert.That(conversion.Emitters.Single().Render.NativeSoftDepth, Is.EqualTo(supported ? 1 : 0));
            Assert.That(conversion.Emitters.Single().Render.DistortionSoftScaleStrength, Is.EqualTo(strength));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "scale_strength" && d.ReasonKey == "Vfx.Preview.Unmapped"), Is.EqualTo(!supported));
        });
    }

    [TestCase(-.3f)]
    [TestCase(65505f)]
    public void OrdinarySoftDepth_OutOfRangeFactorKeepsTheCandidatePath(float factor)
    {
        var document = VfxDocument.Read(Source(distortion: false, premultiplied: true));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "ps_soft_edge"),
            new XElement("property", new XAttribute("name", "soft_factor"), new XAttribute("value", factor.ToString(CultureInfo.InvariantCulture)))));
        var conversion = Convert(document.Write());
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters.Single().Render.NativeSoftDepth, Is.Zero);
            Assert.That(conversion.Emitters.Single().Render.SoftDistance, Is.EqualTo(Math.Max(0, factor)));
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "soft_factor" && d.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
        });
    }

    private static readonly byte[] Normal = [10, 20, 30];
    private static readonly byte[] Mask = [40, 50, 60];

    private static byte[]? Resolve(string path) => path.Replace('\\', '/') switch
    {
        "vfx/normal.dds" => Normal,
        "vfx/mask.dds" => Mask,
        _ => null,
    };

    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "vfx/distortion.xml", Resolve));

    private static byte[] Source(bool distortion = true, bool gradient = false, bool mapAlpha = true, bool crush = false,
        bool premultiplied = false, bool sharp = false, string maskPath = "vfx/mask.dds", string id = "distortion")
    {
        var gradientModifier = gradient ? $"""
            <modifier fragment="ps_gradient"><property name="curve_colour">{Rgb(4)}</property>
            <property name="curve_alpha">{Constant("alpha", .5f)}</property><property name="alpha_mapping" value="{mapAlpha.ToString().ToLowerInvariant()}"/></modifier>
            """ : "";
        var crushModifier = crush ? $"""
            <modifier fragment="ps_alpha_crush"><property name="curve_alpha_crush">progress,0,1;0,0,linear,linear,0,0,0,0,1;1,1,linear,linear,0,0,0,0,1:progress,0;0,0</property>
            <property name="sharp_transition" value="{sharp.ToString().ToLowerInvariant()}"/></modifier>
            """ : "";
        var renderStack = distortion ? $"""
            <effect_render_stacks><effect_render_stack id="{id}"><property name="scene_view_type" quality="0" value="DISTORTION"/></effect_render_stack></effect_render_stacks>
            """ : "";
        return Encoding.UTF8.GetBytes($"""
            <vfx revision="2"><emitters><emitter id="{id}" type="continuous_mesh">
            <property name="InfiniteLife" value="true"/><property name="MaximumParticles" value="1"/>
            <property name="EmissionRateCurve">{Constant("x", .5f)}</property><property name="ParticleLifeTime">{Constant("x", 2)}</property>
            </emitter></emitters><behaviour_chains><behaviour_chain id="{id}"><quality value="0">
            <modifier fragment="vs_translation"/><modifier fragment="ps_texture"><texture_property name="diffuse" path="{maskPath}"/>
            <texture_property name="normal" path="vfx/normal.dds"/><property name="pre_multiplied_alpha" value="{premultiplied.ToString().ToLowerInvariant()}"/></modifier>
            {gradientModifier}{crushModifier}</quality></behaviour_chain></behaviour_chains>{renderStack}</vfx>
            """);
    }

    private static string Constant(string axis, float value) => $"{axis},0,1;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1:{axis},0;0,0";
    private static string Rgb(float value) => $"{Constant("r", value).Split(':')[0]}|{Constant("g", value).Split(':')[0]}|{Constant("b", value).Split(':')[0]}:r,0;0,0|g,0;1,1|b,0;2,2";
}
