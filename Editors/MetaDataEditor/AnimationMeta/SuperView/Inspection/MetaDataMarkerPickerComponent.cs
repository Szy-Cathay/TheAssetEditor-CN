using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Visualisation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Inspection;

public sealed record MetaDataMarkerPickCandidate(
    MetaDataInspectionItem Item,
    IMetaDataMarkerPreview Preview,
    int StableOrder);

public sealed record MetaDataMarkerHit(
    MetaDataMarkerPickCandidate Candidate,
    MetaDataMarkerHitTarget Target);

public static class MetaDataMarkerHitTester
{
    private const float MinimumAngularRadius = 0.008f;

    public static MetaDataMarkerHit? Pick(
        Ray ray,
        IEnumerable<MetaDataMarkerPickCandidate> candidates,
        bool useAngularTolerance)
    {
        MetaDataMarkerHit? bestHit = null;
        var bestDistanceToRaySquared = float.MaxValue;
        var bestDistanceAlongRay = float.MaxValue;
        var bestStableOrder = int.MaxValue;
        var bestTargetOrder = int.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!candidate.Preview.IsHitTestVisible)
                continue;

            IReadOnlyList<MetaDataMarkerHitTarget> hitTargets;
            try
            {
                hitTargets = candidate.Preview.HitTargets;
            }
            catch (NullReferenceException)
            {
                continue;
            }

            for (var targetOrder = 0;
                targetOrder < hitTargets.Count;
                targetOrder++)
            {
                var target = hitTargets[targetOrder];
                var position = target.Position;
                if (!IsFinite(position))
                    continue;

                var toMarker = position - ray.Position;
                var distanceAlongRay = Vector3.Dot(
                    toMarker,
                    ray.Direction);
                if (distanceAlongRay < 0)
                    continue;

                var closestPoint = ray.Position +
                    ray.Direction * distanceAlongRay;
                var distanceToRaySquared = Vector3.DistanceSquared(
                    position,
                    closestPoint);
                var radius = target.HitTestRadius ??
                    candidate.Preview.HitTestRadius;
                if (useAngularTolerance)
                {
                    radius = Math.Max(
                        radius,
                        distanceAlongRay * MinimumAngularRadius);
                }

                if (distanceToRaySquared > radius * radius ||
                    !IsBetter(
                        distanceToRaySquared,
                        distanceAlongRay,
                        candidate.StableOrder,
                        targetOrder,
                        bestDistanceToRaySquared,
                        bestDistanceAlongRay,
                        bestStableOrder,
                        bestTargetOrder))
                {
                    continue;
                }

                bestHit = new MetaDataMarkerHit(candidate, target);
                bestDistanceToRaySquared = distanceToRaySquared;
                bestDistanceAlongRay = distanceAlongRay;
                bestStableOrder = candidate.StableOrder;
                bestTargetOrder = targetOrder;
            }
        }

        return bestHit;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) &&
        float.IsFinite(value.Y) &&
        float.IsFinite(value.Z);

    private static bool IsBetter(
        float distanceToRaySquared,
        float distanceAlongRay,
        int stableOrder,
        int targetOrder,
        float bestDistanceToRaySquared,
        float bestDistanceAlongRay,
        int bestStableOrder,
        int bestTargetOrder)
    {
        var distanceComparison = distanceToRaySquared.CompareTo(
            bestDistanceToRaySquared);
        if (distanceComparison != 0)
            return distanceComparison < 0;

        var depthComparison = distanceAlongRay.CompareTo(
            bestDistanceAlongRay);
        if (depthComparison != 0)
            return depthComparison < 0;

        var stableOrderComparison = stableOrder.CompareTo(bestStableOrder);
        if (stableOrderComparison != 0)
            return stableOrderComparison < 0;

        return targetOrder < bestTargetOrder;
    }
}

public enum MetaDataMarkerPointerActionKind
{
    None,
    ClearSelection,
    Select,
    Focus,
}

public readonly record struct MetaDataMarkerPointerAction(
    MetaDataMarkerPointerActionKind Kind,
    MetaDataMarkerHit? Hit)
{
    public static MetaDataMarkerPointerAction None => new(
        MetaDataMarkerPointerActionKind.None,
        null);
}

public sealed class MetaDataMarkerPointerInteraction
{
    private const float MaximumClickMovementSquared = 16;
    private static readonly TimeSpan s_doubleClickInterval =
        TimeSpan.FromMilliseconds(500);

    private bool _pressActive;
    private Vector2 _pressPosition;
    private TimeSpan _lastClickTime;
    private ParsedMetadataAttribute? _lastClickSource;
    private MetaDataMarkerPoint _lastClickPoint;

