using GameWorld.Core.Components.Selection;

namespace GameWorld.Core.Test.Components.Selection;

public class EdgeIndexCacheBuilderTests
{
    [Test]
    public void Build_SingleTriangle_ReturnsFirstSeenNormalizedEdges()
    {
        var result = EdgeIndexCacheBuilder.Build(new ushort[] { 0, 1, 2 }, 50_000);

        Assert.That(result, Is.EqualTo(new[]
        {
            (0, 1),
            (1, 2),
            (0, 2)
        }));
    }

    [Test]
    public void Build_SharedTriangleEdge_ReturnsUniqueEdgesInFirstSeenOrder()
    {
        var result = EdgeIndexCacheBuilder.Build(
            new ushort[] { 0, 1, 2, 2, 1, 3 },
            50_000);

        Assert.That(result, Is.EqualTo(new[]
        {
            (0, 1),
            (1, 2),
            (0, 2),
            (1, 3),
            (2, 3)
        }));
    }

    [Test]
    public void Build_WhenUniqueEdgeLimitIsReached_StopsAtExactLimit()
    {
        var result = EdgeIndexCacheBuilder.Build(
            new ushort[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 },
            4);

        Assert.That(result, Has.Length.EqualTo(4));
        Assert.That(result, Is.EqualTo(new[]
        {
            (0, 1),
            (1, 2),
            (0, 2),
            (3, 4)
        }));
    }

    [Test]
    public void Build_DuplicateHeavyInput_DoesNotExceedLimit()
    {
        var indices = Enumerable.Repeat(new ushort[] { 0, 1, 2 }, 100)
            .SelectMany(static triangle => triangle)
            .ToArray();

        var result = EdgeIndexCacheBuilder.Build(indices, 2);

        Assert.That(result, Is.EqualTo(new[] { (0, 1), (1, 2) }));
    }

    [Test]
    public void Build_DegenerateTriangle_SkipsZeroLengthEdges()
    {
        var result = EdgeIndexCacheBuilder.Build(new ushort[] { 0, 0, 1 }, 50_000);

        Assert.That(result, Is.EqualTo(new[] { (0, 1) }));
    }

    [Test]
    public void Build_EmptyInput_ReturnsEmptyArray()
    {
        var result = EdgeIndexCacheBuilder.Build(Array.Empty<ushort>(), 50_000);

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void Build_IncompleteTriangle_ThrowsArgumentException()
    {
        Assert.That(
            () => EdgeIndexCacheBuilder.Build(new ushort[] { 0, 1 }, 50_000),
            Throws.ArgumentException);
    }

    [Test]
    public void Build_NegativeLimit_ThrowsArgumentOutOfRangeException()
    {
        Assert.That(
            () => EdgeIndexCacheBuilder.Build(Array.Empty<ushort>(), -1),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Build_ZeroLimit_ReturnsEmptyArray()
    {
        var result = EdgeIndexCacheBuilder.Build(new ushort[] { 0, 1, 2 }, 0);

        Assert.That(result, Is.Empty);
    }
}
