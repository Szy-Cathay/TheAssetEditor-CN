using Microsoft.Xna.Framework;
using System.Collections.Generic;

namespace GameWorld.Core.Utility
{
    internal sealed class NearestPointSearch
    {
        static readonly IComparer<Vector3>[] AxisComparers =
        [
            Comparer<Vector3>.Create((left, right) => left.X.CompareTo(right.X)),
            Comparer<Vector3>.Create((left, right) => left.Y.CompareTo(right.Y)),
            Comparer<Vector3>.Create((left, right) => left.Z.CompareTo(right.Z))
        ];

        readonly Vector3[] _points;

        public NearestPointSearch(Vector3[] points)
        {
            _points = (Vector3[])points.Clone();
            Build(0, _points.Length, 0);
        }

        public float FindNearestDistanceSquared(Vector3 point)
        {
            var closestDistanceSquared = float.PositiveInfinity;
            Search(point, 0, _points.Length, 0, ref closestDistanceSquared);
            return closestDistanceSquared;
        }

        void Build(int start, int length, int depth)
        {
            if (length <= 1)
                return;

            Array.Sort(_points, start, length, AxisComparers[depth % 3]);
            var median = start + length / 2;
            Build(start, median - start, depth + 1);
            Build(median + 1, start + length - median - 1, depth + 1);
        }

        void Search(
            Vector3 point,
            int start,
            int length,
            int depth,
            ref float closestDistanceSquared)
        {
            if (length == 0)
                return;

            var axis = depth % 3;
            var median = start + length / 2;
            var medianPoint = _points[median];
            var distanceSquared = Vector3.DistanceSquared(point, medianPoint);
            if (distanceSquared < closestDistanceSquared)
                closestDistanceSquared = distanceSquared;

            var axisDistance = GetCoordinate(point, axis) - GetCoordinate(medianPoint, axis);
            var leftLength = median - start;
            var rightStart = median + 1;
            var rightLength = start + length - rightStart;
            if (axisDistance <= 0.0f)
            {
                Search(point, start, leftLength, depth + 1, ref closestDistanceSquared);
                if (axisDistance * axisDistance <= closestDistanceSquared)
                    Search(point, rightStart, rightLength, depth + 1, ref closestDistanceSquared);
            }
            else
            {
                Search(point, rightStart, rightLength, depth + 1, ref closestDistanceSquared);
                if (axisDistance * axisDistance <= closestDistanceSquared)
                    Search(point, start, leftLength, depth + 1, ref closestDistanceSquared);
            }
        }

        static float GetCoordinate(Vector3 point, int axis)
        {
            return axis switch
            {
                0 => point.X,
                1 => point.Y,
                _ => point.Z
            };
        }
    }
}
