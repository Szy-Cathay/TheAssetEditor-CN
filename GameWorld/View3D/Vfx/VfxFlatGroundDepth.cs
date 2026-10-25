using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Vfx;

internal sealed class VfxFlatGroundDepth(GraphicsDevice device) : IDisposable
{
    private readonly BasicEffect _effect = new(device) { VertexColorEnabled = true };
    private readonly BlendState _noColour = new() { ColorWriteChannels = ColorWriteChannels.None };

    public bool Draw(CommonShaderParameters camera)
    {
        if (camera.VfxGroundHeight is not { } ground || !float.IsFinite(ground)) return false;
        var targets = device.GetRenderTargets();
        var depth = targets.Length == 0 ? device.PresentationParameters.DepthStencilFormat
            : targets[0].RenderTarget is RenderTarget2D target ? target.DepthStencilFormat : DepthFormat.None;
        if (depth == DepthFormat.None) return false;
        var vertices = Triangles(camera.View * camera.Projection, ground);
        if (vertices.Length == 0) return true;
        var blend = device.BlendState; var depthState = device.DepthStencilState; var raster = device.RasterizerState;
        try
        {
            // The caller explicitly supplies this horizontal receiver; colour remains untouched.
            device.BlendState = _noColour; device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            _effect.World = Matrix.Identity; _effect.View = camera.View; _effect.Projection = camera.Projection;
            foreach (var pass in _effect.CurrentTechnique.Passes)
            {
                pass.Apply(); device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices, 0, vertices.Length / 3);
            }
        }
        finally
        {
            device.BlendState = blend; device.DepthStencilState = depthState; device.RasterizerState = raster;
        }
        return true;
    }

    internal static VertexPositionColor[] Triangles(Matrix viewProjection, float ground)
    {
        var corners = new BoundingFrustum(viewProjection).GetCorners();
        if (corners.Any(p => !float.IsFinite(p.X) || !float.IsFinite(p.Y) || !float.IsFinite(p.Z))) return [];
        ReadOnlySpan<int> edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        var points = new List<Vector3>();
        for (var index = 0; index < edges.Length; index += 2)
        {
            var a = corners[edges[index]]; var b = corners[edges[index + 1]];
            if (a.Y == b.Y) continue;
            var fraction = (ground - a.Y) / (b.Y - a.Y);
            if (fraction < 0 || fraction > 1) continue;
            var point = Vector3.Lerp(a, b, fraction); point.Y = ground;
            if (!points.Any(old => Vector3.DistanceSquared(old, point) < .00000001f)) points.Add(point);
        }
        if (points.Count < 3) return [];
        var center = points.Aggregate(Vector3.Zero, (sum, point) => sum + point) / points.Count;
        points.Sort((a, b) => MathF.Atan2(a.Z - center.Z, a.X - center.X).CompareTo(MathF.Atan2(b.Z - center.Z, b.X - center.X)));
        var result = new List<VertexPositionColor>();
        for (var index = 1; index < points.Count - 1; index++)
            foreach (var point in new[] { points[0], points[index], points[index + 1] }) result.Add(new(point, Color.White));
        return result.ToArray();
    }

    public void Dispose() { _effect.Dispose(); _noColour.Dispose(); }
}
