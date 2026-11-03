using System.Reflection;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace Test.AnimationMeta;

public partial class EffectVfxPreviewTests
{
    [Test]
    public void EffectWorldHistory_PausedSnappedPoseDoesNotShiftEarlierBirths()
    {
        using var control = new AttachmentControl(7, true);
        control.Update(.6f);
        control.Player.Play();
        control.Player.Update(new GameTime(TimeSpan.FromSeconds(.6), TimeSpan.Zero));
        control.Player.Pause();
        control.Player.Update(new GameTime(TimeSpan.FromSeconds(.6), TimeSpan.Zero));
        control.Effect.Update(.6f);
        var node = EffectParticleNode(control.Effect);
        Assert.That(node.PrepareFrame(control.Effect.WorldTransform), Is.True);
        var preview = (CapturingPreview)typeof(VfxPreviewSceneNode)
            .GetField("_preview", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(node)!;
        var interpolatedCurrent = control.Local * control.Bone(.6f) * control.Root.ModelMatrix;
        Assert.That(Vector3.Distance(preview.Attachment.Translation, interpolatedCurrent.Translation), Is.GreaterThan(.1f),
            "The real paused player holds a snapped frame, so inverse sampled-current compensation is invalid.");
        AssertParameterMatrix(preview.History!(0), control.ExpectedAt(control.Start));
        AssertParameterMatrix(preview.History(.15f), control.ExpectedAt(control.Start + .15f));
        Assert.That(preview.UseCurrentAttachmentForFrame, Is.True,
            "Current local-space drawing must use the held model pose while birth history remains continuous.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EffectWorldHistory_ExplicitWorldSamplesPreserveRotatedNonuniformAndMirroredActorRoots(bool mirrored)
    {
        using var control = new TailPlaybackControl(0, 0);
        var actor = Matrix.CreateScale(mirrored ? -2 : 2, 3, .5f)
            * Matrix.CreateFromYawPitchRoll(.7f, -.4f, .3f) * Matrix.CreateTranslation(8, -3, 11);
        Matrix Bone(float seconds) => Matrix.CreateScale(1 + seconds * .2f)
            * Matrix.CreateFromYawPitchRoll(seconds, .3f * seconds, -.4f * seconds)
            * Matrix.CreateTranslation(seconds * 4, 1, -2);
        Matrix At(float seconds) => Bone(seconds) * actor;
        control.Node.CurrentAttachment = () => At(.8f);
        control.Node.WorldAttachmentAtTime = At;
        control.Node.LocalAttachmentAtTime = Bone;
        control.Update(new(.8f, .8, 0, 0, false, false), Matrix.CreateTranslation(100, 200, 300));
        var preview = control.Factory.Created.Single();
        AssertParameterMatrix(preview.Attachment, At(.8f));
        AssertParameterMatrix(preview.History!(0), At(0));
        AssertParameterMatrix(preview.History(.3f), At(.3f));
    }

    [Test]
    public void EffectWorldHistory_ZeroCurrentBoneScaleDoesNotInvalidateEarlierWorldSamples()
    {
        using var control = new TailPlaybackControl(0, 0);
        Matrix At(float seconds) => Matrix.CreateScale(seconds >= .5f ? 0 : 1)
            * Matrix.CreateRotationY(seconds) * Matrix.CreateTranslation(3 + seconds, 2, -1);
        control.Node.CurrentAttachment = () => At(.75f);
        control.Node.WorldAttachmentAtTime = At;
        control.Node.LocalAttachmentAtTime = At;
        control.Update(new(.75f, .75, 0, 0, false, false));
        var preview = control.Factory.Created.Single();
        AssertParameterMatrix(preview.Attachment, At(.75f));
        AssertParameterMatrix(preview.History!(.25f), At(.25f));
        AssertParameterMatrix(preview.History(0), At(0));
    }
}
