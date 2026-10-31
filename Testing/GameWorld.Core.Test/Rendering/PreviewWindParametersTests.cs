using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Rendering;

public class PreviewWindParametersTests
{
    [Test]
    public void PreviewWind_IsScopedAndReachesCommonShaderParameters()
    {
        var first = new SceneRenderParametersStore { WindVector = new Vector2(.12f, -.07f) };
        var other = new SceneRenderParametersStore();
        using var camera = new ArcBallCamera(null!, null!, null!) { ProjectionMatrixOverride = Matrix.Identity };

        var parameters = CommonShaderParameterBuilder.Build(camera, first);

        Assert.Multiple(() =>
        {
            Assert.That(parameters.WindVector, Is.EqualTo(first.WindVector));
            Assert.That(other.WindVector, Is.EqualTo(Vector2.Zero));
            Assert.That(first.GameWeather, Is.Null);
        });
    }

    [TestCase(float.NaN, 1)]
    [TestCase(1, float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity, 0)]
    public void InvalidWind_DoesNotReplaceTheLastFiniteInput(float x, float z)
    {
        var parameters = new SceneRenderParametersStore { WindVector = new Vector2(.1f, .2f) };
        parameters.WindVector = new Vector2(x, z);
        Assert.That(parameters.WindVector, Is.EqualTo(new Vector2(.1f, .2f)));
    }
}
