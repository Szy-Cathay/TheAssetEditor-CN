using Editors.CscEditor.Services;
using GameWorld.Core.Commands;
using GameWorld.Core.Services;
using Shared.Core.Events;

namespace Test.CscEditor;

public class CscEditHistoryTests
{
    [Test]
    public void Identical_snapshot_does_not_create_history()
    {
        var context = new HistoryContext([1, 2, 3]);

        var recorded = context.History.RecordChange("Edit");

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.False);
            Assert.That(context.History.CanUndo, Is.False);
            Assert.That(context.History.HasUnsavedChanges, Is.False);
        });
    }

    [Test]
    public void Undo_and_redo_restore_snapshots_and_selection()
    {
        var context = new HistoryContext([1], CscSelectionSnapshot.Root);
        context.History.BeginGesture();
        context.SetState([2], CscSelectionSnapshot.Element(17));
        Assert.That(context.History.EndGesture("Edit"), Is.True);

        Assert.That(context.History.Undo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(context.State, Is.EqualTo(new byte[] { 1 }));
            Assert.That(context.Selection, Is.EqualTo(CscSelectionSnapshot.Root));
            Assert.That(context.History.CanRedo, Is.True);
        });

        Assert.That(context.History.Redo(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(context.State, Is.EqualTo(new byte[] { 2 }));
            Assert.That(context.Selection, Is.EqualTo(CscSelectionSnapshot.Element(17)));
            Assert.That(context.History.CanUndo, Is.True);
        });
    }

    [Test]
    public void Edit_uses_current_selection_instead_of_previous_history_selection()
    {
        var context = new HistoryContext(
            [1],
            CscSelectionSnapshot.Element(10));
        context.SetSelection(CscSelectionSnapshot.Element(20));
        context.SetState([2]);

        context.History.RecordChange("Edit");
        context.History.Undo();

        Assert.That(
            context.Selection,
            Is.EqualTo(CscSelectionSnapshot.Element(20)));
    }

    [Test]
    public void New_edit_after_undo_clears_redo_branch()
    {
        var context = new HistoryContext([1]);
        context.SetState([2]);
        context.History.RecordChange("First");
        context.History.Undo();

        context.SetState([3]);
        context.History.RecordChange("Second");

        Assert.Multiple(() =>
        {
            Assert.That(context.History.CanRedo, Is.False);
            Assert.That(context.History.Undo(), Is.True);
            Assert.That(context.State, Is.EqualTo(new byte[] { 1 }));
        });
    }

    [Test]
    public void Gesture_with_many_changes_creates_one_history_entry()
    {
        var context = new HistoryContext([1]);

        context.History.BeginGesture();
        context.SetState([2]);
        context.History.RecordChange("Preview 1");
        context.SetState([3]);
        context.History.RecordChange("Preview 2");
        var recorded = context.History.EndGesture("Drag");

        Assert.That(recorded, Is.True);
        Assert.That(context.History.Undo(), Is.True);
        Assert.That(context.State, Is.EqualTo(new byte[] { 1 }));
        Assert.That(context.History.CanUndo, Is.False);
        Assert.That(context.History.Redo(), Is.True);
        Assert.That(context.State, Is.EqualTo(new byte[] { 3 }));
    }

    [Test]
    public void Empty_gesture_does_not_create_history()
    {
        var context = new HistoryContext([1]);

        context.History.BeginGesture();
        var recorded = context.History.EndGesture("Drag");

        Assert.Multiple(() =>
        {
            Assert.That(recorded, Is.False);
            Assert.That(context.History.CanUndo, Is.False);
        });
    }

    [Test]
    public void Cancelled_gesture_restores_start_snapshot_and_allows_later_edits()
    {
        var context = new HistoryContext([1]);
        context.History.BeginGesture();
        context.SetState([2]);

        context.History.CancelGesture();
        context.SetState([3]);
        var recorded = context.History.RecordChange("Later edit");

        Assert.Multiple(() =>
        {
            Assert.That(context.State, Is.EqualTo(new byte[] { 3 }));
            Assert.That(recorded, Is.True);
            Assert.That(context.History.CanUndo, Is.True);
        });
    }

    [Test]
    public void Save_point_tracks_document_state_across_undo_and_redo()
    {
        var context = new HistoryContext([1]);
        context.SetState([2]);
        context.History.RecordChange("First");

        Assert.That(context.History.HasUnsavedChanges, Is.True);
        context.History.MarkSaved();
        Assert.That(context.History.HasUnsavedChanges, Is.False);

        context.SetState([3]);
        context.History.RecordChange("Second");
        Assert.That(context.History.HasUnsavedChanges, Is.True);

        context.History.Undo();
        Assert.That(context.History.HasUnsavedChanges, Is.False);

        context.History.Undo();
        Assert.That(context.History.HasUnsavedChanges, Is.True);

        context.History.Redo();
        Assert.That(context.History.HasUnsavedChanges, Is.False);
    }

    [Test]
    public void Shared_selection_commands_do_not_enter_csc_document_history()
    {
        var eventHub = new TestEventHub();
        var sharedExecutor =
            new CommandExecutor(eventHub);
        var state = new byte[] { 1 };
        var history = new CscEditHistory(
            eventHub,
            () => state,
            () => CscSelectionSnapshot.None,
            (snapshot, _) => state = snapshot);
        history.InitializeLoadedDocument();

        sharedExecutor.ExecuteCommand(
            new SelectionCommand());

        Assert.Multiple(() =>
        {
            Assert.That(sharedExecutor.CanUndo(), Is.True);
            Assert.That(history.CanUndo, Is.False);
        });

        state = [2];
        history.RecordChange("Document edit");
        Assert.That(history.Undo(), Is.True);
        Assert.That(state, Is.EqualTo(new byte[] { 1 }));
        Assert.That(sharedExecutor.CanUndo(), Is.True);
    }

    private sealed class HistoryContext
    {
        public byte[] State { get; private set; }
        public CscSelectionSnapshot Selection { get; private set; }
        public CscEditHistory History { get; }

        public HistoryContext(
            byte[] state,
            CscSelectionSnapshot? selection = null)
        {
            State = state;
            Selection = selection ?? CscSelectionSnapshot.None;
            var executor = new CommandExecutor(new TestEventHub());
            History = new CscEditHistory(
                executor,
                () => State,
                () => Selection,
                (snapshot, restoredSelection) =>
                {
                    State = snapshot;
                    Selection = restoredSelection;
                });
            History.InitializeLoadedDocument();
        }

        public void SetState(
            byte[] state,
            CscSelectionSnapshot? selection = null)
        {
            State = state;
            if (selection != null)
                Selection = selection.Value;
        }

        public void SetSelection(
            CscSelectionSnapshot selection) =>
            Selection = selection;
    }

    private sealed class TestEventHub : IEventHub
    {
        public void PublishGlobalEvent<T>(T e)
        {
        }

        public void Publish<T>(T e)
        {
        }

        public void Register<T>(object owner, Action<T> action)
        {
        }

        public void UnRegister(object owner)
        {
        }
    }

    private sealed class SelectionCommand : ICommand
    {
        public string HintText => "Selection";
        public bool IsMutation => true;
        public bool AffectsDocument => false;
        public void Execute()
        {
        }

        public void Undo()
        {
        }
    }
}
