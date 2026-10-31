using Editors.VfxEditor;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

namespace AssetEditorTests;

[TestClass]
public class VfxAttachmentCadenceTests
{
    [TestMethod]
    public void ConstantHistoryCertification_RejectsMotionAndPausedTeleports()
    {
        var history = new VfxAttachmentHistory();
        Assert.IsTrue(history.IsConstant);
        history.Reset(Matrix.Identity);
        history.Record(.1f, Matrix.Identity);
        history.Record(.2f, Matrix.Identity);
        Assert.IsTrue(history.IsConstant);
        history.Record(.2f, Matrix.CreateTranslation(1, 0, 0));
        Assert.IsFalse(history.IsConstant);
        history.Truncate(.1f);
        Assert.IsTrue(history.IsConstant);
        history.Record(.3f, Matrix.CreateRotationY(.1f));
        Assert.IsFalse(history.IsConstant);
        history.Reset(Matrix.CreateTranslation(2, 0, 0));
        Assert.IsTrue(history.IsConstant);
    }

    [TestMethod]
    public void ReplayEndpoints_KeepTurnsAndSkipDuplicatePausedTimes()
    {
        var history = new VfxAttachmentHistory();
        history.Reset(Matrix.Identity);
        history.Record(.1f, Matrix.CreateTranslation(1, 0, 0));
        history.Record(.2f, Matrix.CreateTranslation(1, 0, 1));
        history.Record(.2f, Matrix.CreateTranslation(2, 0, 1));
        history.Record(.3f, Matrix.CreateTranslation(2, 0, 2));
        Assert.AreEqual(.1f, history.NextSampleTime(0));
        Assert.AreEqual(.2f, history.NextSampleTime(.1f));
        Assert.AreEqual(.3f, history.NextSampleTime(.2f));
        Assert.IsNull(history.NextSampleTime(.3f));
        Assert.AreEqual(new Vector3(1, 0, 1), history.Sample(.2f).Translation);
        Assert.AreEqual(new Vector3(2, 0, 1), history.SampleCurrent(.2f).Translation);
    }

    [TestMethod]
    public void ReplayEndpoints_AfterEditingThePastDoNotConsumeRemovedFutureTurns()
    {
        var history = new VfxAttachmentHistory();
        history.Reset(Matrix.Identity);
        history.Record(.1f, Matrix.CreateTranslation(1, 0, 0));
        history.Record(.2f, Matrix.CreateTranslation(1, 0, 1));
        history.Record(.3f, Matrix.CreateTranslation(2, 0, 1));
        history.Truncate(.15f);
        history.Record(.25f, Matrix.CreateTranslation(-1, 0, 0));
        Assert.AreEqual(.15f, history.NextSampleTime(.1f));
        Assert.AreEqual(.25f, history.NextSampleTime(.15f));
        Assert.IsNull(history.NextSampleTime(.25f));
        Assert.IsTrue(Vector3.Distance(new Vector3(1, 0, .5f), history.Sample(.15f).Translation) < .000001f);
    }
}
