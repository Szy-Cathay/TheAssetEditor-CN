using GameWorld.Core.Commands;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Editing;

public enum CombatMetaDataPoint
{
    Position,
    SplashStart,
    SplashEnd
}

public enum CombatMetaDataTransformMode
{
    Translate,
    Rotate
}

public enum CombatMetaDataEditChangeKind
{
    Preview,
    Committed,
    Cancelled,
    Undone,
    Redone
}

public sealed record CombatMetaDataEditChange(
    object Owner,
    ParsedMetadataAttribute Source,
    CombatMetaDataPoint Point,
    CombatMetaDataEditChangeKind Kind);

public sealed class CombatMetaDataEditSession
{
    private readonly IEventHub _eventHub;
    private readonly Dictionary<object, DocumentHistory> _histories =
        new(ReferenceEqualityComparer.Instance);

    private object? _currentOwner;
    private PointAccessor? _target;
    private bool _gestureActive;
    private CombatMetaDataTransformMode _gestureMode;
    private Vector3 _gestureStart;
    private Quaternion _gestureOrientationStart;

    public event Action<CombatMetaDataEditChange>? ValueChanged;
    public event Action? HistoryChanged;

    public CombatMetaDataEditSession(IEventHub eventHub)
    {
        _eventHub = eventHub;
    }

    public bool CanUndo =>
        _currentOwner != null &&
        GetHistory(_currentOwner).Executor.CanUndo();

    public bool CanRedo =>
        _currentOwner != null &&
        GetHistory(_currentOwner).Executor.CanRedo();

    public bool HasUnsavedChanges =>
        _histories.Values.Any(history => history.HasUnsavedChanges);

    public Vector3? CurrentPosition => _target?.Get();
    public Quaternion? CurrentOrientation => _target?.GetOrientation();
    public Vector3? CurrentWorldPosition => _target?.GetWorldPosition();
    public Quaternion? CurrentWorldOrientation =>
        _target?.GetWorldOrientation();
    public bool CanRotate => _target?.CanRotate == true;
    public CombatMetaDataPoint? CurrentPoint => _target?.Point;
    public ParsedMetadataAttribute? CurrentSource => _target?.Source;

    public static bool CanEdit(ParsedMetadataAttribute source) =>
        PointAccessor.TryCreate(
            source,
            CombatMetaDataPoint.Position) != null ||
        PointAccessor.TryCreate(
            source,
            CombatMetaDataPoint.SplashStart) != null ||
        PointAccessor.TryCreate(
            source,
            CombatMetaDataPoint.SplashEnd) != null;

    public bool SetTarget(
        object owner,
        ParsedMetadataAttribute source,
        CombatMetaDataPoint point,
        Func<Matrix>? referenceWorldTransform = null)
    {
        CancelGesture();
        _currentOwner = owner;
        _target = PointAccessor.TryCreate(source, point);
        _target?.SetTransformSpace(referenceWorldTransform);
        HistoryChanged?.Invoke();
        return _target != null;
    }

    public void ClearTarget(object? owner = null)
    {
        CancelGesture();
        _currentOwner = owner;
        _target = null;
        HistoryChanged?.Invoke();
    }

    public bool BeginGesture(
        CombatMetaDataTransformMode mode = CombatMetaDataTransformMode.Translate)
    {
        if (_target == null ||
            _gestureActive ||
            (mode == CombatMetaDataTransformMode.Rotate && !_target.CanRotate))
        {
            return false;
        }

        _gestureStart = _target.Get();
        _gestureOrientationStart =
            _target.GetOrientation() ?? Quaternion.Identity;
        _gestureMode = mode;
        _gestureActive = true;
        return true;
    }

    public bool Translate(Vector3 delta)
    {
        if (_target == null ||
            !_gestureActive ||
            _gestureMode != CombatMetaDataTransformMode.Translate)
        {
            return false;
        }

        _target.Set(
            _target.Get() + _target.ToLocalTranslation(delta));
        PublishChange(CombatMetaDataEditChangeKind.Preview);
        return true;
    }

    public bool Rotate(Matrix delta)
    {
        if (_target == null ||
            !_gestureActive ||
            _gestureMode != CombatMetaDataTransformMode.Rotate ||
            !_target.CanRotate)
        {
            return false;
        }

        var current = Matrix.CreateFromQuaternion(
            _target.GetOrientation()!.Value);
        var localDelta = _target.ToLocalRotationDelta(delta);
        var next = Quaternion.CreateFromRotationMatrix(
            current * localDelta);
        next.Normalize();
        _target.SetOrientation(next);
        PublishChange(CombatMetaDataEditChangeKind.Preview);
        return true;
    }

    public bool EndGesture()
    {
        if (_target == null || _currentOwner == null || !_gestureActive)
            return false;

        _gestureActive = false;
        IRedoableCommand command;
        if (_gestureMode == CombatMetaDataTransformMode.Rotate)
        {
            var end = _target.GetOrientation()!.Value;
            if (end == _gestureOrientationStart)
                return false;

            command = new OrientationChangeCommand(
                this,
                _currentOwner,
                _target,
                _gestureOrientationStart,
                end);
        }
        else
        {
            var end = _target.Get();
            if (end == _gestureStart)
                return false;

            command = new PointMoveCommand(
                this,
                _currentOwner,
                _target,
                _gestureStart,
                end);
        }
        var executed = GetHistory(_currentOwner)
            .Executor
            .ExecuteCommand(command);
        HistoryChanged?.Invoke();
        return executed;
    }

