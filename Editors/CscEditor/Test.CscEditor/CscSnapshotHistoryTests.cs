using Editors.CscEditor.Data;
using Editors.CscEditor.Services;
using Editors.CscEditor.ViewModels;
using GameWorld.Core.Services;
using Shared.Core.Events;

namespace Test.CscEditor;

public class CscSnapshotHistoryTests
{
    [Test]
    public void Snapshot_undo_redo_restores_fields_hierarchy_manifest_and_bytes()
    {
        var scene = CscScene.Load(
            CscSceneRoundTripTests.BuildSceneWithManifestBytes(
                elementCount: 2,
                entryName: "Preserved Entry",
                entryFlag: true,
                reservedValue: 170));
        var selection = CscSelectionSnapshot.Root;
        var initialDuration = scene.Duration;
        var initialBytes = CscSceneWriter.Write(scene);
        var executor =
            new CommandExecutor(new TestEventHub());
        var history = new CscEditHistory(
            executor,
            () => CscSceneWriter.Write(scene),
            () => selection,
            (snapshot, restoredSelection) =>
            {
                scene = CscScene.Load(snapshot);
                selection = restoredSelection;
            });
        history.InitializeLoadedDocument();

        var parentId = scene.Elements[0].Id;
        var childId = scene.Elements[1].Id;
        history.BeginGesture();
        scene.Duration = 37.5f;
        Assert.That(
            scene.Reparent(
                scene.Elements[1],
                scene.Elements[0]),
            Is.True);
        selection = CscSelectionSnapshot.Element(childId);
        history.EndGesture("Structure edit");
        var editedBytes = CscSceneWriter.Write(scene);

        history.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(scene.Duration, Is.EqualTo(initialDuration));
            Assert.That(
                scene.FindElement(childId)!.Parent,
                Is.Null);
            Assert.That(scene.ManifestParsed, Is.True);
            Assert.That(
                scene.ManifestEntries[0].Fields[0].Value,
                Is.EqualTo("Preserved Entry"));
            Assert.That(
                CscSceneWriter.Write(scene),
                Is.EqualTo(initialBytes));
            Assert.That(
                selection,
                Is.EqualTo(CscSelectionSnapshot.Root));
        });

        history.Redo();
        Assert.Multiple(() =>
        {
            Assert.That(scene.Duration, Is.EqualTo(37.5f));
            Assert.That(
                scene.FindElement(childId)!.Parent!.Id,
                Is.EqualTo(parentId));
            Assert.That(
                CscSceneWriter.Write(scene),
                Is.EqualTo(editedBytes));
            Assert.That(
                selection,
                Is.EqualTo(
                    CscSelectionSnapshot.Element(childId)));
        });
    }

    [Test]
    public void Negative_duration_snapshot_redo_clamps_playback_time_safely()
    {
        var scene = CscScene.Load(
            CscSceneRoundTripTests.BuildSceneWithManifestBytes());
        var playbackTime = 8d;
        var history = new CscEditHistory(
            new TestEventHub(),
            () => CscSceneWriter.Write(scene),
            () => CscSelectionSnapshot.Root,
            (snapshot, _) =>
            {
                scene = CscScene.Load(snapshot);
                playbackTime =
                    CscEditorViewModel.ClampPlaybackTime(
                        playbackTime,
                        scene.Duration);
            });
        history.InitializeLoadedDocument();
        scene.Duration = -3;
        history.RecordChange("Negative duration");

        Assert.DoesNotThrow(() =>
        {
            history.Undo();
            history.Redo();
        });
        Assert.That(playbackTime, Is.Zero);
    }

    private sealed class TestEventHub : IEventHub
    {
        public void PublishGlobalEvent<T>(T e)
        {
        }

        public void Publish<T>(T e)
        {
        }

        public void Register<T>(
            object owner,
            Action<T> action)
        {
        }

        public void UnRegister(object owner)
        {
        }
    }
}
