#nullable enable
using System.Collections;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

// Synthetic diagnostic controls verify cache ownership; these are not official visual acceptance samples.
public class VfxAppearanceCacheTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly MethodInfo Clear = typeof(TotalWarVfxSimulation).GetMethod("ClearAppearanceEntries", Private)!;
    private static readonly string[] ProjectionCaches = ["_projectionEventTints", "_projectionEventAnchors", "_projectionEventScales"];

    [TestCase(false)]
    [TestCase(true)]
    public void PausedViewsAndChangedCurrentRootsPreserveEveryPacketBit(bool model)
    {
        var conversion = Conversion(model);
        var cached = Cached(conversion);
        var original = new TotalWarVfxSimulation(conversion);
        var roots = new[] { Matrix.Identity, Matrix.CreateScale(.7f, 1.3f, 2) * Matrix.CreateRotationY(.3f) * Matrix.CreateTranslation(3, -1, 2),
            Matrix.CreateScale(2) * Matrix.CreateRotationX(-.4f) * Matrix.CreateTranslation(-2, 4, 1), Matrix.Identity };
        var changes = new HashSet<string>();
        var rootChanges = new HashSet<string>();
        foreach (var root in roots)
        {
            cached.SeekWithCurrentAttachment(.717f, root, _ => Matrix.Identity);
            original.SeekWithCurrentAttachment(.717f, root, _ => Matrix.Identity);
            byte[]? modelPose = null;
            for (var view = 0; view < 8; view++)
            {
                var camera = Camera(view);
                var packet = EqualPackets(cached, original, camera);
                Assert.That(packet, Is.Not.Empty);
                changes.Add(System.Convert.ToHexString(packet));
                if (view == 0) rootChanges.Add(System.Convert.ToHexString(packet));
                if (model)
                {
                    modelPose ??= packet;
                    Assert.That(packet, Is.EqualTo(modelPose), "Fixed models keep the same world packet across camera uniforms.");
                }
            }
        }
        Assert.That(rootChanges.Count, Is.EqualTo(3), "The three distinct current root transforms must all change the packet; the fourth repeats Identity.");
        if (!model) Assert.That(changes.Count, Is.GreaterThan(4), "Billboard controls must actually change with the camera.");
        Assert.That(CompleteEntries(cached), Is.GreaterThan(0));
    }

    [Test]
    public void DistanceOpacityAndMinimumPixelScaleRemainCameraDependent()
    {
        var document = Document(false);
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleCullDistances"), new XAttribute("x", 0),
            new XAttribute("y", 1), new XAttribute("z", 20), new XAttribute("w", 60)));
        document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_rendersize_limiter"),
            new XElement("property", new XAttribute("name", "pixelsize_limit"), new XAttribute("value", 8))));
        var conversion = Convert(document);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        cached.Seek(.337f, Matrix.Identity); original.Seek(.337f, Matrix.Identity);
        var packets = new List<byte[]>();
        foreach (var distance in new[] { 12f, 45f, 100f, 12f, 30f })
            packets.Add(EqualPackets(cached, original, Camera(0, distance), cull: true));
        Assert.That(packets[0], Is.Not.Empty);
        Assert.That(packets[2], Is.Empty);
        Assert.That(packets[0], Is.EqualTo(packets[3]), "Returning to the original view must restore its original alpha/size.");
        Assert.That(packets[0], Is.Not.EqualTo(packets[1]));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ForwardReverseAndCheckpointRestoreNeverReuseAnotherParticleOrPhase(bool model)
    {
        var conversion = Conversion(model);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        Assert.That(cached.EnableSeekCheckpoints(), Is.True);
        Assert.That(original.EnableSeekCheckpoints(), Is.True);
        var prior = -1f;
        foreach (var time in new[] { .137f, .717f, 1.37f, 2.13f, 1.837f, .937f, .337f, 0f, 2.437f, .817f, 3.137f })
        {
            cached.Seek(time, Matrix.Identity); original.Seek(time, Matrix.Identity);
            if (time < prior) Assert.That(AppearanceCount(cached), Is.Zero, "Restored or reset particles cannot retain old identity entries.");
            for (var view = 0; view < 3; view++) EqualPackets(cached, original, Camera(view));
            Assert.That(cached.ParticleCount, Is.EqualTo(original.ParticleCount));
            prior = time;
        }
        Assert.That(cached.SeekCheckpointRestoreCount, Is.GreaterThan(0));
    }

    [Test]
    public void ContinuousTrailKeepsOriginalChordsRandomStreamAndAppearanceDuringRewind()
    {
        var document = Document(false); var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "SpawnSpacing", ".12", attribute: true);
        var conversion = Convert(document);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        Assert.That(cached.HasMotionTrailEmitters, Is.True);
        Matrix At(float time) => Matrix.CreateRotationY(time * .3f) * Matrix.CreateTranslation(4 * MathF.Sin(time), 0, 3 * MathF.Cos(time));
        float? Next(float time) => (MathF.Floor(time * 60 + .0001f) + 1) / 60;
        foreach (var time in new[] { .017f, .137f, .337f, .717f, 1.137f, 1.837f, .817f, 1.437f, 2.137f })
        {
            cached.SeekContinuousTrail(time, At(time), At, Next);
            original.SeekContinuousTrail(time, At(time), At, Next);
            for (var view = 0; view < 4; view++) EqualPackets(cached, original, Camera(view));
        }
        Assert.That(cached.ParticleCount, Is.GreaterThan(0));
    }

    [Test]
    public void TrackedEventInheritedColourAlphaAndScaleAreNotBakedIntoLocalAppearance()
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var link = parent.Root.Descendants("property_event").Single();
        link.SetAttributeValue("track_colour", "true"); link.SetAttributeValue("track_alpha", "true"); link.SetAttributeValue("track_scale", "true");
        var child = Document(false); Set(child.Root.Descendants("emitter").Single(), "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", 2));
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/appearance-event.xml",
            path => path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        cached.Seek(.2f, Matrix.Identity); original.Seek(.2f, Matrix.Identity);
        var before = EqualPackets(cached, original, Camera(0));
        Assert.That(before, Is.Not.Empty);
        foreach (var simulation in new[] { cached, original })
        {
            var states = ((IEnumerable)Field(simulation, "_emitters")).Cast<object>();
            var inherited = states.Where(state => state.GetType().GetField("Parent", Private)!.GetValue(state) != null).ToArray();
            Assert.That(inherited, Is.Not.Empty);
            foreach (var state in inherited)
            {
                state.GetType().GetField("FrozenTint", Private)!.SetValue(state, new Vector4(.2f, .8f, .45f, .3f));
                state.GetType().GetField("FrozenScale", Private)!.SetValue(state, new Vector3(1.5f, .7f, 2));
            }
        }
        var after = EqualPackets(cached, original, Camera(0));
        Assert.That(after, Is.Not.EqualTo(before), "The same cached child age must still observe its current inherited event values.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ZeroScaleOrRejectedVelocityBasisDoesNotEagerlyCompleteColourAlpha(bool rejectedBasis)
    {
        var document = Document(false); var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", 0));
        if (rejectedBasis)
        {
            Set(emitter, "MaximumParticles", "1", attribute: true);
            document.Root.Descendants("quality").Single().Add(new XElement("modifier", new XAttribute("fragment", "vs_translation_velocity_v2"),
                new XElement("property", new XAttribute("name", "orientation_mode"), new XAttribute("value", "Camera-facing velocity-aligned")),
                new XElement("property", new XAttribute("name", "velocity_stretch_mul"), new XAttribute("value", 0))));
        }
        if (!rejectedBasis) Set(emitter, "ParticleScale", Curve(("x", 0f, 0f), ("y", 1f, 1f), ("z", 1f, 1f)));
        var conversion = Convert(document);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        var time = rejectedBasis ? 0f : .137f;
        cached.Seek(time, Matrix.Identity); original.Seek(time, Matrix.Identity);
        var camera = Camera(0);
        if (rejectedBasis)
        {
            Assert.That(conversion.Emitters.Single().Alignment, Is.EqualTo(1));
            // Same DIAG velocity input as the independently audited native mode2 near-parallel controls.
            foreach (var simulation in new[] { cached, original })
            {
                var particle = ((IEnumerable)Field(simulation, "_particles")).Cast<object>().Single();
                Assert.That(particle.GetType().GetField("Position", Private)!.GetValue(particle), Is.EqualTo(Vector3.Zero));
                particle.GetType().GetField("Velocity", Private)!.SetValue(particle, Vector3.UnitZ);
            }
            camera = camera with { CameraPosition = new Vector3(0, 0, 15), View = Matrix.CreateLookAt(new(0, 0, 15), Vector3.Zero, Vector3.Up) };
        }
        Assert.That(EqualPackets(cached, original, camera), Is.Empty);
        Assert.That(AppearanceCount(cached), Is.GreaterThan(0));
        Assert.That(CompleteEntries(cached), Is.Zero, "Local colour/alpha remain lazy after the old early exit.");
        if (rejectedBasis)
        {
            Assert.That(EqualPackets(cached, original, Camera(1)), Is.Not.Empty);
            Assert.That(CompleteEntries(cached), Is.GreaterThan(0), "A newly valid basis must complete the previously scale-only entry.");
        }
    }

    [Test]
    public void RecycledIndicesAndExpiredParticlesLeaveNoStaleRetainedEntries()
    {
        var document = Document(false); var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 80));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .08f));
        var conversion = Convert(document);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        foreach (var time in new[] { .017f, .071f, .137f, .237f, .337f, .537f })
        {
            cached.Seek(time, Matrix.Identity); original.Seek(time, Matrix.Identity);
            EqualPackets(cached, original, Camera(0));
        }
        cached.EmissionEndTimeSeconds = original.EmissionEndTimeSeconds = .6f;
        cached.Seek(1.137f, Matrix.Identity); original.Seek(1.137f, Matrix.Identity);
        Assert.That(EqualPackets(cached, original, Camera(0)), Is.Empty);
        Assert.That(AppearanceCount(cached), Is.Zero);
        var entries = (Array)Field(cached, "_appearanceEntries");
        Assert.That(entries.Length, Is.LessThanOrEqualTo(TotalWarVfxSimulation.ParticleLimit));
        foreach (var entry in entries) Assert.That(entry!.GetType().GetProperty("Particle", Private)!.GetValue(entry), Is.Null);
    }

    [Test]
    public void NestedEventAncestorsObserveEveryCurrentRootWithoutRetainingProjectionEntries()
    {
        var conversion = NestedEvents(terrain: false);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        var roots = new[] { Matrix.Identity, Matrix.CreateScale(2) * Matrix.CreateTranslation(3, 2, -1),
            Matrix.CreateRotationY(.4f) * Matrix.CreateTranslation(-2, 1, 4), Matrix.Identity };
        var changes = new HashSet<string>();
        foreach (var root in roots)
        {
            cached.SeekWithCurrentAttachment(.2f, root, _ => Matrix.Identity);
            original.SeekWithCurrentAttachment(.2f, root, _ => Matrix.Identity);
            var packet = EqualPackets(cached, original, Camera(0));
            Assert.That(packet, Is.Not.Empty);
            changes.Add(System.Convert.ToHexString(packet));
        }
        Assert.That(changes.Count, Is.EqualTo(3));
        var depth = ((IEnumerable)Field(cached, "_emitters")).Cast<object>().Select(EventDepth).Max();
        Assert.That(depth, Is.GreaterThanOrEqualTo(2), "The control must contain real event ancestors, not just independent emitters.");
    }

    [Test]
    public void UnsupportedTerrainAncestorsKeepTheirFlagsAndRecoverOnANewProjection()
    {
        var conversion = NestedEvents(terrain: true);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        foreach (var (root, ground, unsupported) in new[] { (Matrix.Identity, (float?)null, true), (Matrix.Identity, (float?)0, false),
            (Matrix.CreateRotationX(.4f), (float?)0, true), (Matrix.CreateTranslation(3, 2, -1), (float?)0, false) })
        {
            cached.SeekWithCurrentAttachment(.2f, root, _ => Matrix.Identity);
            original.SeekWithCurrentAttachment(.2f, root, _ => Matrix.Identity);
            var packet = EqualPackets(cached, original, Camera(0) with { VfxGroundHeight = ground });
            Assert.That(cached.TerrainBasisUnsupported, Is.EqualTo(unsupported));
            Assert.That(original.TerrainBasisUnsupported, Is.EqualTo(unsupported));
            Assert.That(packet.Length > 0, Is.EqualTo(!unsupported));
        }
    }

    [Test]
    public void FailedReadonlyHistoryAlwaysExitsTheProjectionScope()
    {
        var conversion = Conversion(false);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        var unavailable = false;
        Matrix History(float _) => unavailable ? throw new InvalidOperationException("DIAG readonly history unavailable") : Matrix.Identity;
        cached.Seek(.2f, Matrix.Identity, History); original.Seek(.2f, Matrix.Identity, _ => Matrix.Identity);
        unavailable = true;
        Assert.Throws<InvalidOperationException>(() => cached.ProjectedFrame(Camera(0), false));
        AssertProjectionScopeEnded(cached);
        unavailable = false;
        Assert.That(EqualPackets(cached, original, Camera(0)), Is.Not.Empty);
    }

    [Test]
    public void ExistingForceWarningStillMemoizesValidAncestorsAndReplaysDiagnostics()
    {
        var conversion = NestedEvents(terrain: false);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        var historyReads = 0;
        Matrix History(float _) { historyReads++; return Matrix.Identity; }
        cached.Seek(.2f, Matrix.Identity, History); original.Seek(.2f, Matrix.Identity, _ => Matrix.Identity);
        SetFlag(cached, "ForceBasisUnsupported", true); SetFlag(original, "ForceBasisUnsupported", true);
        var child = ((IEnumerable)Field(cached, "_emitters")).Cast<object>().OrderByDescending(EventDepth).First();
        Assert.That(EventDepth(child), Is.GreaterThanOrEqualTo(2));
        using (ProjectionScope(cached))
        {
            historyReads = 0;
            var first = Anchor(cached, child, .2);
            var scale = Scale(cached, child, .2);
            var readsAfterMiss = historyReads;
            Assert.That(readsAfterMiss, Is.GreaterThan(0), "The control must evaluate real readonly ancestor history on the miss.");
            Assert.That(((ICollection)Field(cached, "_projectionEventAnchors")).Count, Is.GreaterThan(0));
            Assert.That(((ICollection)Field(cached, "_projectionEventScales")).Count, Is.GreaterThan(0));
            SetFlag(cached, "ForceBasisUnsupported", false);
            Assert.That(Anchor(cached, child, .2), Is.EqualTo(first));
            Assert.That(Scale(cached, child, .2), Is.EqualTo(scale));
            Assert.That(historyReads, Is.EqualTo(readsAfterMiss), "Repeated keys must really hit, even when a Force diagnostic already exists.");
            Assert.That(cached.ForceBasisUnsupported, Is.True, "A hit must replay the complete cached diagnostic payload.");
        }
        AssertProjectionScopeEnded(cached);
        Assert.That(EqualPackets(cached, original, Camera(0)), Is.Not.Empty);
    }

    [Test]
    public void AngularOverflowInvalidatesEarlierProjectionKeysAndPreservesOriginalPackets()
    {
        var parent = Document(true); var emitter = parent.Root.Descendants("emitter").Single();
        Set(emitter, "MaximumParticles", "1", attribute: true);
        Set(emitter, "ParticleSpinDeceleration", VfxLibraryVisibilityTests.Constant("x", -1000000));
        Set(emitter, "EmitterPosition", Curve(("x", 0, .1f), ("y", 0, 0), ("z", 0, 0)));
        foreach (var axis in new[] { "X", "Y", "Z" }) Set(emitter, "ParticleSpinDirection" + axis, "CCW", attribute: true);
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleSpin_Min"), new XAttribute("x", 150), new XAttribute("y", 220), new XAttribute("z", 300)),
            new XElement("property", new XAttribute("name", "ParticleSpin_Max"), new XAttribute("x", 150), new XAttribute("y", 220), new XAttribute("z", 300)),
            new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")),
            new XElement(VfxDocument.Read(VfxParticleEventTests.Source(true, "true")).Root.Descendants("property_event").Single()));
        var link = emitter.Element("property_event")!; link.SetAttributeValue("track_scale", "true");
        var childDocument = Document(false);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/appearance-angular-overflow.xml", path =>
            path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? childDocument.Write() : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters.Any(definition => definition.ModelQuaternionSpin), Is.True);
        var cached = Cached(conversion); var original = new TotalWarVfxSimulation(conversion);
        cached.Seek(.0001f, Matrix.Identity); original.Seek(.0001f, Matrix.Identity);
        Assert.That(cached.ModelSpinRangeUnsupported, Is.False, "Overflow must first occur during projection, not a committed simulation tick.");
        object Child(TotalWarVfxSimulation simulation) => ((IEnumerable)Field(simulation, "_emitters")).Cast<object>().Single(state => EventDepth(state) == 1);
        var cachedChild = Child(cached); var originalChild = Child(original);
        using (ProjectionScope(cached))
        {
            Assert.That(Anchor(cached, cachedChild, 0), Is.EqualTo(Anchor(original, originalChild, 0)));
            Assert.That(Scale(cached, cachedChild, 0), Is.EqualTo(Scale(original, originalChild, 0)));
            Assert.That(((ICollection)Field(cached, "_projectionEventAnchors")).Count, Is.GreaterThan(0));
            var overflow = Anchor(cached, cachedChild, .0001);
            Assert.That(overflow, Is.EqualTo(Anchor(original, originalChild, .0001)));
            Assert.That(cached.ModelSpinRangeUnsupported, Is.True);
            Assert.That(original.ModelSpinRangeUnsupported, Is.True);
            Assert.That(Field(cached, "_projectionAngularRevision"), Is.EqualTo(1));
            var anchors = (IDictionary)Field(cached, "_projectionEventAnchors");
            Assert.That(anchors.Keys.Cast<object>().Any(key => (long)key.GetType().GetProperty("Time")!.GetValue(key)! == BitConverter.DoubleToInt64Bits(.0001)), Is.False,
                "A calculation that changed angular state must not save a pre-revision key.");
            Assert.That(Anchor(cached, cachedChild, .0001), Is.EqualTo(Anchor(original, originalChild, .0001)));
            Assert.That(Scale(cached, cachedChild, 0), Is.EqualTo(Scale(original, originalChild, 0)));
            Assert.That(anchors.Keys.Cast<object>().Any(key => (int)key.GetType().GetProperty("AngularRevision")!.GetValue(key)! == 1), Is.True);
            Assert.That(Field(cached, "_projectionAngularRevision"), Is.EqualTo(1), "An already unsupported emitter must not mutate the revision again.");
        }
        AssertProjectionScopeEnded(cached);
        EqualPackets(cached, original, Camera(0));
    }

    private static IDisposable ProjectionScope(TotalWarVfxSimulation simulation) => (IDisposable)typeof(TotalWarVfxSimulation)
        .GetNestedType("ProjectionEventScope", BindingFlags.NonPublic)!.GetConstructor(Private, null, [typeof(TotalWarVfxSimulation)], null)!.Invoke([simulation]);
    private static (Matrix Transform, bool Valid) Anchor(TotalWarVfxSimulation simulation, object emitter, double time)
    {
        object[] arguments = [emitter, time, false, false];
        var result = (Matrix)typeof(TotalWarVfxSimulation).GetMethod("EventAnchor", Private)!.Invoke(simulation, arguments)!;
        return (result, (bool)arguments[2]);
    }
    private static Vector3 Scale(TotalWarVfxSimulation simulation, object emitter, double time) => (Vector3)typeof(TotalWarVfxSimulation)
        .GetMethod("EventScale", Private)!.Invoke(simulation, [emitter, time, false])!;
    private static void SetFlag(TotalWarVfxSimulation simulation, string name, bool value) => typeof(TotalWarVfxSimulation)
        .GetProperty(name, Private)!.SetValue(simulation, value);
    private static byte[] EqualPackets(TotalWarVfxSimulation cached, TotalWarVfxSimulation original, CommonShaderParameters camera, bool cull = false)
    {
        Assert.That(cached.ProjectionMemoEnabled, Is.True);
        Assert.That(original.ProjectionMemoEnabled, Is.False);
        Clear.Invoke(original, null);
        var expected = Packet(original.ProjectedFrame(camera, cull));
        AssertProjectionScopeEnded(original);
        var actual = Packet(cached.ProjectedFrame(camera, cull));
        AssertProjectionScopeEnded(cached);
        foreach (var name in new[] { "ModelSpinBasisUnsupported", "ModelMotionScaleUnsupported", "ModelSpinRangeUnsupported", "SpriteSpinBasisUnsupported",
            "SpriteSpinRangeUnsupported", "ForceBasisUnsupported", "TerrainBasisUnsupported" })
            Assert.That(typeof(TotalWarVfxSimulation).GetProperty(name)!.GetValue(cached),
                Is.EqualTo(typeof(TotalWarVfxSimulation).GetProperty(name)!.GetValue(original)), name);
        Assert.That(actual, Is.EqualTo(expected), "Every native packet byte and model variant must match uncached evaluation.");
        return actual;
    }
    private static TotalWarVfxSimulation Cached(VfxConversion conversion) => new(conversion) { ProjectionMemoEnabled = true };
    private static void AssertProjectionScopeEnded(TotalWarVfxSimulation simulation)
    {
        Assert.That(Field(simulation, "_projectingEventTints"), Is.False);
        foreach (var name in ProjectionCaches) Assert.That(((ICollection)Field(simulation, name)).Count, Is.Zero, name);
    }
    private static int EventDepth(object state)
    {
        var result = 0;
        while (state.GetType().GetField("Parent", Private)!.GetValue(state) is { } parent)
        { state = parent.GetType().GetField("Emitter", Private)!.GetValue(parent)!; result++; }
        return result;
    }
    private static VfxConversion NestedEvents(bool terrain)
    {
        var parent = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var middle = VfxDocument.Read(VfxParticleEventTests.Source(true, "true"));
        var child = Document(false);
        parent.Root.Descendants("property_event").Single().SetAttributeValue("vfx_name", "middle");
        foreach (var document in new[] { parent, middle })
        {
            Set(document.Root.Descendants("emitter").Single(), "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION", attribute: true);
            var link = document.Root.Descendants("property_event").Single();
            link.SetAttributeValue("track_colour", "true"); link.SetAttributeValue("track_alpha", "true"); link.SetAttributeValue("track_scale", "true");
        }
        if (terrain)
        {
            var emitter = parent.Root.Descendants("emitter").Single();
            Set(emitter, "ParticlesSpawnOnTerrain", "true", attribute: true);
            Set(emitter, "ParticleRelativeSpawnOrientation", "RELATIVE_TO_TERRAIN_NORMAL", attribute: true);
        }
        return TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/appearance-ancestors.xml", path =>
            path.EndsWith("middle.xml", StringComparison.OrdinalIgnoreCase) ? middle.Write()
                : path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : VfxModelVariantTests.Resolve(path)));
    }
    private static byte[] Packet(VfxPreviewParticle[] frame)
    {
        using var stream = new MemoryStream();
        foreach (var particle in frame)
        {
            var render = particle.Render;
            stream.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref render, 1)));
            stream.Write(BitConverter.GetBytes(particle.ModelVariant));
        }
        return stream.ToArray();
    }
    private static object Field(object value, string name) => value.GetType().GetField(name, Private)!.GetValue(value)!;
    private static int AppearanceCount(TotalWarVfxSimulation simulation) => (int)Field(simulation, "_appearanceCount");
    private static int CompleteEntries(TotalWarVfxSimulation simulation) => ((Array)Field(simulation, "_appearanceEntries")).Cast<object>()
        .Count(entry => (bool)entry.GetType().GetProperty("Complete", Private)!.GetValue(entry)!);
    private static CommonShaderParameters Camera(int index, float distance = 15)
    {
        var eye = new Vector3(distance * MathF.Sin(.13f * index), 3 + index * .1f, distance * MathF.Cos(.13f * index));
        return new(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up), Matrix.CreatePerspectiveFieldOfView(.8f, 1.6f, .01f, 1000),
            eye, Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], ViewportHeight: 1000, ViewportWidth: 1600, VfxGroundHeight: 0,
            MaterialTimeSeconds: 2.5f, VfxLowOpacityCulling: 0);
    }
    private static VfxConversion Conversion(bool model) => Convert(Document(model));
    private static VfxConversion Convert(VfxDocument document)
    {
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/appearance-cache.xml", VfxModelVariantTests.Resolve));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        return conversion;
    }
    private static VfxDocument Document(bool model)
    {
        var document = VfxDocument.Read(model ? VfxModelVariantTests.Source(1) : VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        Set(emitter, "EmissionRateCurve", VfxLibraryVisibilityTests.Constant("x", 12));
        Set(emitter, "ParticleLifeTime", VfxLibraryVisibilityTests.Constant("x", .9f));
        Set(emitter, "MaximumParticles", "64", attribute: true);
        Set(emitter, "ParticleSimulationSpace", "LOCAL_SPACE_POSITION_ROTATION", attribute: true);
        Set(emitter, "ParticleInitialSpeed", VfxLibraryVisibilityTests.Constant("x", .3f));
        Set(emitter, "ParticleScale", Curve(("x", .2f, 1.7f), ("y", .8f, .3f), ("z", .9f, 1.2f)));
        var quality = document.Root.Descendants("quality").First();
        quality.Elements("modifier").Where(element => (string?)element.Attribute("fragment") is "vs_colour" or "vs_vertex_alpha").Remove();
        quality.Add(new XElement("modifier", new XAttribute("fragment", "vs_colour"),
            new XElement("property", new XAttribute("name", "curve_colour"), Curve(("r", .2f, 1f), ("g", 1f, .2f), ("b", .4f, .9f), ("brightness", .25f, 1.5f)))),
            new XElement("modifier", new XAttribute("fragment", "vs_vertex_alpha"),
                new XElement("property", new XAttribute("name", "curve_alpha"), Curve(("alpha", 1.2f, .05f)))));
        return document;
    }
    private static void Set(XElement emitter, string name, string value, bool attribute = false)
    {
        emitter.Elements().Where(element => (string?)element.Attribute("name") == name).Remove();
        var property = new XElement("property", new XAttribute("name", name));
        if (attribute) property.SetAttributeValue("value", value); else property.Value = value;
        emitter.Add(property);
    }
    private static string Curve(params (string Axis, float First, float Last)[] channels)
        => string.Join('|', channels.Select(channel => FormattableString.Invariant(
            $"{channel.Axis}_min,0,1;0,{channel.First},linear,linear,0,0,0,0,0;1,{channel.Last},linear,linear,0,0,0,0,0")))
            + ":" + string.Join('|', channels.Select((channel, index) => $"{channel.Axis},0;{index},{index}"));
}
