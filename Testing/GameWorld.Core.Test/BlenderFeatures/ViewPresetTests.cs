using GameWorld.Core.Components.Navigation;
using Microsoft.Xna.Framework;
using NUnit.Framework;
using System;

namespace GameWorld.Core.Test.BlenderFeatures
{
    /// <summary>
    /// Comprehensive tests for View Preset functionality
    /// </summary>
    [TestFixture]
    public class ViewPresetTests
    {
        private const float Epsilon = 0.001f;

        #region View Angle Tests

        [Test]
        public void ViewPresets_GetViewAngles_Front()
        {
            var (yaw, pitch) = ViewPresets.GetViewAngles(ViewPresetType.Front);

            Assert.That(yaw, Is.EqualTo(0f).Within(Epsilon), "Front view yaw should be 0");
            Assert.That(pitch, Is.EqualTo(0f).Within(Epsilon), "Front view pitch should be 0");
        }

        [Test]
        public void ViewPresets_GetViewAngles_Back()
        {
            var (yaw, pitch) = ViewPresets.GetViewAngles(ViewPresetType.Back);

            Assert.That(yaw, Is.EqualTo(MathHelper.Pi).Within(Epsilon), "Back view yaw should be π");
            Assert.That(pitch, Is.EqualTo(0f).Within(Epsilon), "Back view pitch should be 0");
        }

        [Test]
        public void ViewPresets_GetViewAngles_Right()
        {
            var (yaw, pitch) = ViewPresets.GetViewAngles(ViewPresetType.Right);

            Assert.That(yaw, Is.EqualTo(MathHelper.PiOver2).Within(Epsilon), "Right view looks from +X");
            Assert.That(pitch, Is.EqualTo(0f).Within(Epsilon), "Right view pitch should be 0");
        }

        [Test]
        public void ViewPresets_GetViewAngles_Left()
        {
            var (yaw, pitch) = ViewPresets.GetViewAngles(ViewPresetType.Left);

            Assert.That(yaw, Is.EqualTo(-MathHelper.PiOver2).Within(Epsilon), "Left view looks from -X");
            Assert.That(pitch, Is.EqualTo(0f).Within(Epsilon), "Left view pitch should be 0");
        }

        [Test]
        public void ViewPresets_GetViewAngles_Top()
        {
            var (yaw, pitch) = ViewPresets.GetViewAngles(ViewPresetType.Top);

            Assert.That(yaw, Is.EqualTo(0f).Within(Epsilon), "Top view yaw should be 0");
            Assert.That(pitch, Is.LessThan(0), "Top view pitch should be negative (looking down)");
            Assert.That(pitch, Is.GreaterThanOrEqualTo(-MathHelper.PiOver2), "Top view pitch should be >= -π/2");
        }

        [Test]
        public void ViewPresets_GetViewAngles_Bottom()
        {
            var (yaw, pitch) = ViewPresets.GetViewAngles(ViewPresetType.Bottom);

            Assert.That(yaw, Is.EqualTo(0f).Within(Epsilon), "Bottom view yaw should be 0");
            Assert.That(pitch, Is.GreaterThan(0), "Bottom view pitch should be positive (looking up)");
            Assert.That(pitch, Is.LessThanOrEqualTo(MathHelper.PiOver2), "Bottom view pitch should be <= π/2");
        }

        [Test]
        public void ViewPresets_GetViewAngles_Perspective_HasValidDefaults()
        {
            var (yaw, pitch) = ViewPresets.GetViewAngles(ViewPresetType.Perspective);

            Assert.That(float.IsNaN(yaw), Is.False, "Perspective yaw should not be NaN");
            Assert.That(float.IsNaN(pitch), Is.False, "Perspective pitch should not be NaN");
            Assert.That(yaw, Is.GreaterThan(0), "Perspective yaw should be positive for angled view");
            Assert.That(pitch, Is.GreaterThan(0), "Perspective pitch should be positive for angled view");
        }

        #endregion

        #region View Detection Tests

