using System;
using System.Collections.Generic;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Components.Selection;

internal static class EdgeOverlayDataBuilder
{
    private static readonly Vector3 WireColor = new(0.15f, 0.15f, 0.15f);
    private static readonly Vector3 SelectedColor = new(1.0f, 0.47f, 0.0f);
    private const float SelectedEdgeHalfWidth = 1.5f;

    public static void Fill(
        Span<EdgeData> destination,
        MeshObject geometry,
        Matrix modelMatrix,
        IReadOnlyList<(int v0, int v1)> edges,
        IReadOnlyList<float> weights)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(weights);

        if (destination.Length != edges.Count)
            throw new ArgumentException("The destination length must match the edge count.", nameof(destination));

        for (var i = 0; i < edges.Count; i++)
        {
            var (v0, v1) = edges[i];
            destination[i] = new EdgeData
            {
                P0 = Vector3.Transform(geometry.GetVertexById(v0), modelMatrix),
                P1 = Vector3.Transform(geometry.GetVertexById(v1), modelMatrix),
                C0 = Vector3.Lerp(WireColor, SelectedColor, weights[v0]),
                C1 = Vector3.Lerp(WireColor, SelectedColor, weights[v1]),
                Width = 0
            };
        }
    }

    public static void Fill(
        Span<EdgeData> destination,
        IReadOnlyList<Vector3> worldPositions,
        IReadOnlyList<(int v0, int v1)> edges,
        IReadOnlyList<float> weights)
    {
        ArgumentNullException.ThrowIfNull(worldPositions);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(weights);
        if (destination.Length != edges.Count)
        {
            throw new ArgumentException(
                "The destination length must match the edge count.",
                nameof(destination));
        }

        for (var i = 0; i < edges.Count; i++)
        {
            var (v0, v1) = edges[i];
            destination[i] = new EdgeData
            {
                P0 = worldPositions[v0],
                P1 = worldPositions[v1],
                C0 = Vector3.Lerp(
                    WireColor,
                    SelectedColor,
                    weights[v0]),
                C1 = Vector3.Lerp(
                    WireColor,
                    SelectedColor,
                    weights[v1]),
                Width = 0
            };
        }
    }

    public static void FillSelected(
        Span<EdgeData> destination,
        MeshObject geometry,
        Matrix modelMatrix,
        IReadOnlyList<(int v0, int v1)> edges)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(edges);

        if (destination.Length != edges.Count)
            throw new ArgumentException("The destination length must match the edge count.", nameof(destination));

        for (var i = 0; i < edges.Count; i++)
        {
            var (v0, v1) = edges[i];
            destination[i] = new EdgeData
            {
                P0 = Vector3.Transform(geometry.GetVertexById(v0), modelMatrix),
                P1 = Vector3.Transform(geometry.GetVertexById(v1), modelMatrix),
                C0 = SelectedColor,
                C1 = SelectedColor,
                Width = SelectedEdgeHalfWidth
            };
        }
    }

    public static void FillSelected(
        Span<EdgeData> destination,
        IReadOnlyList<Vector3> worldPositions,
        IReadOnlyList<(int v0, int v1)> edges)
    {
        ArgumentNullException.ThrowIfNull(worldPositions);
        ArgumentNullException.ThrowIfNull(edges);
        if (destination.Length != edges.Count)
        {
            throw new ArgumentException(
                "The destination length must match the edge count.",
                nameof(destination));
        }

        for (var i = 0; i < edges.Count; i++)
        {
            var (v0, v1) = edges[i];
            destination[i] = new EdgeData
            {
                P0 = worldPositions[v0],
                P1 = worldPositions[v1],
                C0 = SelectedColor,
                C1 = SelectedColor,
                Width = SelectedEdgeHalfWidth
            };
        }
    }
}
