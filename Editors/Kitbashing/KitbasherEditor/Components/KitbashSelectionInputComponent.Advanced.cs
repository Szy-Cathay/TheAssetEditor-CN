using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GameWorld.Core.Commands.Edge;
using GameWorld.Core.Commands.Face;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace Editors.KitbasherEditor.Components;

public sealed partial class KitbashSelectionInputComponent
{
    public KitbashSelectionSettings Settings { get; }
    public bool IsCircleSelecting => _circleInitial != null;
    public float CircleRadius { get; private set; } = 25;
    private ISelectionState? _circleInitial;
    private Vector2? _lastPaintPosition;
    private bool _lastPaintRemove;
    private Vector2 _circlePressPosition;
    private bool _circleCanClearSelection;

    // Switch selection shape without taking input away from navigation or Gizmo handles.
    public void CaptureSelectionGesture()
    {
        if (_gizmoComponent?.Gizmo?.IsInModalTransform == true || _mouseComponent.MouseOwner == _gizmoComponent)
            return;
        if (_selectionManager.GetState().Mode is not (GeometrySelectionMode.Vertex or GeometrySelectionMode.Edge or GeometrySelectionMode.Face))
        {
            Settings.IsCircleSelection = false;
            return;
        }
        if (_mouseComponent.MouseOwner != null && _mouseComponent.MouseOwner != this)
            return;
        var alt = _keyboardComponent.IsKeyDownOrReleased(Keys.LeftAlt) || _keyboardComponent.IsKeyDownOrReleased(Keys.RightAlt);
        if (_keyboardComponent.IsKeyPressed(Keys.W) && !alt && !IsControlHeld())
            Settings.IsCircleSelection = !Settings.IsCircleSelection;
    }

    public bool StartCircleSelection()
    {
        if (IsCircleSelecting || _gizmoComponent?.Gizmo?.IsInModalTransform == true ||
            _mouseComponent.MouseOwner != null ||
            _selectionManager.GetState().Mode is not (GeometrySelectionMode.Vertex or GeometrySelectionMode.Edge or GeometrySelectionMode.Face))
            return false;
        Settings.IsCircleSelection = true;
        _circleInitial = _selectionManager.GetStateCopy();
        _lastPaintPosition = null;
        _circlePressPosition = _mouseComponent.GetPressPosition(MouseButton.Left) ?? _mouseComponent.Position();
        _circleCanClearSelection = !IsControlHeld() && !IsShiftHeld();
        _isMouseDown = false;
        _mouseComponent.MouseOwner = this;
        _mouseComponent.BeginContinuousDrag(false);
        return true;
    }

    private bool UpdateAdvancedSelection()
    {
        if (!Settings.IsCircleSelection || _mouseComponent.MouseOwner != null && _mouseComponent.MouseOwner != this)
            return false;
        // The camera and Gizmo have already had their normal chance to claim this click.
        if (!IsCircleSelecting && _mouseComponent.IsMouseButtonPressed(MouseButton.Left))
            StartCircleSelection();
        var radius = CircleRadius;
        var radiusSteps = 0;
        if (_keyboardComponent.IsKeyPressed(Keys.Add)) radiusSteps++;
        if (_keyboardComponent.IsKeyPressed(Keys.Subtract)) radiusSteps--;
        for (var step = 0; step < Math.Abs(radiusSteps); step++)
            CircleRadius = Math.Clamp(CircleRadius + Math.Sign(radiusSteps) * (2 + MathF.Floor(CircleRadius / 10)), 1, 100000);
        if (IsCircleSelecting)
        {
            if (_selectionManager.GetState().Mode != _circleInitial!.Mode ||
                _selectionManager.GetState().GetSingleSelectedObject() != _circleInitial.GetSingleSelectedObject())
            {
                _circleInitial = null;
                Settings.IsCircleSelection = false;
                ReleaseSelectionGesture();
                return true;
            }
            if (_keyboardComponent.IsKeyReleased(Keys.Escape) || _mouseComponent.IsMouseButtonPressed(MouseButton.Right))
            {
                FinishCircleSelection();
                return true;
            }
            var left = _mouseComponent.IsMouseButtonDown(MouseButton.Left) || _mouseComponent.IsMouseButtonPressed(MouseButton.Left) ||
                _lastPaintPosition.HasValue && _mouseComponent.IsMouseButtonReleased(MouseButton.Left);
            if (left)
            {
                var remove = IsControlHeld();
                var point = _mouseComponent.Position();
                _circleCanClearSelection &= !remove && !IsShiftHeld() && Vector2.DistanceSquared(_circlePressPosition, point) <= 16;
                var from = remove == _lastPaintRemove ? _lastPaintPosition : null;
                if (_mouseComponent.IsMouseButtonPressed(MouseButton.Left))
                    from = _mouseComponent.GetPressPosition(MouseButton.Left) ?? from;
                if (_mouseComponent.IsMouseButtonPressed(MouseButton.Left) || !from.HasValue || from.Value != point || radius != CircleRadius)
                    PaintCircle(point, from, remove);
                _lastPaintPosition = point;
                _lastPaintRemove = remove;
                if (_mouseComponent.IsMouseButtonReleased(MouseButton.Left))
                {
                    // Only an unmodified empty click clears selection; never a drag through empty space.
                    if (_circleCanClearSelection && _selectionManager.GetState().SelectionCount() > 0)
                        _selectionManager.GetState().Clear();
                    FinishCircleSelection();
                }
            }
            else if (_lastPaintPosition.HasValue)
                FinishCircleSelection();
            return true;
        }
        return false;
    }

