using System;
using System.Collections.Generic;

namespace GameWorld.Core.Components.Selection;

internal static class EdgeIndexCacheBuilder
{
    public static ushort[] BuildLineIndices(
        ReadOnlySpan<ushort> indices,
        int maxEdges)
    {
        var edges = Build(indices, maxEdges);
        var lineIndices = new ushort[edges.Length * 2];
        for (var i = 0; i < edges.Length; i++)
        {
            lineIndices[i * 2] =
                (ushort)edges[i].v0;
            lineIndices[i * 2 + 1] =
                (ushort)edges[i].v1;
        }

        return lineIndices;
    }

    public static (int v0, int v1)[] Build(ReadOnlySpan<ushort> indices, int maxEdges)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEdges);

        if (indices.Length % 3 != 0)
            throw new ArgumentException("Triangle index data must contain complete triples.", nameof(indices));

        if (indices.IsEmpty || maxEdges == 0)
            return Array.Empty<(int, int)>();

        var processedEdges = new HashSet<(int, int)>();
        var result = new List<(int, int)>(Math.Min(maxEdges, indices.Length));

        for (var i = 0; i < indices.Length; i += 3)
        {
            var i0 = indices[i];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];

            if (AddEdge(i0, i1, maxEdges, processedEdges, result) ||
                AddEdge(i1, i2, maxEdges, processedEdges, result) ||
                AddEdge(i0, i2, maxEdges, processedEdges, result))
            {
                break;
            }
        }

        return result.ToArray();
    }

    private static bool AddEdge(
        ushort first,
        ushort second,
        int maxEdges,
        HashSet<(int, int)> processedEdges,
        List<(int, int)> result)
    {
        if (first == second)
            return false;

        var edge = first < second
            ? ((int)first, (int)second)
            : ((int)second, (int)first);

        if (processedEdges.Add(edge))
            result.Add(edge);

        return result.Count == maxEdges;
    }
}
