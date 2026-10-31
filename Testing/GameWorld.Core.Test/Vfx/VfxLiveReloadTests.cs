using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using NUnit.Framework;

namespace GameWorld.Core.Test.Vfx;

[TestFixture]
public class VfxLiveReloadTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ContentRefreshRetainsTheInspectedPhaseAndPlaybackIntent(bool playing)
    {
        var preview = new RecordingPreview();
        using var component = Create(preview);
        var resetCount = 0;
        component.SnapshotReloaded += () => resetCount++;
        component.Reload(Snapshot());
        Update(component, 3);
        component.IsPlaying = playing;
        var attachmentSeeks = new List<bool>();
        component.PreparingFrame += (_, explicitSeek) => attachmentSeeks.Add(explicitSeek);

        component.Reload(Snapshot(2), preservePlaybackState: true);
        Update(component, 1);

        Assert.Multiple(() =>
        {
            Assert.That(component.CurrentTime, Is.EqualTo(3));
            Assert.That(component.IsPlaying, Is.EqualTo(playing));
            Assert.That(preview.LastSeek, Is.EqualTo(3));
            Assert.That(preview.ParticleCount, Is.EqualTo(2), "The edited delayed contribution must be evaluated at the inspected phase.");
            Assert.That(resetCount, Is.EqualTo(1), "A content refresh must retain recorded attachment history.");
            Assert.That(attachmentSeeks, Is.EqualTo(new[] { false }), "Editing a value must not cancel an active attachment gesture.");
        });
        Update(component, .25f);
        Assert.That(component.CurrentTime, Is.EqualTo(playing ? 3.25f : 3));
    }

    [Test]
    public void ContentRefreshKeepsAnAlreadyRequestedScrub()
    {
        var preview = new RecordingPreview();
        using var component = Create(preview);
        component.Reload(Snapshot());
        Update(component, 3);
        component.IsPlaying = false;
        component.IsScrubbing = true;
        component.Seek(5);

        component.Reload(Snapshot(2), preservePlaybackState: true);
        Update(component, 1);

        Assert.Multiple(() =>
        {
            Assert.That(component.CurrentTime, Is.EqualTo(5));
            Assert.That(preview.LastSeek, Is.EqualTo(5));
            Assert.That(component.IsPlaying, Is.False);
            Assert.That(component.IsScrubbing, Is.True);
        });
    }

    [Test]
    public void MovingTrailRebuildsAtItsContinuousTimeUsingTheExistingHistory()
    {
        var preview = new RecordingPreview { Trail = true, CycleDuration = 1 };
        var attachment = Matrix.Identity;
        var history = new List<(float Time, Matrix Attachment)> { (0, attachment), (.5f, Matrix.CreateTranslation(3, 0, 2)), (2.5f, Matrix.CreateTranslation(-2, 0, 4)) };
        using var component = Create(preview);
        component.EnableContinuousTrailPreview = true;
        component.Attachment = () => attachment;
        component.AttachmentAtTime = time => history.Last(item => item.Time <= time).Attachment;
        component.SnapshotReloaded += () => { history.RemoveAll(item => item.Time > 0); };
        component.Reload(Snapshot());
        Update(component, .25f);
        history.Add((.5f, Matrix.CreateTranslation(3, 0, 2)));
        history.Add((2.5f, Matrix.CreateTranslation(-2, 0, 4)));
        attachment = Matrix.CreateTranslation(3, 0, 2);
        Update(component, .25f);
        attachment = Matrix.CreateTranslation(-2, 0, 4);
        Update(component, 2);
        component.IsPlaying = false;

        component.Reload(Snapshot(2), preservePlaybackState: true);
        Update(component, 1);

        Assert.Multiple(() =>
        {
            Assert.That(component.CurrentTime, Is.EqualTo(2.5f));
            Assert.That(component.Duration, Is.EqualTo(2.5f));
            Assert.That(component.IsContinuousTrailPreview, Is.True);
            Assert.That(preview.LastContinuousSeek, Is.EqualTo(2.5f));
            Assert.That(preview.HistoryAtTurn, Is.EqualTo(Matrix.CreateTranslation(3, 0, 2)));
            Assert.That(history.Count, Is.EqualTo(3));
            Assert.That(component.IsPlaying, Is.False);
        });
    }

    [Test]
    public void ContentRefreshRetainsTheRequestedPhaseWhilePreparationIsPending()
    {
        var preview = new RecordingPreview();
        using var component = Create(preview);
        component.Reload(Snapshot());
        Update(component, 3);
        component.IsPlaying = false;
        preview.DeferReload = true;

        component.Reload(Snapshot(2), preservePlaybackState: true);
        Update(component, 20);
        Assert.That(component.CurrentTime, Is.EqualTo(3));
        preview.FinishPreparation = true;
        Update(component, 20);

        Assert.Multiple(() =>
        {
            Assert.That(component.CurrentTime, Is.EqualTo(3));
            Assert.That(preview.LastSeek, Is.EqualTo(3));
            Assert.That(preview.ParticleCount, Is.EqualTo(2));
            Assert.That(component.IsPlaying, Is.False);
        });
    }

    [TestCase("source")]
    [TestCase("quality")]
    [TestCase("selection")]
    [TestCase("default")]
    public void AChangedPlaybackScopeOrLegacyReloadKeepsTheOriginalReset(string change)
    {
        var preview = new RecordingPreview();
        using var component = Create(preview);
        var snapshot = Snapshot();
        component.Reload(snapshot);
        Update(component, 3);
        component.IsPlaying = false;
        var resets = 0;
        component.SnapshotReloaded += () => resets++;
        var changed = change switch
        {
            "source" => snapshot with { SourcePath = "vfx/other.xml" },
            "quality" => snapshot with { Quality = "1" },
            "selection" => snapshot with { RootSelection = new(["other"], []) },
            _ => snapshot with { GameBytes = [2] },
        };

        component.Reload(changed, preservePlaybackState: change != "default");
        Update(component, 0);

        Assert.Multiple(() =>
        {
            Assert.That(component.CurrentTime, Is.Zero);
            Assert.That(preview.LastSeek, Is.Zero);
            Assert.That(component.IsPlaying, Is.False);
            Assert.That(resets, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnExplicitPendingResetCannotBePromotedByALaterContentRefresh()
    {
        var preview = new RecordingPreview();
        using var component = Create(preview);
        component.Reload(Snapshot());
        Update(component, 3);
        component.IsPlaying = false;

        component.Reload(Snapshot());
        component.Reload(Snapshot(2), preservePlaybackState: true);
        Update(component, 0);

        Assert.That(component.CurrentTime, Is.Zero);
        Assert.That(preview.LastSeek, Is.Zero);
    }

    private static VfxPreviewSnapshot Snapshot(byte value = 1) => new([value], "vfx/diagnostic-live-reload.xml", _ => null,
        RootSelection: new(["emitter"], []));
    private static VfxPreviewComponent Create(RecordingPreview preview)
    {
        var factory = new Mock<IVfxPreviewFactory>();
        factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(preview);
        return new(factory.Object, Mock.Of<IDeviceResolver>(), null!);
    }
    private static void Update(VfxPreviewComponent component, float elapsed)
        => component.Update(new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(elapsed)));

    private sealed class RecordingPreview : IVfxPreview
    {
        private byte _value;
        public bool Trail { get; init; }
        public bool DeferReload { get; set; }
        public bool FinishPreparation { get; set; }
        public IReadOnlyList<VfxPreviewDiagnostic> Diagnostics => [];
        public int EmitterCount => 1;
        public int TotalEmitterCount => 1;
        public int ParticleCount => LastSeek >= 2 ? _value : 0;
        public float CycleDuration { get; init; } = 8;
        public bool HasMotionTrailEmitters => Trail;
        public bool IsPreparing { get; private set; }
        public float LastSeek { get; private set; }
        public float? LastContinuousSeek { get; private set; }
        public Matrix? HistoryAtTurn { get; private set; }
        public void Reload(VfxPreviewSnapshot snapshot) { _value = snapshot.GameBytes[0]; IsPreparing = DeferReload; }
        public void PrepareInteractiveFrame(Matrix attachment, Func<float, Matrix>? attachmentAtTime)
        {
            if (FinishPreparation) IsPreparing = false;
        }
        public void Advance(float seconds, Matrix attachment) => Seek(seconds, attachment);
        public void Seek(float seconds, Matrix attachment) => LastSeek = seconds;
        public void SeekContinuousTrail(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime = null)
        {
            LastSeek = seconds;
            LastContinuousSeek = seconds;
            HistoryAtTurn = attachmentAtTime?.Invoke(.5f);
        }
        public void Draw(GameWorld.Core.Rendering.CommonShaderParameters parameters) { }
        public void Dispose() { }
    }
}
