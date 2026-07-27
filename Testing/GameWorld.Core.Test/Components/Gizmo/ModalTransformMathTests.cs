using GameWorld.Core.Components.Gizmo;
using Microsoft.Xna.Framework;
using TransformGizmo = GameWorld.Core.Components.Gizmo.Gizmo;

namespace Test.GameWorld.Core.Components.Gizmo
{
    [TestFixture]
    public class ModalTransformMathTests
    {
        [TestCase(GizmoAxis.YZ, 0f, 1f, 1f)]
        [TestCase(GizmoAxis.XZ, 1f, 0f, 1f)]
        [TestCase(GizmoAxis.XY, 1f, 1f, 0f)]
        public void CreateModalScaleDelta_AppliesPlaneConstraints(
            GizmoAxis axis,
            float expectedX,
            float expectedY,
            float expectedZ)
        {
            var result = TransformGizmo.CreateModalScaleDelta(2f, axis);

            Assert.That(result, Is.EqualTo(new Vector3(expectedX, expectedY, expectedZ)));
        }

        [TestCase(0f, -1f)]
        [TestCase(-2f, -3f)]
        public void CreateModalScaleDelta_PreservesZeroAndNegativeFactors(float factor, float expectedDelta)
        {
            var result = TransformGizmo.CreateModalScaleDelta(factor, GizmoAxis.None);

            Assert.That(result, Is.EqualTo(new Vector3(expectedDelta)));
        }

        [TestCase(GizmoAxis.YZ, 0f, 3f, 3f)]
        [TestCase(GizmoAxis.XZ, 3f, 0f, 3f)]
        [TestCase(GizmoAxis.XY, 3f, 3f, 0f)]
        public void CreateNumericTranslation_AppliesValueToBothPlaneAxes(
            GizmoAxis axis,
            float expectedX,
            float expectedY,
            float expectedZ)
        {
            var result = TransformGizmo.CreateNumericTranslation(
                Vector3.UnitX,
                Vector3.UnitY,
                Vector3.UnitZ,
                axis,
                3f);

            Assert.That(result, Is.EqualTo(new Vector3(expectedX, expectedY, expectedZ)));
        }
    }
}
