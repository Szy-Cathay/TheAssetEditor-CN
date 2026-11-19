namespace Shared.Core.ModAssistant;

// Used only where the owning editor has no general undo stack. Never overwrites later edits.
public sealed class AssistantUndoJournal(Func<byte[]> capture, Action<byte[]> restore)
    : AssistantUndoJournal<byte[]>(capture, restore, (a, b) => a.SequenceEqual(b));

public class AssistantUndoJournal<T>(Func<T> capture, Action<T> restore, Func<T, T, bool> equal)
{
    private sealed record Change(T Before, T After);
    private readonly Stack<Change> _undo = new();
    private readonly Stack<Change> _redo = new();
    public bool CanUndo => _undo.TryPeek(out var change) && equal(capture(), change.After);
    public bool CanRedo => _redo.TryPeek(out var change) && equal(capture(), change.Before);
    public void Apply(Action edit)
    {
        var before = capture();
        try
        {
            edit();
            var after = capture();
            if (!equal(before, after))
            {
                _undo.Push(new(before, after));
                if (_undo.Count > 32)
                {
                    var retained = _undo.Take(32).Reverse().ToArray();
                    _undo.Clear();
                    foreach (var item in retained) _undo.Push(item);
                }
                _redo.Clear();
            }
        }
        catch { restore(before); throw; }
    }
    public void Undo()
    {
        if (!CanUndo) throw new InvalidOperationException(AssistantJson.Text("ChangedSinceEdit"));
        var change = _undo.Peek();
        restore(change.Before);
        _undo.Pop(); _redo.Push(change);
    }
    public void Redo()
    {
        if (!CanRedo) throw new InvalidOperationException(AssistantJson.Text("ChangedSinceEdit"));
        var change = _redo.Peek();
        restore(change.After);
        _redo.Pop(); _undo.Push(change);
    }
    public void Clear() { _undo.Clear(); _redo.Clear(); }
}
