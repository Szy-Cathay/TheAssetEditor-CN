using System.Threading;
using System.Windows;
using Editors.VfxEditor;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
[Apartment(ApartmentState.STA)]
public class VfxPlaybackAdapterTests
{
    [Test]
    public void MissingRuntimeDoesNotInventPlaybackOrAnimationFrames()
    {
        using var player = new VfxPlaybackViewModel(null);
        player.ToggleAnimationPausePlay(); player.SetAnimationFirstFrame(); player.SetAnimationLastFrame();
        player.PlaybackPositionSeconds = 2;
        Assert.Multiple(() =>
        {
            Assert.That(player.IsPlaying.Value, Is.False);
            Assert.That(player.PlayerControlsVisibility.Value, Is.EqualTo(Visibility.Collapsed));
            Assert.That(player.SelectedAnimationCurrentTime.Value, Is.Zero);
            Assert.That(player.SelectedAnimationMaxTime.Value, Is.Zero);
            Assert.That(player.SelectedAnimationFps.Value, Is.Zero);
            Assert.That(player.SelectedAnimationFrameCount.Value, Is.Zero);
        });
    }

    [Test]
    public void RangeRefreshDoesNotTurnWpfCoercionIntoAQueuedSeek()
    {
        using var host = new Host(); host.Update(2);
        using var player = new VfxPlaybackViewModel(host.Component);
        player.SelectedAnimationMaxTime.PropertyChanged += (_, _) => player.PlaybackPositionSeconds = 0;
        host.Duration = 4;
        host.Update(0); host.Update(0);
        Assert.Multiple(() =>
        {
            Assert.That(player.SelectedAnimationMaxTime.Value, Is.EqualTo(4));
            Assert.That(player.PlaybackPositionSeconds, Is.EqualTo(2));
            Assert.That(host.Component.CurrentTime, Is.EqualTo(2));
            Assert.That(host.Seeks, Is.All.EqualTo(2));
        });
    }

