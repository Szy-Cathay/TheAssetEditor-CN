using System.Reflection;
using System.Text;
using System.Runtime.CompilerServices;
using System.Windows.Interop;
using Editors.VfxEditor;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace AssetEditorTests;

[TestClass]
public class VfxEmitterInteractionTests
{
    [TestMethod]
    public void Transform_ReplacesFromOneBaselineAndCancellationRestoresIt()
    {
        var target = new VfxPreviewTransform();
        var initial = Matrix.CreateRotationY(.5f) * Matrix.CreateTranslation(1, 2, 3);
        target.Set(initial); target.Begin();
        target.Replace(ModalPreviewReplacement.Translate(new(2, 0, 0), PivotType.ObjectCenter));
        target.Replace(ModalPreviewReplacement.Translate(new(3, 0, 0), PivotType.ObjectCenter));
        Assert.AreEqual(new Vector3(4, 2, 3), target.Position);
        target.Cancel();
        Assert.AreEqual(initial, target.World);
        Assert.IsFalse(target.IsTransformActive);
    }

    [TestMethod]
    public void RotationScaleAndRejectedSingularity_RetainTheRootPivotAndBaseline()
    {
        var target = new VfxPreviewTransform(); target.Position = new(4, 5, 6); target.Begin();
        target.Replace(ModalPreviewReplacement.Rotate(Matrix.CreateRotationZ(1), PivotType.ObjectCenter));
        Assert.AreEqual(new Vector3(4, 5, 6), target.Position);
        target.Replace(ModalPreviewReplacement.Scale(Vector3.One, PivotType.ObjectCenter));
        Assert.AreEqual(new Vector3(4, 5, 6), target.Position);
        Assert.AreEqual(new Vector3(2), target.Scale);
        Assert.IsFalse(target.Replace(ModalPreviewReplacement.Scale(-Vector3.One, PivotType.ObjectCenter)));
        Assert.AreEqual(Matrix.CreateTranslation(4, 5, 6), target.World);
    }

    [DataTestMethod]
    [DataRow(GizmoMode.Translate)]
    [DataRow(GizmoMode.Rotate)]
    [DataRow(GizmoMode.NonUniformScale)]
    public void TransformHistory_ConfirmedGesturePublishesOneOriginalAndFinalPose(GizmoMode mode)
    {
        using var host = new Host(); host.Update(0);
        var original = Matrix.CreateTranslation(2, 3, 4);
        host.Interaction.Target.Set(original);
        var edits = new List<(Matrix Before, Matrix After)>();
        host.Interaction.TransformCommitted += (before, after) => edits.Add((before, after));
        host.Interaction.BeginTransform();
        var replacement = mode == GizmoMode.Translate ? ModalPreviewReplacement.Translate(new(3, 0, 0), PivotType.ObjectCenter)
            : mode == GizmoMode.Rotate ? ModalPreviewReplacement.Rotate(Matrix.CreateRotationY(.5f), PivotType.ObjectCenter)
            : ModalPreviewReplacement.Scale(new(1, .5f, .25f), PivotType.ObjectCenter);
        host.Interaction.ApplyTransform(replacement);
        var final = host.Interaction.Target.World;
        ConfirmTransform(host.Interaction);
        Assert.AreEqual(1, edits.Count);
        Assert.AreEqual(original, edits[0].Before); Assert.AreEqual(final, edits[0].After);
        Assert.AreNotEqual(original, final);
        Assert.IsFalse(host.Interaction.HasActiveInteraction);
        Assert.IsNull(host.Mouse.Object.MouseOwner);
    }

    [TestMethod]
    public void TransformHistory_CancelNoOpAndRestorationDoNotPublishEdits()
    {
        using var host = new Host(); host.Update(0);
        var edits = 0; host.Interaction.TransformCommitted += (_, _) => edits++;
        Assert.IsFalse(host.Interaction.CancelActiveInteraction());
        host.Interaction.BeginTransform();
        host.Interaction.ApplyTransform(ModalPreviewReplacement.Translate(new(3, 0, 0), PivotType.ObjectCenter));
        Assert.IsTrue(host.Interaction.HasActiveInteraction);
        Assert.IsTrue(host.Interaction.CancelActiveInteraction());
        Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
        host.Interaction.BeginTransform(); ConfirmTransform(host.Interaction);
        host.Interaction.BeginTransform();
        host.Interaction.ApplyTransform(ModalPreviewReplacement.Translate(new(5, 0, 0), PivotType.ObjectCenter));
        host.Mouse.Raise(mouse => mouse.CaptureInterrupted += null!);
        Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
        var restored = Matrix.CreateRotationZ(.3f) * Matrix.CreateTranslation(4, 5, 6);
        host.Interaction.RestoreTransform(restored);
        Assert.AreEqual(restored, host.Interaction.Target.World);
        Assert.IsFalse(host.Interaction.HasActiveInteraction);
        Assert.IsFalse(host.Interaction.IsSelected, "Restoration must not silently select the emitter.");
        Assert.IsNull(host.Mouse.Object.MouseOwner);
        Assert.AreEqual(0, edits);
    }