    public void CancelGesture()
    {
        if (_target == null || !_gestureActive)
            return;

        _gestureActive = false;
        if (_gestureMode == CombatMetaDataTransformMode.Rotate)
            _target.SetOrientation(_gestureOrientationStart);
        else
            _target.Set(_gestureStart);
        PublishChange(CombatMetaDataEditChangeKind.Cancelled);
    }

    public bool Undo()
    {
        if (_currentOwner == null)
            return false;

        var result = GetHistory(_currentOwner).Executor.Undo();
        if (result)
            HistoryChanged?.Invoke();
        return result;
    }

    public bool Redo()
    {
        if (_currentOwner == null)
            return false;

        var result = GetHistory(_currentOwner).Executor.Redo();
        if (result)
            HistoryChanged?.Invoke();
        return result;
    }

    public void ResetDocument(object owner)
    {
        if (ReferenceEquals(_currentOwner, owner))
            ClearTarget(owner);

        _histories.Remove(owner);
        HistoryChanged?.Invoke();
    }

    public void MarkSaved(object owner)
    {
        GetHistory(owner).SavedDocumentStateId =
            GetHistory(owner).Executor.CurrentDocumentStateId;
        HistoryChanged?.Invoke();
    }

    public bool HasUnsavedChangesFor(object owner) =>
        _histories.TryGetValue(owner, out var history) &&
        history.HasUnsavedChanges;

    private DocumentHistory GetHistory(object owner)
    {
        if (_histories.TryGetValue(owner, out var history))
            return history;

        history = new DocumentHistory(new CommandExecutor(_eventHub));
        _histories.Add(owner, history);
        return history;
    }

    private void Apply(
        object owner,
        PointAccessor target,
        Vector3 value,
        CombatMetaDataEditChangeKind kind)
    {
        target.Set(value);
        ValueChanged?.Invoke(new CombatMetaDataEditChange(
            owner,
            target.Source,
            target.Point,
            kind));
    }

    private void ApplyOrientation(
        object owner,
        PointAccessor target,
        Quaternion value,
        CombatMetaDataEditChangeKind kind)
    {
        target.SetOrientation(value);
        ValueChanged?.Invoke(new CombatMetaDataEditChange(
            owner,
            target.Source,
            target.Point,
            kind));
    }

    private void PublishChange(CombatMetaDataEditChangeKind kind)
    {
        if (_currentOwner == null || _target == null)
            return;

        ValueChanged?.Invoke(new CombatMetaDataEditChange(
            _currentOwner,
            _target.Source,
            _target.Point,
            kind));
    }

    private sealed class DocumentHistory(CommandExecutor executor)
    {
        public CommandExecutor Executor { get; } = executor;
        public long SavedDocumentStateId { get; set; }
        public bool HasUnsavedChanges =>
            Executor.CurrentDocumentStateId != SavedDocumentStateId;
    }

    private sealed class PointMoveCommand : IRedoableCommand
    {
        private readonly CombatMetaDataEditSession _session;
        private readonly object _owner;
        private readonly PointAccessor _target;
        private readonly Vector3 _previous;
        private readonly Vector3 _next;
        private bool _hasExecuted;

        public string HintText => "Move combat metadata point";
        public bool IsMutation => true;

        public PointMoveCommand(
            CombatMetaDataEditSession session,
            object owner,
            PointAccessor target,
            Vector3 previous,
            Vector3 next)
        {
            _session = session;
            _owner = owner;
            _target = target;
            _previous = previous;
            _next = next;
        }

        public void Execute()
        {
            _session.Apply(
                _owner,
                _target,
                _next,
                _hasExecuted
                    ? CombatMetaDataEditChangeKind.Redone
                    : CombatMetaDataEditChangeKind.Committed);
            _hasExecuted = true;
        }

        public void Undo() =>
            _session.Apply(
                _owner,
                _target,
                _previous,
                CombatMetaDataEditChangeKind.Undone);

        public void Redo() =>
            _session.Apply(
                _owner,
                _target,
                _next,
                CombatMetaDataEditChangeKind.Redone);
    }

    private sealed class OrientationChangeCommand : IRedoableCommand
    {
        private readonly CombatMetaDataEditSession _session;
        private readonly object _owner;
        private readonly PointAccessor _target;
        private readonly Quaternion _previous;
        private readonly Quaternion _next;
        private bool _hasExecuted;

        public string HintText => "Rotate effect metadata";
        public bool IsMutation => true;

        public OrientationChangeCommand(
            CombatMetaDataEditSession session,
            object owner,
            PointAccessor target,
            Quaternion previous,
            Quaternion next)
        {
            _session = session;
            _owner = owner;
            _target = target;
            _previous = previous;
            _next = next;
        }

