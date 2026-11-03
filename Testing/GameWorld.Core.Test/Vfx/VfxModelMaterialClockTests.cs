#nullable enable
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelMaterialClockTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(.5f, 1f)]
    [TestCase(1f, 1f)]
    [TestCase(2f, 1f)]
    [TestCase(2f, 1.5f)]
    public void ModelMaterialAge_UsesAllReferenceSpeedFactorsWhileTheGlobalClockKeepsPreviewSeconds(float outerSpeed, float innerSpeed)
    {
        var leaf = Leaf(material: true);
        var conversion = Referenced(leaf, outerSpeed, innerSpeed);
        Assert.That(conversion.MeshMaterials.Single().Parameters, Has.Length.EqualTo(VfxLegacyRigidDissolveMaterial.ParameterCount));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.25f, Matrix.Identity);
        var first = simulation.Frame(Camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(first.Time, Is.EqualTo(.25f * outerSpeed * innerSpeed).Within(.00001f));
            Assert.That(first.Time, Is.EqualTo(first.AppearancePhase * 2).Within(.00001f), "The native instance clock uses source seconds, not a normalized lifetime fraction.");
            Assert.That(first.MaterialTime, Is.EqualTo(.25f), "The shipped camera clock is independent of reference simulation speed.");
        });
        simulation.Seek(.4f, Matrix.Identity);
        simulation.Seek(.25f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single(), Is.EqualTo(first), "Rewinding must recover the same material clocks and birth randoms.");
    }

    [Test]
    public void StaticModelLoop_ExportsTheRepeatedSourceAgeRatherThanElapsedAgeOrPhase()
    {
        var leaf = Leaf(material: true);
        var emitter = leaf.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleLoopParams"), new XAttribute("x", .2f), new XAttribute("y", .4f)));
        var conversion = Referenced(leaf, 2, 1);
        Assert.That(conversion.Emitters.Single().MeshLoop, Is.EqualTo(new Vector2(.2f, .4f)));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(.55f, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(particle.Time, Is.EqualTo(.7f).Within(.00001f), "Source age 1.1 repeats within the original 0.4-0.8 second interval.");
            Assert.That(particle.AppearancePhase, Is.EqualTo(.35f).Within(.00001f));
            Assert.That(particle.MaterialTime, Is.EqualTo(.55f));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void OrdinarySpriteAndUnadaptedModel_RetainTheirExistingTimeField(bool model)
    {
        var leaf = Leaf(material: false, model: model);
        var conversion = Referenced(leaf, 2, 1);
        Assert.That(conversion.MeshMaterials, Is.Empty);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.25f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().Time, Is.EqualTo(.25f).Within(.00001f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MappedLaterPartOrSelectedVariant_ReceivesSourceAgeAndBirthUvEvenWhenTheFirstPartIsOrdinary(bool variant)
    {
        var conversion = Referenced(Leaf(material: true), 2, 1);
        var emitter = conversion.Emitters.Single();
        var mapped = emitter.Render;
        var ordinary = mapped; ordinary.MeshMaterialIndex = -1; ordinary.OpaqueModel = 0;
        emitter.Render = ordinary;
        // DIAG: exercise the actual selected render-part packet independently of converter admission rules.
        if (variant) emitter.ModelVariants = [[mapped]];
        else emitter.AdditionalModelParts = [mapped];
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.25f, Matrix.Identity);
        var particle = VfxModelInstanceUvTests.Particle(simulation);
        particle.GetType().GetField("RandomAlpha")!.SetValue(particle, 1f / 2048);
        particle.GetType().GetField("ModelVariant")!.SetValue(particle, variant ? 1 : 0);
        var frame = simulation.ProjectedFrame(Camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(emitter.Render.MeshMaterialIndex, Is.EqualTo(-1));
            Assert.That(frame.ModelVariant, Is.EqualTo(variant ? 1 : 0));
            Assert.That(frame.Render.Time, Is.EqualTo(.5f).Within(.00001f));
            Assert.That(frame.Render.MaterialTime, Is.EqualTo(.25f));
            Assert.That(frame.Render.NormalRotation, Is.EqualTo(new Vector4(.49462890625f, .11962890625f, 0, 1)));
        });
        if (variant)
        {
            particle.GetType().GetField("ModelVariant")!.SetValue(particle, 0);
            var plainFrame = simulation.Frame(Camera).Single();
            Assert.Multiple(() =>
            {
                Assert.That(plainFrame.Time, Is.EqualTo(.25f).Within(.00001f), "An unselected mapped variant must not change the ordinary selected variant.");
                Assert.That(plainFrame.NormalRotation.X, Is.Zero);
                Assert.That(plainFrame.NormalRotation.Y, Is.Zero);
            });
        }
    }

    private static VfxDocument Leaf(bool material, bool model = true)
    {
        var leaf = VfxDocument.Read(material ? VfxOpaqueModelMaterialTests.Source(Vector3.One, 1)
            : model ? VfxModelVariantTests.Source(1) : VfxLibraryVisibilityTests.Source(""));
        var emitter = leaf.Root.Descendants("emitter").Single();
        emitter.Elements().Single(x => (string?)x.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        return leaf;
    }

    private static VfxConversion Referenced(VfxDocument leaf, float outerSpeed, float innerSpeed)
    {
        var middle = VfxDocument.CreateComposition();
        var inner = middle.AddReference("clock-leaf", leaf); inner.Apply();
        inner.Selection.SetAttributeValue("simulation_speed_multiplier", innerSpeed);
        var root = VfxDocument.CreateComposition();
        var outer = root.AddReference("clock-middle", middle); outer.Apply();
        outer.Selection.SetAttributeValue("simulation_speed_multiplier", outerSpeed);
        var material = Encoding.UTF8.GetBytes(VfxLegacyRigidDissolveMaterialTests.Material().ToString());
        var conversion = TotalWarVfxConverter.Convert(new(root.Write(), "vfx/material-clock-diag.xml", path =>
        {
            path = path.Replace('\\', '/');
            if (path.EndsWith("clock-middle.xml", StringComparison.Ordinal)) return middle.Write();
            if (path.EndsWith("clock-leaf.xml", StringComparison.Ordinal)) return leaf.Write();
            if (path == "vfx/character.xml.material") return material;
            return path.EndsWith(".wsmodel", StringComparison.Ordinal) ? VfxOpaqueModelMaterialTests.Resolve(path, [255])
                : VfxModelVariantTests.Resolve(path);
        }));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return conversion;
    }
}

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxModelMaterialClockNativeTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ScrollingEmission_UsesSourceSecondsOnBothLegacyAndOneShotMaterials(bool oneShot)
    {
        var fast = Render(2, .25f, oneShot);
        var sourceControl = Render(1, .5f, oneShot);
        var wrongClockControl = Render(1, .25f, oneShot);
        Assert.Multiple(() =>
        {
            Assert.That(fast.Count(p => p.X > .001f), Is.GreaterThan(100));
            Assert.That(fast.Zip(sourceControl).Max(p => Vector4.Distance(p.First, p.Second)), Is.LessThan(.0001f));
            Assert.That(fast.Zip(wrongClockControl).Count(p => Vector4.Distance(p.First, p.Second) > .005f), Is.GreaterThan(100));
        });
    }

    private static Vector4[] Render(float speed, float seconds, bool oneShot)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var material = oneShot ? VfxRigidDissolveMaterialTests.Material() : VfxLegacyRigidDissolveMaterialTests.Material();
            foreach (var field in new[] { "diffuse_tint", "edge_emissive_colour" }) VfxGemstoneMaterialTests.Set(material, field, "0,0,0");
            foreach (var field in new[] { "emissive_a_brightness", "emissive_b_brightness", "threshold_source" }) VfxGemstoneMaterialTests.Set(material, field, "1");
            foreach (var field in new[] { "emissive_a_channels", "emissive_b_channels" }) VfxGemstoneMaterialTests.Set(material, field, "1,0,0,0");
            VfxGemstoneMaterialTests.Set(material, "emissive_tint", "1,1,1");
            VfxGemstoneMaterialTests.Set(material, "emissive_a_direction", "1,0");
            if (oneShot) VfxGemstoneMaterialTests.Set(material, "emissive_intensity", "1");
            foreach (var source in material.Descendants("source"))
                source.Value = source.Parent!.Element("slot")!.Value switch
                {
                    "t_xml_base_colour" or "t_xml_dither" => "black.dds",
                    "t_xml_emissive_a" or "t_xml_emissive_a_texture" => "scrolling-emission.dds",
                    _ => "white.dds",
                };
            var pattern = VfxModelVariantPixelTests.Dds(Color.White);
            for (var texel = 0; texel < 16; ++texel) pattern[128 + texel * 4] = (byte)(20 + texel * 13);
            var leaf = VfxDocument.Read(VfxOpaqueModelMaterialTests.Source(Vector3.One, 1));
            var emitter = leaf.Root.Descendants("emitter").Single();
            emitter.Elements().Single(x => (string?)x.Attribute("name") == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
            emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
            var root = VfxDocument.CreateComposition(); var reference = root.AddReference("scrolling-leaf", leaf); reference.Apply();
            reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
            byte[]? Resolve(string path)
            {
                path = path.Replace('\\', '/');
                if (path.EndsWith("scrolling-leaf.xml", StringComparison.Ordinal)) return leaf.Write();
                return path switch
                {
                    "vfx/character.xml.material" => Encoding.UTF8.GetBytes(material.ToString()),
                    "black.dds" => VfxModelVariantPixelTests.Dds(Color.Black),
                    "scrolling-emission.dds" => pattern,
                    _ => VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White)),
                };
            }
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(root.Write(), "vfx/material-clock-gpu-diag.xml", Resolve));
            Assert.That(preview.EmitterCount, Is.EqualTo(1), string.Join(';', preview.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
            preview.Seek(seconds, Matrix.Identity);
            var camera = new Vector3(.5f, .5f, 10);
            using var target = new RenderTarget2D(device, 128, 128, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1, 0);
            preview.Draw(new CommonShaderParameters(Matrix.CreateLookAt(camera, new(.5f, .5f, 0), Vector3.Up),
                Matrix.CreateOrthographic(2, 2, .1f, 100), camera, Vector3.Zero, 0, 0, 0, 1, Vector3.One, []));
            device.SetRenderTarget(null); var pixels = new Vector4[128 * 128]; target.GetData(pixels);
            return pixels;
        }
        finally { graphics.Release(); }
    }
}
