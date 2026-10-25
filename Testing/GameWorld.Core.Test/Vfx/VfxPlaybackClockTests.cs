using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace GameWorld.Core.Test.Vfx;

public class VfxPlaybackClockTests
{
    [TestCase(.016)]
    [TestCase(.25)]
    [TestCase(1.5)]
    public void DelayedUiFrame_PreservesTheElapsedPlaybackTime(double elapsed)
    {
        using var host = new Host();
        host.Update(elapsed);
        Assert.That(host.Seeks.Last(), Is.EqualTo(elapsed).Within(.00001));
    }

    [Test]
    public void DifferentUiFrameRates_ReachTheSameAuthoredTime()
    {
        using var slow = new Host(); using var fast = new Host();
        for (var frame = 0; frame < 10; frame++) slow.Update(.25);
        for (var frame = 0; frame < 25; frame++) fast.Update(.1);
        Assert.Multiple(() =>
        {
            Assert.That(slow.Seeks.Last(), Is.EqualTo(2.5).Within(.00001));
            Assert.That(fast.Seeks.Last(), Is.EqualTo(slow.Seeks.Last()).Within(.00001));
        });
    }

    [Test]
    public void Pause_KeepsTimeButStillUpdatesTheAttachment()
    {
        using var host = new Host(); host.Update(.25);
        host.Component.IsPlaying = false;
        host.Component.Attachment = () => Matrix.CreateTranslation(3, 4, 5);
        host.Update(1.5);
        Assert.Multiple(() =>
        {
            Assert.That(host.Seeks.Last(), Is.EqualTo(.25));
            Assert.That(host.LastAttachment.Translation, Is.EqualTo(new Vector3(3, 4, 5)));
        });
    }

    [Test]
    public void DelayedFrameAtCycleEnd_PreservesTheExistingRestartBoundary()
    {
        using var host = new Host(); host.Update(7.9); host.Update(.25);
        Assert.That(host.Seeks.TakeLast(2), Is.EqualTo(new[] { 8f, 0f }));
        host.Update(.1);
        Assert.That(host.Seeks.Last(), Is.EqualTo(.1f));
    }

    [Test]
    public void Reload_RestartsTheClockInsteadOfReusingThePreviousTime()
    {
        using var host = new Host(); host.Update(1.5);
        host.Component.Reload(new([], "vfx/reloaded.xml", _ => null)); host.Update(.25);
        Assert.That(host.Seeks.Last(), Is.EqualTo(.25));
    }

    [Test]
    public void NegativeElapsedTime_DoesNotRewindPlayback()
    {
        using var host = new Host(); host.Update(.25); host.Update(-1);
        Assert.That(host.Seeks.Last(), Is.EqualTo(.25));
    }

