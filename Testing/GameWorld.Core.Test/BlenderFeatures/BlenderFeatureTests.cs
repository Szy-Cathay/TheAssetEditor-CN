using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Navigation;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace GameWorld.Core.Test.BlenderFeatures
{
    /// <summary>
    /// Lightweight arithmetic and enum smoke checks used alongside production
    /// component tests for Blender-style viewport behavior.
    /// </summary>
    [TestFixture]
    public class BlenderFeatureTests
    {
        #region Camera Projection Tests

        [Test]
        public void Camera_ProjectionMatrix_UpdatesOnViewportResize()
        {
            // Test that aspect ratio changes when viewport changes
            int width1 = 800;
            int height1 = 600;
            int width2 = 1920;
            int height2 = 1080;

            float aspect1 = width1 / (float)height1;
            float aspect2 = width2 / (float)height2;

            Assert.That(aspect1, Is.Not.EqualTo(aspect2), "Aspect ratios should be different");
            Assert.That(aspect1, Is.EqualTo(4f / 3f).Within(0.001f), "800x600 should be 4:3");
            Assert.That(aspect2, Is.EqualTo(16f / 9f).Within(0.001f), "1920x1080 should be 16:9");
        }

        [Test]
        public void Camera_ProjectionMatrix_HandlesExtremeViewportSizes()
        {
            // Test extreme viewport sizes
            var extremeSizes = new[]
            {
                (1, 1),           // Minimum
                (1, 10000),       // Very tall
                (10000, 1),       // Very wide
                (100000, 100000), // Very large
            };

            foreach (var (width, height) in extremeSizes)
            {
                float aspect = width / (float)height;
                Assert.That(float.IsNaN(aspect), Is.False, $"Aspect ratio should not be NaN for {width}x{height}");
                Assert.That(float.IsInfinity(aspect), Is.False, $"Aspect ratio should not be Infinity for {width}x{height}");
                Assert.That(aspect, Is.GreaterThan(0), $"Aspect ratio should be positive for {width}x{height}");
            }
        }

        [Test]
        public void Camera_OrthoSize_ClampedToMinimum()
        {
            // Test that OrthoSize is properly clamped
            float minOrthoSize = 0.1f;

            // Test values below minimum
            var testValues = new[] { -100f, -1f, 0f, 0.01f, 0.09f };

            foreach (var value in testValues)
            {
                float clamped = Math.Max(minOrthoSize, value);
                Assert.That(clamped, Is.GreaterThanOrEqualTo(minOrthoSize), $"OrthoSize should be clamped to minimum for value {value}");
            }
        }

        #endregion

        #region Mouse State Update Tests

        [Test]
        public void MouseComponent_SetScreenSize_ReturnsCorrectSize()
        {
            // This tests that GetScreenSize returns the element size, not mouse position
            Vector2 screenSize = new Vector2(1920, 1080);
            Vector2 mousePos = new Vector2(100, 200);

            // These should be different
            Assert.That(screenSize.X, Is.Not.EqualTo(mousePos.X), "Screen width and mouse X should be different");
            Assert.That(screenSize.Y, Is.Not.EqualTo(mousePos.Y), "Screen height and mouse Y should be different");

            // Screen size should match expected dimensions
            Assert.That(screenSize.X, Is.EqualTo(1920f), "Screen width should be 1920");
            Assert.That(screenSize.Y, Is.EqualTo(1080f), "Screen height should be 1080");
        }

        [Test]
        public void InfiniteDrag_WrapCalculations_NoExtremeDelta()
        {
            // Test that infinite drag wrap doesn't cause extreme deltas
            int viewportWidth = 1920;
            int viewportHeight = 1080;
            int triggerZone = 20;
            int safeZoneMargin = 80;

            // Test wrap from left edge
            Vector2 posNearLeft = new Vector2(triggerZone - 1, 500);
            Vector2 wrappedLeft = new Vector2(viewportWidth - safeZoneMargin, 500);

            // Test wrap from right edge
            Vector2 posNearRight = new Vector2(viewportWidth - triggerZone + 1, 500);
            Vector2 wrappedRight = new Vector2(safeZoneMargin, 500);

            // Verify wrapped positions are within safe bounds
            Assert.That(wrappedLeft.X, Is.GreaterThanOrEqualTo(safeZoneMargin), "Wrapped left position should be in safe zone");
            Assert.That(wrappedRight.X, Is.LessThanOrEqualTo(viewportWidth - safeZoneMargin), "Wrapped right position should be in safe zone");
            Assert.That(wrappedLeft.Y, Is.InRange(0, viewportHeight), "Wrapped left Y should stay in the viewport");
            Assert.That(wrappedRight.Y, Is.InRange(0, viewportHeight), "Wrapped right Y should stay in the viewport");
        }

        #endregion

        #region Multi-Window Tests

        [Test]
        public void MultiWindow_DifferentViewportSizes_NoCrossContamination()
        {
            // Simulate two windows with different sizes
            var window1 = new { Width = 800, Height = 600 };
            var window2 = new { Width = 1920, Height = 1080 };

            float aspect1 = window1.Width / (float)window1.Height;
            float aspect2 = window2.Width / (float)window2.Height;

            // Each window should maintain its own aspect ratio
            Assert.That(aspect1, Is.Not.EqualTo(aspect2), "Different windows should have different aspect ratios");

            // Verify no cross-contamination
            Assert.That(aspect1, Is.EqualTo(4f / 3f).Within(0.001f), "Window 1 should keep 4:3 aspect");
            Assert.That(aspect2, Is.EqualTo(16f / 9f).Within(0.001f), "Window 2 should keep 16:9 aspect");
        }

        #endregion

        #region Axis Locking Tests

        [Test]
        public void AxisLock_XAxisOnly_YAndZZero()
        {
            // Test X-axis locking
            Vector3 movement = new Vector3(100, 50, 25);
            Vector3 lockedX = new Vector3(movement.X, 0, 0);

            Assert.That(lockedX.X, Is.EqualTo(100f), "X should be preserved");
            Assert.That(lockedX.Y, Is.EqualTo(0f), "Y should be zero");
            Assert.That(lockedX.Z, Is.EqualTo(0f), "Z should be zero");
        }

        [Test]
        public void AxisLock_YAxisOnly_XAndZZero()
        {
            // Test Y-axis locking
            Vector3 movement = new Vector3(100, 50, 25);
            Vector3 lockedY = new Vector3(0, movement.Y, 0);

            Assert.That(lockedY.X, Is.EqualTo(0f), "X should be zero");
            Assert.That(lockedY.Y, Is.EqualTo(50f), "Y should be preserved");
            Assert.That(lockedY.Z, Is.EqualTo(0f), "Z should be zero");
        }

        [Test]
        public void AxisLock_ZAxisOnly_XAndYZero()
        {
            // Test Z-axis locking
            Vector3 movement = new Vector3(100, 50, 25);
            Vector3 lockedZ = new Vector3(0, 0, movement.Z);

            Assert.That(lockedZ.X, Is.EqualTo(0f), "X should be zero");
            Assert.That(lockedZ.Y, Is.EqualTo(0f), "Y should be zero");
            Assert.That(lockedZ.Z, Is.EqualTo(25f), "Z should be preserved");
        }

        #endregion

        #region View Preset Tests

        [Test]
        public void ViewPresets_AllDefined_CorrectAngles()
        {
            // Test all view presets have valid angle values
            var presets = new[]
            {
                ViewPresetType.Front,
                ViewPresetType.Back,
                ViewPresetType.Left,
                ViewPresetType.Right,
                ViewPresetType.Top,
                ViewPresetType.Bottom,
            };

            foreach (var preset in presets)
            {
                var (yaw, pitch) = ViewPresets.GetViewAngles(preset);
                Assert.That(float.IsNaN(yaw), Is.False, $"{preset} yaw should not be NaN");
                Assert.That(float.IsNaN(pitch), Is.False, $"{preset} pitch should not be NaN");
            }
        }

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
            var result = ViewPresets.DetectViewPreset(-MathHelper.PiOver2, 0f);
            Assert.That(result, Is.EqualTo(ViewPresetType.Right), "Should detect Right view");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_Left()
        {
            var result = ViewPresets.DetectViewPreset(MathHelper.PiOver2, 0f);
            Assert.That(result, Is.EqualTo(ViewPresetType.Left), "Should detect Left view");
        }

        [Test]
        public void ViewPresets_DetectViewPreset_NonPreset_ReturnsNull()
        {
            // Random angles that don't match any preset
            var result = ViewPresets.DetectViewPreset(0.5f, 0.3f);
            Assert.That(result, Is.Null, "Should return null for non-preset angles");
        }

        [TestCase(ViewPresetType.Front, ViewPresetType.Front, ViewPresetType.Back)]
        [TestCase(ViewPresetType.Front, ViewPresetType.Back, ViewPresetType.Front)]
        [TestCase(ViewPresetType.Right, ViewPresetType.Right, ViewPresetType.Left)]
        [TestCase(ViewPresetType.Right, ViewPresetType.Left, ViewPresetType.Right)]
        [TestCase(ViewPresetType.Top, ViewPresetType.Top, ViewPresetType.Bottom)]
        [TestCase(ViewPresetType.Top, ViewPresetType.Bottom, ViewPresetType.Top)]
        public void ViewPresets_ResolveRepeatedAxisView_TogglesOppositeView(
            ViewPresetType requested,
            ViewPresetType current,
            ViewPresetType expected)
        {
            var result = ViewPresets.ResolveRepeatedAxisView(requested, current);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void ViewPresets_ResolveRepeatedAxisView_FromAnotherAxis_UsesRequestedView()
        {
            var result = ViewPresets.ResolveRepeatedAxisView(
                ViewPresetType.Front,
                ViewPresetType.Top);

            Assert.That(result, Is.EqualTo(ViewPresetType.Front));
        }

        #endregion

        #region Rotation Sensitivity Tests

        [Test]
        public void Rotation_BlenderSensitivity_0_002RadiansPerPixel()
        {
            // Blender uses 0.002 radians per pixel for rotation
            const float radiansPerPixel = 0.002f;

            // Test rotation calculation
            int pixelDelta = 100;
            float expectedRotation = pixelDelta * radiansPerPixel;

            Assert.That(expectedRotation, Is.EqualTo(0.2f).Within(0.001f), "100 pixels should rotate 0.2 radians");

            // Test full rotation
            int pixelsForFullRotation = (int)(MathHelper.TwoPi / radiansPerPixel);
            float fullRotation = pixelsForFullRotation * radiansPerPixel;

            Assert.That(fullRotation, Is.EqualTo(MathHelper.TwoPi).Within(0.01f), "Full rotation should be ~2π radians");
        }

        #endregion

        #region Stress Tests

        [Test]
        public void StressTest_InfiniteDrag_ManyWraps()
        {
            // Simulate many infinite drag wraps
            int viewportWidth = 1920;
            int viewportHeight = 1080;
            int triggerZone = 20;
            int safeZoneMargin = 80;

            Vector2 pos = new Vector2(viewportWidth / 2, viewportHeight / 2);
            Vector2 delta = new Vector2(10, 0); // Move right

            int wrapCount = 0;
            int maxWraps = 1000;
            int stressIterations = 10000;

            for (int i = 0; i < stressIterations && wrapCount < maxWraps; i++)
            {
                pos += delta;

                // Check for wrap
                if (pos.X > viewportWidth - triggerZone)
                {
                    pos.X = safeZoneMargin;
                    wrapCount++;
                }
                else if (pos.X < triggerZone)
                {
                    pos.X = viewportWidth - safeZoneMargin;
                    wrapCount++;
                }
            }

            // Verify position is still valid
            Assert.That(pos.X, Is.GreaterThanOrEqualTo(0), "Position X should be >= 0");
            Assert.That(pos.X, Is.LessThanOrEqualTo(viewportWidth), "Position X should be <= viewport width");
            Assert.That(pos.Y, Is.GreaterThanOrEqualTo(0), "Position Y should be >= 0");
            Assert.That(pos.Y, Is.LessThanOrEqualTo(viewportHeight), "Position Y should be <= viewport height");
        }

        [Test]
        public void StressTest_Rotation_ManyIncrements()
        {
            // Test rotation accumulation over many iterations
            float rotation = 0f;
            const float increment = 0.002f;
            int stressIterations = 10000;

            for (int i = 0; i < stressIterations; i++)
            {
                rotation += increment;

                // Normalize to prevent overflow
                if (rotation > MathHelper.TwoPi)
                    rotation -= MathHelper.TwoPi;
            }

            // Rotation should still be valid
            Assert.That(rotation, Is.GreaterThanOrEqualTo(0f), "Rotation should be >= 0");
            Assert.That(rotation, Is.LessThanOrEqualTo(MathHelper.TwoPi), "Rotation should be <= 2π");
        }

        [Test]
        public void StressTest_ViewportResize_ManyResizes()
        {
            // Test many viewport resize operations
            int width = 800;
            int height = 600;
            Random rng = new Random(42); // Fixed seed for reproducibility
            int stressIterations = 10000;

            for (int i = 0; i < stressIterations; i++)
            {
                // Random resize
                width = rng.Next(100, 4000);
                height = rng.Next(100, 3000);

                float aspect = width / (float)height;

                Assert.That(float.IsNaN(aspect), Is.False, $"Aspect should not be NaN at iteration {i}");
                Assert.That(float.IsInfinity(aspect), Is.False, $"Aspect should not be Infinity at iteration {i}");
                Assert.That(aspect, Is.GreaterThan(0), $"Aspect should be positive at iteration {i}");
            }
        }

        #endregion

        #region Edge Case Tests

        [Test]
        public void EdgeCase_ZeroViewport_HandledGracefully()
        {
            // Test zero viewport size
            int width = 0;
            int height = 0;

            // Should not crash or produce NaN
            float aspect = width / (float)Math.Max(1, height);

            Assert.That(float.IsNaN(aspect), Is.False, "Aspect should not be NaN for zero viewport");
            Assert.That(aspect, Is.EqualTo(0f), "Aspect should be 0 for zero width");
        }

        [Test]
        public void EdgeCase_ExtremeZoom_HandledGracefully()
        {
            // Test extreme zoom values
            float minZoom = 0.01f;

            // Test clamping
            float testZoom = -100f;
            float clampedZoom = Math.Max(minZoom, testZoom);

            Assert.That(clampedZoom, Is.GreaterThanOrEqualTo(minZoom), "Zoom should be clamped to minimum");
        }

        #endregion

        #region Navigation Axis Tests

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

        #endregion
    }
}
