using System.Reflection;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Test.Vfx;

[NonParallelizable]
public class VfxRuntimePreparationTests
{
    private static int _staticExecutions, _constructors, _simulation, _io;

    private sealed class BodySentinel
    {
        static BodySentinel() => _staticExecutions++;
        public BodySentinel() => _constructors++;
        public void Simulate() => _simulation++;
        public static void ReadOrWriteFiles() => _io++;
    }
    private sealed class AdditionalScene
    {
        public void Update() { }
        public void Draw() { }
    }
    private sealed class AnotherScene
    {
        public void Update() { }
        public void Draw() { }
    }

    [Test]
    public async Task ActualPreparationDoesNotExecuteStaticConstructorsInstancesSimulationOrIo()
    {
        _staticExecutions = _constructors = _simulation = _io = 0;
        var planned = VfxRuntimePreparation.Methods(typeof(BodySentinel), true).ToArray();
        Assert.That(planned.Select(method => method.Name), Does.Contain(".cctor"));
        var queue = new VfxRuntimePreparation.PreparationQueue(VfxRuntimePreparation.PrepareMethod, () => planned);
        await queue.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(queue.Report.Prepared, Is.EqualTo(planned.Length));
            Assert.That(queue.Report.Failed, Is.Zero);
            Assert.That(_staticExecutions, Is.Zero);
            Assert.That(_constructors, Is.Zero);
            Assert.That(_simulation, Is.Zero);
            Assert.That(_io, Is.Zero);
        });
    }

    [Test]
    public async Task RepeatedAndConcurrentRequestsShareOneSerialWorkerAndDoNotPrepareMethodsTwice()
    {
        var owner = Environment.CurrentManagedThreadId;
        using var entered = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var inFlight = 0; var max = 0; var calls = new List<MethodBase>();
        var queue = new VfxRuntimePreparation.PreparationQueue(method =>
        {
            Assert.That(Environment.CurrentManagedThreadId, Is.Not.EqualTo(owner));
            var active = Interlocked.Increment(ref inFlight); max = Math.Max(max, active);
            try { entered.Set(); Assert.That(release.Wait(TimeSpan.FromSeconds(5)), Is.True); lock (calls) calls.Add(method); }
            finally { Interlocked.Decrement(ref inFlight); }
        }, () => VfxRuntimePreparation.Methods(typeof(BodySentinel), true));
        var core = queue.PrepareAsync();
        Assert.That(entered.Wait(TimeSpan.FromSeconds(5)), Is.True);
        var first = queue.PrepareAsync(typeof(AdditionalScene));
        var again = queue.PrepareAsync(typeof(AdditionalScene));
        var other = queue.PrepareAsync(typeof(AnotherScene));
        Assert.That(again, Is.SameAs(first)); Assert.That(queue.PrepareAsync(), Is.SameAs(core));
        release.Set(); await Task.WhenAll(core, first, other).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Multiple(() =>
        {
            Assert.That(max, Is.EqualTo(1));
            Assert.That(calls.Count, Is.EqualTo(calls.Distinct().Count()));
            Assert.That(calls.Any(method => method.DeclaringType == typeof(AdditionalScene)), Is.True);
            Assert.That(calls.Any(method => method.DeclaringType == typeof(AnotherScene)), Is.True);
        });
    }

    [Test]
    public async Task AFailedMethodOrPlanCompletesTheGateAndAllowsTheFollowingSceneToPrepare()
    {
        var failures = 0;
        var queue = new VfxRuntimePreparation.PreparationQueue(_ => { if (Interlocked.Increment(ref failures) == 1) throw new NotSupportedException(); },
            () => VfxRuntimePreparation.Methods(typeof(BodySentinel), true));
        await queue.PrepareAsync(typeof(AdditionalScene)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(queue.Report.Failed, Is.EqualTo(1)); Assert.That(queue.Report.Prepared, Is.GreaterThan(0));
        var brokenPlan = new VfxRuntimePreparation.PreparationQueue(_ => { }, () => throw new InvalidOperationException());
        await brokenPlan.PrepareAsync(typeof(AdditionalScene)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(brokenPlan.Report.Failed, Is.EqualTo(1)); Assert.That(brokenPlan.Report.Prepared, Is.GreaterThan(0));
    }

    [Test]
    public async Task MethodAndElapsedBoundsCompleteWithoutTryingTheRemainingCode()
    {
        var count = new VfxRuntimePreparation.PreparationQueue(_ => { }, () => VfxRuntimePreparation.Methods(typeof(BodySentinel), true), maximumMethods: 2);
        await count.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(count.Report.Attempted, Is.EqualTo(2)); Assert.That(count.Report.LimitedBatches, Is.EqualTo(1));
        var elapsed = new VfxRuntimePreparation.PreparationQueue(_ => Assert.Fail("A depleted budget must not invoke the compiler."),
            () => VfxRuntimePreparation.Methods(typeof(BodySentinel), true), maximumTime: TimeSpan.Zero);
        await elapsed.PrepareAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(elapsed.Report.Attempted, Is.Zero); Assert.That(elapsed.Report.LimitedBatches, Is.EqualTo(1));
    }

    [Test]
    public void TheBoundedPlanContainsMeasuredRenderRootsAndConcreteCollectionSortAndVertexMethods()
    {
        var plan = VfxRuntimePreparation.CoreMethods().ToArray();
        Assert.That(plan, Is.Not.Empty);
        TestContext.Out.WriteLine($"Bounded concrete plan: {plan.Distinct().Count()} unique methods.");
        Assert.That(plan.Distinct().Count(), Is.LessThanOrEqualTo(VfxRuntimePreparation.MaximumMethods));
        Assert.That(plan.All(method => !method.IsAbstract && !method.ContainsGenericParameters), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType == typeof(EffekseerVfxPreview) && method.Name == "DrawCore"), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType == typeof(TotalWarVfxSimulation) && method.Name == "FillProjectedFrame"), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType == typeof(List<EffekseerParticle>) && method.Name == "Add"), Is.True);
        Assert.That(plan.OfType<MethodInfo>().Any(method => method.Name == "DrawUserPrimitives" && method.IsGenericMethod
            && method.GetGenericArguments().SequenceEqual(new[] { typeof(VertexPositionColor) })), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType?.FullName?.Contains("GenericArraySortHelper", StringComparison.Ordinal) == true), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType?.Name == "SpriteFont" && method.Name == "MeasureString"), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType?.Name == "D3D11FramePresenter" && method.Name == "TrySubmit"), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType?.Name == "D3D11Image" && method.Name == "TryLockForPresentation"), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType?.Name == "GraphicsDevice" && method.Name == "PlatformApplyRenderTargets"), Is.True);
        Assert.That(plan.Any(method => method.DeclaringType?.Name == "AnimationSampler" && method.Name == "Sample"), Is.True);
        Assert.That(plan.OfType<MethodInfo>().Any(method => method.Name == "DrawUserIndexedPrimitives" && method.IsGenericMethod
            && method.GetGenericArguments().SequenceEqual(new[] { typeof(VertexPositionColorTexture) })), Is.True);
        Assert.That(plan.Count(method => method.DeclaringType?.Namespace?.StartsWith("Editors.", StringComparison.Ordinal) == true), Is.Zero);
    }
}