    [Test]
    public void LoadedClock_ExposesTheRuntimeDurationWithoutInventingAFrameRate()
    {
        using var host = new Host();
        Assert.That(host.Component.IsLoaded, Is.False);
        Assert.That(host.Component.Duration, Is.Zero);
        host.Update(0);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.IsLoaded, Is.True);
            Assert.That(host.Component.Duration, Is.EqualTo(8));
            Assert.That(host.Component.CurrentTime, Is.Zero);
            Assert.That(host.Component.Loop, Is.True);
        });
    }

    [Test]
    public void Seek_QueuesOnlyTheLatestPositionAndPreservesPlaybackState()
    {
        using var host = new Host(); host.Update(.5);
        host.Component.Seek(2); host.Component.Seek(3);
        Assert.That(host.Seeks.Last(), Is.EqualTo(.5f), "UI seeking must not invoke the runtime outside Update.");
        host.Update(.25);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(3));
            Assert.That(host.Seeks.Last(), Is.EqualTo(3));
            Assert.That(host.Component.IsPlaying, Is.True);
        });
        host.Update(.25);
        Assert.That(host.Component.CurrentTime, Is.EqualTo(3.25f));
    }

    [TestCase(false, false)]
    [TestCase(true, true)]
    public void ExplicitSeekToEnd_StaysAtTheEndWhilePausedOrScrubbing(bool playing, bool scrubbing)
    {
        using var host = new Host(); host.Update(0);
        host.Component.IsPlaying = playing; host.Component.IsScrubbing = scrubbing;
        host.Component.Seek(8); host.Update(1); host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(8));
            Assert.That(host.Seeks.TakeLast(2), Is.EqualTo(new[] { 8f, 8f }));
            Assert.That(host.Component.IsPlaying, Is.EqualTo(playing));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Scrubbing_FreezesElapsedTimeAndResumesOnlyThePreviousPlaybackState(bool playing)
    {
        using var host = new Host(); host.Update(1);
        host.Component.IsPlaying = playing; host.Component.IsScrubbing = true;
        host.Component.Seek(3); host.Update(1); host.Update(1);
        Assert.That(host.Component.CurrentTime, Is.EqualTo(3));
        host.Component.IsScrubbing = false; host.Update(.25);
        Assert.That(host.Component.CurrentTime, Is.EqualTo(playing ? 3.25f : 3));
        Assert.That(host.Component.IsPlaying, Is.EqualTo(playing));
    }

    [Test]
    public void NonLoopingPlayback_PausesAtTheAuthoredEndInsteadOfRestarting()
    {
        using var host = new Host(); host.Component.Loop = false;
        host.Update(7.9); host.Update(.25); host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(8));
            Assert.That(host.Component.IsPlaying, Is.False);
            Assert.That(host.Seeks.TakeLast(2), Is.EqualTo(new[] { 8f, 8f }));
        });
    }

    [Test]
    public void Restart_IsDeferredAndDoesNotResumeAPausedPreview()
    {
        using var host = new Host(); host.Update(3); host.Component.IsPlaying = false;
        host.Component.Restart();
        Assert.That(host.Seeks.Last(), Is.EqualTo(3));
        host.Update(.25);
        Assert.That(host.Component.CurrentTime, Is.Zero);
        Assert.That(host.Component.IsPlaying, Is.False);
    }

    [Test]
    public void Reload_DiscardsAnOlderQueuedSeekAndRestartsTheNewDocument()
    {
        using var host = new Host(); host.Update(3); host.Component.Seek(6);
        host.Component.Reload(new([], "vfx/new-clock.xml", _ => null)); host.Update(0);
        Assert.That(host.Component.CurrentTime, Is.Zero);
        Assert.That(host.Seeks.Last(), Is.Zero);
    }

    [Test]
    public void StopAndDispose_ClearTheClockAndPendingOperations()
    {
        using var host = new Host(); host.Update(3);
        host.Component.IsScrubbing = true; host.Component.Seek(6); host.Component.Stop();
        var count = host.Seeks.Count; host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.Zero);
            Assert.That(host.Component.Duration, Is.Zero);
            Assert.That(host.Component.IsLoaded, Is.False);
            Assert.That(host.Component.IsPlaying, Is.False);
            Assert.That(host.Component.IsScrubbing, Is.False);
            Assert.That(host.Seeks.Count, Is.EqualTo(count));
            Assert.That(host.Disposals, Is.EqualTo(1));
        });
        host.Component.Dispose(); host.Component.Restart(); host.Update(1);
        Assert.That(host.Seeks.Count, Is.EqualTo(count));
        Assert.That(host.Disposals, Is.EqualTo(1));
    }

    [Test]
    public void PlaybackChanged_ReportsRealStateChangesWithoutRefreshingAnIdleClock()
    {
        using var host = new Host(); var changes = 0;
        host.Component.PlaybackChanged += () => changes++;
        host.Update(0); Assert.That(changes, Is.EqualTo(1));
        host.Update(0); Assert.That(changes, Is.EqualTo(1));
        host.Component.IsPlaying = false; host.Component.IsPlaying = false;
        Assert.That(changes, Is.EqualTo(2));
        host.Component.Seek(2); Assert.That(changes, Is.EqualTo(2));
        host.Update(1); Assert.That(changes, Is.EqualTo(3));
        host.Component.Stop(); Assert.That(changes, Is.EqualTo(4));
        host.Component.Stop(); Assert.That(changes, Is.EqualTo(4));
    }

    [Test]
    public void MovingTrail_CrossesShortCyclesWithoutResettingOrReloading()
    {
        using var host = new Host(.2f, hasMotionTrails: true, enableContinuous: true);
        host.Component.AttachmentAtTime = time => Matrix.CreateTranslation(time, 0, 0);
        host.BeginMotion();
        for (var frame = 1; frame <= 24; frame++)
        {
            host.Attachment = Matrix.CreateTranslation(frame + 1, 0, 0);
            host.Update(.1);
        }
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.IsContinuousTrailPreview, Is.True);
            Assert.That(host.Component.CurrentTime, Is.EqualTo(2.5f).Within(.00001));
            Assert.That(host.Component.Duration, Is.GreaterThanOrEqualTo(host.Component.CurrentTime));
            Assert.That(host.ContinuousSeeks, Has.Count.EqualTo(25));
            Assert.That(host.ContinuousSeeks, Is.Ordered.And.All.GreaterThan(0));
            Assert.That(host.LastAttachment.Translation, Is.EqualTo(new Vector3(25, 0, 0)));
            Assert.That(host.LastHistory, Is.SameAs(host.Component.AttachmentAtTime));
            Assert.That(host.Reloads, Is.EqualTo(1));
            Assert.That(host.Disposals, Is.Zero);
        });
    }

    [TestCase(false, true, true)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    public void ContinuousTrail_RequiresEditorOptInCapabilityAndActualMotion(bool enabled, bool hasMotionTrails, bool moved)
    {
        using var host = new Host(.5f, hasMotionTrails, enabled); host.Update(0);
        if (moved) host.Attachment = Matrix.CreateTranslation(1, 0, 0);
        host.Update(.5);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.IsContinuousTrailPreview, Is.False);
            Assert.That(host.Component.CurrentTime, Is.Zero);
            Assert.That(host.Seeks.TakeLast(2), Is.EqualTo(new[] { .5f, 0f }));
            Assert.That(host.ContinuousSeeks, Is.Empty);
        });
    }

    [Test]
    public void ContinuousTrail_SeekRestartPauseAndScrubRetainTheRecordedRange()
    {
        using var host = new Host(.25f, hasMotionTrails: true, enableContinuous: true);
        host.BeginMotion(); host.Update(1.9); host.Component.IsPlaying = false;
        host.Component.Seek(.5f); host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(.5f));
            Assert.That(host.Component.Duration, Is.EqualTo(2).Within(.00001));
            Assert.That(host.Component.IsPlaying, Is.False);
        });
        host.Component.Restart(); host.Update(1);
        Assert.That(host.Component.CurrentTime, Is.Zero);
        Assert.That(host.Component.Duration, Is.EqualTo(2).Within(.00001));
        host.Component.Seek(2); host.Update(0);
        host.Component.IsPlaying = true; host.Component.IsScrubbing = true; host.Update(1);
        Assert.That(host.Component.CurrentTime, Is.EqualTo(2));
        host.Component.IsScrubbing = false; host.Update(.25);
        Assert.That(host.Component.CurrentTime, Is.EqualTo(2.25f).Within(.00001));
    }

    [Test]
    public void ContinuousTrail_DisablingLoopReturnsToTheAuthoredEndAsAnExplicitSeek()
    {
        using var host = new Host(.25f, hasMotionTrails: true, enableContinuous: true);
        host.BeginMotion(); host.Update(1.9);
        var count = host.ContinuousSeeks.Count;
        var prepared = new List<(float Time, bool Explicit)>();
        host.Component.PreparingFrame += (time, explicitSeek) => prepared.Add((time, explicitSeek));
        host.Component.Loop = false; host.Update(.1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(.25f));
            Assert.That(host.Component.Duration, Is.EqualTo(.25f));
            Assert.That(host.Component.IsContinuousTrailPreview, Is.False);
            Assert.That(host.Component.IsPlaying, Is.False);
            Assert.That(host.ContinuousSeeks, Has.Count.EqualTo(count));
            Assert.That(prepared.Last(), Is.EqualTo((.25f, true)), "A mode switch must not be mistaken for a natural loop and erase the attachment path.");
        });
    }

    [Test]
    public void ContinuousTrail_ReloadAndStopClearTheMotionAndRecordedRange()
    {
        using var host = new Host(.25f, hasMotionTrails: true, enableContinuous: true);
        host.BeginMotion(); host.Update(1.9);
        host.Component.Reload(new([], "vfx/new-trail.xml", _ => null)); host.Update(0);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.Zero);
            Assert.That(host.Component.Duration, Is.EqualTo(.25f));
            Assert.That(host.Component.IsContinuousTrailPreview, Is.False);
            Assert.That(host.Reloads, Is.EqualTo(2));
            Assert.That(host.Disposals, Is.Zero);
        });
        host.HasMotionTrails = false;
        host.Component.Reload(new([], "vfx/ordinary.xml", _ => null)); host.Update(.5);
        Assert.That(host.Seeks.TakeLast(2), Is.EqualTo(new[] { .25f, 0f }));
        host.Component.Stop(); var count = host.Seeks.Count; host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.Duration, Is.Zero);
            Assert.That(host.Component.IsContinuousTrailPreview, Is.False);
            Assert.That(host.Seeks, Has.Count.EqualTo(count));
            Assert.That(host.Disposals, Is.EqualTo(1));
        });
    }

    [Test]
    public void ContinuousTrail_NaturalPlaybackPassesSixHundredSecondsWithoutResetting()
    {
        using var host = new Host(.25f, hasMotionTrails: true, enableContinuous: true);
        host.BeginMotion(); host.Update(599.4); host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(600.5f).Within(.001));
            Assert.That(host.Component.Duration, Is.GreaterThanOrEqualTo(host.Component.CurrentTime));
            Assert.That(host.Component.IsPlaying, Is.True);
            Assert.That(host.ContinuousSeeks.Skip(1), Is.All.GreaterThan(0));
            Assert.That(host.Reloads, Is.EqualTo(1));
        });
    }

    private sealed class Host : IDisposable
    {
        public List<float> Seeks { get; } = [];
        public List<float> ContinuousSeeks { get; } = [];
        public Matrix LastAttachment;
        public Matrix Attachment = Matrix.Identity;
        public Func<float, Matrix> LastHistory = null!;
        public bool HasMotionTrails;
        public int Disposals;
        public int Reloads;
        public VfxPreviewComponent Component { get; }

        public Host(float duration = 8, bool hasMotionTrails = false, bool enableContinuous = false)
        {
            HasMotionTrails = hasMotionTrails;
            var preview = new Mock<IVfxPreview>();
            preview.SetupGet(value => value.CycleDuration).Returns(duration);
            preview.SetupGet(value => value.HasMotionTrailEmitters).Returns(() => HasMotionTrails);
            preview.Setup(value => value.Reload(It.IsAny<VfxPreviewSnapshot>())).Callback(() => Reloads++);
            preview.SetupGet(value => value.EmitterCount).Returns(1);
            preview.SetupGet(value => value.Diagnostics).Returns(Array.Empty<VfxPreviewDiagnostic>());
            preview.Setup(value => value.Dispose()).Callback(() => Disposals++);
            preview.Setup(value => value.Seek(It.IsAny<float>(), It.IsAny<Matrix>()))
                .Callback<float, Matrix>((time, attachment) => { Seeks.Add(time); LastAttachment = attachment; });
            preview.Setup(value => value.Advance(It.IsAny<float>(), It.IsAny<Matrix>()))
                .Callback<float, Matrix>((delta, attachment) => { Seeks.Add((Seeks.LastOrDefault()) + delta); LastAttachment = attachment; });
            preview.Setup(value => value.SeekContinuousTrail(It.IsAny<float>(), It.IsAny<Matrix>(), It.IsAny<Func<float, Matrix>>()))
                .Callback<float, Matrix, Func<float, Matrix>>((time, attachment, history) =>
                {
                    ContinuousSeeks.Add(time); Seeks.Add(time); LastAttachment = attachment; LastHistory = history;
                });
            var factory = new Mock<IVfxPreviewFactory>();
            factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(preview.Object);
            Component = new(factory.Object, Mock.Of<IDeviceResolver>(), null!)
            {
                EnableContinuousTrailPreview = enableContinuous,
                Attachment = () => Attachment,
            };
            Component.Reload(new([], "vfx/clock.xml", _ => null));
        }

        public void BeginMotion()
        {
            Update(0); Attachment = Matrix.CreateTranslation(1, 0, 0); Update(.1);
        }
        public void Update(double elapsed) => Component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(elapsed)));
        public void Dispose() => Component.Dispose();
    }
}
