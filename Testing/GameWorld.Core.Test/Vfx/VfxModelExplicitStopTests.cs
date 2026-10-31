#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

// Source controls exercise explicit caller stop independently of authored particle lifetime.
[NonParallelizable]
public class VfxModelExplicitStopTests
{
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 0, 10),
        Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(null, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    public void OnlyExplicitTrueClearsModelsAtCallerStop(bool? flag, bool clears)
    {
        var source = Source(flag); var conversion = Convert(source);
        Assert.That(conversion.Emitters.Single().DestroyOnExplicitStop, Is.EqualTo(clears));
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .253f };
        simulation.Seek(.25f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
        Assert.That(Value<float>(Raw(simulation).Single(), "Life"), Is.EqualTo(5));
        simulation.Seek(.253f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(clears ? 0 : 1));
        Assert.That(simulation.IsDrained, Is.EqualTo(clears));
        simulation.Seek(.8f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(clears ? 0 : 1));
        Assert.That(conversion.Emitters.Single().Life.Maximum, Is.EqualTo(5));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [TestCase(0f)]
    [TestCase(.25f)]
    [TestCase(.253f)]
    public void ExactFractionalAndZeroCutoffCannotRespawnTheClearedRoot(float cutoff)
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(true, many: true))) { EmissionEndTimeSeconds = cutoff };
        simulation.Seek(cutoff, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        Assert.That(simulation.IsDrained, Is.True);
        simulation.Seek(cutoff + .4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        Assert.That(simulation.ProjectedFrame(Camera), Is.Empty);
    }

    [Test]
    public void NaturalFiniteEmitterEndDoesNotApplyTheExplicitStopFlag()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(true, finite: true)));
        simulation.Seek(.7f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
        Assert.That(Value<float>(Raw(simulation).Single(), "Life"), Is.EqualTo(5));
        Assert.That(simulation.IsDrained, Is.False);
        simulation.Seek(5.1f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void PauseAndChangedCurrentAttachmentKeepUnstoppedAttachedModels()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(true)));
        simulation.Seek(.4f, Matrix.Identity);
        var particle = Raw(simulation).Single();
        Matrix History(float _) => Matrix.CreateRotationX(.8f) * Matrix.CreateTranslation(3, 1, -2);
        simulation.SeekWithCurrentAttachment(.4f, History(.4f), History);
        Assert.That(Raw(simulation).Single(), Is.SameAs(particle));
        Assert.That(simulation.ProjectedFrame(Camera), Has.Length.EqualTo(1));
    }

    [Test]
    public void CheckpointsRewindAcrossStopAndReplayExactPreStopPackets()
    {
        var conversion = Convert(Source(true, many: true));
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 1.003f };
        Assert.That(simulation.EnableSeekCheckpoints(), Is.True);
        simulation.Seek(1.4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        foreach (var target in new[] { .7f, .8f, 1.003f, .637f, 1.31f })
        {
            simulation.Seek(target, Matrix.Identity);
            var fresh = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = 1.003f };
            fresh.Seek(target, Matrix.Identity);
            Assert.That(simulation.Frame(Camera), Is.EqualTo(fresh.Frame(Camera)));
            Assert.That(simulation.ParticleCount, Is.EqualTo(fresh.ParticleCount));
        }
        Assert.That(simulation.SeekCheckpointRestoreCount, Is.GreaterThan(0));
    }

    [TestCase(null)]
    [TestCase(.8f)]
    public void RemovingOrExtendingFractionalStopRestoresAccurateSurvivingParticles(float? newCutoff)
    {
        var conversion = Convert(Source(true, many: true));
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .253f };
        simulation.Seek(.253f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.EmissionEndTimeSeconds = newCutoff;
        simulation.Seek(.253f, Matrix.Identity);
        var fresh = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = newCutoff };
        fresh.Seek(.253f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
        Assert.That(simulation.Frame(Camera), Is.EqualTo(fresh.Frame(Camera)));
    }

    [Test]
    public void MovingStopIntoTheAlreadySimulatedPastRebuildsTheCorrectEmptyState()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(true, many: true)));
        simulation.Seek(.4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
        simulation.EmissionEndTimeSeconds = .253f;
        simulation.Seek(.4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void ContinuousDistanceModelTrailStopsAtTheCallerBoundary()
    {
        var simulation = new TotalWarVfxSimulation(Convert(Source(true, spacing: true))) { EmissionEndTimeSeconds = .253f };
        Matrix History(float time) => Matrix.CreateTranslation(time * 4, 0, 0);
        simulation.SeekContinuousTrail(.2f, History(.2f), History);
        Assert.That(simulation.ParticleCount, Is.GreaterThan(0));
        simulation.SeekContinuousTrail(.253f, History(.253f), History);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.SeekContinuousTrail(.7f, History(.7f), History);
        Assert.That(simulation.ParticleCount, Is.Zero);
        Assert.That(simulation.IsDrained, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InteractivePreparationAppliesFractionalStopOnlyWhenTheFrameIsComplete(bool continuous)
    {
        var conversion = Convert(Source(true, many: !continuous, spacing: continuous));
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .253f };
        Matrix History(float time) => Matrix.CreateTranslation(time * 4, 0, 0);
        var completed = false;
        for (var slice = 0; slice < 200 && !completed; slice++)
            completed = simulation.PrepareInteractiveSeek(.253f, History(.253f), History,
                useCurrentAttachmentForFrame: continuous, continueMotionTrails: continuous, budget: TimeSpan.Zero);
        Assert.That(completed, Is.True);
        Assert.That(simulation.ParticleCount, Is.Zero);
        Assert.That(simulation.IsDrained, Is.True);
        simulation.SeekFrameForPreparation(.7f, History(.7f), History, continuous, null, CancellationToken.None);
        Assert.That(simulation.ParticleCount, Is.Zero);
    }

    [Test]
    public void ExplicitStopDoesNotInvokeOnDeathOrDeleteIndependentFalseLayers()
    {
        var doc = VfxDocument.Read(Source(true)); var parent = doc.Root.Descendants("emitter").Single();
        parent.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")),
            new XElement("property_event", new XAttribute("name", "OnParticleDeath"), new XAttribute("vfx_name", "child"),
                new XAttribute("spawn_event_time_offset", 0), new XAttribute("track_position", "true"),
                new XAttribute("track_scale", "false"), new XAttribute("track_colour", "false"),
                new XAttribute("track_alpha", "false"), new XAttribute("track_lifetime", "false"),
                new XAttribute("track_orientation_mode", "orientation")));
        var child = VfxParticleEventTests.Source(false);
        var conversion = TotalWarVfxConverter.Convert(new(doc.Write(), "diagnostic/model-stop-death.xml", path =>
            path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters[0].Events, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .253f };
        simulation.Seek(.253f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero);
        simulation.Seek(5.3f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.Zero, "Owner stop must not manufacture the authored OnDeath child.");

        var composition = VfxDocument.CreateComposition();
        composition.AddReference("stop", VfxDocument.Read(Source(true))).Apply();
        composition.AddReference("tail", VfxDocument.Read(Source(false))).Apply();
        var mixed = TotalWarVfxConverter.Convert(new(composition.Write(), "diagnostic/mixed-model-stop.xml", path =>
            path.EndsWith("stop.xml", StringComparison.OrdinalIgnoreCase) ? Source(true)
                : path.EndsWith("tail.xml", StringComparison.OrdinalIgnoreCase) ? Source(false) : VfxModelVariantTests.Resolve(path)));
        var separate = new TotalWarVfxSimulation(mixed) { EmissionEndTimeSeconds = .253f };
        separate.Seek(.4f, Matrix.Identity);
        Assert.That(separate.ParticleCount, Is.EqualTo(1));
        Assert.That(Value<object>(Value<object>(Raw(separate).Single(), "Emitter"), "Definition"), Is.SameAs(mixed.Emitters[1]));
    }

    [Test]
    public void OrdinarySpriteFlagRemainsOnItsExistingNaturalTailRoute()
    {
        var doc = VfxDocument.Read(Source(true)); doc.Root.Descendants("emitter").Single().SetAttributeValue("type", "continuous_mesh");
        doc.Root.Descendants("property_rigid_data").Remove();
        var conversion = Convert(doc.Write());
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .253f };
        simulation.Seek(.4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
        Assert.That(conversion.Emitters.Single().DestroyOnExplicitStop, Is.False);
    }

    [Test]
    public void RootCallerStopDoesNotClearAnExistingEventChildModel()
    {
        var doc = VfxDocument.Read(Source(false)); var emitter = doc.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")),
            new XElement("property_event", new XAttribute("name", "OnParticleSpawn"), new XAttribute("vfx_name", "child"),
                new XAttribute("spawn_event_time_offset", 0), new XAttribute("track_position", "true"),
                new XAttribute("track_scale", "false"), new XAttribute("track_colour", "false"),
                new XAttribute("track_alpha", "false"), new XAttribute("track_lifetime", "false"),
                new XAttribute("track_orientation_mode", "orientation")));
        var child = Source(true, finite: true);
        var conversion = TotalWarVfxConverter.Convert(new(doc.Write(), "diagnostic/model-stop-child.xml", path =>
            path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child : VfxModelVariantTests.Resolve(path)));
        var simulation = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .253f };
        simulation.Seek(.4f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(2));
        Assert.That(Raw(simulation).Count(particle => Value<object>(Value<object>(particle, "Emitter"), "Parent") != null), Is.EqualTo(1));
        Assert.That(simulation.Frame(Camera), Has.Length.EqualTo(2));
    }

    [Test]
    public async Task ConstantBackgroundPreparationPublishesTheAccurateStoppedState()
    {
        var conversion = Convert(Source(true, many: true));
        var context = new VfxConstantReloadContext(Matrix.Identity, Matrix.Identity, null, .253f, false, false, false);
        using var worker = new VfxConstantReloadPreparation(conversion, context, .253f, true);
        TotalWarVfxSimulation? actual = null;
        for (var wait = 0; wait < 5000; wait++)
        {
            if (worker.TryTake(out var result)) { actual = result!.Simulation; break; }
            Assert.That(worker.Failure, Is.Null);
            await Task.Delay(1);
        }
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual!.ParticleCount, Is.Zero);
        Assert.That(actual.IsDrained, Is.True);
        var fresh = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = .253f };
        fresh.Seek(.253f, Matrix.Identity);
        Assert.That(actual.Frame(Camera), Is.EqualTo(fresh.Frame(Camera)));
    }

    [TestCase(false, 122)]
    [TestCase(true, 101)]
    public void CrossingStopConsumesTheLastActiveRateOrDistanceFractionAndKeepsItsSpawnChildren(bool distance, int children)
    {
        VfxConversion WithSpawnChildren(bool destroy)
        {
            var doc = VfxDocument.Read(Source(destroy, many: !distance, spacing: distance));
            var emitter = doc.Root.Descendants("emitter").Single();
            if (distance) emitter.Elements().Single(node => (string?)node.Attribute("name") == "SpawnSpacing").SetAttributeValue("value", ".01");
            else emitter.Elements().Single(node => (string?)node.Attribute("name") == "EmissionRateCurve").Value = VfxLibraryVisibilityTests.Constant("x", 480);
            emitter.Add(new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")),
                new XElement("property_event", new XAttribute("name", "OnParticleSpawn"), new XAttribute("vfx_name", "child"),
                    new XAttribute("spawn_event_time_offset", 0), new XAttribute("track_position", "true"),
                    new XAttribute("track_scale", "false"), new XAttribute("track_colour", "false"),
                    new XAttribute("track_alpha", "false"), new XAttribute("track_lifetime", "false"),
                    new XAttribute("track_orientation_mode", "orientation")));
            var child = VfxParticleEventTests.Source(false);
            return TotalWarVfxConverter.Convert(new(doc.Write(), "diagnostic/model-stop-spawn.xml", path =>
                path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child : VfxModelVariantTests.Resolve(path)));
        }
        var stopped = new TotalWarVfxSimulation(WithSpawnChildren(true)) { EmissionEndTimeSeconds = .253f };
        var control = new TotalWarVfxSimulation(WithSpawnChildren(false)) { EmissionEndTimeSeconds = .253f };
        Matrix History(float time) => Matrix.CreateTranslation(time * 4, 0, 0);
        if (distance)
        {
            stopped.SeekContinuousTrail(.4f, History(.4f), History);
            control.SeekContinuousTrail(.4f, History(.4f), History);
        }
        else
        {
            stopped.Seek(.4f, History(.4f), History);
            control.Seek(.4f, History(.4f), History);
        }
        var expected = Raw(control).Where(particle => Value<object>(Value<object>(particle, "Emitter"), "Parent") != null).ToArray();
        Assert.That(expected, Has.Length.EqualTo(children));
        Assert.That(Raw(stopped), Has.Length.EqualTo(children));
        Assert.That(Raw(stopped).All(particle => Value<object>(Value<object>(particle, "Emitter"), "Parent") != null), Is.True);
        Assert.That(Raw(stopped).Select(particle => Value<double>(particle, "Birth")),
            Is.EqualTo(expected.Select(particle => Value<double>(particle, "Birth"))));
    }

    private static object[] Raw(TotalWarVfxSimulation simulation)
        => ((IEnumerable)simulation.GetType().GetField("_particles", Private)!.GetValue(simulation)!).Cast<object>().ToArray();
    private static T Value<T>(object source, string name) => (T)source.GetType().GetField(name, Private)!.GetValue(source)!;
    private static VfxConversion Convert(byte[] source) => TotalWarVfxConverter.Convert(new(source, "diagnostic/model-stop.xml", VfxModelVariantTests.Resolve));
    private static byte[] Source(bool? destroy, bool finite = false, bool many = false, bool spacing = false)
    {
        var doc = VfxDocument.Read(VfxModelVariantTests.Source(1)); var emitter = doc.Root.Descendants("emitter").Single();
        void Set(string name, object value, bool attribute = false)
        {
            emitter.Elements().Where(node => (string?)node.Attribute("name") == name).Remove();
            var text = System.Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!;
            emitter.Add(attribute ? new XElement("property", new XAttribute("name", name), new XAttribute("value", text))
                : new XElement("property", new XAttribute("name", name), text));
        }
        Set("InfiniteLife", finite ? "false" : "true", true);
        Set("MaximumParticles", many || spacing ? 128 : 1, true);
        Set("EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", spacing ? 0 : many ? 120 : 1));
        Set("ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 5));
        Set("ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        Set("ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION", true);
        if (destroy.HasValue) Set("ParticleDestroyImmediately", destroy.Value ? "true" : "false", true);
        if (spacing) Set("SpawnSpacing", .1f, true);
        if (finite) emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", .1f), new XAttribute("max", .1f)));
        return doc.Write();
    }
}