    private void PaintCircle(Vector2 point, Vector2? start, bool remove)
    {
        var state = _selectionManager.GetState();
        var selected = state.GetSingleSelectedObject();
        var positions = GetEvaluatedWorldPositions(selected) ?? selected.Geometry.VertexArray
            .Select(vertex => Vector3.Transform(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z), selected.RenderMatrix)).ToArray();
        var origin = start ?? point;
        var minimum = Vector2.Min(point, origin) - new Vector2(CircleRadius);
        var maximum = Vector2.Max(point, origin) + new Vector2(CircleRadius);
        var bounds = new Rectangle((int)MathF.Floor(minimum.X), (int)MathF.Floor(minimum.Y),
            (int)MathF.Ceiling(maximum.X - minimum.X) + 1, (int)MathF.Ceiling(maximum.Y - minimum.Y) + 1);
        var projection = _camera.ViewMatrix * _camera.ProjectionMatrix;
        var viewport = _camera.InputViewport;
        if (state is VertexSelectionState vertices)
        {
            var hit = IntersectionMath.IntersectVisibleVertices(bounds, selected.Geometry, positions, projection,
                viewport.Width, viewport.Height, Settings.IsXRay, point, CircleRadius, start);
            _circleCanClearSelection &= hit.Count == 0;
            if (hit.Any(index => vertices.SelectedVertices.Contains(index) == remove)) vertices.ModifySelection(hit, remove);
        }
        else if (state is EdgeSelectionState edges)
        {
            var hit = IntersectionMath.IntersectVisibleEdges(bounds, selected.Geometry, positions, projection,
                viewport.Width, viewport.Height, Settings.IsXRay, point, CircleRadius, start);
            _circleCanClearSelection &= hit.Count == 0;
            if (hit.Any(edge => edges.SelectedEdges.Contains(edge) == remove)) edges.ModifySelection(hit, remove);
        }
        else if (state is FaceSelectionState faces)
        {
            var hit = IntersectionMath.IntersectVisibleFaces(bounds, selected.Geometry, positions, projection,
                viewport.Width, viewport.Height, Settings.IsXRay, point, CircleRadius, start);
            _circleCanClearSelection &= hit.Count == 0;
            if (hit.Any(face => faces.SelectedFaces.Contains(face) == remove)) faces.ModifySelection(hit, remove);
        }
    }

    public void FinishCircleSelection()
    {
        if (_circleInitial == null) return;
        var initial = _circleInitial;
        _circleInitial = null;
        var final = _selectionManager.GetStateCopy();
        try
        {
            // Commit each completed stroke as one selection-only undo.
            if (final is VertexSelectionState vertices && initial is VertexSelectionState oldVertices &&
                !vertices.SelectedVertices.SequenceEqual(oldVertices.SelectedVertices))
            {
                _selectionManager.SetState(initial);
                var items = vertices.SelectedVertices.Where(i => i != vertices.ActiveVertex).ToList();
                if (vertices.ActiveVertex.HasValue) items.Add(vertices.ActiveVertex.Value);
                _commandFactory.Create<VertexSelectionCommand>().Configure(command => command.Configure(items, false, false)).BuildAndExecute();
            }
            else if (final is EdgeSelectionState edges && initial is EdgeSelectionState oldEdges &&
                !edges.SelectedEdges.SetEquals(oldEdges.SelectedEdges))
            {
                _selectionManager.SetState(initial);
                var items = edges.SelectedEdges.Where(i => i != edges.ActiveEdge).ToList();
                if (edges.ActiveEdge.HasValue) items.Add(edges.ActiveEdge.Value);
                _commandFactory.Create<EdgeSelectionCommand>().Configure(command => command.Configure(items, false, false)).BuildAndExecute();
            }
            else if (final is FaceSelectionState faces && initial is FaceSelectionState oldFaces &&
                !faces.SelectedFaces.SequenceEqual(oldFaces.SelectedFaces))
            {
                _selectionManager.SetState(initial);
                var items = faces.SelectedFaces.Where(i => i != faces.ActiveFace).ToList();
                if (faces.ActiveFace.HasValue) items.Add(faces.ActiveFace.Value);
                _commandFactory.Create<FaceSelectionCommand>().Configure(command => command.Configure(items, false, false)).BuildAndExecute();
            }
        }
        finally { ReleaseSelectionGesture(); }
    }

    private void OnSelectionCaptureInterrupted()
    {
        FinishCircleSelection();
        _isMouseDown = false;
        if (_mouseComponent.MouseOwner == this) ReleaseSelectionGesture();
    }

    private void OnSelectionSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.IsCircleSelection) && !Settings.IsCircleSelection)
            FinishCircleSelection();
    }

    private void ReleaseSelectionGesture()
    {
        _lastPaintPosition = null;
        _skipNextSelection = true;
        _mouseComponent.EndContinuousDrag();
        if (_mouseComponent.MouseOwner == this) _mouseComponent.MouseOwner = null;
        _mouseComponent.ClearStates();
    }

    private void SelectXrayFace(Vector2 point, FaceSelectionState state, bool add, bool remove)
    {
        var selected = state.RenderObject;
        var positions = GetEvaluatedWorldPositions(selected) ?? selected.Geometry.VertexArray
            .Select(vertex => Vector3.Transform(new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z), selected.RenderMatrix)).ToArray();
        var viewport = _camera.InputViewport;
        var bounds = new Rectangle((int)point.X - 12, (int)point.Y - 12, 25, 25);
        var faces = IntersectionMath.IntersectVisibleFaces(bounds, selected.Geometry, positions,
            _camera.ViewMatrix * _camera.ProjectionMatrix, viewport.Width, viewport.Height, true);
        var indices = selected.Geometry.IndexArray;
        var ordered = faces.OrderBy(face =>
        {
            var center = (positions[indices[face]] + positions[indices[face + 1]] + positions[indices[face + 2]]) / 3;
            var screen = viewport.Project(center, _camera.ProjectionMatrix, _camera.ViewMatrix, Matrix.Identity);
            return Vector2.Distance(new Vector2(screen.X, screen.Y), point) + (state.SelectedFaces.Contains(face) ? 5 : 0);
        }).ToList();
        if (ordered.Count > 0)
            _commandFactory.Create<FaceSelectionCommand>().Configure(c => c.Configure(ordered[0], add,
                remove || add && state.SelectedFaces.Contains(ordered[0]))).BuildAndExecute();
        else if (!add && !remove && state.SelectedFaces.Count > 0)
            _commandFactory.Create<FaceSelectionCommand>().Configure(c => c.Configure(new List<int>(), false, false)).BuildAndExecute();
    }

    private void DrawCircleSelection()
    {
        var mouse = _mouseComponent.CapturedCursorPosition ?? _mouseComponent.Position();
        var viewport = _deviceResolverComponent.Device.Viewport;
        var input = _mouseComponent.GetScreenSize();
        var scale = new Vector2(viewport.Width / input.X, viewport.Height / input.Y);
        var sprites = _resourceLibrary.CommonSpriteBatch;
        sprites.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        for (var i = 0; i < 96; i++)
        {
            var a = i * MathHelper.TwoPi / 96;
            var b = (i + 1) * MathHelper.TwoPi / 96;
            var start = (mouse + new Vector2(MathF.Cos(a), MathF.Sin(a)) * CircleRadius) * scale;
            var end = (mouse + new Vector2(MathF.Cos(b), MathF.Sin(b)) * CircleRadius) * scale;
            var delta = end - start;
            sprites.Draw(_textTexture, start, null, Color.White, MathF.Atan2(delta.Y, delta.X), Vector2.Zero,
                new Vector2(delta.Length(), MathF.Max(1, scale.Y)), SpriteEffects.None, 0);
        }
        sprites.End();
    }
}
