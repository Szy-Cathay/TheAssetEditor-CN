using System;
using System.Linq;
using GameWorld.Core.Commands;
using GameWorld.Core.Services;
using Shared.Core.Events;

namespace Editors.CscEditor.Services;

public interface ICscUndoRedoEditor
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    bool Undo();
    bool Redo();
}

public readonly record struct CscSelectionSnapshot(
    bool IsSceneRoot,
    int? ElementId)
{
    public static CscSelectionSnapshot None => new(false, null);
    public static CscSelectionSnapshot Root => new(true, null);
    public static CscSelectionSnapshot Element(int id) => new(false, id);
}

public sealed class CscEditHistory
{
    private readonly CommandExecutor _commandExecutor;
    private readonly Func<byte[]> _captureSnapshot;
    private readonly Func<CscSelectionSnapshot> _captureSelection;
    private readonly Action<byte[], CscSelectionSnapshot> _applySnapshot;

    private byte[]? _currentSnapshot;
    private bool _gestureActive;
    private byte[]? _gestureStartSnapshot;
    private CscSelectionSnapshot _gestureStartSelection;
    private long _savedDocumentStateId;

    public CscEditHistory(
        IEventHub eventHub,
        Func<byte[]> captureSnapshot,
        Func<CscSelectionSnapshot> captureSelection,
        Action<byte[], CscSelectionSnapshot> applySnapshot)
        : this(
            new CommandExecutor(eventHub),
            captureSnapshot,
            captureSelection,
            applySnapshot)
    {
    }

    internal CscEditHistory(
        CommandExecutor commandExecutor,
        Func<byte[]> captureSnapshot,
        Func<CscSelectionSnapshot> captureSelection,
        Action<byte[], CscSelectionSnapshot> applySnapshot)
    {
        _commandExecutor = commandExecutor;
        _captureSnapshot = captureSnapshot;
        _captureSelection = captureSelection;
        _applySnapshot = applySnapshot;
    }

    public bool CanUndo => _commandExecutor.CanUndo();
    public bool CanRedo => _commandExecutor.CanRedo();
    public bool HasUnsavedChanges =>
        _commandExecutor.CurrentDocumentStateId !=
        _savedDocumentStateId;

    public void InitializeLoadedDocument()
    {
        _currentSnapshot = CaptureBytes();
        _savedDocumentStateId =
            _commandExecutor.CurrentDocumentStateId;
        _gestureActive = false;
        _gestureStartSnapshot = null;
    }

    public void MarkSaved() =>
        _savedDocumentStateId =
            _commandExecutor.CurrentDocumentStateId;

    public void BeginGesture()
    {
        if (_currentSnapshot == null ||
            _gestureActive)
            return;

        _gestureActive = true;
        _gestureStartSnapshot =
            _currentSnapshot.ToArray();
        _gestureStartSelection =
            _captureSelection();
    }

    public bool EndGesture(string hintText)
    {
        if (!_gestureActive)
            return false;

        _gestureActive = false;
        var startSelection = _gestureStartSelection;
        _gestureStartSnapshot = null;
        return RecordChange(
            hintText,
            startSelection);
    }

    public void CancelGesture()
    {
        if (!_gestureActive)
            return;

        _gestureActive = false;
        var snapshot = _gestureStartSnapshot;
        var selection = _gestureStartSelection;
        _gestureStartSnapshot = null;
        if (snapshot != null)
            Apply(snapshot, selection);
    }

    public bool RecordChange(string hintText)
    {
        if (_gestureActive)
            return false;

        return RecordChange(
            hintText,
            _captureSelection());
    }

    private bool RecordChange(
        string hintText,
        CscSelectionSnapshot previousSelection)
    {
        if (_currentSnapshot == null)
            return false;

        var nextSnapshot = CaptureBytes();
        if (_currentSnapshot.AsSpan().SequenceEqual(nextSnapshot))
            return false;

        var command = new SceneSnapshotCommand(
            this,
            _currentSnapshot,
            nextSnapshot,
            previousSelection,
            _captureSelection(),
            hintText);
        if (!_commandExecutor.ExecuteCommand(command))
            return false;

        _currentSnapshot = nextSnapshot;
        return true;
    }

    public bool Undo() => _commandExecutor.Undo();
    public bool Redo() => _commandExecutor.Redo();

    private byte[] CaptureBytes() =>
        _captureSnapshot().ToArray();

    private void Apply(
        byte[] snapshot,
        CscSelectionSnapshot selection)
    {
        var bytes = snapshot.ToArray();
        _applySnapshot(bytes, selection);
        _currentSnapshot = bytes;
    }

    private sealed class SceneSnapshotCommand :
        IRedoableCommand
    {
        private readonly CscEditHistory _history;
        private readonly byte[] _previousSnapshot;
        private readonly byte[] _nextSnapshot;
        private readonly CscSelectionSnapshot _previousSelection;

        public CscSelectionSnapshot NextSelection { get; }
        public string HintText { get; }
        public bool IsMutation => true;

        public SceneSnapshotCommand(
            CscEditHistory history,
            byte[] previousSnapshot,
            byte[] nextSnapshot,
            CscSelectionSnapshot previousSelection,
            CscSelectionSnapshot nextSelection,
            string hintText)
        {
            _history = history;
            _previousSnapshot = previousSnapshot.ToArray();
            _nextSnapshot = nextSnapshot.ToArray();
            _previousSelection = previousSelection;
            NextSelection = nextSelection;
            HintText = hintText;
        }

        public void Execute()
        {
        }

        public void Undo() =>
            _history.Apply(
                _previousSnapshot,
                _previousSelection);

        public void Redo() =>
            _history.Apply(
                _nextSnapshot,
                NextSelection);
    }
}