        [Test]
        public void ViewPresets_DetectViewPreset_Front()
        {
                var result = ViewPresets.DetectViewPreset(0f, 0f);
                Assert.That(result, Is.EqualTo(ViewPresetType.Front), "Should detect Front view");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_Back()
        {
            var result1 = ViewPresets.DetectViewPreset(MathHelper.Pi, 0f);
            var result2 = ViewPresets.DetectViewPreset(-MathHelper.Pi, 0f);

            Assert.That(result1, Is.EqualTo(ViewPresetType.Back), "Should detect Back view from π");
            Assert.That(result2, Is.EqualTo(ViewPresetType.Back), "Should detect Back view from -π");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_Right()
        {
            var result = ViewPresets.DetectViewPreset(MathHelper.PiOver2, 0f);
            Assert.That(result, Is.EqualTo(ViewPresetType.Right), "Should detect Right view");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_Left()
        {
            var result = ViewPresets.DetectViewPreset(-MathHelper.PiOver2, 0f);
            Assert.That(result, Is.EqualTo(ViewPresetType.Left), "Should detect Left view");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_Top()
        {
            var result = ViewPresets.DetectViewPreset(0f, -MathHelper.PiOver2 + 0.005f);
            Assert.That(result, Is.EqualTo(ViewPresetType.Top), "Should detect Top view");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_Bottom()
        {
            var result = ViewPresets.DetectViewPreset(0f, MathHelper.PiOver2 - 0.005f);
            Assert.That(result, Is.EqualTo(ViewPresetType.Bottom), "Should detect Bottom view");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_NonPreset_ReturnsNull()
        {
            // Random angles that don't match any preset
            var result = ViewPresets.DetectViewPreset(0.5f, 0.3f);
            Assert.That(result, Is.Null, "Should return null for non-preset angles");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_WithThreshold_WorksWithinTolerance()
        {
            // Slightly off from Front view
            var result = ViewPresets.DetectViewPreset(0.05f, 0.05f, 0.1f);
            Assert.That(result, Is.EqualTo(ViewPresetType.Front), "Should detect Front view within threshold");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_WithThreshold_FailsOutsideTolerance()
        {
            // Too far from any preset
            var result = ViewPresets.DetectViewPreset(0.5f, 0.5f, 0.1f);
            Assert.That(result, Is.Null, "Should return null outside threshold");
        }

        #endregion

        #region Axis to View Mapping Tests

        [Test]
        public void ViewPresets_AxisToViewPreset_PosX_IsRight()
        {
            var result = ViewPresets.AxisToViewPreset(NavigationAxis.PosX);
            Assert.That(result, Is.EqualTo(ViewPresetType.Right), "+X should map to Right view");
        }

        [Test]
        public void ViewPresets_AxisToViewPreset_NegX_IsLeft()
        {
            var result = ViewPresets.AxisToViewPreset(NavigationAxis.NegX);
            Assert.That(result, Is.EqualTo(ViewPresetType.Left), "-X should map to Left view");
        }

        [Test]
        public void ViewPresets_AxisToViewPreset_PosY_IsTop()
        {
            var result = ViewPresets.AxisToViewPreset(NavigationAxis.PosY);
            Assert.That(result, Is.EqualTo(ViewPresetType.Top), "+Y should map to Top view");
        }

        [Test]
        public void ViewPresets_AxisToViewPreset_NegY_IsBottom()
        {
            var result = ViewPresets.AxisToViewPreset(NavigationAxis.NegY);
            Assert.That(result, Is.EqualTo(ViewPresetType.Bottom), "-Y should map to Bottom view");
        }

        [Test]
        public void ViewPresets_AxisToViewPreset_PosZ_IsFront()
        {
            var result = ViewPresets.AxisToViewPreset(NavigationAxis.PosZ);
            Assert.That(result, Is.EqualTo(ViewPresetType.Front), "+Z should map to Front view");
        }

        [Test]
        public void ViewPresets_AxisToViewPreset_NegZ_IsBack()
        {
            var result = ViewPresets.AxisToViewPreset(NavigationAxis.NegZ);
            Assert.That(result, Is.EqualTo(ViewPresetType.Back), "-Z should map to Back view");
        }

        [Test]
        public void ViewPresets_AxisToViewPreset_None_IsPerspective()
        {
            var result = ViewPresets.AxisToViewPreset(NavigationAxis.None);
            Assert.That(result, Is.EqualTo(ViewPresetType.Perspective), "None should map to Perspective");
        }

        #endregion

        #region Stress Tests

        [Test]
        public void ViewPresets_DetectViewPreset_AllAngles_NoCrash()
        {
            // Test detection across all possible angles
            int steps = 100;

            for (int yawStep = 0; yawStep < steps; yawStep++)
            {
                for (int pitchStep = 0; pitchStep < steps; pitchStep++)
                {
                    float yaw = MathHelper.Lerp(-MathHelper.Pi, MathHelper.Pi, yawStep / (float)steps);
                    float pitch = MathHelper.Lerp(-MathHelper.PiOver2, MathHelper.PiOver2, pitchStep / (float)steps);

                    // Should not throw
                    var result = ViewPresets.DetectViewPreset(yaw, pitch);

                    // Result should be valid
                    if (result.HasValue)
                    {
                        Assert.That(Enum.IsDefined(typeof(ViewPresetType), result.Value), Is.True,
                            $"Result should be valid enum value for yaw={yaw}, pitch={pitch}");
                    }
                }
            }
        }

        [Test]
        public void ViewPresets_GetViewAngles_AllTypes_NoNaN()
        {
            foreach (ViewPresetType type in Enum.GetValues<ViewPresetType>())
            {
                var (yaw, pitch) = ViewPresets.GetViewAngles(type);

                Assert.That(float.IsNaN(yaw), Is.False, $"{type} yaw should not be NaN");
                Assert.That(float.IsNaN(pitch), Is.False, $"{type} pitch should not be NaN");
                Assert.That(float.IsInfinity(yaw), Is.False, $"{type} yaw should not be Infinity");
                Assert.That(float.IsInfinity(pitch), Is.False, $"{type} pitch should not be Infinity");
            }
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void ViewPresets_DetectViewPreset_BoundaryAngles()
        {
            // Test at exact boundary values
            var testCases = new (float, float, ViewPresetType)[]
            {
                (0f, 0f, ViewPresetType.Front),
                (MathHelper.Pi, 0f, ViewPresetType.Back),
                (-MathHelper.Pi, 0f, ViewPresetType.Back),
                (MathHelper.PiOver2, 0f, ViewPresetType.Right),
                (-MathHelper.PiOver2, 0f, ViewPresetType.Left),
            };

            foreach (var (yaw, pitch, expected) in testCases)
            {
                var result = ViewPresets.DetectViewPreset(yaw, pitch);
                Assert.That(result, Is.EqualTo(expected), $"Yaw={yaw}, Pitch={pitch} should detect {expected}");
            }
        }

        [Test]
        public void ViewPresets_WrapAngle_Behavior()
        {
            // Test that wrapped angles are handled correctly
            float[] testAngles = {
                0f,
                MathHelper.Pi,
                -MathHelper.Pi,
                2 * MathHelper.Pi,
                -2 * MathHelper.Pi,
                100 * MathHelper.Pi,
                -100 * MathHelper.Pi,
            };

            foreach (var angle in testAngles)
            {
                float wrapped = MathHelper.WrapAngle(angle);

                Assert.That(wrapped, Is.GreaterThanOrEqualTo(-MathHelper.Pi), $"Wrapped angle should be >= -π for {angle}");
                Assert.That(wrapped, Is.LessThanOrEqualTo(MathHelper.Pi), $"Wrapped angle should be <= π for {angle}");
                Assert.That(float.IsNaN(wrapped), Is.False, $"Wrapped angle should not be NaN for {angle}");
            }
        }

        #endregion

        #region Navigation Axis Tests

        [Test]
        public void NavigationAxis_LegacyCompatibility_XEqualsPosX()
        {
            Assert.That((int)NavigationAxis.X, Is.EqualTo((int)NavigationAxis.PosX), "Legacy X should equal PosX");
        }

        [Test]
        public void NavigationAxis_LegacyCompatibility_YEqualsPosY()
        {
            Assert.That((int)NavigationAxis.Y, Is.EqualTo((int)NavigationAxis.PosY), "Legacy Y should equal PosY");
        }

        [Test]
        public void NavigationAxis_LegacyCompatibility_ZEqualsPosZ()
        {
            Assert.That((int)NavigationAxis.Z, Is.EqualTo((int)NavigationAxis.PosZ), "Legacy Z should equal PosZ");
        }

        [Test]
        public void NavigationAxis_AllAxes_DistinctValues()
        {
            var axes = new[]
            {
                NavigationAxis.None,
                NavigationAxis.PosX,
                NavigationAxis.NegX,
                NavigationAxis.PosY,
                NavigationAxis.NegY,
                NavigationAxis.PosZ,
                NavigationAxis.NegZ,
            };

            // Verify all have distinct values
            var values = new HashSet<int>();
            foreach (var axis in axes)
            {
                Assert.That(values.Contains((int)axis), Is.False, $"{axis} should have unique value");
                values.Add((int)axis);
            }
        }

        #endregion
    }
}
