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

[NonParallelizable]
[Apartment(System.Threading.ApartmentState.STA)]
public class VfxParticleEventTests
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    [TestCase("true", 1)]
    [TestCase("false", 0)]
    [TestCase(null, 0)]
    public void NoModelHelper_TriggersOnlyExplicitlyEnabledEvents(string? enable, int visible)
    {
        var source = Source(true, enable); var child = Source(false);
        var lookups = 0;
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/parent.xml", path => { lookups++; return child; }));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        Assert.Multiple(() =>
        {
            Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(visible));
            Assert.That(lookups, Is.EqualTo(visible));
            Assert.That(VfxPreviewInspector.Inspect(new(source, "vfx/parent.xml", _ => child)).EmitterCount, Is.EqualTo(1));
            Assert.That(conversion.TotalEmitterCount, Is.EqualTo(1), "Event templates are dependencies, not authored root layers.");
            Assert.That(conversion.Diagnostics.Any(d => d.Field == "RigidData" && d.ReasonKey == "Vfx.Preview.GeometryUnsupported"), Is.False);
            Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
        });
    }

    [TestCase("OnParticleSpawn", .5f, .4f, 0)]
    [TestCase("OnParticleSpawn", .5f, .6f, 1)]
    [TestCase("OnParticleSpawn", -.5f, .2f, 1)]
    [TestCase("OnParticleDeath", .5f, .9f, 0)]
    [TestCase("OnParticleDeath", .5f, 1.1f, 1)]
    public void TriggerTiming_MatchesNativeBirthDelayAndDeathBoundary(string name, float offset, float time, int count)
    {
        var conversion = TotalWarVfxConverter.Convert(new(Source(true, "true", name, offset), "vfx/parent.xml", _ => Source(false)));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(time, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(count));
        simulation.Seek(0, Matrix.Identity); simulation.Seek(time, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(count), "Rewind must replay each event exactly once.");
    }

    [Test]
    public void ChildTail_SurvivesParentAndExtendsPreviewCycle()
    {
        var conversion = TotalWarVfxConverter.Convert(new(Source(true, "true", "OnParticleDeath"), "vfx/parent.xml", _ => Source(false)));
        var simulation = new TotalWarVfxSimulation(conversion);
        Assert.That(simulation.CycleDuration, Is.GreaterThanOrEqualTo(3));
        simulation.Seek(2.8f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        simulation.Seek(3.2f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [Test]
    public void ReferencedParent_DoesNotApplyItsOffsetTwiceToEventTemplates()
    {
        var parent = VfxDocument.Read(Source(true, "true")); var composition = VfxDocument.CreateComposition();
        var edit = composition.AddReference("parent", parent); edit.Apply(); edit.Selection.SetAttributeValue("pos_x", 3);
        var child = VfxDocument.Read(Source(false));
        child.Root.Descendants("emitter").Single().Add(new XElement("property", new XAttribute("name", "EmitterPosition"),
            Curve(["x", "y", "z"], 2, 0, 0)));
        var conversion = TotalWarVfxConverter.Convert(new(composition.Write(), "vfx/root.xml",
            p => p.EndsWith("parent.xml", StringComparison.OrdinalIgnoreCase) ? parent.Write() : child.Write()));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.CreateTranslation(4, 0, 0));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(2), "A referenced helper must resolve its event graph once.");
        Assert.That(simulation.Frame(Camera).Single().Transform.Translation.X, Is.EqualTo(9).Within(.00001));
    }

    [Test]
    public void MissingEventResource_LeavesAnIndependentRenderedParentVisible()
    {
        var doc = VfxDocument.Read(VfxModelVariantTests.Source(1));
        var emitter = doc.Root.Descendants("emitter").Single(); emitter.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")));
        emitter.Add(Event("OnParticleSpawn", 0));
        var conversion = TotalWarVfxConverter.Convert(new(doc.Write(), "vfx/parent.xml", p =>
            p.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? null : VfxModelVariantTests.Resolve(p)));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        Assert.That(conversion.Diagnostics.Any(d => d.Field.StartsWith("OnParticleSpawn", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void MeshEvent_RemainsUntriggeredAsInTheNativeVisibleParentControl()
    {
        var doc = VfxDocument.Read(Source(false)); var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")), Event("OnParticleSpawn", 0));
        var lookups = 0;
        var conversion = TotalWarVfxConverter.Convert(new(doc.Write(), "vfx/parent.xml", _ => { lookups++; return Source(false); }));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
        Assert.That(lookups, Is.Zero);
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.ParticleEventEmitterUnsupported"), Is.True);
    }

    [TestCase(true, 7f)]
    [TestCase(false, 5f)]
    public void VisibleRigidEventAnchor_UsesBirthPointAndStopsFollowingAfterParentDeath(bool track, float expected)
    {
        var parent = VfxDocument.Read(Source(true, "true")); var emitter = parent.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleInitialSpeed"), Curve(["x"], 2)),
            new XElement("property", new XAttribute("name", "EmitterOrientation"), Curve(["x", "y", "z"], 0, 0, -90)),
            new XElement("property", new XAttribute("name", "SpawnAngle"), new XAttribute("x", 0), new XAttribute("y", 0)),
            new XElement("ranged_property", new XAttribute("name", "SpawnSpread"), new XAttribute("min", 90), new XAttribute("max", 90)));
        emitter.Element("property_event")!.SetAttributeValue("track_position", track ? "true" : "false");
        emitter.Element("property_rigid_data")!.Add(new XElement("rigid_entry",
            new XElement("ModelFile", new XAttribute("value", "vfx/model-0.rigid_model_v2")),
            new XElement("PivotPoint", new XAttribute("x", .5), new XAttribute("y", .5), new XAttribute("z", .5))));
        var child = VfxDocument.Read(Source(false));
        child.Root.Descendants("emitter").Single().Add(new XElement("property",
            new XAttribute("name", "ParticleSimulationSpace"), new XAttribute("value", "LOCAL_SPACE_POSITION_ONLY")));
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml",
            path => path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path))));
        simulation.Seek(.2f, Matrix.CreateTranslation(5, 0, 0));
        Assert.That(simulation.Frame(Camera).Single(p => p.Layer != 0).Transform.Translation.X, Is.EqualTo(5).Within(.00001));
        simulation.Seek(1.02f, Matrix.CreateTranslation(7, 0, 0));
        simulation.Seek(1.3f, Matrix.CreateTranslation(11, 0, 0));
        Assert.That(simulation.Frame(Camera).Single().Transform.Translation.X, Is.EqualTo(expected).Within(.00001));
    }

    [Test]
    public void TerrainHelper_RequiresAnExplicitReceiverAndReplaysWhenItBecomesAvailable()
    {
        var parent = VfxDocument.Read(Source(true, "true")); var emitter = parent.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticlesSpawnOnTerrain"), new XAttribute("value", "true")),
            new XElement("property", new XAttribute("name", "ParticleRelativeSpawnOrientation"), new XAttribute("value", "RELATIVE_TO_TERRAIN_NORMAL")));
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", _ => Source(false))));
        simulation.Seek(.2f, Matrix.CreateTranslation(0, 4, 0));
        Assert.That(simulation.Frame(Camera with { VfxGroundHeight = null }), Is.Empty);
        Assert.That(simulation.TerrainBasisUnsupported, Is.True);
        Assert.That(simulation.Frame(Camera).Single().Transform.Translation.Y, Is.EqualTo(0).Within(.00001));
    }

    [Test]
    public void DenseEvents_HitTheInstanceBudgetBeforeTheGlobalParticleBudgetAndCanRewind()
    {
        var parent = VfxDocument.Read(Source(true, "true")); var emitter = parent.Root.Descendants("emitter").Single();
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "MaximumParticles").SetAttributeValue("value", 65536);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value = Curve(["x"], 10000);
        var duration = emitter.Elements().Single(e => (string?)e.Attribute("name") == "LifeTime");
        duration.SetAttributeValue("min", 1); duration.SetAttributeValue("max", 1);
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", _ => Source(false))));
        simulation.Seek(.5f, Matrix.Identity);
        Assert.That(simulation.CapacityReached, Is.True);
        Assert.That(simulation.ParticleCount, Is.LessThan(TotalWarVfxSimulation.ParticleLimit));
        simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.CapacityReached, Is.False);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
    }

    [Test]
    public void EventCycle_IsBoundedAndDiagnosed()
    {
        var source = Source(true, "true");
        var conversion = TotalWarVfxConverter.Convert(new(source, "vfx/child.xml", _ => source));
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.ReferenceCycle"), Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(2, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Is.Empty);
    }

    [Test]
    public void EventChild_CanSelectAnotherLayerFromItsParentLibraryWithoutAFalseCycle()
    {
        var parent = VfxDocument.Read(Source(true, "true"));
        var sprite = new XElement(VfxDocument.Read(Source(false)).Root.Descendants("emitter").Single());
        sprite.SetAttributeValue("id", "selected_sprite");
        parent.Root.Element("vfx")!.Element("emitters")!.Add(sprite);
        var child = VfxDocument.CreateComposition(); var edit = child.AddReference("parent", parent); edit.Apply();
        foreach (var emitter in edit.Selection.Elements("emitter"))
            emitter.SetAttributeValue("enable", (string?)emitter.Attribute("name") == "selected_sprite" ? "true" : "false");
        var root = VfxDocument.CreateComposition(); var rootEdit = root.AddReference("parent", parent); rootEdit.Apply();
        rootEdit.Selection.Elements("emitter").Single(e => (string?)e.Attribute("name") == "selected_sprite").Remove();
        var conversion = TotalWarVfxConverter.Convert(new(root.Write(), "vfx/root.xml", p =>
            p.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : parent.Write()));
        Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.ReferenceCycle"), Is.False);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(1));
    }

    [TestCase(true, 0)]
    [TestCase(false, 1)]
    public void TrackedLifetime_StopsEmissionAndPreservesExistingChildTails(bool track, int minimum)
    {
        var parent = VfxDocument.Read(Source(true, "true"));
        parent.Root.Descendants("property_event").Single().SetAttributeValue("track_lifetime", track ? "true" : "false");
        var child = VfxDocument.Read(Source(false)); var emitter = child.Root.Descendants("emitter").Single();
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "InfiniteLife").SetAttributeValue("value", "true");
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "MaximumParticles").SetAttributeValue("value", 64);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleLifeTime").Value = Curve(["x"], .4f);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "EmissionRateCurve").Value = Curve(["x"], 8);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", _ => child.Write()));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(1.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Length, Is.GreaterThan(0), "Stopping emission must preserve the .4 second child tail.");
        simulation.Seek(1.5f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Length, minimum == 0 ? Is.Zero : Is.GreaterThanOrEqualTo(minimum));
    }

    [TestCase(true, 1.5f)]
    [TestCase(false, 1f)]
    public void ScaleTracking_UsesCurrentParentCurveForAlreadyBornChild(bool track, float expected)
    {
        var parent = VfxDocument.Read(Source(true, "true"));
        parent.Root.Descendants("property_event").Single().SetAttributeValue("track_scale", track ? "true" : "false");
        var emitter = parent.Root.Descendants("emitter").Single();
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleLifeTime").Value = Curve(["x"], 4);
        emitter.Elements().Single(e => (string?)e.Attribute("name") == "ParticleScale").Value = string.Join('|', new[] { "x", "y", "z" }
            .Select(a => $"{a}_min,0,0;0,1,linear,linear,0,0,0,0,1;0.5,2,linear,linear,0,0,0,0,1;1,1,linear,linear,0,0,0,0,1")) + ":x,0;0,0|y,0;1,1|z,0;2,2";
        var simulation = new TotalWarVfxSimulation(TotalWarVfxConverter.Convert(new(parent.Write(), "vfx/parent.xml", _ => Source(false))));
        simulation.Seek(1, Matrix.Identity);
        Assert.That(simulation.Frame(Camera).Single().Transform.Right.Length(), Is.EqualTo(expected).Within(.00001));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativePreview_DrawsEventChildAndAcceptsACompletelyNonRenderingHelper(bool enabled)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice; using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(Source(true, enabled ? "true" : "false"), "vfx/parent.xml", _ => Source(false)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            Assert.That(preview.Diagnostics.Any(d => d.ReasonKey == "Vfx.Preview.RuntimeUnavailable"), Is.False);
            using var target = new RenderTarget2D(device, 64, 64, false, SurfaceFormat.Color, DepthFormat.Depth24);
            preview.Seek(.2f, Matrix.Identity); device.SetRenderTarget(target); device.Clear(Color.Black);
            preview.Draw(Camera with { View = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up), Projection = Matrix.CreateOrthographic(2, 2, .1f, 100) });
            device.SetRenderTarget(null); var pixels = new Color[4096]; target.GetData(pixels);
            Assert.That(pixels.Count(p => p.R > 100 && p.G > 100), enabled ? Is.GreaterThan(100) : Is.Zero);
        }
        finally { graphics.Release(); }
    }

    internal static byte[] Source(bool helper, string? enable = null, string eventName = "OnParticleSpawn", float offset = 0)
    {
        var emitter = new XElement("emitter", new XAttribute("id", "event_test"), new XAttribute("type", helper ? "continuous_rigid" : "continuous_mesh"),
            new XElement("property", new XAttribute("name", "Enable"), new XAttribute("value", "true")),
            new XElement("property", new XAttribute("name", "InfiniteLife"), new XAttribute("value", "false")),
            new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", .01), new XAttribute("max", .01)),
            new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), Curve(["x"], helper ? 1 : 2)),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), Curve(["x"], 1)),
            new XElement("property", new XAttribute("name", "ParticleScale"), Curve(["x", "y", "z"], 1, 1, 1)));
        if (enable != null) emitter.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", enable)));
        if (helper) emitter.Add(new XElement("property_rigid_data", new XAttribute("name", "RigidData")), Event(eventName, offset));
        return Encoding.UTF8.GetBytes(new XElement("vfx", new XAttribute("revision", 2), new XElement("emitters", emitter)).ToString(SaveOptions.DisableFormatting));
    }

    private static XElement Event(string name, float offset) => new("property_event", new XAttribute("name", name), new XAttribute("vfx_name", "child"),
        new XAttribute("spawn_event_time_offset", offset), new XAttribute("track_position", "true"), new XAttribute("track_scale", "false"),
        new XAttribute("track_colour", "false"), new XAttribute("track_alpha", "false"), new XAttribute("track_lifetime", "false"), new XAttribute("track_orientation_mode", "orientation"));
    internal static string Curve(string[] axes, params float[] values) => string.Join('|', axes.Select((a, i) =>
        $"{a}_min,0,0;0,{values[i].ToString(System.Globalization.CultureInfo.InvariantCulture)},linear,linear,0,0,0,0,1"))
        + ":" + string.Join('|', axes.Select((a, i) => $"{a},0;{i},{i}"));
}
