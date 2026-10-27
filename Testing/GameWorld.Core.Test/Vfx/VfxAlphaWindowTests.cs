using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxAlphaWindowTests
{
    [TestCase(0, .5f)]
    [TestCase(.5f, .4f)]
    [TestCase(1, .25f)]
    public void RandomOffsets_KeepTheParticlesExistingRandomWeight(float random, float expected)
    {
        var definition = VfxModelVariantTests.Convert(VfxModelVariantTests.Source(1)).Emitters.Single();
        var window = new VfxPreviewEmitter
        {
            Name = definition.Name, Render = definition.Render, Emission = definition.Emission, Life = definition.Life,
            Scale = definition.Scale, InitialScale = definition.InitialScale, InitialAlpha = definition.InitialAlpha,
            Position = definition.Position, Orientation = definition.Orientation, Speed = definition.Speed,
            SpawnBox = definition.SpawnBox, SpawnRadius = definition.SpawnRadius, Colour = definition.Colour,
            Force = definition.Force, Deceleration = definition.Deceleration,
            Alpha = new("alpha,0,0;0,0,linear,linear,0,0,0,0,1;1,1,linear,linear,0,0,0,0,1:alpha,0;0,0", "curve_alpha", 1),
            AlphaStartOffset = new(-.1f, .1f), AlphaEndOffset = new(.5f, .5f),
        };
        Assert.That(window.AlphaAt(.2, random), Is.EqualTo(expected).Within(.00001f));
    }

    [TestCase(.1f, 0)]
    [TestCase(.3f, .25f)]
    [TestCase(.5f, .75f)]
    [TestCase(.7f, 1)]
    public void AlphaOffsets_RemapOnlyTheAlphaCurveWithinTheAuthoredLifeWindow(float phase, float expected)
    {
        var document = VfxDocument.Read(VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)));
        emitter.Elements().Single(n => (string)n.Attribute("name")! == "ParticleLifeTime").Value = VfxLibraryVisibilityTests.Constant("x", 2);
        document.Root.Descendants("behaviour_chain").Single().Element("quality")!.Add(new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"),
            new XElement("property", new XAttribute("name", "curve_alpha"), new XAttribute("type", "curve"), "alpha,0,0;0,0,linear,linear,0,0,0,0,1;1,1,linear,linear,0,0,0,0,1:alpha,0;0,0"),
            new XElement("property", new XAttribute("name", "alpha_START_offset"), new XAttribute("value", "float2(0.2,0.2)")),
            new XElement("property", new XAttribute("name", "alpha_END_offset"), new XAttribute("value", "float2(0.4,0.4)"))));
        var bytes = document.Write(); var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/alpha-window.xml", _ => [1]));
        Assert.That(result.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(result); simulation.Seek(phase * 2, Matrix.Identity);
        var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);
        // Inspect the authored curve before the separately asserted native render gate.
        var particle = simulation.ProjectedFrame(camera with { VfxLowOpacityCulling = -1 }).Single().Render;
        Assert.Multiple(() =>
        {
            Assert.That(particle.Colour.W, Is.EqualTo(expected).Within(.00001f));
            Assert.That(particle.AppearancePhase, Is.EqualTo(phase).Within(.00001f));
            Assert.That(result.Diagnostics.Any(d => d.Field is "alpha_START_offset" or "alpha_END_offset"), Is.False);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
            if (expected == 0) Assert.That(simulation.ProjectedFrame(camera), Is.Empty);
        });
    }
}
