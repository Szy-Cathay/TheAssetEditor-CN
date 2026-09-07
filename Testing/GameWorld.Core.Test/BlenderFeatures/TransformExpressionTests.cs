using GameWorld.Core.Components.Gizmo;

namespace GameWorld.Core.Test.BlenderFeatures;

public class TransformExpressionTests
{
    [TestCase("2+3*4", 14)]
    [TestCase("(2+3)*4", 20)]
    [TestCase("2**3**2", 512)]
    [TestCase("-2**2", -4)]
    [TestCase("2**-2", 0.25f)]
    [TestCase("1e-3 * 2000", 2)]
    [TestCase("sin(pi/2)*3", 3)]
    [TestCase("math.sqrt(16)+abs(-2)", 6)]
    [TestCase("degrees(pi)", 180)]
    [TestCase("max(2, min(4, 3))", 3)]
    [TestCase("-7//3", -3)]
    [TestCase("-7%3", 2)]
    [TestCase("pow(2,3)+log(100,10)", 10)]
    public void ExpressionEvaluatesWithMathPrecedence(string text, float expected)
    {
        Assert.That(TransformExpression.TryEvaluate(text, out var value), Is.True);
        Assert.That(value, Is.EqualTo(expected).Within(0.00001));
    }

    [TestCase("1/0")]
    [TestCase("sqrt(-1)")]
    [TestCase("2+")]
    [TestCase("(1+2")]
    [TestCase("1e40")]
    [TestCase("pow(2)")]
    [TestCase("2 3")]
    [TestCase("__import__('os')")]
    [TestCase("min()")]
    public void InvalidExpressionDoesNotProduceATransform(string text)
        => Assert.That(TransformExpression.TryEvaluate(text, out _), Is.False);

    [Test]
    public void InputSizeAndNestingAreBounded()
    {
        Assert.That(TransformExpression.TryEvaluate(new string('1', 513), out _), Is.False);
        Assert.That(TransformExpression.TryEvaluate(new string('(', 40) + "1" + new string(')', 40), out _), Is.False);
    }
}