    public MetaDataMarkerPointerAction Update(
        TimeSpan currentTime,
        Vector2 pointerPosition,
        bool isPressed,
        bool isReleased,
        bool isBlocked,
        MetaDataMarkerHit? hit)
    {
        if (isBlocked)
        {
            Reset();
            return MetaDataMarkerPointerAction.None;
        }

        if (isPressed)
        {
            _pressActive = true;
            _pressPosition = pointerPosition;
        }

        if (!isReleased || !_pressActive)
            return MetaDataMarkerPointerAction.None;

        _pressActive = false;
        if (Vector2.DistanceSquared(_pressPosition, pointerPosition) >
            MaximumClickMovementSquared)
        {
            _lastClickSource = null;
            return MetaDataMarkerPointerAction.None;
        }

        if (hit == null)
        {
            _lastClickSource = null;
            return new MetaDataMarkerPointerAction(
                MetaDataMarkerPointerActionKind.ClearSelection,
                null);
        }

        var elapsed = currentTime - _lastClickTime;
        if (ReferenceEquals(
                _lastClickSource,
                hit.Candidate.Item.Source) &&
            _lastClickPoint == hit.Target.Point &&
            elapsed >= TimeSpan.Zero &&
            elapsed <= s_doubleClickInterval)
        {
            _lastClickSource = null;
            return new MetaDataMarkerPointerAction(
                MetaDataMarkerPointerActionKind.Focus,
                hit);
        }

        _lastClickSource = hit.Candidate.Item.Source;
        _lastClickPoint = hit.Target.Point;
        _lastClickTime = currentTime;
        return new MetaDataMarkerPointerAction(
            MetaDataMarkerPointerActionKind.Select,
            hit);
    }

    public void Reset()
    {
        _pressActive = false;
        _lastClickSource = null;
    }
}

public sealed class MetaDataMarkerPickerComponent : BaseComponent, IDisposable
{
    private readonly ArcBallCamera _camera;
    private readonly IMouseComponent _mouse;
    private readonly CombatMetaDataGizmoComponent _gizmo;
    private readonly MetaDataMarkerPointerInteraction _interaction = new();

    private Func<IReadOnlyList<MetaDataMarkerPickCandidate>>?
        _candidateProvider;
    private Action<MetaDataInspectionItem, MetaDataMarkerPoint>? _select;
    private Action<MetaDataInspectionItem>? _focus;
    private Action? _clearSelection;
    private IMetaDataMarkerPreview? _hoveredPreview;

    public MetaDataMarkerPickerComponent(
        ArcBallCamera camera,
        IMouseComponent mouse,
        CombatMetaDataGizmoComponent gizmo)
    {
        _camera = camera;
        _mouse = mouse;
        _gizmo = gizmo;
        UpdateOrder = (int)ComponentUpdateOrderEnum.SelectionComponent;
    }

    public void Connect(
        Func<IReadOnlyList<MetaDataMarkerPickCandidate>> candidateProvider,
        Action<MetaDataInspectionItem, MetaDataMarkerPoint> select,
        Action<MetaDataInspectionItem> focus,
        Action clearSelection)
    {
        _candidateProvider = candidateProvider;
        _select = select;
        _focus = focus;
        _clearSelection = clearSelection;
    }

    public override void Update(GameTime gameTime)
    {
        if (_candidateProvider == null)
            return;

        try
        {
            var isBlocked = _gizmo.IsGestureActive ||
                !_mouse.IsMouseOwner(this);
            MetaDataMarkerHit? hit = null;
            if (!isBlocked)
            {
                hit = MetaDataMarkerHitTester.Pick(
                    _camera.CreateCameraRay(_mouse.Position()),
                    _candidateProvider(),
                    _camera.CurrentProjectionType ==
                        ProjectionType.Perspective);
            }

            SetHovered(isBlocked ? null : hit?.Candidate.Preview);
            var action = _interaction.Update(
                gameTime.TotalGameTime,
                _mouse.Position(),
                _mouse.IsMouseButtonPressed(MouseButton.Left),
                _mouse.IsMouseButtonReleased(MouseButton.Left),
                isBlocked,
                hit);
            if (action.Kind ==
                MetaDataMarkerPointerActionKind.ClearSelection)
            {
                _clearSelection?.Invoke();
                return;
            }

            if (action.Hit == null)
                return;

            if (action.Kind == MetaDataMarkerPointerActionKind.Select)
            {
                _select?.Invoke(
                    action.Hit.Candidate.Item,
                    action.Hit.Target.Point);
            }
            else if (action.Kind == MetaDataMarkerPointerActionKind.Focus)
                _focus?.Invoke(action.Hit.Candidate.Item);
        }
        catch
        {
            ClearInteractionState();
            throw;
        }
    }

    public void ClearInteractionState()
    {
        ClearHover();
        _interaction.Reset();
    }

    public void ClearHover() => SetHovered(null);

    public void Disconnect()
    {
        ClearInteractionState();
        _candidateProvider = null;
        _select = null;
        _focus = null;
        _clearSelection = null;
    }

    private void SetHovered(IMetaDataMarkerPreview? preview)
    {
        if (ReferenceEquals(_hoveredPreview, preview))
            return;

        if (_hoveredPreview != null)
            _hoveredPreview.IsHovered = false;
        _hoveredPreview = preview;
        if (_hoveredPreview != null)
            _hoveredPreview.IsHovered = true;
    }

    public void Dispose() => Disconnect();
}
