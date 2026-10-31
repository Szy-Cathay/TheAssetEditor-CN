#nullable enable
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxInteractiveFrameBudgetTests
{
    [Test]
    public void WholeFrameStopsAfterItsAllowanceAndPermitsOnlyTheCurrentAtomicOvershoot()
    {
        var clock = TimeSpan.Zero;
        var budget = new VfxInteractiveFrameBudget(clock: () => clock);
        var first = new object(); var second = new object(); var third = new object();
        budget.ResetFrame();
        Assert.That(budget.TryBegin(first), Is.True);
        clock += TimeSpan.FromMilliseconds(3);
        budget.End(first, stillPending: false);
        Assert.That(budget.TryBegin(second), Is.True);
        clock += TimeSpan.FromMilliseconds(3);
        budget.End(second, stillPending: false);
        Assert.That(budget.Spent, Is.EqualTo(TimeSpan.FromMilliseconds(6)));
        Assert.That(budget.TryBegin(third), Is.False, "Do not start another atom after the first unavoidable overshoot.");
        budget.ResetFrame();
        Assert.That(budget.TryBegin(third), Is.True);
        budget.End(third, stillPending: false);
    }

    [Test]
    public void TwentyFourPendingEffectsGetFairTurnsWithoutStarvationOrReducingTheirWork()
    {
        var clock = TimeSpan.Zero;
        var budget = new VfxInteractiveFrameBudget(clock: () => clock);
        var owners = Enumerable.Range(0, 24).Select(_ => new object()).ToArray();
        var completed = new int[owners.Length];
        for (var frame = 0; frame < 50 && completed.Any(count => count < 3); frame++)
        {
            budget.ResetFrame();
            var turns = 0;
            for (var index = 0; index < owners.Length; index++)
            {
                if (completed[index] >= 3 || !budget.TryBegin(owners[index])) continue;
                completed[index]++;
                turns++;
                clock += TimeSpan.FromMilliseconds(2);
                budget.End(owners[index], stillPending: completed[index] < 3);
            }
            Assert.That(turns, Is.LessThanOrEqualTo(2));
            Assert.That(budget.Spent, Is.LessThanOrEqualTo(TimeSpan.FromMilliseconds(4)));
        }
        Assert.That(completed, Is.All.EqualTo(3), "Every original two-millisecond operation must eventually run, including late scene entries.");
        Assert.That(budget.PendingCount, Is.Zero);
    }

    [Test]
    public void AnAbsentHeadCanDelayOneFrameThenIsRemovedBeforeOtherEffectsRun()
    {
        var clock = TimeSpan.Zero;
        var budget = new VfxInteractiveFrameBudget(clock: () => clock);
        var hidden = new object(); var visible = new object();
        budget.ResetFrame();
        Assert.That(budget.TryBegin(hidden), Is.True);
        clock += TimeSpan.FromMilliseconds(4);
        budget.End(hidden, stillPending: true);
        Assert.That(budget.TryBegin(visible), Is.False);
        budget.ResetFrame();
        Assert.That(budget.TryBegin(visible), Is.False);
        budget.ResetFrame();
        Assert.That(budget.PendingCount, Is.EqualTo(1));
        Assert.That(budget.TryBegin(visible), Is.True);
        budget.End(visible, stillPending: false);
        Assert.That(budget.PendingCount, Is.Zero);
    }

    [Test]
    public void ExplicitCancellationUnblocksTheNextOwnerAndStillChargesCancelledActiveWork()
    {
        var clock = TimeSpan.Zero;
        var budget = new VfxInteractiveFrameBudget(clock: () => clock);
        var first = new object(); var second = new object();
        budget.ResetFrame();
        Assert.That(budget.TryBegin(first), Is.True);
        budget.Cancel(first);
        clock += TimeSpan.FromMilliseconds(2);
        budget.End(first, stillPending: true);
        Assert.That(budget.TryBegin(second), Is.True);
        clock += TimeSpan.FromMilliseconds(2);
        budget.End(second, stillPending: false);
        Assert.That(budget.PendingCount, Is.Zero);
        Assert.That(budget.Spent, Is.EqualTo(TimeSpan.FromMilliseconds(4)));
        budget.Cancel(first);
    }

    [Test]
    public void OneOwnerCannotSpendMultipleTurnsInTheSameFrameAndNestedTurnsAreRejected()
    {
        var budget = new VfxInteractiveFrameBudget(clock: () => TimeSpan.Zero);
        var owner = new object();
        budget.ResetFrame();
        Assert.That(budget.TryBegin(owner), Is.True);
        Assert.Throws<InvalidOperationException>(() => budget.TryBegin(new object()));
        Assert.Throws<InvalidOperationException>(() => budget.ResetFrame());
        Assert.Throws<InvalidOperationException>(() => budget.End(new object(), stillPending: true));
        budget.End(owner, stillPending: true);
        Assert.That(budget.TryBegin(owner), Is.False);
        budget.ResetFrame();
        Assert.That(budget.TryBegin(owner), Is.True);
        budget.End(owner, stillPending: false);
    }
}