        public void Execute()
        {
            _session.ApplyOrientation(
                _owner,
                _target,
                _next,
                _hasExecuted
                    ? CombatMetaDataEditChangeKind.Redone
                    : CombatMetaDataEditChangeKind.Committed);
            _hasExecuted = true;
        }

        public void Undo() =>
            _session.ApplyOrientation(
                _owner,
                _target,
                _previous,
                CombatMetaDataEditChangeKind.Undone);

        public void Redo() =>
            _session.ApplyOrientation(
                _owner,
                _target,
                _next,
                CombatMetaDataEditChangeKind.Redone);
    }

    private sealed class PointAccessor
    {
        private readonly Func<Vector3> _get;
        private readonly Action<Vector3> _set;
        private readonly Func<Quaternion>? _getOrientation;
        private readonly Action<Quaternion>? _setOrientation;
        private MetaDataTransformSpace _transformSpace = new();

        public ParsedMetadataAttribute Source { get; }
        public CombatMetaDataPoint Point { get; }

        private PointAccessor(
            ParsedMetadataAttribute source,
            CombatMetaDataPoint point,
            Func<Vector3> get,
            Action<Vector3> set,
            Func<Quaternion>? getOrientation = null,
            Action<Quaternion>? setOrientation = null)
        {
            Source = source;
            Point = point;
            _get = get;
            _set = set;
            _getOrientation = getOrientation;
            _setOrientation = setOrientation;
        }

        public bool CanRotate =>
            _getOrientation != null && _setOrientation != null;
        public Vector3 Get() => _get();
        public void Set(Vector3 value) => _set(value);
        public Quaternion? GetOrientation() => _getOrientation?.Invoke();
        public void SetOrientation(Quaternion value) =>
            _setOrientation?.Invoke(value);
        public Vector3 GetWorldPosition() =>
            _transformSpace.ToWorldPosition(Get());
        public Quaternion? GetWorldOrientation() =>
            GetOrientation() is Quaternion orientation
                ? _transformSpace.ToWorldOrientation(orientation)
                : null;
        public Vector3 ToLocalTranslation(Vector3 worldTranslation) =>
            _transformSpace.ToLocalTranslation(worldTranslation);
        public Matrix ToLocalRotationDelta(Matrix worldRotationDelta) =>
            _transformSpace.ToLocalRotationDelta(worldRotationDelta);
        public void SetTransformSpace(
            Func<Matrix>? referenceWorldTransform) =>
            _transformSpace = new MetaDataTransformSpace(
                referenceWorldTransform);

        public static PointAccessor? TryCreate(
            ParsedMetadataAttribute source,
            CombatMetaDataPoint point)
        {
            PointAccessor? accessor = (source, point) switch
            {
                (ImpactPosition_v2 value, CombatMetaDataPoint.Position) =>
                    new(source, point, () => value.Position, position => value.Position = position),
                (ImpactPosition_v10 value, CombatMetaDataPoint.Position) =>
                    new(source, point, () => value.Position, position => value.Position = position),
                (TargetPos_0 value, CombatMetaDataPoint.Position) =>
                    new(source, point, () => value.Position, position => value.Position = position),
                (TargetPos_10 value, CombatMetaDataPoint.Position) =>
                    new(source, point, () => value.Position, position => value.Position = position),
                (FirePos_v0 value, CombatMetaDataPoint.Position) =>
                    new(source, point, () => value.Position, position => value.Position = position),
                (FirePos_v2 value, CombatMetaDataPoint.Position) =>
                    new(source, point, () => value.Position, position => value.Position = position),
                (FirePos_v10 value, CombatMetaDataPoint.Position) =>
                    new(source, point, () => value.Position, position => value.Position = position),
                (SplashAttack_v3 value, CombatMetaDataPoint.SplashStart) =>
                    new(source, point, () => value.StartPosition, position => value.StartPosition = position),
                (SplashAttack_v3 value, CombatMetaDataPoint.SplashEnd) =>
                    new(source, point, () => value.EndPosition, position => value.EndPosition = position),
                (SplashAttack_v10 value, CombatMetaDataPoint.SplashStart) =>
                    new(source, point, () => value.StartPosition, position => value.StartPosition = position),
                (SplashAttack_v10 value, CombatMetaDataPoint.SplashEnd) =>
                    new(source, point, () => value.EndPosition, position => value.EndPosition = position),
                (IEffectMeta value, CombatMetaDataPoint.Position) =>
                    new(
                        source,
                        point,
                        () => value.Position,
                        position => value.Position = position,
                        () => new Quaternion(value.Orientation),
                        orientation => value.Orientation = orientation.ToVector4()),
                _ => null
            };

            if (accessor != null ||
                point != CombatMetaDataPoint.Position ||
                !SpatialMetaDataCatalog.TryCreate(source, out var binding))
            {
                return accessor;
            }

            return new PointAccessor(
                source,
                point,
                () => binding.Position,
                value => binding.Position = value,
                binding.CanRotate
                    ? () => binding.Orientation!.Value
                    : null,
                binding.CanRotate
                    ? value => binding.Orientation = value
                    : null);
        }
    }
}
