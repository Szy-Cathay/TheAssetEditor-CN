using Editors.AnimationMeta.SuperView.Visualisation;
using Microsoft.Xna.Framework;

namespace Test.AnimationMeta;

public class EffectAttachmentTransformTests
{
    [TestCase(false, 1.5f)]
    [TestCase(true, 4.5f)]
    public void ParticleScaleUsesTheActorYAxisWhileTheOffsetUsesBoneAndActorScale(bool followParent, float expectedScale)
    {
        var bone = Matrix.CreateScale(5, 7, 9) * Matrix.CreateTranslation(3, 4, 5);
        var parent = Matrix.CreateScale(2, 3, 4) * Matrix.CreateTranslation(10, 20, 30);
        var actual = EffectAttachmentTransform.Create(Quaternion.Identity, new(1, 2, 3), 1.5f, followParent, bone, parent);
        AssertBasis(actual, Vector3.Right * expectedScale, Vector3.Up * expectedScale, Vector3.Backward * expectedScale);
        AssertVector(actual.Translation, new(26, 74, 158));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BoneScaleDoesNotBecomeParticleGeometryScale(bool followParent)
    {
        var bone = Matrix.CreateScale(2, 5, 9);
        var actual = EffectAttachmentTransform.Create(Quaternion.Identity, new(1, 2, 3), 2, followParent, bone, Matrix.Identity);
        AssertBasis(actual, Vector3.Right * 2, Vector3.Up * 2, Vector3.Backward * 2);
        AssertVector(actual.Translation, new(2, 10, 27));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActorShearUsesZFirstOrthonormalizationAndKeepsTheOriginalTranslatedOffset(bool followParent)
    {
        var parent = new Matrix(2, 0, 0, 0, 1, 3, 0, 0, 2, 1, 4, 0, 11, -7, 5, 1);
        var actual = EffectAttachmentTransform.Create(Quaternion.Identity, new(1, 2, 3), 2, followParent, Matrix.Identity, parent);
        var magnification = followParent ? 2 * MathF.Sqrt(10) : 2;
        var x = new Vector3(12, -4, -5) / MathF.Sqrt(185);
        var y = new Vector3(11, 58, -20) / MathF.Sqrt(3885);
        var z = new Vector3(2, 1, 4) / MathF.Sqrt(21);
        AssertBasis(actual, x * magnification, y * magnification, z * magnification);
        AssertVector(actual.Translation, new(21, 2, 17));
    }

    [TestCase(false, 2f)]
    [TestCase(true, 6f)]
    public void MirroredActorChangesThePositionWithoutAddingReflectedParticleGeometry(bool followParent, float magnification)
    {
        var parent = Matrix.CreateScale(-2, 3, 4);
        var actual = EffectAttachmentTransform.Create(Quaternion.Identity, new(1, 2, 3), 2, followParent, Matrix.Identity, parent);
        AssertBasis(actual, Vector3.Right * magnification, Vector3.Up * magnification, Vector3.Backward * magnification);
        AssertVector(actual.Translation, new(-2, 6, 12));
        Assert.That(actual.Determinant(), Is.GreaterThan(0));
    }

    [TestCase(false, 2f)]
    [TestCase(true, 0f)]
    public void ZeroActorScaleRemainsFiniteAndKeepsItsTranslation(bool followParent, float magnification)
    {
        var parent = Matrix.CreateScale(0) * Matrix.CreateTranslation(7, -3, 2);
        var actual = EffectAttachmentTransform.Create(Quaternion.Identity, new(1, 2, 3), 2, followParent, Matrix.Identity, parent);
        AssertBasis(actual, Vector3.Right * magnification, Vector3.Up * magnification, Vector3.Backward * magnification);
        AssertVector(actual.Translation, new(7, -3, 2));
    }

    [Test]
    public void RawNonUnitQuaternionIsOrthonormalizedAfterCompositionRatherThanNormalizedBeforeIt()
    {
        var actual = EffectAttachmentTransform.Create(new(.7f, 0, 0, .7f), Vector3.Zero, 1, false, Matrix.Identity, Matrix.Identity);
        var c = 1 - 2 * .7f * .7f;
        var s = 2 * .7f * .7f;
        var length = MathF.Sqrt(c * c + s * s);
        AssertBasis(actual, Vector3.Right, new(0, c / length, s / length), new(0, -s / length, c / length));
        Assert.That(Math.Abs(actual.M22), Is.GreaterThan(.01f), "Normalizing the original quaternion would incorrectly force an exact 90 degree turn.");
    }

    [TestCase(.01f, 0f)]
    [TestCase(0f, 90f)]
    public void NonUnitScalarAndSmallAxisQuaternionsKeepTheNativeIdentityProjection(float x, float w)
    {
        var actual = EffectAttachmentTransform.Create(new(x, 0, 0, w), new(2, 3, 4), 1, false, Matrix.Identity, Matrix.Identity);
        AssertBasis(actual, Vector3.Right, Vector3.Up, Vector3.Backward);
        AssertVector(actual.Translation, new(2, 3, 4));
    }

    [TestCase(1f, 0f, 0f)]
    [TestCase(0f, 1f, 0f)]
    [TestCase(0f, 0f, 1f)]
    public void NegativeTraceRotationsKeepTheirCorrectAxis(float x, float y, float z)
    {
        var input = new Quaternion(x, y, z, 0);
        var actual = EffectAttachmentTransform.Create(input, Vector3.Zero, 1, false, Matrix.Identity, Matrix.Identity);
        var expected = Matrix.CreateFromQuaternion(input);
        AssertBasis(actual, expected.Right, expected.Up, expected.Backward);
    }

    [Test]
    public void LocalRotationAndBoneRotationAreComposedBeforeTheActorTransform()
    {
        var local = Quaternion.CreateFromAxisAngle(Vector3.Right, MathHelper.PiOver2);
        var bone = Matrix.CreateRotationY(MathHelper.PiOver2) * Matrix.CreateTranslation(1, 2, 3);
        var parent = Matrix.CreateRotationZ(MathHelper.PiOver2) * Matrix.CreateTranslation(4, 5, 6);
        var actual = EffectAttachmentTransform.Create(local, new(2, 0, 0), -2, false, bone, parent);
        AssertBasis(actual, new(0, 0, 2), new(0, -2, 0), new(-2, 0, 0));
        AssertVector(actual.Translation, new(2, 6, 7));
    }

    private static void AssertBasis(Matrix actual, Vector3 right, Vector3 up, Vector3 backward)
    {
        AssertVector(actual.Right, right);
        AssertVector(actual.Up, up);
        AssertVector(actual.Backward, backward);
    }

    private static void AssertVector(Vector3 actual, Vector3 expected) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(.00002f));
}
