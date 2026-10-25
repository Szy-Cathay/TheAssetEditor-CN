#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxAtlasNativePhaseTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(.4f, .5f, .25f)]
    [TestCase(.4f, .5f, 1.5f)]
    [TestCase(2f, .001f, .75f)]
    [TestCase(2f, .001f, 2.5f)]
    public void LifetimeAtlas_InterpolatesLoopEndpointsByLifeInsteadOfBirthRandom(float start, float end, float time)
    {
        var bytes = Source("using_particle_lifetime", start, end);
        var simulation = Simulation(bytes);
        simulation.Seek(time, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        var phase = particle.Time / 4.0;
        var expected = NativeFrame(phase * (start + (end - start) * phase), 64, true);
        Assert.That(particle.Flipbook, Is.EqualTo(expected).Within(.00002));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    [TestCase("using_particle_lifetime")]
    [TestCase("fixed_animation_speed")]
    public void RandomCell_KeepsItsFractionalBlendPhaseAtBirth(string mode)
    {
        var simulation = Simulation(Source(mode, 0, 0, randomCell: true));
        simulation.Seek(.25f, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        var random = RandomAlpha(RawParticles(simulation).Single());
        Assert.That(particle.Flipbook, Is.EqualTo(random * 64).Within(.00002));
        Assert.That(particle.Flipbook - MathF.Floor(particle.Flipbook), Is.GreaterThan(.0001),
            "The fixed seed must exercise a fractional initial crossfade.");
    }

    [TestCase("using_particle_lifetime")]
    [TestCase("fixed_animation_speed")]
    public void RandomRow_IsAppliedWithoutRestrictingAnimationToOneRow(string mode)
    {
        var simulation = Simulation(Source(mode, 0, 0, columns: 2, rows: 2, randomRow: true, rate: 32));
        simulation.Seek(.75f, Matrix.Identity);
        var particles = simulation.Frame(Camera);
        var raw = RawParticles(simulation);
        Assert.That(particles, Has.Length.EqualTo(raw.Length));
        var rowCount = 0;
        for (var index = 0; index < particles.Length; index++)
        {
            var expectedRow = (int)MathF.Floor(RandomAlpha(raw[index]) * 2);
            rowCount += expectedRow;
            Assert.That(particles[index].UV.Y, Is.EqualTo(expectedRow * .5f).Within(.00001),
                "Random row is independent of the row-restriction flag.");
        }
        Assert.That(rowCount, Is.GreaterThan(0), "The fixed seed must exercise the second row.");
    }

    [TestCase("using_particle_lifetime", false)]
    [TestCase("using_particle_lifetime", true)]
    [TestCase("fixed_animation_speed", false)]
    [TestCase("fixed_animation_speed", true)]
    public void NonLoopingAtlas_HoldsItsLastFrameWithOrWithoutRandomOffset(string mode, bool randomCell)
    {
        var simulation = Simulation(Source(mode, 4, 4, loop: false, randomCell: randomCell));
        simulation.Seek(3, Matrix.Identity);
        var particle = simulation.Frame(Camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(particle.Flipbook, Is.EqualTo(63));
            Assert.That(particle.UV, Is.EqualTo(new Vector4(.875f, .875f, .125f, .125f)));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public void RestrictedRow_AddsTheAuthoredRowOnlyOnce(int row)
    {
        var simulation = Simulation(Source("using_particle_lifetime", 0, 0, columns: 2, rows: 2,
            restricted: true, row: row));
        simulation.Seek(.25f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().UV, Is.EqualTo(new Vector4(0, row * .5f, .5f, .5f)));
    }

    [Test]
    public void UnrestrictedRandomRow_DoesNotWrapTheRowOffsetBackIntoTheSheet()
    {
        var atlas = new VfxAtlas(2, 2, Animated: true, Loops: new(1, 1), RandomRow: true);
        var rectangle = TotalWarVfxSimulation.AtlasRectangle(atlas, 0, 1, 1, .75);
        Assert.That(rectangle, Is.EqualTo(new Vector4(.5f, 1, .5f, .5f)),
            "The native row offset may cross the sheet boundary and must reach texture sampling unchanged.");
    }

    [TestCase(.5f, .5f)]
    [TestCase(.2f, .8f)]
    [TestCase(1f, 0f)]
    [TestCase(0f, 1f)]
    public void FlipUv_UsesTheNative251And149Scrambles(float flipU, float flipV)
    {
        var simulation = Simulation(Source(null, 0, 0, rate: 32, flipU: flipU, flipV: flipV));
        simulation.Seek(.75f, Matrix.Identity);
        var particles = simulation.Frame(Camera);
        var raw = RawParticles(simulation);
        Assert.That(particles, Has.Length.EqualTo(raw.Length));
        for (var index = 0; index < particles.Length; index++)
        {
            var random = RandomAlpha(raw[index]);
            var u = Fraction(random * 251) > 1 - flipU;
            var v = Fraction(random * 149) > 1 - flipV;
            Assert.Multiple(() =>
            {
                Assert.That(particles[index].UV.Z < 0, Is.EqualTo(u), $"Particle {index} U");
                Assert.That(particles[index].UV.W < 0, Is.EqualTo(v), $"Particle {index} V");
            });
        }
    }

    [Test]
    public void StaticCells_KeepTheAuthoredIntegerCellAndOuterAtlas()
    {
        var bytes = Source(null, 0, 0);
        var document = XDocument.Parse(Encoding.UTF8.GetString(bytes));
        document.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_cell_texture"),
            Property("number_of_cells_x", 2), Property("number_of_cells_y", 2), Property("cell", 3),
            Property("atlas_number_of_cells_x", 2), Property("atlas_number_of_cells_y", 2), Property("atlas_cell", 3)));
        var simulation = Simulation(Encoding.UTF8.GetBytes(document.ToString()));
        simulation.Seek(.25f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().UV, Is.EqualTo(new Vector4(.75f, .75f, .25f, .25f)));
    }

    [Test]
    public void TrailDistribution_StaysRejectedUntilItsParticleIdContractHasAnOfficialExample()
    {
        var converted = TotalWarVfxConverter.Convert(new(Source("trail_distribution", 1, 1), "test.xml", _ => [1]));
        Assert.Multiple(() =>
        {
            Assert.That(converted.Emitters, Is.Empty);
            Assert.That(converted.Diagnostics.Any(d => d.Field == "time_mode" && d.ReasonKey == "Vfx.Preview.UvUnsupported"), Is.True);
        });
    }

    private static TotalWarVfxSimulation Simulation(byte[] bytes)
    {
        var converted = TotalWarVfxConverter.Convert(new(bytes, "vfx/test.xml", _ => [1]));
        Assert.That(converted.Emitters, Has.Count.EqualTo(1),
            string.Join(";", converted.Diagnostics.Select(d => d.Field + ":" + d.ReasonKey)));
        return new(converted);
    }

    private static object[] RawParticles(TotalWarVfxSimulation simulation) =>
        ((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(simulation)!).Cast<object>().ToArray();

    private static float RandomAlpha(object particle) => (float)particle.GetType().GetField("RandomAlpha")!.GetValue(particle)!;
    private static float Fraction(float value) => value - MathF.Floor(value);
    private static float NativeFrame(double unary, int count, bool loop) =>
        (float)(loop ? (unary - Math.Floor(unary)) * count : Math.Clamp(unary * count, 0, count - 1));

    private static XElement Property(string name, object value) => new("property", new XAttribute("name", name),
        new XAttribute("value", value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value));

    private static byte[] Source(string? mode, float start, float end, int columns = 8, int rows = 8,
        bool loop = true, bool randomCell = false, bool randomRow = false, bool restricted = false, int row = 0,
        float rate = .1f, float? flipU = null, float? flipV = null)
    {
        var quality = new XElement("quality", new XAttribute("value", "0"),
            new XElement("modifier", new XAttribute("fragment", "vs_translation")),
            new XElement("modifier", new XAttribute("fragment", "ps_texture"),
                new XElement("texture_property", new XAttribute("name", "diffuse"), new XAttribute("path", "vfx/test.dds"))));
        if (mode != null)
            quality.Add(new XElement("modifier", new XAttribute("fragment", "vs_animated_texture"),
                Property("no_cells_xy", FormattableString.Invariant($"float2({columns},{rows})")),
                Property("no_loops", FormattableString.Invariant($"float2({start},{end})")), Property("time_mode", mode),
                Property("loop", loop ? "true" : "false"), Property("randomize_cell", randomCell ? "true" : "false"),
                Property("randomize_row", randomRow ? "true" : "false"), Property("row_restricted", restricted ? "true" : "false"),
                Property("row_restriction", row), Property("cell_blend", "true")));
        if (flipU != null || flipV != null)
            quality.Add(new XElement("modifier", new XAttribute("fragment", "vs_flip_uv"),
                Property("flip_u", flipU ?? 0), Property("flip_v", flipV ?? 0)));
        var document = new XDocument(new XElement("vfx", new XAttribute("revision", 2),
            new XElement("emitters", new XElement("emitter", new XAttribute("id", "test"), new XAttribute("type", "continuous_mesh"),
                new XElement("property", new XAttribute("name", "EmissionRateCurve"), Constant("x", rate)),
                new XElement("property", new XAttribute("name", "ParticleLifeTime"), Constant("x", 4)),
                new XElement("property", new XAttribute("name", "ParticleScale"), Constant("xyz", 2)), Property("InfiniteLife", "true"))),
            new XElement("behaviour_chains", new XElement("behaviour_chain", new XAttribute("id", "test"), quality))));
        return Encoding.UTF8.GetBytes(document.ToString());
    }

    private static string Constant(string axes, double value) => string.Join('|', axes.Select(axis =>
        $"{axis}_min,0,0;0,{value.ToString(CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1")) + ":" +
        string.Join('|', axes.Select((axis, index) => $"{axis},0;{index},{index}"));
}
