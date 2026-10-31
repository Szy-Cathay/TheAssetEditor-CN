using GameWorld.Core.Animation;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [Test]
    public void EffectPlayback_OnlyRunningUnchangedTimelineUsesIncrementalForwardUpdates()
    {
        using var control = new TailPlaybackControl(0, 0);
        control.Node.EnableInteractiveReload = true;
        void Update(AnimationPlaybackTime time, bool advancing)
        {
            control.Update(time);
            Assert.That(control.Node.IsPlaybackAdvancing, Is.EqualTo(advancing));
            Assert.That(control.Factory.Created.Last().ForwardReplayAllowed, Is.EqualTo(!advancing),
                "The shared node must distinguish slow playback from a new user timeline target.");
        }
        Update(new(.1f, .1, 1, 0, true, false), false);
        Update(new(.17f, .17, 1, 0, true, false), true);
        Assert.That(control.Factory.Created.Single().Time, Is.EqualTo(.17f));
        Update(new(.17f, .17, 1, 0, false, false), false);
        Update(new(.24f, .24, 1, 0, true, false), true);
        Update(new(.8f, .8, 2, 0, true, false), false);
        Update(new(.9f, .9, 2, 0, true, false), true);
        Update(new(0, 1, 2, 1, true, false), false);
        Update(new(.07f, 1.07, 2, 1, true, false), true);
        control.Update(new(.07f, 0, 3, 0, false, true));
        Assert.That(control.Node.IsPlaybackAdvancing, Is.False);
    }
}
