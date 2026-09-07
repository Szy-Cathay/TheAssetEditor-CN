using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace GameWorld.Core.Components.Navigation
{
    /// <summary>
    /// Navigation axis gizmo (Blender-style viewport corner axis indicator)
    /// Reference: Blender source/blender/editors/space_view3d/view3d_gizmo_navigate_type.cc
    /// Shows 6 axis endpoints: +X, -X, +Y, -Y, +Z, -Z
    /// </summary>
    public class NavigationGizmo : IDisposable
    {
        private readonly GraphicsDevice _graphics;
        private readonly ArcBallCamera _camera;
        private readonly IMouseComponent _mouse;
        private readonly RenderEngineComponent _renderEngine;

        // Gizmo size and position
        private const float GIZMO_SIZE = 70f;           // Display size (pixels)
        private const float GIZMO_MARGIN = 20f;         // Margin from edge
        private const float AXIS_LENGTH = 1.0f;         // Axis length in local space
#pragma warning disable CA1823 // Unused field - kept for documentation/future use
        private const float AXIS_HANDLE_SIZE = 0.20f;   // Axis endpoint size ratio (Blender: 0.20)
#pragma warning restore CA1823
        private const float HIT_RADIUS = 18f;           // Click detection radius (pixels)
        private const float LINE_THICKNESS = 2f;        // Axis line thickness
        private const float CIRCLE_RADIUS = 8f;         // Label circle radius
        private const float CENTER_RADIUS = 6f;         // Center indicator radius
        private const int CIRCLE_TEXTURE_SIZE = 64;
        private const int LINE_TEXTURE_HEIGHT = 8;
        private const float OVERLAY_FONT_SCALE = 0.5f;

        // Colors (Blender style)
        private static readonly Color ColorX = new Color(220, 60, 60);    // Red
        private static readonly Color ColorY = new Color(60, 220, 60);    // Green
        private static readonly Color ColorZ = new Color(60, 100, 220);   // Blue
        private static readonly Color ColorHighlight = new Color(255, 200, 50); // Gold
        private static readonly Color ColorOutline = new Color(40, 40, 40); // Dark outline
        private static readonly Color ColorCenter = new Color(80, 80, 80); // Center circle

        // Rendering
        private Texture2D _circleTexture;
        private Texture2D _lineTexture;

        // State
        private NavigationAxis _hoveredAxis = NavigationAxis.None;
        private Vector2 _screenPosition;

        // Axis data for rendering
        private struct AxisDrawData
        {
            public NavigationAxis Axis;
            public float Depth;
            public Vector2 ScreenPos;
            public bool IsPositive;
            public int AxisIndex; // 0=X, 1=Y, 2=Z
        }

        public NavigationAxis HoveredAxis => _hoveredAxis;

        public event Action<ViewPresetType> ViewPresetRequested;

        public NavigationGizmo(GraphicsDevice graphics, ArcBallCamera camera,
            IMouseComponent mouse, RenderEngineComponent renderEngine)
        {
            _graphics = graphics;
            _camera = camera;
            _mouse = mouse;
            _renderEngine = renderEngine;

            Initialize();
        }

        private void Initialize()
        {
            _circleTexture = CreateCircleTexture(
                _graphics,
                CIRCLE_TEXTURE_SIZE);
            _lineTexture = CreateLineTexture(
                _graphics,
                LINE_TEXTURE_HEIGHT);
        }

        /// <summary>
        /// Update gizmo state (detect mouse hover)
        /// </summary>
        public void Update(GameTime gameTime)
        {
            // Calculate screen position (top-right corner)
            _screenPosition = new Vector2(
                _graphics.Viewport.Width - GIZMO_MARGIN - GIZMO_SIZE / 2,
                GIZMO_MARGIN + GIZMO_SIZE / 2
            );

            // Detect mouse hover
            _hoveredAxis = HitTestAxis(_mouse.Position());
        }

        /// <summary>
        /// Hit test: check if mouse is near any of the 6 axis endpoints
        /// </summary>
        private NavigationAxis HitTestAxis(Vector2 mousePos)
        {
            var inputSize = _mouse.GetScreenSize();
            if (inputSize.X > 0 && inputSize.Y > 0)
                mousePos *= new Vector2(_graphics.Viewport.Width / inputSize.X, _graphics.Viewport.Height / inputSize.Y);
            var axisEndpoints = GetAllAxisScreenPositions();

            float minDist = float.MaxValue;
            NavigationAxis closestAxis = NavigationAxis.None;

            foreach (var data in axisEndpoints)
            {
                // When aligned, both ends overlap; clicking the current axis selects its reverse.
                var projectedAxis = (data.ScreenPos - _screenPosition) / (GIZMO_SIZE / 2);
                if (projectedAxis.LengthSquared() < 1e-6f && data.Depth > 0)
                    continue;

                float dist = Vector2.Distance(mousePos, data.ScreenPos);
                if (dist < HIT_RADIUS && dist < minDist)
                {
                    minDist = dist;
                    closestAxis = data.Axis;
                }
            }

            return closestAxis;
        }

        /// <summary>
        /// Get screen positions and data for all 6 axis endpoints
        /// </summary>
        private List<AxisDrawData> GetAllAxisScreenPositions()
        {
            var result = new List<AxisDrawData>();
            // Use the view matrix directly (like Blender's rv3d->viewmat).
            // The view matrix rotation transforms world axes into camera/screen space,
            // giving the correct screen-space projection of world X/Y/Z directions.
            var viewMatrix = _camera.ViewMatrix;
            float scale = GIZMO_SIZE / (AXIS_LENGTH * 2);

            // 6 axes: +X, -X, +Y, -Y, +Z, -Z
            var axes = new[] { NavigationAxis.PosX, NavigationAxis.NegX,
                              NavigationAxis.PosY, NavigationAxis.NegY,
                              NavigationAxis.PosZ, NavigationAxis.NegZ };

            foreach (var axis in axes)
            {
                int axisIndex = ((int)axis - 1) / 2;  // 0=X, 1=Y, 2=Z
                bool isPositive = ((int)axis - 1) % 2 == 0;

                // Get base axis direction
                var baseDir = axisIndex switch
                {
                    0 => Vector3.UnitX,
                    1 => Vector3.UnitY,
                    2 => Vector3.UnitZ,
                    _ => Vector3.Zero
                };

                // Apply sign and transform to camera space using view matrix
                var axisEnd = baseDir * (isPositive ? AXIS_LENGTH : -AXIS_LENGTH);
                var rotatedAxis = Vector3.TransformNormal(axisEnd, viewMatrix);

                // Calculate screen position (negate X to match the projection matrix's CreateScale(-1,1,1) flip)
                var screenPos = _screenPosition + new Vector2(-rotatedAxis.X, -rotatedAxis.Y) * scale;

                result.Add(new AxisDrawData
                {
                    Axis = axis,
                    Depth = rotatedAxis.Z,
                    ScreenPos = screenPos,
                    IsPositive = isPositive,
                    AxisIndex = axisIndex
                });
            }

            return result;
        }

        /// <summary>
        /// Handle mouse click
        /// </summary>
        public bool HandleClick(Vector2 mousePos)
        {
            var hitAxis = HitTestAxis(mousePos);
            if (hitAxis != NavigationAxis.None)
            {
                var viewPreset = ViewPresets.AxisToViewPreset(hitAxis);
                ViewPresetRequested?.Invoke(viewPreset);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Draw navigation gizmo
        /// </summary>
        public void Draw()
        {
            // Save current render state
            var oldDepthStencil = _graphics.DepthStencilState;
            var oldRasterizer = _graphics.RasterizerState;
            var oldBlend = _graphics.BlendState;

            try
            {
                // Set render state for 2D overlay
                _graphics.DepthStencilState = DepthStencilState.None;
                _graphics.RasterizerState = RasterizerState.CullNone;
                _graphics.BlendState = BlendState.AlphaBlend;

                // Begin SpriteBatch for all drawing
                _renderEngine.CommonSpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);

                // Get all axis data and sort by depth
                var axisDataList = GetAllAxisScreenPositions();
                axisDataList.Sort((a, b) => a.Depth.CompareTo(b.Depth));

                // Draw axes (lines first, then endpoints)
                DrawAxesLines(axisDataList);
                DrawAxisEndpoints(axisDataList);

                // Draw center indicator
                DrawCenterIndicator();
            }
            finally
            {
                // Always end the sprite batch
                _renderEngine.CommonSpriteBatch.End();

                // Restore render state
                _graphics.DepthStencilState = oldDepthStencil;
                _graphics.RasterizerState = oldRasterizer;
                _graphics.BlendState = oldBlend;
            }
        }

        /// <summary>
        /// Draw axis lines from center to endpoints
        /// </summary>
        private void DrawAxesLines(List<AxisDrawData> axisDataList)
        {
            // Use view matrix for correct world-to-screen axis projection
            var viewMatrix = _camera.ViewMatrix;
            float scale = GIZMO_SIZE / (AXIS_LENGTH * 2);

            // Draw lines for each axis pair (+/-)
            for (int axisIndex = 0; axisIndex < 3; axisIndex++)
            {
                var baseDir = axisIndex switch
                {
                    0 => Vector3.UnitX,
                    1 => Vector3.UnitY,
                    2 => Vector3.UnitZ,
                    _ => Vector3.Zero
                };

                // Get color for this axis
                var axisColor = axisIndex switch
                {
                    0 => ColorX,
                    1 => ColorY,
                    2 => ColorZ,
                    _ => Color.White
                };

                // Check if either positive or negative is hovered
                bool isHovered = (_hoveredAxis == (NavigationAxis)(axisIndex * 2 + 1)) ||
                                 (_hoveredAxis == (NavigationAxis)(axisIndex * 2 + 2));
                if (isHovered)
                    axisColor = ColorHighlight;

                // Draw positive line (from center to +endpoint)
                var posEnd = baseDir * AXIS_LENGTH;
                var rotatedPos = Vector3.TransformNormal(posEnd, viewMatrix);
                var posScreen = _screenPosition + new Vector2(-rotatedPos.X, -rotatedPos.Y) * scale;
                DrawThickLine(_screenPosition, posScreen, axisColor * 0.8f, LINE_THICKNESS);

                // Draw negative line (from center to -endpoint)
                var negEnd = -baseDir * AXIS_LENGTH;
                var rotatedNeg = Vector3.TransformNormal(negEnd, viewMatrix);
                var negScreen = _screenPosition + new Vector2(-rotatedNeg.X, -rotatedNeg.Y) * scale;
                DrawThickLine(_screenPosition, negScreen, axisColor * 0.6f, LINE_THICKNESS);
            }
        }

        /// <summary>
        /// Draw axis endpoint circles (6 endpoints)
        /// </summary>
        private void DrawAxisEndpoints(List<AxisDrawData> axisDataList)
        {
            foreach (var data in axisDataList)
            {
                bool isHovered = (_hoveredAxis == data.Axis);
                bool isFront = data.Depth <= 0;

                // Get base color
                var baseColor = data.AxisIndex switch
                {
                    0 => ColorX,
                    1 => ColorY,
                    2 => ColorZ,
                    _ => Color.White
                };

                // Determine final color
                Color circleColor;
                if (isHovered)
                {
                    circleColor = ColorHighlight;
                }
                else if (data.IsPositive)
                {
                    // Positive axis: full color
                    circleColor = baseColor * (isFront ? 1.0f : 0.7f);
                }
                else
                {
                    // Negative axis: dimmer, blended with background for back-facing
                    if (isFront)
                    {
                        // Front-facing negative: blend with white
                        circleColor = new Color(
                            (int)(baseColor.R * 0.5f + 127),
                            (int)(baseColor.G * 0.5f + 127),
                            (int)(baseColor.B * 0.5f + 127),
                            220
                        );
                    }
                    else
                    {
                        // Back-facing negative: very dim
                        circleColor = baseColor * 0.4f;
                    }
                }

                // Draw outline behind the circle background.
                DrawCircleOutline(data.ScreenPos, CIRCLE_RADIUS, ColorOutline * 0.8f, 1f);
                DrawFilledCircle(data.ScreenPos, CIRCLE_RADIUS, circleColor);

                // Draw label only for positive axes
                if (data.IsPositive)
                {
                    DrawAxisLabel(data.AxisIndex, data.ScreenPos, isHovered);
                }
            }
        }

        /// <summary>
        /// Draw axis label (X, Y, Z) - only for positive axes
        /// </summary>
        private void DrawAxisLabel(int axisIndex, Vector2 position, bool isHovered)
        {
            string label = axisIndex switch
            {
                0 => "X",
                1 => "Y",
                2 => "Z",
                _ => ""
            };

            var font = _renderEngine.ViewportOverlayFont;
            var textSize = font.MeasureString(label) * OVERLAY_FONT_SCALE;
            var textPos = position - textSize / 2;

            _renderEngine.CommonSpriteBatch.DrawString(
                font,
                label,
                textPos + Vector2.UnitY,
                ColorOutline,
                0,
                Vector2.Zero,
                OVERLAY_FONT_SCALE,
                SpriteEffects.None,
                0);
            _renderEngine.CommonSpriteBatch.DrawString(
                font,
                label,
                textPos,
                Color.White,
                0,
                Vector2.Zero,
                OVERLAY_FONT_SCALE,
                SpriteEffects.None,
                0);
        }

        /// <summary>
        /// Draw center indicator (Blender style: shows projection mode)
        /// </summary>
        private void DrawCenterIndicator()
        {
            DrawCircleOutline(_screenPosition, CENTER_RADIUS, ColorOutline * 0.7f, 1f);
            DrawFilledCircle(_screenPosition, CENTER_RADIUS, ColorCenter * 0.9f);

            // Draw projection mode indicator
            bool isPerspective = _camera.CurrentProjectionType == ProjectionType.Perspective;

            if (isPerspective)
            {
                // Perspective: draw a small filled circle
                DrawFilledCircle(_screenPosition, CENTER_RADIUS * 0.4f, Color.White * 0.8f);
            }
            else
            {
                // Ortho: draw a small outline square
                float size = CENTER_RADIUS * 0.5f;
                var p1 = _screenPosition + new Vector2(-size, -size);
                var p2 = _screenPosition + new Vector2(size, -size);
                var p3 = _screenPosition + new Vector2(size, size);
                var p4 = _screenPosition + new Vector2(-size, size);
                DrawThickLine(p1, p2, Color.White * 0.8f, 1.5f);
                DrawThickLine(p2, p3, Color.White * 0.8f, 1.5f);
                DrawThickLine(p3, p4, Color.White * 0.8f, 1.5f);
                DrawThickLine(p4, p1, Color.White * 0.8f, 1.5f);
            }

        }

        private void DrawThickLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 delta = end - start;
            float length = delta.Length();
            if (length < 0.001f) return; // Skip zero-length lines

            float angle = (float)Math.Atan2(delta.Y, delta.X);

            var scale = new Vector2(
                length / _lineTexture.Width,
                thickness / _lineTexture.Height);

            _renderEngine.CommonSpriteBatch.Draw(
                _lineTexture,
                start,
                null,
                color,
                angle,
                new Vector2(0, _lineTexture.Height * 0.5f),
                scale,
                SpriteEffects.None,
                0
            );
        }

        private void DrawFilledCircle(Vector2 center, float radius, Color color)
        {
            var scale = radius * 2 / CIRCLE_TEXTURE_SIZE;
            _renderEngine.CommonSpriteBatch.Draw(
                _circleTexture,
                center,
                null,
                color,
                0,
                new Vector2(
                    CIRCLE_TEXTURE_SIZE * 0.5f,
                    CIRCLE_TEXTURE_SIZE * 0.5f),
                scale,
                SpriteEffects.None,
                0);
        }

        private void DrawCircleOutline(Vector2 center, float radius, Color color, float thickness)
        {
            DrawFilledCircle(center, radius + thickness, color);
        }

        private static Texture2D CreateCircleTexture(
            GraphicsDevice graphics,
            int size)
        {
            var pixels = new Color[size * size];
            var center = size * 0.5f;
            var radius = center - 1;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x + 0.5f - center;
                    var dy = y + 0.5f - center;
                    var distance = MathF.Sqrt(dx * dx + dy * dy);
                    var coverage = Math.Clamp(
                        radius + 0.5f - distance,
                        0,
                        1);
                    var alpha = (byte)MathF.Round(
                        coverage * byte.MaxValue);
                    pixels[y * size + x] = new Color(
                        alpha,
                        alpha,
                        alpha,
                        alpha);
                }
            }

            var texture = new Texture2D(graphics, size, size);
            texture.SetData(pixels);
            return texture;
        }

        private static Texture2D CreateLineTexture(
            GraphicsDevice graphics,
            int height)
        {
            var pixels = new Color[height];
            var center = height * 0.5f;
            var radius = center - 1.25f;
            for (var y = 0; y < height; y++)
            {
                var distance = MathF.Abs(y + 0.5f - center);
                var coverage = Math.Clamp(
                    radius + 0.5f - distance,
                    0,
                    1);
                var alpha = (byte)MathF.Round(
                    coverage * byte.MaxValue);
                pixels[y] = new Color(alpha, alpha, alpha, alpha);
            }

            var texture = new Texture2D(graphics, 1, height);
            texture.SetData(pixels);
            return texture;
        }

        public void Dispose()
        {
            _circleTexture?.Dispose();
            _lineTexture?.Dispose();
        }
    }
}