    [TestMethod]
    public void TransformHistory_ResetPublishesOnlyItsConfirmedChange()
    {
        using var host = new Host(); host.Update(0);
        var edits = new List<(Matrix Before, Matrix After)>();
        host.Interaction.TransformCommitted += (before, after) => edits.Add((before, after));
        host.Interaction.ResetTransform();
        Assert.AreEqual(0, edits.Count);
        var original = Matrix.CreateScale(2) * Matrix.CreateRotationY(.3f) * Matrix.CreateTranslation(4, 5, 6);
        host.Interaction.Target.Set(original);
        host.Interaction.ResetTransform();
        Assert.AreEqual(1, edits.Count);
        Assert.AreEqual(original, edits[0].Before); Assert.AreEqual(Matrix.Identity, edits[0].After);
        host.Interaction.ResetTransform();
        Assert.AreEqual(1, edits.Count);
        Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void TransformHistory_RestorationPreservesEarlierBirthSamplesAndCurrentPlayback(bool playing)
    {
        using var host = new Host(); host.Update(0);
        host.Interaction.Target.Position = new(2, 0, 0); host.Update(1);
        host.Interaction.Target.Position = new(4, 0, 0); host.Update(1);
        host.Preview.IsPlaying = playing;
        var time = host.Preview.CurrentTime; var duration = host.Preview.Duration;
        var edits = 0; host.Interaction.TransformCommitted += (_, _) => edits++;
        var restored = Matrix.CreateTranslation(10, 0, 0);
        host.Interaction.RestoreTransform(restored);
        Assert.AreEqual(time, host.Preview.CurrentTime); Assert.AreEqual(duration, host.Preview.Duration);
        Assert.AreEqual(playing, host.Preview.IsPlaying);
        Assert.AreEqual(1f, host.Interaction.History.Sample(.5f).Translation.X);
        Assert.AreEqual(3f, host.Interaction.History.Sample(1.5f).Translation.X);
        Assert.AreEqual(4f, host.Interaction.History.Sample(time).Translation.X, "Births at the restore boundary retain the previous left limit.");
        Assert.AreEqual(restored, host.Interaction.History.SampleCurrent(time));
        host.Update(0);
        Assert.AreEqual(restored, host.Interaction.Target.World);
        host.Update(.5);
        Assert.AreEqual(time + (playing ? .5f : 0), host.Preview.CurrentTime);
        Assert.AreEqual(restored, host.Interaction.Target.World, "The next frame must not replay the pre-undo attachment over the restored pose.");
        Assert.AreEqual(0, edits);
    }

    [TestMethod]
    public void TransformHistory_RestorationAfterRewindStartsANewPathWithoutSeekingOrReplayingTheRemovedFuture()
    {
        using var host = new Host(); host.Update(0);
        host.Interaction.Target.Position = new(2, 0, 0); host.Update(1);
        host.Interaction.Target.Position = new(4, 0, 0); host.Update(1);
        host.Preview.IsPlaying = false; host.Preview.Seek(.5f); host.Update(0);
        var restored = Matrix.CreateTranslation(7, 0, 0);
        host.Interaction.RestoreTransform(restored);
        Assert.AreEqual(.5f, host.Preview.CurrentTime); Assert.IsFalse(host.Preview.IsPlaying);
        Assert.AreEqual(.5f, host.Interaction.History.EndTime);
        Assert.AreEqual(.5f, host.Interaction.History.Sample(.25f).Translation.X);
        Assert.AreEqual(1f, host.Interaction.History.Sample(.5f).Translation.X);
        Assert.AreEqual(restored, host.Interaction.History.SampleCurrent(.5f));
        Assert.AreEqual(restored, host.Interaction.History.Sample(2));
        host.Update(0); host.Preview.IsPlaying = true; host.Update(.5);
        Assert.AreEqual(1f, host.Preview.CurrentTime);
        Assert.AreEqual(restored, host.Interaction.Target.World);
    }

    private static void ConfirmTransform(VfxEmitterInteractionComponent interaction)
        => typeof(VfxEmitterInteractionComponent).GetMethod("EndTransform", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(interaction, null);

    [TestMethod]
    public void History_PausedTeleportDoesNotRewriteEarlierBirthsOrGrowForEveryStationaryFrame()
    {
        var history = new VfxAttachmentHistory(); history.Reset(Matrix.Identity);
        for (var frame = 1; frame <= 100; frame++) history.Record(frame / 100f, Matrix.Identity);
        Assert.AreEqual(2, history.Count);
        history.Record(1, Matrix.CreateTranslation(10, 0, 0));
        history.Record(1, Matrix.CreateTranslation(20, 0, 0));
        Assert.AreEqual(3, history.Count);
        Assert.AreEqual(0f, history.Sample(.999f).Translation.X);
        Assert.AreEqual(0f, history.Sample(1).Translation.X, "Historical births at the pause boundary use the left limit.");
        Assert.AreEqual(20f, history.SampleCurrent(1).Translation.X);
        history.Record(2, Matrix.CreateTranslation(30, 0, 0));
        Assert.AreEqual(25f, history.Sample(1.5f).Translation.X);
    }

    [TestMethod]
    public void Clock_UsesHistoricalAttachmentsAndSeekReplaysInsteadOfRecordingTheFinalPose()
    {
        using var host = new Host(); host.Update(0);
        host.Interaction.Target.Position = new(2, 0, 0); host.Update(1);
        host.Interaction.Target.Position = new(4, 0, 0); host.Update(1);
        Assert.AreEqual(1f, host.LastHistory!(.5f).Translation.X);
        host.Preview.IsPlaying = false; host.Preview.Seek(.5f); host.Update(1);
        Assert.AreEqual(1f, host.Interaction.Target.Position.X);
        Assert.AreEqual(4f, host.Interaction.History.Sample(2).Translation.X);
        host.Preview.IsPlaying = true; host.Update(.5);
        Assert.AreEqual(2f, host.Interaction.Target.Position.X);
        host.Update(1);
        Assert.AreEqual(4f, host.Interaction.Target.Position.X, "Reaching the recorded endpoint must replay it instead of replacing it.");
        host.Preview.Seek(.5f); host.Update(0); host.Update(2);
        Assert.AreEqual(4f, host.Interaction.Target.Position.X, "Playback crossing the recorded endpoint must retain its final pose.");
    }

    [TestMethod]
    public void Clock_NewSnapshotAndNaturalLoopStartNewHistoryButPausedMovementPreservesOldSamples()
    {
        using var host = new Host(); host.Update(0);
        host.Interaction.Target.Position = new(2, 0, 0); host.Update(1);
        host.Preview.IsPlaying = false; host.Interaction.Target.Position = new(10, 0, 0); host.Update(1);
        Assert.AreEqual(1f, host.Interaction.History.Sample(.5f).Translation.X);
        Assert.AreEqual(10f, host.Interaction.History.SampleCurrent(1).Translation.X);
        host.Preview.Reload(new([], "vfx/new.xml", _ => null)); host.Update(0);
        Assert.AreEqual(10f, host.Interaction.History.Sample(.5f).Translation.X);
        host.Preview.IsPlaying = true; host.Interaction.Target.Position = new(20, 0, 0); host.Update(8);
        Assert.AreEqual(0f, host.Preview.CurrentTime);
        Assert.AreEqual(20f, host.Interaction.History.Sample(0).Translation.X);
    }

    [TestMethod]
    public void CaptureInterruptionSelectionAndDisposal_RestoreOnlyThePreviewAttachmentAndReleaseMouse()
    {
        using var host = new Host(); host.Interaction.Select(); host.Interaction.BeginTransform();
        host.Interaction.ApplyTransform(ModalPreviewReplacement.Translate(new(3, 0, 0), PivotType.ObjectCenter));
        Assert.AreSame(host.Interaction, host.Mouse.Object.MouseOwner);
        host.Mouse.Raise(mouse => mouse.CaptureInterrupted += null!);
        Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
        Assert.IsNull(host.Mouse.Object.MouseOwner);
        host.Interaction.BeginTransform(); host.Interaction.ApplyTransform(ModalPreviewReplacement.Translate(new(3, 0, 0), PivotType.ObjectCenter));
        host.Interaction.Select(false);
        Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
        host.Interaction.Dispose();
        Assert.IsNull(host.Preview.AttachmentAtTime);
        Assert.IsNull(host.Mouse.Object.MouseOwner);
    }

    [TestMethod]
    public void LiveAttachmentHistory_DrivesDistanceBirthsAndPausedWorldParticlesStayAtTheirBirthPositions()
    {
        var history = new VfxAttachmentHistory(); history.Reset(Matrix.Identity); history.Record(1, Matrix.CreateTranslation(2, 0, 0));
        var simulation = new CpuSimulation("WORLD_SPACE");
        simulation.Seek(1, history.Sample(1), history.Sample);
        CollectionAssert.AreEqual(new[] { .5f, 1, 1.5f, 2 }, simulation.Positions().Select(position => MathF.Round(position.X, 3)).Order().ToArray());
        var before = simulation.Positions();
        history.Record(1, Matrix.CreateTranslation(10, 0, 0)); simulation.Seek(1, history.SampleCurrent(1), history.Sample);
        CollectionAssert.AreEqual(before, simulation.Positions());
        simulation.Seek(0, history.SampleCurrent(0), history.Sample); simulation.Seek(1, history.SampleCurrent(1), history.Sample);
        CollectionAssert.AreEqual(before, simulation.Positions());
    }

    [TestMethod]
    public void LocalParticles_KeepTheirAuthoredFollowSemanticsInsteadOfForcingAWorldTrail()
    {
        var history = new VfxAttachmentHistory(); history.Reset(Matrix.Identity); history.Record(1, Matrix.CreateTranslation(2, 0, 0));
        var simulation = new CpuSimulation("LOCAL_SPACE_POSITION_ONLY");
        simulation.Seek(1, history.Sample(1), history.Sample);
        Assert.IsTrue(simulation.Positions().All(position => Math.Abs(position.X - 2) < .001f));
        history.Record(1, Matrix.CreateTranslation(10, 0, 0)); simulation.Seek(1, history.SampleCurrent(1), history.Sample);
        Assert.IsTrue(simulation.Positions().All(position => Math.Abs(position.X - 10) < .001f));
    }

    [TestMethod]
    public void GroundEventRebuild_RetainsTheExplicitCurrentAttachmentOptIn()
    {
        var history = new VfxAttachmentHistory(); history.Reset(Matrix.Identity); history.Record(1, Matrix.CreateTranslation(2, 0, 0));
        history.Record(1, Matrix.CreateTranslation(10, 0, 0));
        var simulation = new CpuSimulation("LOCAL_SPACE_POSITION_ONLY", groundEvent: true);
        simulation.Seek(1, history.SampleCurrent(1), history.Sample);
        var first = simulation.Positions(0); var second = simulation.Positions(1);
        Assert.AreEqual(4, first.Length);
        Assert.AreEqual(4, second.Length);
        Assert.IsTrue(first.Concat(second).All(position => Math.Abs(position.X - 10) < .001f));
    }

    [TestMethod]
    public void SharedGizmo_CaptureFailureImmediatelyCancelsAndReleasesThePreview()
        => RunGizmoInput(Keys.G, false, captureFailure: true);

    [DataTestMethod]
    [DataRow(Keys.G, false)]
    [DataRow(Keys.R, false)]
    [DataRow(Keys.S, false)]
    [DataRow(Keys.G, true)]
    [DataRow(Keys.R, true)]
    [DataRow(Keys.S, true)]
    public void SharedGizmo_UpdateConsumesKeysAxesAndMouseWithoutDesktopInputAndEndsThePreview(Keys key, bool confirm)
        => RunGizmoInput(key, confirm, false);

    private static void RunGizmoInput(Keys key, bool confirm, bool captureFailure)
    {
        using var host = new Host(); host.Update(0);
        var start = new Vector2(600, 500);
        host.Mouse.Setup(mouse => mouse.GetScreenSize()).Returns(new Vector2(1000, 1000));
        host.Mouse.Setup(mouse => mouse.Position()).Returns(start);
        host.Keyboard.Setup(keyboard => keyboard.GetKeyPressPosition(key)).Returns(start);
        var camera = new ArcBallCamera(Mock.Of<IDeviceResolver>(), host.Keyboard.Object, host.Mouse.Object)
        {
            ViewMatrixOverride = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
            ProjectionMatrixOverride = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, 1000),
        };
        var gizmo = CreateInputGizmo(host, camera);
        try
        {
            host.Interaction.Select();
            host.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(key)).Returns(true);
            if (captureFailure)
            {
                host.Mouse.Setup(mouse => mouse.BeginContinuousDrag(true)).Throws(new InvalidOperationException("capture failed"));
                Assert.ThrowsException<InvalidOperationException>(() => host.Interaction.Update(new GameTime()));
                Assert.IsFalse(gizmo.IsInModalTransform);
                Assert.IsFalse(host.Interaction.Target.IsTransformActive);
                Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
                Assert.IsNull(host.Mouse.Object.MouseOwner);
                return;
            }
            host.Interaction.Update(new GameTime());
            host.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(key)).Returns(false);
            var axisKey = key == Keys.R ? Keys.Y : Keys.X;
            host.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(axisKey)).Returns(true);
            host.Mouse.Setup(mouse => mouse.Position()).Returns(key == Keys.R ? new Vector2(500, 600) : new Vector2(700, 500));
            host.Interaction.Update(new GameTime());
            host.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(axisKey)).Returns(false);
            Assert.IsTrue(gizmo.IsInModalTransform);
            Assert.AreEqual(key == Keys.R ? GizmoAxis.Y : GizmoAxis.X, gizmo.ActiveAxis);
            Assert.AreNotEqual(Matrix.Identity, host.Interaction.Target.World);
            Assert.AreSame(host.Interaction, host.Mouse.Object.MouseOwner);
            var displayed = host.Interaction.Target.World;
            host.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(confirm ? Keys.Enter : Keys.Escape)).Returns(true);
            host.Interaction.Update(new GameTime());
            Assert.IsFalse(gizmo.IsInModalTransform);
            Assert.AreEqual(confirm ? displayed : Matrix.Identity, host.Interaction.Target.World);
            Assert.IsNull(host.Mouse.Object.MouseOwner);
            host.Mouse.Verify(mouse => mouse.BeginContinuousDrag(key != Keys.R), Times.Once);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    private static Gizmo CreateInputGizmo(Host host, ArcBallCamera camera)
    {
        // Production input math only; no native cursor or GPU operations.
        var gizmo = (Gizmo)RuntimeHelpers.GetUninitializedObject(typeof(Gizmo));
        SetField(gizmo, "_camera", camera); SetField(gizmo, "_mouse", host.Mouse.Object); SetField(gizmo, "_keyboard", host.Keyboard.Object);
        SetField(gizmo, "_isActive", true); SetField(gizmo, "_rotationMatrix", Matrix.Identity);
        SetField(gizmo, "_translationLineVertices", new VertexPositionColor[18]);
        SetField(gizmo, "_axisColors", new[] { Color.Red, Color.Green, Color.Blue });
        SetField(gizmo, "_numericInput", "");
        SetField(gizmo, "_numericFields", new[] { "", "", "" }); SetField(gizmo, "_numericValues", new float[3]);
        gizmo.Selection = []; gizmo.SceneWorld = Matrix.Identity; gizmo.Enabled = true; gizmo.ScaleModifier = 1;
        gizmo.ActivePivot = PivotType.ObjectCenter;
        gizmo.StartEvent += host.Interaction.BeginTransform;
        gizmo.StopEvent += () => typeof(VfxEmitterInteractionComponent).GetMethod("EndTransform", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(host.Interaction, null);
        gizmo.ReplacePreviewFromInitialRequested += host.Interaction.ApplyTransform;
        SetField(host.Interaction, "_gizmo", gizmo);
        SetField(gizmo, "_modelLocalSpace", new[] { Matrix.Identity, Matrix.Identity, Matrix.Identity });
        SetField(host.Interaction, "_camera", camera);
        return gizmo;
    }

    private static ArcBallCamera CreateInputCamera(Host host)
    {
        host.Mouse.Setup(mouse => mouse.GetScreenSize()).Returns(new Vector2(1000, 1000));
        return new ArcBallCamera(Mock.Of<IDeviceResolver>(), host.Keyboard.Object, host.Mouse.Object)
        {
            ViewMatrixOverride = Matrix.CreateLookAt(new(0, 0, 10), Vector3.Zero, Vector3.Up),
            ProjectionMatrixOverride = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, .01f, 1000),
        };
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectionMarker_UnloadedDrawDoesNotReadGraphicsOrChangeAnActiveTransform(bool selected)
    {
        using var host = new Host(); host.Update(0);
        var devices = new Mock<IDeviceResolver>(MockBehavior.Strict);
        SetField(host.Interaction, "_devices", devices.Object);
        host.Interaction.Select(selected);
        host.Preview.Stop();
        host.Interaction.BeginTransform();
        host.Interaction.ApplyTransform(ModalPreviewReplacement.Translate(new(2, 3, 4), PivotType.ObjectCenter));
        var before = host.Interaction.Target.World;
        host.Interaction.Draw(new GameTime());
        Assert.AreEqual(before, host.Interaction.Target.World);
        Assert.IsTrue(host.Interaction.Target.IsTransformActive);
        Assert.AreSame(host.Interaction, host.Mouse.Object.MouseOwner);
        devices.VerifyNoOtherCalls();
    }

    [DataTestMethod]
    [DataRow(3f, false, 1f)]
    [DataRow(10f, false, 1.25f)]
    [DataRow(250f, false, 1.5f)]
    [DataRow(10f, true, 1f)]
    [DataRow(250f, true, 1.5f)]
    public void SelectionMarker_RemainsSmallAtTheEmitterAcrossDistanceProjectionAndScale(float distance, bool orthographic, float inputScale)
    {
        using var host = new Host(); host.Update(0); host.Interaction.Select();
        var camera = CreateInputCamera(host);
        host.Mouse.Setup(mouse => mouse.GetScreenSize()).Returns(new Vector2(1000 * inputScale));
        camera.ViewMatrixOverride = Matrix.CreateLookAt(new(0, 0, distance), Vector3.Zero, Vector3.Up);
        if (orthographic) camera.ProjectionMatrixOverride = Matrix.CreateOrthographic(20, 20, .01f, 1000);
        SetField(host.Interaction, "_camera", camera);
        host.Interaction.Target.Position = new(.25f, -.5f, .1f);
        var vertices = SelectionMarker(host.Interaction);
        Assert.AreEqual(24, vertices.Length, "The marker consists only of the twelve cube edges.");
        var bounds = BoundingBox.CreateFromPoints(vertices.Select(vertex => vertex.Position));
        Assert.IsTrue(Vector3.Distance((bounds.Min + bounds.Max) / 2, host.Interaction.Target.Position) < .0001f);
        var side = bounds.Max - bounds.Min;
        Assert.IsTrue(Math.Abs(side.X - side.Y) < .0001f && Math.Abs(side.X - side.Z) < .0001f);
        var viewport = camera.InputViewport;
        var left = viewport.Project(host.Interaction.Target.Position - Vector3.UnitX * side.X / 2, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
        var right = viewport.Project(host.Interaction.Target.Position + Vector3.UnitX * side.X / 2, camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity);
        Assert.AreEqual(14f, Math.Abs(right.X - left.X), .02f, "Distance and projection must not enlarge the screen-space marker.");
        host.Interaction.Target.Scale = new(.01f, 20, 7);
        host.Interaction.Target.Orientation = Quaternion.CreateFromYawPitchRoll(.7f, .3f, .8f);
        CollectionAssert.AreEqual(vertices.Select(vertex => vertex.Position).ToArray(), SelectionMarker(host.Interaction).Select(vertex => vertex.Position).ToArray(),
            "The marker must not inherit the effect's scale or rotation.");
        var moved = new Vector3(.1f, .2f, 0);
        host.Interaction.Target.Position += moved;
        var after = SelectionMarker(host.Interaction);
        for (var index = 0; index < vertices.Length; index++)
            Assert.IsTrue(Vector3.Distance(vertices[index].Position + moved, after[index].Position) < .0001f);
    }

    [TestMethod]
    public void SelectionMarker_ClippedOrNonFiniteEmitterDoesNotProduceGeometry()
    {
        using var host = new Host(); host.Update(0); host.Interaction.Select();
        SetField(host.Interaction, "_camera", CreateInputCamera(host));
        foreach (var position in new[] { new Vector3(0, 0, 20), new Vector3(float.NaN, 0, 0), new Vector3(float.PositiveInfinity, 0, 0) })
        {
            host.Interaction.Target.Position = position;
            Assert.AreEqual(0, SelectionMarker(host.Interaction).Length);
        }
    }

    [DataTestMethod]
    [DataRow(1f, false)]
    [DataRow(1.25f, false)]
    [DataRow(1.5f, false)]
    [DataRow(1f, true)]
    [DataRow(1.25f, true)]
    [DataRow(1.5f, true)]
    public void SelectionMarker_OffscreenDrawIsHollowSmallAndSelectionScopedWithoutChangingTheGesture(float renderScale, bool lightBackground)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_SELECTION_HEADLESS_TEST") != "1")
            Assert.Inconclusive("Set ASSETEDITOR_VFX_SELECTION_HEADLESS_TEST=1 for the hidden-window GPU marker check.");
        WpfTestApplicationHost.Invoke(_ =>
        {
            var width = (int)(384 * renderScale); var height = (int)(288 * renderScale);
            // No WS_VISIBLE and no activation, keyboard or mouse input.
            using var window = new HwndSource(new HwndSourceParameters("VfxSelectionMarkerOffscreen")
            {
                Width = width, Height = height, WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
            });
            using var device = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef, new PresentationParameters
            {
                BackBufferWidth = width, BackBufferHeight = height, IsFullScreen = false,
                DeviceWindowHandle = window.Handle, PresentationInterval = PresentInterval.Immediate,
            });
            using var target = new RenderTarget2D(device, width, height, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
            using var host = new Host(); host.Update(0); host.Interaction.Select();
            var resolver = new Mock<IDeviceResolver>(); resolver.SetupGet(value => value.Device).Returns(device);
            SetField(host.Interaction, "_devices", resolver.Object);
            host.Mouse.Setup(mouse => mouse.GetScreenSize()).Returns(new Vector2(384, 288));
            host.Interaction.BeginTransform();
            host.Interaction.ApplyTransform(ModalPreviewReplacement.Translate(new(1, 2, 3), PivotType.ObjectCenter));
            var before = host.Interaction.Target.World;
            var camera = new ArcBallCamera(resolver.Object, host.Keyboard.Object, host.Mouse.Object)
            {
                ViewMatrixOverride = Matrix.CreateLookAt(before.Translation + new Vector3(3, 2, 5), before.Translation, Vector3.Up),
                ProjectionMatrixOverride = Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 4f / 3, .01f, 1000),
            };
            SetField(host.Interaction, "_camera", camera);
            var background = lightBackground ? new Color(220, 220, 220) : new Color(38, 38, 38);
            device.SetRenderTarget(target); device.Clear(background);
            device.BlendState = BlendState.NonPremultiplied; device.DepthStencilState = DepthStencilState.DepthRead; device.RasterizerState = RasterizerState.CullCounterClockwise;
            var blend = device.BlendState; var depth = device.DepthStencilState; var raster = device.RasterizerState;
            host.Interaction.Draw(new GameTime());
            var effect = (BasicEffect)typeof(VfxEmitterInteractionComponent).GetField("_selectionMarker", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host.Interaction)!;
            Assert.AreEqual(Matrix.Identity, effect.World); Assert.AreEqual(camera.ViewMatrix, effect.View); Assert.AreEqual(camera.ProjectionMatrix, effect.Projection);
            Assert.IsTrue(effect.VertexColorEnabled); Assert.IsFalse(effect.TextureEnabled); Assert.IsFalse(effect.LightingEnabled);
            Assert.AreSame(blend, device.BlendState); Assert.AreSame(depth, device.DepthStencilState); Assert.AreSame(raster, device.RasterizerState);
            Assert.AreEqual(before, host.Interaction.Target.World); Assert.IsTrue(host.Interaction.Target.IsTransformActive);
            Assert.AreSame(host.Interaction, host.Mouse.Object.MouseOwner);
            device.SetRenderTarget(null);
            var pixels = new Color[width * height]; target.GetData(pixels);
            var drawn = Enumerable.Range(0, pixels.Length).Where(index => pixels[index] != background).ToArray();
            Assert.IsTrue(drawn.Length > 20, "The selected marker must actually draw its edges.");
            var minX = drawn.Min(index => index % width); var maxX = drawn.Max(index => index % width);
            var minY = drawn.Min(index => index / width); var maxY = drawn.Max(index => index / width);
            Assert.IsTrue(maxX - minX <= 32 * renderScale && maxY - minY <= 32 * renderScale, "The selection indicator must stay compact.");
            Assert.IsTrue(drawn.Length < (maxX - minX + 1) * (maxY - minY + 1) * .6f, "The marker must remain hollow instead of obscuring the effect.");
            Assert.IsTrue(Math.Abs((minX + maxX) / 2f - width / 2f) <= 2 * renderScale && Math.Abs((minY + maxY) / 2f - height / 2f) <= 2 * renderScale);
            void SaveRender(string state)
            {
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_SELECTION_RENDER_OUTPUT");
                if (string.IsNullOrEmpty(output)) return;
                Directory.CreateDirectory(output);
                var path = Path.Combine(output, $"vfx-selection-{state}-{(lightBackground ? "light" : "dark")}-{(int)(renderScale * 100)}-{Guid.NewGuid():N}.png");
                using var stream = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                target.SaveAsPng(stream, width, height);
                Console.WriteLine(path);
            }
            SaveRender("selected");
            host.Interaction.Target.Commit();
            host.Interaction.Select(false);
            device.SetRenderTarget(target); device.Clear(background); host.Interaction.Draw(new GameTime()); device.SetRenderTarget(null);
            target.GetData(pixels);
            var unselected = Enumerable.Range(0, pixels.Length).Where(index => pixels[index] != background).ToArray();
            CollectionAssert.AreEqual(drawn, unselected, "Deselecting retains the same small wire cube.");
            Assert.IsTrue(unselected.All(index => pixels[index] == Color.Gray), "The unselected marker stays neutral instead of highlighted.");
            SaveRender("unselected");
            SetField(host.Interaction, "_isBoxSelecting", true);
            SetField(host.Interaction, "_selectionStart", new Vector2(142, 104));
            SetField(host.Interaction, "_selectionEnd", new Vector2(242, 184));
            device.SetRenderTarget(target); device.Clear(background); host.Interaction.Draw(new GameTime()); device.SetRenderTarget(null);
            target.GetData(pixels);
            bool EdgeVisible(Vector2 point)
            {
                var x = (int)MathF.Round(point.X * renderScale); var y = (int)MathF.Round(point.Y * renderScale);
                return Enumerable.Range(-2, 5).Any(dy => Enumerable.Range(-2, 5).Any(dx => pixels[(y + dy) * width + x + dx] == Color.Yellow));
            }
            Assert.IsTrue(EdgeVisible(new(192, 104)) && EdgeVisible(new(242, 144)) && EdgeVisible(new(192, 184)) && EdgeVisible(new(142, 144)),
                "The active selection rectangle must draw all four screen-space edges.");
            Assert.AreEqual(background, pixels[(int)(115 * renderScale) * width + (int)(155 * renderScale)], "The selection rectangle must leave its interior transparent.");
            Assert.AreSame(blend, device.BlendState); Assert.AreSame(depth, device.DepthStencilState); Assert.AreSame(raster, device.RasterizerState);
            Assert.AreEqual(camera.ViewMatrix, effect.View); Assert.AreEqual(camera.ProjectionMatrix, effect.Projection);
            Assert.AreEqual(before, host.Interaction.Target.World);
            SaveRender("marquee");
            SetField(host.Interaction, "_isBoxSelecting", false);
            host.Interaction.Select(); host.Preview.Stop();
            device.SetRenderTarget(target); device.Clear(background); host.Interaction.Draw(new GameTime()); device.SetRenderTarget(null);
            target.GetData(pixels);
            Assert.IsTrue(pixels.All(pixel => pixel == background), "An unloaded preview must not retain the marker.");
        });
    }

    private static VertexPositionColor[] SelectionMarker(VfxEmitterInteractionComponent interaction)
        => (VertexPositionColor[])typeof(VfxEmitterInteractionComponent).GetMethod("CreateSelectionMarker", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(interaction, null)!;

    [TestMethod]
    public void HiddenControls_EffectBodySelectsThenDragsFreelyAndCommitsOnMouseRelease()
    {
        using var host = new Host(); host.Bounds = new BoundingBox(new(-1), new(1)); host.Update(0);
        var gizmo = CreateInputGizmo(host, CreateInputCamera(host));
        var point = new Vector2(560, 500);
        host.Mouse.Setup(mouse => mouse.Position()).Returns(point);
        host.Mouse.Setup(mouse => mouse.GetPressPosition(MouseButton.Left)).Returns(point);
        host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
        try
        {
            host.Interaction.Update(new GameTime());
            Assert.IsTrue(host.Interaction.IsSelected);
            Assert.IsFalse(gizmo.IsInModalTransform, "First press only selects the effect.");
            host.Interaction.Update(new GameTime());
            Assert.IsTrue(gizmo.IsInModalTransform);
            Assert.AreEqual(GizmoAxis.None, gizmo.ActiveAxis, "An invisible axis must not constrain a direct drag.");
            host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            host.Mouse.Setup(mouse => mouse.Position()).Returns(point + new Vector2(40, 35));
            host.Interaction.Update(new GameTime());
            Assert.AreNotEqual(0f, host.Interaction.Target.Position.X);
            Assert.AreNotEqual(0f, host.Interaction.Target.Position.Y);
            var preview = host.Interaction.Target.World;
            host.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            host.Interaction.Update(new GameTime());
            Assert.IsFalse(gizmo.IsInModalTransform);
            Assert.IsFalse(host.Interaction.Target.IsTransformActive);
            Assert.AreEqual(preview, host.Interaction.Target.World);
            Assert.IsNull(host.Mouse.Object.MouseOwner);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    [TestMethod]
    public void HiddenControls_EmptyPressDoesNotUseAStaleAxisAndClearsSelection()
    {
        using var host = new Host(); host.Bounds = new BoundingBox(new(-.02f), new(.02f)); host.Update(0);
        var gizmo = CreateInputGizmo(host, CreateInputCamera(host));
        host.Interaction.Select();
        gizmo.ActiveAxis = GizmoAxis.X;
        var point = new Vector2(700, 500);
        host.Mouse.Setup(mouse => mouse.Position()).Returns(point);
        host.Mouse.Setup(mouse => mouse.GetPressPosition(MouseButton.Left)).Returns(point);
        host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
        try
        {
            host.Interaction.Update(new GameTime());
            Assert.IsFalse(host.Interaction.IsSelected);
            Assert.IsFalse(gizmo.IsInModalTransform);
            Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
            Assert.AreSame(host.Interaction, host.Mouse.Object.MouseOwner, "An empty press owns the pending selection rectangle.");
            host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            host.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            host.Interaction.Update(new GameTime());
            Assert.IsFalse(host.Interaction.IsSelected);
            Assert.IsNull(host.Mouse.Object.MouseOwner);
            host.Mouse.Verify(mouse => mouse.BeginContinuousDrag(It.IsAny<bool>()), Times.Never);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    [DataTestMethod]
    [DataRow(450f, 450f, 550f, 550f, true)]
    [DataRow(450f, 550f, 550f, 450f, true)]
    [DataRow(550f, 450f, 450f, 550f, true)]
    [DataRow(550f, 550f, 450f, 450f, true)]
    [DataRow(600f, 600f, 700f, 700f, false)]
    [DataRow(450f, 450f, 494f, 494f, true)]
    public void BoxSelection_UsesTheMarkerProjectionInEveryDragDirectionAndReleasesTheMouse(float startX, float startY, float endX, float endY, bool selected)
    {
        using var host = new Host(); host.Update(0);
        var gizmo = StartBoxSelection(host, new(startX, startY));
        try
        {
            Assert.IsTrue(IsBoxSelecting(host.Interaction));
            Assert.AreSame(host.Interaction, host.Mouse.Object.MouseOwner);
            host.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(endX, endY));
            host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            host.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            host.Interaction.Update(new GameTime());
            Assert.AreEqual(selected, host.Interaction.IsSelected);
            Assert.IsFalse(IsBoxSelecting(host.Interaction));
            Assert.IsFalse(gizmo.IsInModalTransform);
            Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
            Assert.IsNull(host.Mouse.Object.MouseOwner);
            host.Mouse.Verify(mouse => mouse.BeginContinuousDrag(It.IsAny<bool>()), Times.Never);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    public void BoxSelection_CancellationRestoresSelectionWithoutStealingMouseOwnership(int cancellation)
    {
        using var host = new Host(); host.Update(0);
        StartBoxSelection(host, new(450, 450), selected: true);
        var other = Mock.Of<GameWorld.Core.Components.BaseComponent>();
        try
        {
            Assert.IsFalse(host.Interaction.IsSelected);
            host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            if (cancellation == 0) host.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.Escape)).Returns(true);
            else if (cancellation == 1) host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Right)).Returns(true);
            else if (cancellation == 2) host.Mouse.Raise(mouse => mouse.CaptureInterrupted += null!);
            else host.Mouse.Object.MouseOwner = other;
            host.Interaction.Update(new GameTime());
            Assert.IsFalse(IsBoxSelecting(host.Interaction));
            Assert.IsTrue(host.Interaction.IsSelected);
            Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
            Assert.AreSame(cancellation == 3 ? other : null, host.Mouse.Object.MouseOwner);
            host.Mouse.Verify(mouse => mouse.BeginContinuousDrag(It.IsAny<bool>()), Times.Never);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void BoxSelection_DoesNotStartDuringCameraNavigationOrAnotherMouseOwner(bool alt)
    {
        using var host = new Host(); host.Update(0);
        CreateInputGizmo(host, CreateInputCamera(host)); host.Interaction.Select();
        var other = Mock.Of<GameWorld.Core.Components.BaseComponent>();
        host.Keyboard.Setup(keyboard => keyboard.IsKeyDownOrReleased(Keys.LeftAlt)).Returns(alt);
        if (!alt) host.Mouse.Object.MouseOwner = other;
        host.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(700, 500));
        host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
        try
        {
            host.Interaction.Update(new GameTime());
            Assert.IsTrue(host.Interaction.IsSelected);
            Assert.IsFalse(IsBoxSelecting(host.Interaction));
            Assert.AreSame(alt ? null : other, host.Mouse.Object.MouseOwner);
            host.Mouse.Verify(mouse => mouse.BeginContinuousDrag(It.IsAny<bool>()), Times.Never);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    [TestMethod]
    public void BoxSelection_UnloadingClearsThePendingRectangleAndSelection()
    {
        using var host = new Host(); host.Update(0);
        StartBoxSelection(host, new(450, 450), selected: true);
        try
        {
            host.Preview.Stop(); host.Interaction.Update(new GameTime());
            Assert.IsFalse(IsBoxSelecting(host.Interaction));
            Assert.IsFalse(host.Interaction.IsSelected);
            Assert.IsNull(host.Mouse.Object.MouseOwner);
            Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    [TestMethod]
    public void BoxSelection_EnablesTheExistingGShortcutAndCancellation()
    {
        using var host = new Host(); host.Update(0);
        var gizmo = StartBoxSelection(host, new(450, 450));
        try
        {
            host.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(550, 550));
            host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(false);
            host.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(true);
            host.Interaction.Update(new GameTime());
            host.Mouse.Setup(mouse => mouse.IsMouseButtonReleased(MouseButton.Left)).Returns(false);
            host.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(Keys.G)).Returns(true);
            host.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(600, 500));
            host.Interaction.Update(new GameTime());
            Assert.IsTrue(gizmo.IsInModalTransform); Assert.IsTrue(host.Interaction.IsSelected);
            host.Keyboard.Setup(keyboard => keyboard.IsKeyPressed(Keys.G)).Returns(false);
            host.Mouse.Setup(mouse => mouse.Position()).Returns(new Vector2(650, 500));
            host.Interaction.Update(new GameTime());
            Assert.AreNotEqual(Matrix.Identity, host.Interaction.Target.World);
            host.Keyboard.Setup(keyboard => keyboard.IsKeyReleased(Keys.Escape)).Returns(true);
            host.Interaction.Update(new GameTime());
            Assert.AreEqual(Matrix.Identity, host.Interaction.Target.World);
            Assert.IsFalse(gizmo.IsInModalTransform); Assert.IsTrue(host.Interaction.IsSelected);
            Assert.IsNull(host.Mouse.Object.MouseOwner);
        }
        finally { SetField(host.Interaction, "_gizmo", null); }
    }

    private static Gizmo StartBoxSelection(Host host, Vector2 start, bool selected = false)
    {
        var gizmo = CreateInputGizmo(host, CreateInputCamera(host));
        host.Interaction.Select(selected);
        host.Mouse.Setup(mouse => mouse.Position()).Returns(start);
        host.Mouse.Setup(mouse => mouse.GetPressPosition(MouseButton.Left)).Returns(start);
        host.Mouse.Setup(mouse => mouse.IsMouseButtonPressed(MouseButton.Left)).Returns(true);
        host.Interaction.Update(new GameTime());
        return gizmo;
    }

    private static bool IsBoxSelecting(VfxEmitterInteractionComponent interaction)
        => (bool)typeof(VfxEmitterInteractionComponent).GetField("_isBoxSelecting", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(interaction)!;

    private static void SetField(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private sealed class Host : IDisposable
    {
        public Mock<IMouseComponent> Mouse { get; } = new();
        public Mock<IKeyboardComponent> Keyboard { get; } = new();
        public VfxPreviewComponent Preview { get; }
        public VfxEmitterInteractionComponent Interaction { get; }
        public Func<float, Matrix>? LastHistory;
        public BoundingBox? Bounds;
        public Host()
        {
            Mouse.SetupProperty(mouse => mouse.MouseOwner);
            var runtime = new Mock<IVfxPreview>(); runtime.SetupGet(preview => preview.EmitterCount).Returns(1);
            runtime.SetupGet(preview => preview.CycleDuration).Returns(8);
            runtime.SetupGet(preview => preview.PreviewBounds).Returns(() => Bounds);
            runtime.SetupGet(preview => preview.Diagnostics).Returns(Array.Empty<VfxPreviewDiagnostic>());
            runtime.Setup(preview => preview.Seek(It.IsAny<float>(), It.IsAny<Matrix>(), It.IsAny<Func<float, Matrix>>(), true))
                .Callback<float, Matrix, Func<float, Matrix>, bool>((_, _, history, _) => LastHistory = history);
            var factory = new Mock<IVfxPreviewFactory>(); factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(runtime.Object);
            Preview = new(factory.Object, Mock.Of<IDeviceResolver>(), null!);
            Interaction = new(Mouse.Object, Keyboard.Object, null!, Mock.Of<IDeviceResolver>(), null!, Preview);
            Preview.Reload(new([], "vfx/preview.xml", _ => null));
        }
        public void Update(double seconds) => Preview.Update(new GameTime(TimeSpan.Zero, TimeSpan.FromSeconds(seconds)));
        public void Dispose() { Interaction.Dispose(); Preview.Dispose(); }
    }

    // Exercises the real CPU converter/simulation without loading native rendering or opening a window.
    private sealed class CpuSimulation
    {
        private readonly object _simulation;
        private readonly Type _type;
        private static string Curve(string axes, float value) => string.Join('|', axes.Split(',').Select((axis, index) =>
            $"{axis}_min,0,0;0,{value},linear,linear,0,0,0,0,1")) + ":" + string.Join('|', axes.Split(',').Select((axis, index) => $"{axis},0;{index},{index}"));
        public CpuSimulation(string space, bool groundEvent = false)
        {
            var bytes = Encoding.UTF8.GetBytes($"""
                <vfx revision="2"><emitters><emitter id="cpu_attachment" type="continuous_mesh">
                <property name="Enable" value="true"/><property name="InfiniteLife" value="false"/>
                <ranged_property name="LifeTime" min="4" max="4"/><property name="MaximumParticles" value="256"/>
                <property name="ParticleLifeTime">{Curve("x", 5)}</property><property name="EmissionRateCurve">{Curve("x", 0)}</property>
                <property name="ParticleScale">{Curve("x,y,z", 1)}</property><property name="SpawnSpacing" value="0.5"/>
                <property name="ParticleSimulationSpace" value="{space}"/></emitter></emitters></vfx>
                """);
            var assembly = typeof(VfxPreviewComponent).Assembly;
            var converter = assembly.GetType("GameWorld.Core.Vfx.TotalWarVfxConverter", true)!;
            var conversion = converter.GetMethod("Convert", BindingFlags.Static | BindingFlags.Public, [typeof(VfxPreviewSnapshot)])!
                .Invoke(null, [new VfxPreviewSnapshot(bytes, "vfx/cpu.xml", _ => [1])]);
            if (groundEvent)
            {
                var emitters = (System.Collections.IEnumerable)conversion!.GetType().GetProperty("Emitters")!.GetValue(conversion)!;
                var emitter = emitters.Cast<object>().First();
                emitter.GetType().GetProperty("RequiresFlatTerrain")!.SetValue(emitter, true);
                var eventType = assembly.GetType("GameWorld.Core.Vfx.VfxPreviewParticleEvent", true)!;
                var particleEvent = Activator.CreateInstance(eventType, ["vfx/empty.xml", false, 0f, true, false, false, false, false, "orientation"]);
                var events = Array.CreateInstance(eventType, 1); events.SetValue(particleEvent, 0);
                emitter.GetType().GetProperty("Events")!.SetValue(emitter, events);
            }
            _type = assembly.GetType("GameWorld.Core.Vfx.TotalWarVfxSimulation", true)!;
            _simulation = Activator.CreateInstance(_type, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, [conversion], null)!;
        }
        public void Seek(float time, Matrix attachment, Func<float, Matrix> sample) => _type.GetMethod("SeekWithCurrentAttachment")!.Invoke(_simulation, [time, attachment, sample]);
        public Vector3[] Positions(float? ground = null)
        {
            var camera = new CommonShaderParameters(Matrix.Identity, Matrix.Identity, new(0, 0, 10), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: ground);
            var particles = (Array)_type.GetMethod("Frame")!.Invoke(_simulation, [camera])!;
            return particles.Cast<object>().Select(particle => ((Matrix)particle.GetType().GetField("Transform")!.GetValue(particle)!).Translation).ToArray();
        }
    }
}
