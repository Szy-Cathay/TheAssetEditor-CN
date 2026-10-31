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
public class VfxModelTerrainBirthTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static CommonShaderParameters Camera(float? ground) => new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 30), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: ground);

    [TestCase(0, .6f, 0)]
    [TestCase(0, .8f, 43)]
    [TestCase(0, .6f, 180)]
    [TestCase(2, .8f, 0)]
    [TestCase(2, .6f, -61)]
    public void BirthReplacesOnlyWorldYWithUnscaledEmitterHeight(int space, float scale, float yaw)
    {
        var conversion = Conversion(Source(space, box: true));
        var control = Conversion(Source(space, spawn: false, box: true));
        var root = Root(scale, yaw);
        var simulation = Prepared(conversion, root, 2);
        var original = Prepared(control, root, 2);
        simulation.Seek(0, root); original.Seek(0, root);
        var before = original.ProjectedFrame(Camera(2), false).Single();
        var frame = simulation.ProjectedFrame(Camera(2), false).Single();
        var authored = (Vector3)Field(Particles(original).Single(), "Position")!;
        Assert.That(authored.Y, Is.Not.EqualTo(-1.5f), "The control must contain an actual random vertical birth offset.");
        Assert.Multiple(() =>
        {
            Assert.That(frame.Render.Transform.Translation.Y, Is.EqualTo(.5f).Within(.000002));
            Assert.That(frame.Render.Transform.Translation.X, Is.EqualTo(before.Render.Transform.Translation.X));
            Assert.That(frame.Render.Transform.Translation.Z, Is.EqualTo(before.Render.Transform.Translation.Z));
            Assert.That(frame.Render.Transform.Right.Length(), Is.EqualTo(scale).Within(.000002));
            Assert.That(frame.Render.Transform.Up.Length(), Is.EqualTo(scale).Within(.000002));
            Assert.That(frame.ModelVariant, Is.EqualTo(before.ModelVariant));
            Assert.That(Field(Particles(simulation).Single(), "Velocity"), Is.EqualTo(Field(Particles(original).Single(), "Velocity")));
            Assert.That(conversion.Emitters.Single().SpawnOnTerrain, Is.True);
            Assert.That(conversion.Emitters.Single().RequiresFlatTerrain, Is.False, "Birth projection must not enter the old every-frame height replacement.");
        });
    }

    [TestCase(0, .6f, 1f)]
    [TestCase(0, .8f, 2f)]
    [TestCase(2, .6f, 2f)]
    public void LaterMotionKeepsTheOriginalVelocityAndScaledDisplacement(int space, float scale, float speed)
    {
        var root = Root(scale, 37);
        var simulation = Prepared(Conversion(Source(space, moving: true), speed), root, 2);
        var control = Prepared(Conversion(Source(space, spawn: false, moving: true), speed), root, 2);
        simulation.Seek(0, root); control.Seek(0, root);
        var originalBirth = control.ProjectedFrame(Camera(2), false).Single().Render.Transform.Translation;
        foreach (var time in new[] { .1f, .25f, .4f })
        {
            simulation.Seek(time, root); control.Seek(time, root);
            var actual = simulation.ProjectedFrame(Camera(2), false).Single().Render.Transform.Translation;
            var unprojected = control.ProjectedFrame(Camera(2), false).Single().Render.Transform.Translation;
            var expected = unprojected + Vector3.Up * (.5f - originalBirth.Y);
            Near(actual, expected);
            Assert.That(actual.Y, Is.GreaterThan(.5f), "A birth-only projection must not clamp away the crystal's upward velocity.");
            Assert.That(Field(Particles(simulation).Single(), "Velocity"), Is.EqualTo(Field(Particles(control).Single(), "Velocity")));
        }
    }

    [Test]
    public void DifferentBezierBirthStagesKeepTheirOwnGroundOffset()
    {
        var conversion = Conversion(Source(dynamic: true, many: true)); var root = Root(.6f, 24);
        var simulation = Prepared(conversion, root, 2); var births = new Dictionary<double, float>();
        for (var tick = 0; tick <= 60; tick++)
        {
            simulation.Seek(tick / 120f, root);
            foreach (var particle in Particles(simulation))
            {
                var born = (double)Field(particle, "Birth")!;
                if (births.ContainsKey(born)) continue;
                var emitter = Field(particle, "Emitter")!;
                var phase = Math.Clamp((born - (float)Field(emitter, "Start")!) / (float)Field(emitter, "Duration")!, 0, 1);
                var expected = 2 + conversion.Emitters.Single().Position.Vector(phase, Vector3.Zero).Y;
                var position = Vector3.Transform((Vector3)Field(particle, "Position")!, (Matrix)Field(particle, "Root")!);
                Assert.That(position.Y, Is.EqualTo(expected).Within(.000002));
                births.Add(born, position.Y);
            }
        }
        Assert.That(births.Count, Is.GreaterThanOrEqualTo(5));
        Assert.That(births.Values.Distinct().Count(), Is.GreaterThan(3));
        var first = Particles(simulation).Single(particle => (double)Field(particle, "Birth")! == births.Keys.Min());
        var firstWorld = Vector3.Transform((Vector3)Field(first, "Position")!, (Matrix)Field(first, "Root")!);
        Assert.That(firstWorld.Y, Is.EqualTo(births.Values.First()).Within(.000002), "Later emitter-curve samples cannot move an already born stationary crystal.");
    }

    [Test]
    public void PreparedGroundDoesNotReplayOnFirstProjectionAndChangedGroundInvalidatesCheckpoints()
    {
        var conversion = Conversion(Source(dynamic: true, many: true)); var root = Root(.8f, 43);
        var simulation = Prepared(conversion, root, 2, checkpoints: true);
        simulation.Seek(1.2f, root); simulation.Seek(.7f, root);
        Assert.That(simulation.SeekCheckpointRestoreCount, Is.GreaterThan(0));
        var particles = Particles(simulation); var streams = Randoms(simulation);
        var first = simulation.ProjectedFrame(Camera(2), false);
        Assert.That(first, Is.Not.Empty);
        Assert.That(Particles(simulation).Zip(particles).All(pair => ReferenceEquals(pair.First, pair.Second)), Is.True);
        Assert.That(Randoms(simulation).Zip(streams).All(pair => ReferenceEquals(pair.First, pair.Second)), Is.True);
        var changed = simulation.ProjectedFrame(Camera(-3), false);
        Assert.That(Particles(simulation).Any(particles.Contains), Is.False, "Changing the birth receiver must rebuild complete births, not just a camera projection.");
        foreach (var time in new[] { .7f, .337f, .93f })
        {
            simulation.Seek(time, root);
            var fresh = Prepared(conversion, root, -3, checkpoints: true); fresh.Seek(time, root);
            Assert.That(simulation.ProjectedFrame(Camera(-3), false), Is.EqualTo(fresh.ProjectedFrame(Camera(-3), false)));
            Assert.That(State(simulation), Is.EqualTo(State(fresh)), "Positions, motion origins, variants and every birth random channel must match a fresh receiver replay.");
            Assert.That(CursorState(simulation), Is.EqualTo(CursorState(fresh)));
        }
        Assert.That(changed, Is.Not.EqualTo(first));
    }

    [Test]
    public void GroundFirstArrivingAtDrawMatchesPreparedBirths()
    {
        var conversion = Conversion(Source(moving: true, many: true)); var root = Root(.6f, 71);
        var late = new TotalWarVfxSimulation(conversion); late.Seek(.4f, root);
        var expected = Prepared(conversion, root, 2); expected.Seek(.4f, root);
        Assert.That(late.ProjectedFrame(Camera(2), false), Is.EqualTo(expected.ProjectedFrame(Camera(2), false)));
        Assert.That(State(late), Is.EqualTo(State(expected)));
    }

    [Test]
    public async Task ConstantWorkerAndSharedConversionKeepEachReceiversExactBirthState()
    {
        var conversion = Conversion(Source(dynamic: true, many: true)); var root = Root(.8f, -32);
        var context = new VfxConstantReloadContext(root, root, 2, null, false, false, false);
        using var worker = new VfxConstantReloadPreparation(conversion, context, .7f, true);
        var prepared = await Take(worker);
        var expected = Prepared(conversion, root, 2, true); expected.Seek(.7f, root);
        Assert.That(prepared.ProjectedFrame(Camera(2), false), Is.EqualTo(expected.ProjectedFrame(Camera(2), false)));
        var other = Prepared(conversion, root, -3); other.Seek(.7f, root);
        var before = prepared.ProjectedFrame(Camera(2), false);
        // Ground height changes camera-distance sorting; match immutable birth ages before comparing receivers.
        var otherByAge = other.ProjectedFrame(Camera(-3), false).OrderBy(particle => particle.Render.Time).ToArray();
        var beforeByAge = before.OrderBy(particle => particle.Render.Time).ToArray();
        Assert.That(otherByAge.Select(particle => (particle.Render.Time, particle.ModelVariant)),
            Is.EqualTo(beforeByAge.Select(particle => (particle.Render.Time, particle.ModelVariant))));
        Assert.That(otherByAge.Select(particle => particle.Render.Time).Distinct().Count(), Is.EqualTo(otherByAge.Length));
        var differences = otherByAge.Zip(beforeByAge)
            .Select(pair => pair.First.Render.Transform.Translation.Y - pair.Second.Render.Transform.Translation.Y).ToArray();
        Assert.That(differences.All(value => Math.Abs(value + 5) < .000002), Is.True,
            "Receiver deltas: " + string.Join(',', differences.Select(value => value.ToString("R", CultureInfo.InvariantCulture))));
        Assert.That(prepared.ProjectedFrame(Camera(2), false), Is.EqualTo(before), "Shared immutable model resources must not share receiver-dependent simulation state.");
        Assert.That(State(prepared), Is.EqualTo(State(expected)));
        Assert.That(CursorState(prepared), Is.EqualTo(CursorState(expected)));
    }

    [TestCase(0, 2f)]
    [TestCase(2, 0f)]
    public void BirthOnlyProjectionPreservesExistingPositionOnlyAndWorldAttachmentTransport(int space, float expectedMotion)
    {
        var initial = Root(.8f, 43); var current = initial; current.Translation += Vector3.Up * 2;
        var simulation = Prepared(Conversion(Source(space)), initial, 2);
        simulation.Seek(0, initial); simulation.Seek(.1f, current);
        Assert.That(simulation.ProjectedFrame(Camera(2), false).Single().Render.Transform.Translation.Y,
            Is.EqualTo(.5f + expectedMotion).Within(.000002));
    }

    [Test]
    public void FullLocalSourceRetainsTheOriginalFallbackAndItsConversionDiagnostic()
    {
        var conversion = Conversion(Source(space: 1)); var root = Root(.6f, 43);
        var simulation = Prepared(conversion, root, 2); simulation.Seek(.1f, root);
        var control = Prepared(Conversion(Source(space: 1, spawn: false)), root, 2); control.Seek(.1f, root);
        Assert.That(simulation.ProjectedFrame(Camera(2), false), Is.EqualTo(control.ProjectedFrame(Camera(2), false)));
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticlesSpawnOnTerrain"
            && d.ReasonKey == "Vfx.Preview.ModelTerrainBirthUnsupported"), Is.True);
        Assert.That(NeedsDepth(simulation, 0), Is.False);
    }

    [Test]
    public void ParentEventBirthRetainsItsOriginalFallbackWithoutWritingGroundDepth()
    {
        var parent = VfxParticleEventTests.Source(true, "true");
        VfxConversion Child(bool spawn) => TotalWarVfxConverter.Convert(new(parent, "diagnostic/parent-ground.xml", path =>
            path.Replace('/', '\\') == @"vfx\child.xml" ? Source(spawn: spawn) : VfxModelVariantTests.Resolve(path)));
        var root = Root(.8f, 43); var conversion = Child(true); Assert.That(conversion.Emitters, Has.Count.EqualTo(2));
        var simulation = Prepared(conversion, root, 2); var control = Prepared(Child(false), root, 2);
        simulation.Seek(.1f, root); control.Seek(.1f, root);
        var frame = simulation.ProjectedFrame(Camera(2), false);
        Assert.That(frame, Is.Not.Empty);
        Assert.That(frame, Is.EqualTo(control.ProjectedFrame(Camera(2), false)));
        Assert.That(simulation.GetType().GetProperty("ModelTerrainBirthUnsupported", Private)!.GetValue(simulation), Is.True);
        Assert.That(frame.All(particle => !particle.RequiresGroundDepth), Is.True);
    }

    [Test]
    public void MixedBirthRootsGateReceiverDepthPerParticleRatherThanPerLayer()
    {
        var simulation = Prepared(Conversion(Source(space: 2, many: true)), Matrix.Identity, 0);
        simulation.Seek(0, Matrix.Identity); simulation.Seek(.2f, Matrix.CreateRotationX(.3f));
        var frame = simulation.ProjectedFrame(Camera(0), false);
        Assert.That(frame.Count(particle => particle.RequiresGroundDepth), Is.EqualTo(1));
        Assert.That(frame.Count(particle => !particle.RequiresGroundDepth), Is.GreaterThan(0));
        Assert.That(frame.Select(particle => particle.Render.Layer).Distinct().Count(), Is.EqualTo(1));
        Assert.That(simulation.GetType().GetProperty("ModelTerrainBirthUnsupported", Private)!.GetValue(simulation), Is.True);
    }

    [Test]
    public async Task CanceledQueuedGroundPreparationCannotPublishIntoTheNextReceiver()
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupied = VfxInteractivePrewarm.RunPreparation(_ => release.Task, CancellationToken.None);
        var conversion = Conversion(Source(many: true)); var root = Root(.8f, 43);
        var context = new VfxConstantReloadContext(root, root, 2, null, false, false, false);
        using var canceled = new VfxConstantReloadPreparation(conversion, context, .7f, true);
        try { canceled.Dispose(); }
        finally { release.TrySetResult(true); await occupied; }
        using var replacement = new VfxConstantReloadPreparation(conversion, context with { GroundHeight = -3 }, .7f, true);
        var actual = await Take(replacement);
        Assert.That(canceled.TryTake(out _), Is.False);
        var fresh = Prepared(conversion, root, -3, true); fresh.Seek(.7f, root);
        Assert.That(actual.ProjectedFrame(Camera(-3), false), Is.EqualTo(fresh.ProjectedFrame(Camera(-3), false)));
        Assert.That(State(actual), Is.EqualTo(State(fresh)));
    }

    [TestCase("pitch")]
    [TestCase("roll")]
    [TestCase("nonuniform")]
    [TestCase("mirror")]
    [TestCase("missing-ground")]
    public void UnconfirmedBirthBasisKeepsTheOriginalFallbackAndExplicitDiagnostic(string kind)
    {
        var root = kind switch
        {
            "pitch" => Matrix.CreateRotationX(.2f), "roll" => Matrix.CreateRotationZ(.3f),
            "nonuniform" => Matrix.CreateScale(1, 2, 1), "mirror" => Matrix.CreateScale(-1, 1, 1), _ => Root(.6f, 43),
        };
        var ground = kind == "missing-ground" ? (float?)null : 2;
        var simulation = Prepared(Conversion(Source()), root, ground);
        var control = Prepared(Conversion(Source(spawn: false)), root, ground);
        simulation.Seek(.1f, root); control.Seek(.1f, root);
        Assert.That(simulation.ProjectedFrame(Camera(ground), false), Is.EqualTo(control.ProjectedFrame(Camera(ground), false)));
        var property = simulation.GetType().GetProperty("ModelTerrainBirthUnsupported", Private);
        Assert.That(property, Is.Not.Null, "A visible fallback must have an explicit receiver/basis diagnostic.");
        Assert.That(property!.GetValue(simulation), Is.True);
        Assert.That(NeedsDepth(simulation, 0), Is.False);
    }

    [Test]
    public void DepthAdmissionIsOnlyForSuccessfullyProjectedModelBirths()
    {
        var simulation = Prepared(Conversion(Source()), Matrix.Identity, 0); simulation.Seek(0, Matrix.Identity);
        Assert.That(simulation.ProjectedFrame(Camera(0), false), Has.Length.EqualTo(1));
        Assert.That(NeedsDepth(simulation, 0), Is.True);
        var ordinary = Prepared(Conversion(Source(spawn: false)), Matrix.Identity, 0); ordinary.Seek(0, Matrix.Identity);
        ordinary.ProjectedFrame(Camera(0), false);
        Assert.That(NeedsDepth(ordinary, 0), Is.False);
    }

    [TestCase("translation-x-infinity")]
    [TestCase("translation-y-nan")]
    [TestCase("translation-z-infinity")]
    [TestCase("basis-nan")]
    [TestCase("affine-x-nan")]
    [TestCase("affine-y-infinity")]
    [TestCase("affine-w-nan")]
    [TestCase("zero-basis")]
    [TestCase("collinear-basis")]
    [TestCase("overflowing-basis")]
    [TestCase("zero-affine-w")]
    public void InvalidRootCannotMakeBirthHelperThrowOrMutateAndKeepsLegacyPipelineBehavior(string field)
    {
        var root = Root(.8f, 43);
        switch (field)
        {
            case "translation-x-infinity": root.M41 = float.PositiveInfinity; break;
            case "translation-y-nan": root.M42 = float.NaN; break;
            case "translation-z-infinity": root.M43 = float.NegativeInfinity; break;
            case "basis-nan": root.M11 = float.NaN; break;
            case "affine-x-nan": root.M14 = float.NaN; break;
            case "affine-y-infinity": root.M24 = float.PositiveInfinity; break;
            case "affine-w-nan": root.M44 = float.NaN; break;
            case "zero-basis": root.M11 = root.M12 = root.M13 = 0; break;
            case "collinear-basis": root.Up = root.Right; break;
            case "overflowing-basis": root = Matrix.CreateScale(float.MaxValue); break;
            default: root.M44 = 0; break;
        }
        (VfxPreviewParticle[]? Frame, Exception? Error) Pipeline(bool spawn)
        {
            try
            {
                var simulation = Prepared(Conversion(Source(spawn: spawn)), root, 2); simulation.Seek(0, root);
                return (simulation.ProjectedFrame(Camera(2), false), null);
            }
            catch (Exception exception) { return (null, exception); }
        }
        // The pre-existing RotationOnly path can itself reject invalid matrices. Establish that baseline first.
        var expected = Pipeline(false);
        TestContext.WriteLine($"Legacy no-terrain {field}: {expected.Error?.GetType().Name ?? "native packet returned"}");
        Assert.That(expected.Error, Is.Null.Or.TypeOf<ArithmeticException>());
        var healthyRoot = Root(.8f, 43); var healthy = Prepared(Conversion(Source()), healthyRoot, 2); healthy.Seek(0, healthyRoot);
        var state = Field(Particles(healthy).Single(), "Emitter")!;
        var position = new Vector3(BitConverter.Int32BitsToSingle(int.MinValue), -1.5f, 3);
        object?[] arguments = [state, root, -1.5f, position];
        var method = healthy.GetType().GetMethod("PrepareModelTerrainBirth", Private)!;
        var admitted = true;
        Assert.DoesNotThrow(() => admitted = (bool)method.Invoke(healthy, arguments)!, "The new helper must reject before unsafe matrix decomposition.");
        Assert.That(admitted, Is.False, "The returned admission bit is the caller's per-particle ground-depth requirement.");
        Assert.That(MemoryMarshal.AsBytes(new[] { (Vector3)arguments[3]! }.AsSpan()).ToArray(),
            Is.EqualTo(MemoryMarshal.AsBytes(new[] { position }.AsSpan()).ToArray()), "Keep all original position bits, including negative zero.");
        var actual = Pipeline(true);
        if (expected.Error != null)
        {
            Assert.That(actual.Error, Is.TypeOf(expected.Error.GetType()), "Do not broaden or hide the pre-existing pipeline's invalid-matrix rejection.");
            return;
        }
        Assert.That(actual.Error, Is.Null);
        Assert.That(actual.Frame, Has.Length.EqualTo(1));
        Assert.That(MemoryMarshal.AsBytes(actual.Frame!.Select(particle => particle.Render).ToArray().AsSpan()).ToArray(),
            Is.EqualTo(MemoryMarshal.AsBytes(expected.Frame!.Select(particle => particle.Render).ToArray().AsSpan()).ToArray()));
        Assert.That(actual.Frame.Single().ModelVariant, Is.EqualTo(expected.Frame.Single().ModelVariant));
        Assert.That(actual.Frame.Single().RequiresGroundDepth, Is.False);
    }

    [TestCase("circle", "211c6b5d32c1b6fd7523a6e222b4ad32c72c95642caf3a2239abfe92ff5d7f0b", 1)]
    [TestCase("wall", "970af2ae8f39213bcd3ff9621d8c91ea8f19fe1f2220fe2792628fde80879af4", 3)]
    public void CompleteOfficialRootsProjectEveryCrystalBirthAndKeepFollowTerrainUnmapped(string attack, string sha, int layers)
    {
        var data = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_GAME_DATA");
        if (string.IsNullOrWhiteSpace(data)) Assert.Ignore("Original official manifest game data is required for this CPU control.");
        var manifest = File.ReadAllLines(Path.Combine(data!, "manifest.txt")).Select(line => line.Split('\t'))
            .Where(parts => parts.Length > 1).ToDictionary(parts => parts[0], parts => long.Parse(parts[1], CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        var entries = new Dictionary<string, PackFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in new[] { "vfx_desc.pack", "vfx.pack", "vfx_models.pack", "vfx_models_dds.pack", "commontextures.pack", "data.pack" })
        {
            var path = Path.Combine(data!, name); Assert.That(new FileInfo(path).Length, Is.EqualTo(manifest[name]));
            foreach (var entry in new PackFileContainerLoader(null!).Load(path)!.FileList) entries[entry.Key.Replace('/', '\\')] = entry.Value;
        }
        var pathKey = $@"vfx\wh3_dlc24_cth_jade_dragon_human_{attack}_attk.xml";
        var bytes = entries[pathKey].DataSource.ReadData(); Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)), Is.EqualTo(sha));
        var cache = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        byte[]? Resolve(string path)
        {
            path = path.Replace('/', '\\'); if (cache.TryGetValue(path, out var value)) return value;
            return entries.TryGetValue(path, out var entry) ? cache[path] = entry.DataSource.ReadData() : null;
        }
        var conversion = TotalWarVfxConverter.Convert(new(bytes, pathKey, Resolve, "0"));
        var crystals = conversion.Emitters.Where(emitter => emitter.Name.StartsWith("gem spike", StringComparison.Ordinal)).ToHashSet();
        Assert.That(crystals, Has.Count.EqualTo(layers));
        Assert.That(crystals.All(emitter => emitter.SpawnOnTerrain && !emitter.RequiresFlatTerrain), Is.True);
        Assert.That(conversion.Diagnostics.Any(d => d.Field == "ParticlesFollowTerrain" && d.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
        var root = Root(.8f, 43); var simulation = Prepared(conversion, root, 2); var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var tick = 0; tick <= 72; tick++)
        {
            simulation.Seek(tick / 120f, root);
            foreach (var particle in Particles(simulation))
            {
                var emitter = Field(particle, "Emitter")!; var definition = (VfxPreviewEmitter)Field(emitter, "Definition")!;
                if (!crystals.Contains(definition) || !seen.Add(particle)) continue;
                var born = (double)Field(particle, "Birth")!;
                var phase = Math.Clamp((born - (float)Field(emitter, "Start")!) / (float)Field(emitter, "Duration")!, 0, 1);
                var birthPosition = Vector3.Transform((Vector3)Field(particle, "MotionOrigin")!, (Matrix)Field(particle, "Root")!);
                Assert.That(birthPosition.Y, Is.EqualTo(2 + definition.Position.Vector(phase, Vector3.Zero).Y).Within(.000005));
            }
        }
        Assert.That(seen.Count, Is.GreaterThan(layers));
        Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
    }

    private static bool NeedsDepth(TotalWarVfxSimulation simulation, int layer)
    {
        var property = typeof(VfxPreviewParticle).GetProperty("RequiresGroundDepth", Private);
        Assert.That(property, Is.Not.Null, "Ground depth must be gated by each successfully admitted birth, including mixed valid/fallback roots in one layer.");
        return simulation.ProjectedFrame(Camera(simulation.CurrentGroundHeight), false)
            .Any(particle => particle.Render.Layer == layer && (bool)property!.GetValue(particle)!);
    }
    private static async Task<TotalWarVfxSimulation> Take(VfxConstantReloadPreparation worker)
    {
        for (var wait = 0; wait < 5000; wait++)
        {
            if (worker.TryTake(out var result)) return result!.Simulation;
            Assert.That(worker.Failure, Is.Null); await Task.Delay(1);
        }
        throw new AssertionException("Exact constant preparation did not complete.");
    }
    private static TotalWarVfxSimulation Prepared(VfxConversion conversion, Matrix root, float? ground, bool checkpoints = false)
    {
        var simulation = new TotalWarVfxSimulation(conversion);
        if (checkpoints) Assert.That(simulation.EnableSeekCheckpoints(), Is.True);
        simulation.PrepareInitialGround(ground, root, null, false, false, null); return simulation;
    }
    private static Matrix Root(float scale, float yaw) => Matrix.CreateScale(scale) * Matrix.CreateRotationY(MathHelper.ToRadians(yaw)) * Matrix.CreateTranslation(3, 7, -2);
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, Private)!.GetValue(owner);
    private static object[] Particles(TotalWarVfxSimulation simulation) => ((IEnumerable)Field(simulation, "_particles")!).Cast<object>().ToArray();
    private static object[] Randoms(TotalWarVfxSimulation simulation) => ((IEnumerable)Field(simulation, "_emitters")!).Cast<object>().Select(emitter => Field(emitter, "Random")!).ToArray();
    private static object[] State(TotalWarVfxSimulation simulation) => Particles(simulation).Select(particle => (object)particle.GetType().GetFields()
        .Where(field => field.FieldType.IsValueType).OrderBy(field => field.Name).Select(field => field.GetValue(particle)).ToArray()).ToArray();
    private static object[] CursorState(TotalWarVfxSimulation simulation) => Randoms(simulation).Select(random => (object)random.GetType().GetFields(Private)
        .Where(field => field.FieldType.IsValueType).OrderBy(field => field.Name).Select(field => field.GetValue(random)).ToArray()).ToArray();
    private static void Near(Vector3 actual, Vector3 expected) => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.000006f));
    private static VfxConversion Conversion(byte[] bytes, float speed = 1)
    {
        var parent = VfxDocument.CreateComposition(); var reference = parent.AddReference("leaf", VfxDocument.Read(bytes)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed.ToString("R", CultureInfo.InvariantCulture));
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/model-ground-birth.xml", path =>
            path.Replace('/', '\\') == @"vfx\leaf.xml" ? bytes : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics)); return conversion;
    }
    private static byte[] Source(int space = 0, bool spawn = true, bool box = false, bool moving = false, bool dynamic = false, bool many = false)
    {
        var doc = VfxDocument.Read(VfxModelVariantTests.Source(2)); var emitter = doc.Root.Descendants("emitter").Single();
        void Set(string name, string text, bool attribute = false)
        {
            emitter.Elements().Where(node => (string?)node.Attribute("name") == name).Remove();
            emitter.Add(attribute ? new XElement("property", new XAttribute("name", name), new XAttribute("value", text))
                : new XElement("property", new XAttribute("name", name), text));
        }
        Set("MaximumParticles", many ? "32" : "1", true);
        Set("EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", many ? 12 : .1f));
        Set("ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 4));
        Set("ParticleRelativeSpawnOrientation", "RELATIVE_TO_VFX_UP_AXIS", true);
        Set("ParticlesSpawnOnTerrain", spawn ? "true" : "false", true);
        Set("ParticleSimulationSpace", space == 2 ? "WORLD_SPACE" : space == 1 ? "LOCAL_SPACE_POSITION_ROTATION" : "LOCAL_SPACE_POSITION_ONLY", true);
        Set("ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", moving ? 15 : 0));
        Set("ParticleDeceleration", VfxLibraryVisibilityTests.Constant("x", moving ? 10 : 0));
        string Y(string y) => VfxLibraryVisibilityTests.Constant("x", 0).Split(':')[0] + "|" + y.Split(':')[0]
            + "|" + VfxLibraryVisibilityTests.Constant("z", 0).Split(':')[0] + ":x,0;0,0|y,0;1,1|z,0;2,2";
        Set("EmitterPosition", Y(dynamic ? "y_min,0,0;0,-1.5,bezier_c,bezier_c,-.1,0,.333,-.4,1;1,-3,bezier_c,bezier_c,-.333,.4,.1,0,1:y,0;0,0"
            : VfxLibraryVisibilityTests.Constant("y", -1.5f)));
        if (box) Set("SpawnAreaBox", Y(VfxLibraryVisibilityTests.Constant("y", 8)));
        return doc.Write();
    }
}
