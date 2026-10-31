#nullable enable
using System.Collections;
using System.Reflection;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxConstantReloadPreparationTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity, new(0, 10, 30), Vector3.Zero,
        0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 2);

    [TestCase(false, false, false, false)]
    [TestCase(false, false, false, true)]
    [TestCase(false, false, true, false)]
    [TestCase(false, false, true, true)]
    [TestCase(false, true, false, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, true, true, false)]
    [TestCase(false, true, true, true)]
    [TestCase(true, false, false, false)]
    [TestCase(true, false, false, true)]
    [TestCase(true, false, true, false)]
    [TestCase(true, false, true, true)]
    [TestCase(true, true, false, false)]
    [TestCase(true, true, false, true)]
    [TestCase(true, true, true, false)]
    [TestCase(true, true, true, true)]
    public async Task WholeCapturedFramePreservesNegativeGroundModelsEventsFutureAndReverse(bool model, bool terrainEvents, bool initialize, bool current)
    {
        var conversion = Conversion(model, terrainEvents);
        var root = Matrix.CreateScale(1.25f) * Matrix.CreateTranslation(2, 4, -1);
        var ownerThread = Environment.CurrentManagedThreadId;
        var calls = 0;
        Matrix LiveHistory(float _) { Assert.That(Environment.CurrentManagedThreadId, Is.EqualTo(ownerThread)); calls++; return root; }
        var input = new VfxInteractiveReloadFrame(1.117f, root, LiveHistory, initialize, current, EmissionEnd: 1.8f, GroundHeight: 2);
        var reference = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = input.EmissionEnd };
        reference.EnableSeekCheckpoints();
        reference.PrepareInitialGround(input.GroundHeight, root, _ => root, current, false, null);
        if (initialize) _ = reference.CycleDuration;
        TotalWarVfxSimulation? prepared = null;
        foreach (var requested in new[] { 1.117f, 1.617f, .337f, 2.117f })
        {
            var frame = input with { Time = requested };
            ownerThread = Environment.CurrentManagedThreadId;
            var capture = Capture(frame);
            var observed = calls;
            using var worker = Create(conversion, capture, requested, prepared);
            var result = await Take(worker);
            prepared = (TotalWarVfxSimulation)result.GetType().GetProperty("Simulation")!.GetValue(result)!;
            var time = (float)result.GetType().GetProperty("Time")!.GetValue(result)!;
            Assert.That(calls, Is.EqualTo(observed), "The worker must never invoke the owner history delegate.");
            var expectedTime = initialize ? Math.Min(requested, reference.CycleDuration) : requested;
            if (current) reference.SeekWithCurrentAttachment(expectedTime, root, _ => root); else reference.Seek(expectedTime, root, _ => root);
            Assert.That(time, Is.EqualTo(expectedTime));
            Assert.That(prepared.ProjectedFrame(Camera, false), Is.Not.Empty, "The diagnostic must contain real submitted sprite/model/event particles.");
            Assert.That(prepared.ProjectedFrame(Camera, false), Is.EqualTo(reference.ProjectedFrame(Camera, false)));
            Assert.That(ParticleState(prepared), Is.EqualTo(ParticleState(reference)), "Include hidden births, variants, velocities and unused random channels.");
            Assert.That(Get(prepared, "_simulatedTime"), Is.EqualTo(Get(reference, "_simulatedTime")));
        }
    }

    [TestCase("history")]
    [TestCase("attachment")]
    [TestCase("ground")]
    [TestCase("end")]
    [TestCase("initialize")]
    [TestCase("current")]
    [TestCase("next")]
    [TestCase("continuous")]
    public async Task ChangedContextCancelsQueuedWholeFrameAndUsesAccurateOwnerFallback(string change)
    {
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var occupied = VfxInteractivePrewarm.RunPreparation(_ => release.Task, CancellationToken.None);
        var conversion = Conversion(false, true);
        using var preview = new EffekseerVfxPreview(null!);
        Set(preview, "_conversion", conversion); Set(preview, "_simulation", new TotalWarVfxSimulation(conversion));
        Set(preview, "_preparedReload", new VfxPreparedConversion(conversion, null, []));
        Set(preview, "_reloadSnapshot", new VfxPreviewSnapshot([1], "DIAG-whole-frame.xml", _ => [1]));
        Set(preview, "_reloadUploaded", true); Set(preview, "_reloadRetainsPlayback", true);
        preview.ConfigureInteractiveSeekHistory(true);
        var root = Matrix.CreateTranslation(2, 4, -1);
        var original = new VfxInteractiveReloadFrame(.317f, root, _ => root, GroundHeight: 2);
        try
        {
            Assert.That(preview.ContinueInteractiveReload(original), Is.False);
            var worker = Get(preview, "_reloadConstantFrame");
            Assert.That(worker, Is.Not.Null, "Explicitly constant history must select whole-frame background preparation.");
            var changed = change switch
            {
                "attachment" => original with { Attachment = Matrix.CreateTranslation(5, 4, 2) },
                "ground" => original with { GroundHeight = 1 },
                "end" => original with { EmissionEnd = .2f },
                "initialize" => original with { InitializeDuration = true },
                "current" => original with { UseCurrentAttachmentForFrame = true },
                "next" => original with { NextAttachmentSample = _ => null },
                "continuous" => original with { ContinuousTrail = true },
                _ => original,
            };
            if (change == "history") preview.ConfigureInteractiveSeekHistory(false);
            var complete = preview.ContinueInteractiveReload(changed);
            Assert.That(Get(worker!, "_disposed"), Is.True);
            Assert.That(Get(preview, "_reloadConstantFrame"), Is.Null);
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!complete)
            {
                Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "The exact owner fallback must make progress.");
                complete = preview.ContinueInteractiveReload(changed);
            }
            var actual = (TotalWarVfxSimulation)Get(preview, "_simulation")!;
            var reference = new TotalWarVfxSimulation(conversion) { EmissionEndTimeSeconds = changed.EmissionEnd };
            reference.PrepareInitialGround(changed.GroundHeight, changed.Attachment, changed.History, changed.UseCurrentAttachmentForFrame, changed.ContinuousTrail, changed.NextAttachmentSample);
            if (changed.InitializeDuration) _ = reference.CycleDuration;
            if (changed.ContinuousTrail) reference.SeekContinuousTrail(changed.Time, changed.Attachment, changed.History, changed.NextAttachmentSample);
            else if (changed.UseCurrentAttachmentForFrame) reference.SeekWithCurrentAttachment(changed.Time, changed.Attachment, changed.History!);
            else reference.Seek(changed.Time, changed.Attachment, changed.History);
            var camera = Camera with { VfxGroundHeight = changed.GroundHeight };
            Assert.That(actual.ProjectedFrame(camera, false), Is.EqualTo(reference.ProjectedFrame(camera, false)));
            Assert.That(ParticleState(actual), Is.EqualTo(ParticleState(reference)));
        }
        finally { release.TrySetResult(true); await occupied; }
    }

    private static VfxConversion Conversion(bool model, bool events)
    {
        if (events) return (VfxConversion)typeof(VfxInteractiveGroundPreparationTests).GetMethod("Conversion", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [model, false])!;
        var document = VfxDocument.Read(model ? VfxModelVariantTests.Source(2) : VfxLibraryVisibilityTests.Source(""));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("ranged_property", new XAttribute("name", "StartTime"), new XAttribute("min", -.5f), new XAttribute("max", -.5f)));
        emitter.Elements().FirstOrDefault(node => (string?)node.Attribute("name") == "ParticleLifeTime")?.SetValue(VfxLibraryVisibilityTests.Constant("x", 4));
        return TotalWarVfxConverter.Convert(new(document.Write(), "DIAG-whole.xml", model ? VfxModelVariantTests.Resolve : _ => [1]));
    }

    private static Type WorkerType => typeof(EffekseerVfxPreview).Assembly.GetType("GameWorld.Core.Vfx.VfxConstantReloadPreparation")
        ?? throw new AssertionException("Exact constant whole-frame preparation is missing.");
    private static object Capture(VfxInteractiveReloadFrame frame)
        => WorkerType.GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [frame])!;
    private static IDisposable Create(VfxConversion conversion, object context, float time, TotalWarVfxSimulation? simulation)
        => (IDisposable)WorkerType.GetConstructors(Private).Single().Invoke([conversion, context, time, true, simulation]);
    private static async Task<object> Take(object worker)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        var method = WorkerType.GetMethod("TryTake", Private)!;
        while (DateTime.UtcNow < deadline)
        {
            object?[] args = [null];
            if ((bool)method.Invoke(worker, args)!) return args[0]!;
            Assert.That(WorkerType.GetProperty("Failure", Private)!.GetValue(worker), Is.Null);
            await Task.Delay(1);
        }
        throw new AssertionException("The complete captured frame did not finish.");
    }
    private static object[] ParticleState(TotalWarVfxSimulation simulation)
        => ((IEnumerable)Get(simulation, "_particles")!).Cast<object>().Select(particle => (object)particle.GetType().GetFields()
            .Where(field => field.FieldType.IsValueType).OrderBy(field => field.Name).Select(field => field.GetValue(particle)).ToArray()).ToArray();
    private static object? Get(object owner, string field) => owner.GetType().GetField(field, Private)?.GetValue(owner);
    private static void Set(object owner, string field, object? value) => owner.GetType().GetField(field, Private)!.SetValue(owner, value);
}
