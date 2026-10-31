#nullable enable
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxModelConstantForceTests
{
    private const double Step = 1.0 / 120;
    private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Vector3 Force = new(127, -914, -63);
    private static CommonShaderParameters Camera(float? ground = null) => new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []) { VfxGroundHeight = ground };

    [TestCase(.5f, 0f, .6f, "WORLD_SPACE")]
    [TestCase(1f, 2f, .8f, "WORLD_SPACE")]
    [TestCase(2f, 10f, 1f, "WORLD_SPACE")]
    [TestCase(.5f, 10f, .8f, "LOCAL_SPACE_POSITION_ONLY")]
    [TestCase(1.4f, 2f, .6f, "LOCAL_SPACE_POSITION_ONLY")]
    [TestCase(2f, 0f, .8f, "LOCAL_SPACE_POSITION_ONLY")]
    public void ModelForceDampsFirstThenUsesSourceAccelerationAndHalfSquaredDelta(float speed, float damping, float scale, string space)
    {
        var conversion = Conversion(Source(space, damping), speed);
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.True);
        Assert.That(conversion.Emitters.Single().ParticleForceCurve, Is.False);
        var root = Root(scale);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, root); var birth = Snapshot(Particles(simulation).Single());
        simulation.Seek(.4f, root); var actual = Snapshot(Particles(simulation).Single());
        var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
        var expected = NativeOracle(birth, ticks, damping);
        Near(actual.WorldPosition, expected.Position); Near(actual.WorldVelocity, expected.Velocity);
        Assert.That(simulation.ForceBasisUnsupported, Is.False);
        Assert.That(actual.InitialScale, Is.EqualTo(birth.InitialScale));
        Assert.That(actual.RandomAlpha, Is.EqualTo(birth.RandomAlpha));
        // The former force-before-damping semi-implicit sprite step is deliberately a different oracle.
        var wrongVelocity = birth.WorldVelocity; var wrongPosition = birth.WorldPosition;
        var previewAcceleration = Force * .01f * birth.Scale * (birth.Speed * birth.Speed);
        var decay = MathF.Exp(-damping * ((float)Step * speed));
        for (var tick = 0; tick < ticks; tick++)
        {
            wrongVelocity = (wrongVelocity + previewAcceleration * (float)Step) * decay;
            wrongPosition += wrongVelocity * (float)Step;
        }
        Assert.That(Vector3.Distance(expected.Position, wrongPosition), Is.GreaterThan(.0001f));
    }

    [TestCase(.5f, 2f)]
    [TestCase(2f, .7f)]
    public void NestedReferencesUseTheProductOfSourceSpeeds(float innerSpeed, float outerSpeed)
    {
        var leaf = Source("WORLD_SPACE", 2); var inner = Wrap(leaf, innerSpeed, "leaf");
        var outer = Wrap(inner, outerSpeed, "inner");
        var conversion = TotalWarVfxConverter.Convert(new(outer, "diagnostic/nested-model-force.xml", path => path.Replace('/', '\\') switch
        {
            @"vfx\leaf.xml" => leaf, @"vfx\inner.xml" => inner, _ => VfxModelVariantTests.Resolve(path),
        }));
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); var root = Root(.8f);
        simulation.Seek(0, root); var birth = Snapshot(Particles(simulation).Single());
        Assert.That(birth.Speed, Is.EqualTo(innerSpeed * outerSpeed));
        simulation.Seek(.4f, root); var actual = Snapshot(Particles(simulation).Single());
        var expected = NativeOracle(birth, (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step), 2);
        Near(actual.WorldPosition, expected.Position); Near(actual.WorldVelocity, expected.Velocity);
    }

    [TestCase("WORLD_SPACE")]
    [TestCase("LOCAL_SPACE_POSITION_ONLY")]
    public void WorldForceKeepsItsBirthBasisWhilePositionOnlyFollowsAttachmentTranslation(string space)
    {
        var conversion = Conversion(Source(space, 2), 1.4f); var origin = Root(.8f);
        Matrix History(float time) { var root = origin; root.Translation += new Vector3(2, .1f, -.3f) * time; return root; }
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, origin, History); var birth = Snapshot(Particles(simulation).Single());
        simulation.Seek(.4f, History(.4f), History); var particle = Particles(simulation).Single();
        var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
        var expected = NativeOracle(birth, ticks, 2);
        var currentRoot = (Matrix)typeof(TotalWarVfxSimulation).GetMethod("ParticleRoot", Private)!
            .Invoke(simulation, [particle, (double).4f, false])!;
        var actualPosition = Vector3.Transform((Vector3)Field(particle, "Position"), currentRoot);
        var actualVelocity = Vector3.TransformNormal((Vector3)Field(particle, "Velocity"), currentRoot);
        var translation = space == "LOCAL_SPACE_POSITION_ONLY" ? History(.4f).Translation - origin.Translation : Vector3.Zero;
        Near(actualPosition, expected.Position + translation); Near(actualVelocity, expected.Velocity);
        Assert.That(simulation.ForceBasisUnsupported, Is.False);
        var fresh = new TotalWarVfxSimulation(conversion); fresh.Seek(.4f, History(.4f), History);
        Assert.That(Packet(simulation), Is.EqualTo(Packet(fresh)));
    }

    [Test]
    public void SeekRewindPartialTicksAndCheckpointsKeepExactPacketsAndRandomState()
    {
        var source = Source("LOCAL_SPACE_POSITION_ONLY", 2); var conversion = Conversion(source, 1.4f); var root = Root(.8f);
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.True);
        var checkpointed = new TotalWarVfxSimulation(conversion); Assert.That(checkpointed.EnableSeekCheckpoints(), Is.True);
        checkpointed.Seek(1.2f, root); checkpointed.Seek(.7f, root);
        Assert.That(checkpointed.SeekCheckpointRestoreCount, Is.GreaterThan(0));
        foreach (var time in new[] { .7f, .85f, .2f, .637f, 1.31f })
        {
            checkpointed.Seek(time, root); var fresh = new TotalWarVfxSimulation(conversion); fresh.Seek(time, root);
            Assert.That(Packet(checkpointed), Is.EqualTo(Packet(fresh)));
            Assert.That(Snapshot(Particles(checkpointed).Single()), Is.EqualTo(Snapshot(Particles(fresh).Single())));
        }
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public async Task ConstantWorkerAndTerrainBirthPreserveTheSameForceStateAndGroundReplay()
    {
        var conversion = Conversion(Source("LOCAL_SPACE_POSITION_ONLY", 2, spawnOnTerrain: true), 2); var root = Root(.6f);
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.True);
        Assert.That(conversion.Emitters.Single().ModelTerrainBirth, Is.True);
        var context = new VfxConstantReloadContext(root, root, 2, null, false, false, false);
        using var worker = new VfxConstantReloadPreparation(conversion, context, .637f, true);
        TotalWarVfxSimulation? actual = null;
        for (var wait = 0; wait < 5000; wait++)
        {
            if (worker.TryTake(out var result)) { actual = result!.Simulation; break; }
            Assert.That(worker.Failure, Is.Null); await Task.Delay(1);
        }
        Assert.That(actual, Is.Not.Null);
        var expected = Prepared(conversion, root, 2); expected.Seek(.637f, root);
        Assert.That(Packet(actual!, 2), Is.EqualTo(Packet(expected, 2)));
        Assert.That(Snapshot(Particles(actual!).Single()), Is.EqualTo(Snapshot(Particles(expected).Single())));
        actual!.ProjectedFrame(Camera(-3), false);
        var changed = Prepared(conversion, root, -3); changed.Seek(.637f, root);
        Assert.That(Packet(actual, -3), Is.EqualTo(Packet(changed, -3)));
        Assert.That(Snapshot(Particles(actual).Single()), Is.EqualTo(Snapshot(Particles(changed).Single())));
    }

    [TestCase("LOCAL_SPACE_POSITION_ROTATION")]
    public void FullLocalKeepsTheExistingForceFallback(string space)
    {
        var conversion = Conversion(Source(space, 2), 1);
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.False);
        Assert.That(conversion.Diagnostics.Any(item => item.Field == "ForceA" && item.ReasonKey == "Vfx.Preview.ForceUnsupported"), Is.True);
    }

    [TestCase("nonuniform")]
    [TestCase("mirror")]
    public void UnconfirmedRuntimeRootPreservesOldDampingAndReportsForceBasis(string kind)
    {
        var root = kind switch
        {
            "nonuniform" => Matrix.CreateScale(1, 2, 1), "mirror" => Matrix.CreateScale(-1, 1, 1), _ => throw new ArgumentException(kind),
        };
        var conversion = Conversion(Source("WORLD_SPACE", 2), 1);
        Assert.That(conversion.Emitters.Single().ModelConstantForce, Is.True);
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(0, root);
        var birth = Snapshot(Particles(simulation).Single()); simulation.Seek(.4f, root);
        var actual = Snapshot(Particles(simulation).Single());
        Assert.That(simulation.ForceBasisUnsupported, Is.True);
        var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
        var velocity = birth.WorldVelocity; var position = birth.WorldPosition;
        for (var tick = 0; tick < ticks; tick++) { velocity *= MathF.Exp(-2 * (float)Step); position += velocity * (float)Step; }
        Near(actual.WorldPosition, position); Near(actual.WorldVelocity, velocity);
    }

    [TestCase("WORLD_SPACE", .5f, 0f)]
    [TestCase("WORLD_SPACE", 1.4f, 2f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", 1f, 2f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", 2f, 0f)]
    public void TiltedMovingWeaponKeepsModelForceInWorldAxes(string space, float speed, float damping)
    {
        Matrix History(float time) => Matrix.CreateScale(.8f)
            * Matrix.CreateFromYawPitchRoll(.6f + time, .5f - time * .8f, -.7f + time * .3f)
            * Matrix.CreateTranslation(new Vector3(2, 3, -1) + new Vector3(1.5f, -.2f, .7f) * time);
        var conversion = Conversion(Source(space, damping), speed);
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, History(0), History);
        var birth = Snapshot(Particles(simulation).Single());
        simulation.Seek(.4f, History(.4f), History);
        var particle = Particles(simulation).Single();
        var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
        var expected = NativeOracle(birth, ticks, damping);
        var currentRoot = (Matrix)typeof(TotalWarVfxSimulation).GetMethod("ParticleRoot", Private)!
            .Invoke(simulation, [particle, (double).4f, false])!;
        var translation = space == "LOCAL_SPACE_POSITION_ONLY" ? History(.4f).Translation - History(0).Translation : Vector3.Zero;
        Near(Vector3.Transform((Vector3)Field(particle, "Position"), currentRoot), expected.Position + translation);
        Near(Vector3.TransformNormal((Vector3)Field(particle, "Velocity"), currentRoot), expected.Velocity);
        Assert.That(simulation.ForceBasisUnsupported, Is.False);
        var fresh = new TotalWarVfxSimulation(conversion); fresh.Seek(.4f, History(.4f), History);
        Assert.That(Packet(simulation), Is.EqualTo(Packet(fresh)));
        simulation.Seek(.1f, History(.1f), History); simulation.Seek(.4f, History(.4f), History);
        Assert.That(Packet(simulation), Is.EqualTo(Packet(fresh)));
    }

    [TestCase("WORLD_SPACE", .5f)]
    [TestCase("WORLD_SPACE", 1.4f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", 1f)]
    [TestCase("LOCAL_SPACE_POSITION_ONLY", 2f)]
    public void DistanceEmittedModelsKeepTheCurrentRigidBasisAndWorldForce(string space, float speed)
    {
        var source = VfxDocument.Read(Source(space, 0));
        var emitter = source.Root.Descendants("emitter").Single();
        void Set(string name, string text, bool attribute = false)
        {
            emitter.Elements().Where(item => (string?)item.Attribute("name") == name).Remove();
            emitter.Add(attribute ? new XElement("property", new XAttribute("name", name), new XAttribute("value", text))
                : new XElement("property", new XAttribute("name", name), text));
        }
        Set("MaximumParticles", "256", true); Set("SpawnSpacing", ".025", true);
        Set("EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 0));
        Set("ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        Matrix History(float time) => Matrix.CreateScale(.8f)
            * Matrix.CreateFromYawPitchRoll(.6f + time * 3, .5f - time * 4, -.7f + time * 2)
            * Matrix.CreateTranslation(new Vector3(2, 3, -1) + new Vector3(10, 0, 0) * time);
        var conversion = Conversion(source.Write(), speed);
        var simulation = new TotalWarVfxSimulation(conversion);
        var births = new Dictionary<object, Sample>(ReferenceEqualityComparer.Instance);
        for (var tick = 0; tick <= 48; tick++)
        {
            var time = (float)(tick * Step); simulation.Seek(time, History(time), History);
            foreach (var particle in Particles(simulation))
            {
                if (births.ContainsKey(particle)) continue;
                var birth = Snapshot(particle); births.Add(particle, birth);
                var root = (Matrix)Field(particle, "Root"); var current = History((float)birth.Birth);
                Assert.That(Vector3.Distance(root.Right, current.Right), Is.LessThan(.000001f));
                Assert.That(Vector3.Distance(root.Up, current.Up), Is.LessThan(.000001f));
                Assert.That(Vector3.Distance(root.Backward, current.Backward), Is.LessThan(.000001f));
                AssertModelBasis(simulation, particle);
            }
        }
        Assert.That(births.Count, Is.GreaterThan(20));
        foreach (var particle in Particles(simulation))
        {
            var birth = births[particle];
            var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
            var expected = NativeOracle(birth, ticks, 0);
            var root = (Matrix)typeof(TotalWarVfxSimulation).GetMethod("ParticleRoot", Private)!
                .Invoke(simulation, [particle, (double).4f, false])!;
            var translation = space == "LOCAL_SPACE_POSITION_ONLY" ? History(.4f).Translation - ((Matrix)Field(particle, "Root")).Translation : Vector3.Zero;
            Near(Vector3.Transform((Vector3)Field(particle, "Position"), root), expected.Position + translation);
            Near(Vector3.TransformNormal((Vector3)Field(particle, "Velocity"), root), expected.Velocity);
        }
        Assert.That(simulation.ForceBasisUnsupported, Is.False);
    }

    [Test]
    public void SixCompleteOfficialRootsAdmitActualFragmentLayersAndRetainUnforcedPillars()
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original official game data is required for this CPU control.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t')).Where(parts => parts.Length > 1)
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" })
        {
            var path = Path.Combine(data!, name); Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var entry in new PackFileContainerLoader(null!).Load(path)!.FileList) entries[entry.Key.Replace('/', '\\')] = entry.Value;
        }
        var library = entries[@"vfx\wh3_dlc24_lib_entity_green_dragon.xml"].DataSource.ReadData();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(library)), Is.EqualTo("6e992c150f080bbaf5c5ffcd3c870fdca7d4b20d5c0726b9b7a8ef50ef85acba"));
        var cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? Resolve(string path)
        {
            path = path.Replace('/', '\\');
            if (cache.TryGetValue(path, out var bytes)) return bytes;
            return entries.TryGetValue(path, out var entry) ? cache[path] = entry.DataSource.ReadData() : null;
        }
        foreach (var suffix in new[] { "circle_attk", "jump_attk", "jump_attk_fast", "spin_attk", "wall_attk", "wall_attk_slow" })
        {
            var path = $@"vfx\wh3_dlc24_cth_jade_dragon_human_{suffix}.xml"; var source = entries[path].DataSource.ReadData();
            var conversion = TotalWarVfxConverter.Convert(new(source, path, Resolve, "0"));
            var fragments = conversion.Emitters.Where(item => item.Name is "gems4" or "gems5" or "gems8" or "gems9" or "gems10" or "gems11").ToArray();
            Assert.That(fragments.Length, Is.GreaterThan(0), suffix);
            Assert.That(fragments.All(item => item.ModelConstantForce), Is.True, suffix);
            Assert.That(conversion.Emitters.Where(item => item.Name.StartsWith("gem spike", StringComparison.Ordinal)).All(item => !item.ModelConstantForce), Is.True);
            var root = Root(.8f); var simulation = Prepared(conversion, root, 0); var births = new Dictionary<object, Sample>(ReferenceEqualityComparer.Instance);
            for (var tick = 0; tick <= 60; tick++)
            {
                simulation.Seek((float)(tick * Step), root);
                foreach (var particle in Particles(simulation).Where(item => Definition(item).ModelConstantForce)) births.TryAdd(particle, Snapshot(particle));
            }
            var checkedParticles = 0;
            foreach (var particle in Particles(simulation).Where(births.ContainsKey))
            {
                var birth = births[particle]; var actual = Snapshot(particle); var definition = Definition(particle);
                Assert.That(definition.Force.TryConstantVector(out var force), Is.True); Assert.That(force, Is.EqualTo(new Vector3(0, -914, 0)));
                Assert.That(definition.Deceleration.TryConstantRange("x", out var damping), Is.True); Assert.That(damping.Min, Is.EqualTo(damping.Max));
                AssertModelBasis(simulation, particle);
                var ticks = (int)Math.Round(((double)Field(simulation, "_simulatedTime") - birth.Birth) / Step);
                var expected = NativeOracle(birth, ticks, Math.Max(0, damping.Min), force);
                Near(actual.WorldPosition, expected.Position); Near(actual.WorldVelocity, expected.Velocity); checkedParticles++;
            }
            Assert.That(checkedParticles, Is.GreaterThan(0), suffix);
            // Other authored sprite layers may retain their own force-basis warning in this complete root.
            var legacy = TotalWarVfxConverter.Convert(new(source, path, Resolve, "0"));
            foreach (var definition in legacy.Emitters)
                typeof(VfxPreviewEmitter).GetProperty(nameof(VfxPreviewEmitter.ModelConstantForce))!.SetValue(definition, false);
            var control = Prepared(legacy, root, 0);
            for (var tick = 0; tick <= 60; tick++) control.Seek((float)(tick * Step), root);
            Assert.That(simulation.ForceBasisUnsupported, Is.EqualTo(control.ForceBasisUnsupported), suffix);
            Assert.That(entries[path].DataSource.ReadData(), Is.EqualTo(source));
        }
    }

    private static (Vector3 Position, Vector3 Velocity) NativeOracle(Sample birth, int ticks, float damping, Vector3? authoredForce = null)
    {
        var position = birth.WorldPosition; var velocity = birth.WorldVelocity / birth.Speed;
        var dt = (float)Step * birth.Speed; var acceleration = (authoredForce ?? Force) * .01f * birth.Scale;
        for (var tick = 0; tick < ticks; tick++)
        {
            velocity *= MathF.Exp(-damping * dt);
            position += velocity * dt + acceleration * ((dt * .5f) * dt);
            velocity += acceleration * dt;
        }
        return (position, velocity * birth.Speed);
    }
    private static TotalWarVfxSimulation Prepared(VfxConversion conversion, Matrix root, float ground)
    {
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.PrepareInitialGround(ground, root, null, false, false, null); return simulation;
    }
    private static IEnumerable<object> Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Field(simulation, "_particles")).Cast<object>();
    private static void AssertModelBasis(TotalWarVfxSimulation simulation, object particle)
    {
        var state = Field(particle, "Emitter"); var birthRoot = (Matrix)Field(particle, "Root");
        var currentRoot = (Matrix)typeof(TotalWarVfxSimulation).GetMethod("CurrentRoot", Private)!
            .Invoke(simulation, [state, Field(simulation, "_simulatedTime"), false])!;
        object?[] birth = [birthRoot, null, null]; object?[] current = [currentRoot, null, null];
        var method = typeof(TotalWarVfxSimulation).GetMethod("TryModelForceRoot", Private | BindingFlags.Static)!;
        Assert.That(method.Invoke(null, birth), Is.True);
        Assert.That(method.Invoke(null, current), Is.True);
        Assert.That(Math.Abs((float)birth[1]! - (float)current[1]!), Is.LessThanOrEqualTo(.00001f));
    }
    private static object Field(object instance, string name) => instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static VfxPreviewEmitter Definition(object particle) => (VfxPreviewEmitter)Field(Field(particle, "Emitter"), "Definition");
    private static Sample Snapshot(object particle)
    {
        var root = (Matrix)Field(particle, "Root"); root.Decompose(out var scale, out _, out _);
        return new(Vector3.Transform((Vector3)Field(particle, "Position"), root), Vector3.TransformNormal((Vector3)Field(particle, "Velocity"), root),
            (double)Field(particle, "Birth"), (float)Field(Field(particle, "Emitter"), "TimeScale"), scale.X,
            (Vector3)Field(particle, "InitialScale"), (float)Field(particle, "RandomAlpha"));
    }
    private readonly record struct Sample(Vector3 WorldPosition, Vector3 WorldVelocity, double Birth, float Speed, float Scale, Vector3 InitialScale, float RandomAlpha);
    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.0001f));
    private static Matrix Root(float scale) => Matrix.CreateScale(scale) * Matrix.CreateRotationY(.73f) * Matrix.CreateTranslation(3, 4, -2);
    private static byte[] Packet(TotalWarVfxSimulation simulation, float? ground = null) => MemoryMarshal.AsBytes(simulation.Frame(Camera(ground)).AsSpan()).ToArray();
    private static VfxConversion Conversion(byte[] source, float speed)
    {
        var parent = Wrap(source, speed, "leaf");
        var result = TotalWarVfxConverter.Convert(new(parent, "diagnostic/model-force.xml", path => path.Replace('/', '\\') == @"vfx\leaf.xml" ? source : VfxModelVariantTests.Resolve(path)));
        Assert.That(result.Emitters, Has.Count.EqualTo(1)); return result;
    }
    private static byte[] Wrap(byte[] source, float speed, string name)
    {
        var parent = VfxDocument.CreateComposition(); var reference = parent.AddReference(name, VfxDocument.Read(source)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed.ToString("R", CultureInfo.InvariantCulture)); return parent.Write();
    }
    private static byte[] Source(string space, float damping, bool spawnOnTerrain = false)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1)); var emitter = document.Root.Descendants("emitter").Single();
        void Set(string name, string text, bool attribute = false)
        {
            emitter.Elements().Where(item => (string?)item.Attribute("name") == name).Remove();
            emitter.Add(attribute ? new XElement("property", new XAttribute("name", name), new XAttribute("value", text))
                : new XElement("property", new XAttribute("name", name), text));
        }
        Set("MaximumParticles", "1", true); Set("InfiniteLife", "false", true);
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "LifeTime"), new XAttribute("min", 3), new XAttribute("max", 3)));
        Set("EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", .1f)); Set("ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 5));
        Set("ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 5)); Set("ParticleDeceleration", VfxLibraryVisibilityTests.Constant("x", damping));
        Set("ParticleSimulationSpace", space, true);
        Set("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS", true);
        Set("ForceA", string.Join('|', new[] { ("x", Force.X), ("y", Force.Y), ("z", Force.Z) }
            .Select(item => VfxLibraryVisibilityTests.Constant(item.Item1, item.Item2).Split(':')[0])) + ":x,0;0,0|y,0;1,1|z,0;2,2");
        if (spawnOnTerrain) Set("ParticlesSpawnOnTerrain", "true", true);
        return document.Write();
    }
}
