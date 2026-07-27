using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Utility;

public class NearestPointSearchTests
{
    [Test]
    public void FindNearestDistanceSquared_MatchesBruteForceSearch()
    {
        var points = new[]
        {
            new Vector3(-5.0f, 1.0f, 2.0f),
            new Vector3(4.0f, -3.0f, 7.0f),
            new Vector3(2.0f, 8.0f, -1.0f),
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(12.0f, 4.0f, 3.0f)
        };
        var queries = new[]
        {
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(10.0f, 2.0f, 4.0f),
            new Vector3(-8.0f, 4.0f, 1.0f)
        };
        var search = new NearestPointSearch(points);

        Assert.Multiple(() =>
        {
            foreach (var query in queries)
            {
                var expected = points.Min(point => Vector3.DistanceSquared(query, point));
                Assert.That(
                    search.FindNearestDistanceSquared(query),
                    Is.EqualTo(expected).Within(0.0001f));
            }
        });
    }
}
