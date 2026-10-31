using System.Diagnostics;

namespace GameWorld.Core.Vfx;

// Owner-thread scheduling only. Deferred work keeps its full simulation and resumes on its next turn.
internal sealed class VfxInteractiveFrameBudget
{
    private readonly TimeSpan _allowance;
    private readonly Func<TimeSpan> _clock;
    private readonly Dictionary<object, PendingOwner> _owners = new(ReferenceEqualityComparer.Instance);
    private readonly LinkedList<PendingOwner> _pending = new();
    private object? _activeOwner;
    private TimeSpan _activeStarted, _spent;
    private long _frame;

    internal VfxInteractiveFrameBudget(TimeSpan? allowance = null, Func<TimeSpan>? clock = null)
    {
        _allowance = allowance ?? TimeSpan.FromMilliseconds(4);
        if (_allowance <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(allowance));
        _clock = clock ?? (() => Stopwatch.GetElapsedTime(0));
    }

    internal TimeSpan Spent => _spent;
    internal int PendingCount => _pending.Count;

    internal void ResetFrame()
    {
        if (_activeOwner != null) throw new InvalidOperationException("Finish the current preparation before resetting its frame budget.");
        _frame++;
        _spent = TimeSpan.Zero;
        var node = _pending.First;
        while (node != null)
        {
            var next = node.Next;
            // An unseen queue head can defer others for one frame, never indefinitely.
            if (node.Value.LastSeenFrame < _frame - 1) Remove(node.Value);
            node = next;
        }
    }

    internal bool TryBegin(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_activeOwner != null) throw new InvalidOperationException("Interactive preparations cannot share an active frame-budget turn.");
        if (!_owners.TryGetValue(owner, out var entry))
        {
            entry = new(owner);
            entry.Node = _pending.AddLast(entry);
            _owners.Add(owner, entry);
        }
        entry.LastSeenFrame = _frame;
        if (_spent >= _allowance || entry.LastGrantedFrame == _frame || !ReferenceEquals(_pending.First?.Value, entry)) return false;
        entry.LastGrantedFrame = _frame;
        _activeOwner = owner;
        _activeStarted = _clock();
        return true;
    }

    internal void End(object owner, bool stillPending)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (!ReferenceEquals(_activeOwner, owner)) throw new InvalidOperationException("Only the active preparation can finish its frame-budget turn.");
        var elapsed = _clock() - _activeStarted;
        if (elapsed > TimeSpan.Zero) _spent += elapsed;
        _activeOwner = null;
        if (!_owners.TryGetValue(owner, out var entry)) return;
        if (!stillPending) Remove(entry);
        else
        {
            _pending.Remove(entry.Node!);
            _pending.AddLast(entry.Node!);
        }
    }

    internal void Cancel(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (_owners.TryGetValue(owner, out var entry)) Remove(entry);
    }

    private void Remove(PendingOwner entry)
    {
        _pending.Remove(entry.Node!);
        _owners.Remove(entry.Owner);
    }

    private sealed class PendingOwner(object owner)
    {
        public object Owner { get; } = owner;
        public long LastSeenFrame { get; set; }
        public long LastGrantedFrame { get; set; } = -1;
        public LinkedListNode<PendingOwner>? Node { get; set; }
    }
}