    [Test]
    public void ScrubbingQueuesOnlyTheLatestSecondsAndKeepsPause()
    {
        using var host = new Host(); host.Update(0);
        host.Component.IsPlaying = false;
        using var player = new VfxPlaybackViewModel(host.Component);
        var count = host.Seeks.Count;
        player.IsScrubbing = true;
        player.PlaybackPositionSeconds = 3.1f;
        player.PlaybackPositionSeconds = 1.75f;
        player.PlaybackPositionSeconds = 4.25f;
        Assert.That(host.Seeks.Count, Is.EqualTo(count));
        host.Update(1);
        player.IsScrubbing = false;
        host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(4.25f));
            Assert.That(player.SelectedAnimationCurrentTime.Value, Is.EqualTo(4.25f));
            Assert.That(host.Component.IsPlaying, Is.False);
            Assert.That(player.IsPlaying.Value, Is.False);
        });
    }

    [Test]
    public void StartEndAndCollapseKeepTheLoadedPreviewAndPlaybackPreferences()
    {
        using var host = new Host(); host.Update(2);
        using var player = new VfxPlaybackViewModel(host.Component);
        player.LoopAnimation.Value = false;
        player.SetAnimationLastFrame(); host.Update(.25);
        Assert.That(host.Component.CurrentTime, Is.EqualTo(8));
        Assert.That(host.Component.IsPlaying, Is.True);
        player.SetAnimationFirstFrame(); host.Update(.25);
        Assert.That(host.Component.CurrentTime, Is.Zero);
        player.IsEnabled.Value = false;
        host.Update(1);
        Assert.That(host.Component.IsPlaying, Is.False);
        Assert.That(host.Component.CurrentTime, Is.Zero);
        player.IsEnabled.Value = true;
        host.Update(.25);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.IsPlaying, Is.True);
            Assert.That(host.Component.CurrentTime, Is.EqualTo(.25f));
            Assert.That(host.Component.IsLoaded, Is.True);
            Assert.That(host.Component.Loop, Is.False);
            Assert.That(host.Disposals, Is.Zero);
        });
    }

    [Test]
    public void DisposeUnsubscribesWithoutOwningThePreview()
    {
        using var host = new Host(); host.Update(2);
        var player = new VfxPlaybackViewModel(host.Component);
        player.Dispose(); player.Dispose();
        host.Component.Seek(3); host.Update(0);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(3));
            Assert.That(player.SelectedAnimationCurrentTime.Value, Is.EqualTo(2));
            Assert.That(host.Disposals, Is.Zero);
        });
    }

    [Test]
    public void MovingTrail_RangeGrowthDoesNotQueueCoercionSeeksAndRecordedTailRemainsReachable()
    {
        using var host = new Host(.25f, hasMotionTrails: true, enableContinuous: true); host.Update(0);
        using var player = new VfxPlaybackViewModel(host.Component);
        player.SelectedAnimationMaxTime.PropertyChanged += (_, _) => player.PlaybackPositionSeconds = 0;
        host.Attachment = Matrix.CreateTranslation(1, 0, 0);
        host.Update(.5); host.Update(1.5);
        Assert.Multiple(() =>
        {
            Assert.That(player.SelectedAnimationMaxTime.Value, Is.EqualTo(2).Within(.00001));
            Assert.That(player.PlaybackPositionSeconds, Is.EqualTo(2).Within(.00001));
            Assert.That(host.Seeks, Is.EqualTo(new[] { 0f, .5f, 2f }).Within(.00001));
            Assert.That(host.Component.IsContinuousTrailPreview, Is.True);
        });
        host.Component.IsPlaying = false; player.IsScrubbing = true;
        player.PlaybackPositionSeconds = .5f; host.Update(1);
        Assert.That(player.SelectedAnimationMaxTime.Value, Is.EqualTo(2).Within(.00001));
        Assert.That(player.PlaybackPositionSeconds, Is.EqualTo(.5f));
        player.PlaybackPositionSeconds = 2; host.Update(0); player.IsScrubbing = false;
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(2));
            Assert.That(player.SelectedAnimationCurrentTime.Value, Is.EqualTo(2));
            Assert.That(player.IsPlaying.Value, Is.False);
            Assert.That(host.Reloads, Is.EqualTo(1));
            Assert.That(host.Disposals, Is.Zero);
        });
    }

    [Test]
    public void MovingTrail_CollapseAndExpandPreserveTheLoadedTrailAndContinuousTime()
    {
        using var host = new Host(.2f, hasMotionTrails: true, enableContinuous: true);
        host.BeginMotion(); host.Update(.9);
        using var player = new VfxPlaybackViewModel(host.Component);
        player.IsEnabled.Value = false; host.Update(1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(1).Within(.00001));
            Assert.That(host.Component.IsLoaded, Is.True);
            Assert.That(host.Component.IsPlaying, Is.False);
        });
        player.IsEnabled.Value = true; host.Update(.1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.CurrentTime, Is.EqualTo(1.1f).Within(.00001));
            Assert.That(player.SelectedAnimationCurrentTime.Value, Is.EqualTo(host.Component.CurrentTime));
            Assert.That(player.IsPlaying.Value, Is.True);
            Assert.That(player.LoopAnimation.Value, Is.True);
            Assert.That(host.Reloads, Is.EqualTo(1));
            Assert.That(host.Disposals, Is.Zero);
        });
    }

    [Test]
    public void MovingTrail_DisablingTheSharedLoopReturnsToTheOriginalEndControls()
    {
        using var host = new Host(.2f, hasMotionTrails: true, enableContinuous: true);
        host.BeginMotion(); host.Update(.9);
        using var player = new VfxPlaybackViewModel(host.Component);
        var count = host.ContinuousSeeks.Count;
        player.LoopAnimation.Value = false; host.Update(.1);
        Assert.Multiple(() =>
        {
            Assert.That(host.Component.IsContinuousTrailPreview, Is.False);
            Assert.That(host.Component.CurrentTime, Is.EqualTo(.2f));
            Assert.That(player.SelectedAnimationMaxTime.Value, Is.EqualTo(.2f));
            Assert.That(player.SelectedAnimationCurrentTime.Value, Is.EqualTo(.2f));
            Assert.That(player.IsPlaying.Value, Is.False);
            Assert.That(host.ContinuousSeeks, Has.Count.EqualTo(count));
        });
        player.SetAnimationFirstFrame(); host.Update(0);
        Assert.That(host.Component.CurrentTime, Is.Zero);
        player.SetAnimationLastFrame(); host.Update(0);
        Assert.That(host.Component.CurrentTime, Is.EqualTo(.2f));
        Assert.That(player.IsPlaying.Value, Is.False);
    }

    // Fake runtime checks clock/binding ownership only; no GPU or visual acceptance.
    private sealed class Host : IDisposable
    {
        public float Duration = 8;
        public int Disposals;
        public int Reloads;
        public Matrix Attachment = Matrix.Identity;
        public List<float> Seeks { get; } = [];
        public List<float> ContinuousSeeks { get; } = [];
        public VfxPreviewComponent Component { get; }

        public Host(float duration = 8, bool hasMotionTrails = false, bool enableContinuous = false)
        {
            Duration = duration;
            var preview = new Mock<IVfxPreview>();
            preview.SetupGet(value => value.HasMotionTrailEmitters).Returns(hasMotionTrails);
            preview.Setup(value => value.Reload(It.IsAny<VfxPreviewSnapshot>())).Callback(() => Reloads++);
            preview.SetupGet(value => value.CycleDuration).Returns(() => Duration);
            preview.SetupGet(value => value.EmitterCount).Returns(1);
            preview.SetupGet(value => value.Diagnostics).Returns(Array.Empty<VfxPreviewDiagnostic>());
            preview.Setup(value => value.Seek(It.IsAny<float>(), It.IsAny<Matrix>()))
                .Callback<float, Matrix>((time, _) => Seeks.Add(time));
            preview.Setup(value => value.Dispose()).Callback(() => Disposals++);
            preview.Setup(value => value.SeekContinuousTrail(It.IsAny<float>(), It.IsAny<Matrix>(), It.IsAny<Func<float, Matrix>>()))
                .Callback<float, Matrix, Func<float, Matrix>>((time, _, _) => { Seeks.Add(time); ContinuousSeeks.Add(time); });
            var factory = new Mock<IVfxPreviewFactory>();
            factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(preview.Object);
            Component = new(factory.Object, Mock.Of<IDeviceResolver>(), null!)
            {
                EnableContinuousTrailPreview = enableContinuous,
                Attachment = () => Attachment,
            };
            Component.Reload(new([], "clock-only-no-assets", _ => null));
        }

        public void BeginMotion()
        {
            Update(0); Attachment = Matrix.CreateTranslation(1, 0, 0); Update(.1);
        }
        public void Update(double elapsed) => Component.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(elapsed)));
        public void Dispose() => Component.Dispose();
    }
}
