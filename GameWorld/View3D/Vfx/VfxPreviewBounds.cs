using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal static class VfxPreviewBounds
{
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 100), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxGroundHeight: 0);

    public static BoundingBox? Estimate(VfxConversion conversion)
    {
        var simulation = new TotalWarVfxSimulation(conversion);
        var end = Math.Min(8, simulation.CycleDuration);
        var models = conversion.Models.Select(model => model.AnimationVertices == null
            ? new[] { BoundingBox.CreateFromPoints(model.Vertices.Select(vertex => vertex.Position)) }
            : Enumerable.Range(0, model.AnimationFrameCount).Select(frame => BoundingBox.CreateFromPoints(model.AnimationVertices
                .Skip(frame * model.Vertices.Length).Take(model.Vertices.Length).Select(vertex => vertex.Position))).ToArray()).ToArray();
        BoundingBox? colour = null, ground = null, fallback = null;
        foreach (var time in new[] { 0f, 1f/120, 1f/60, .033f, .05f, .1f, .2f, .35f, .5f, .75f, 1f, 1.5f, 2f, 3f, 4f, 6f, 8f }.Where(t => t <= end))
        {
            simulation.Seek(time, Matrix.Identity);
            var bounds = Current(conversion, simulation, models);
            Merge(ref colour, bounds.Colour); Merge(ref ground, bounds.Ground); Merge(ref fallback, bounds.All);
        }
        return colour ?? ground ?? fallback;
    }

    private static (BoundingBox? Colour, BoundingBox? Ground, BoundingBox? All) Current(
        VfxConversion conversion, TotalWarVfxSimulation simulation, BoundingBox[][] models)
    {
        BoundingBox? colour = null, ground = null, fallback = null;
        foreach (var projected in simulation.ProjectedFrame(Camera))
        {
            var particle = projected.Render;
            var emitter = conversion.Emitters[particle.Layer];
            if (emitter.Kind != 1 && particle.Colour.W <= .000001f) continue;
            if (emitter.Kind == 1)
            {
                // Frame every authored choice, including choices not drawn by this deterministic sample.
                var parts = Enumerable.Range(0, emitter.ModelVariants.Count + 1).SelectMany(emitter.RenderParts);
                foreach (var part in parts)
                {
                    if (particle.Colour.W <= .000001f && !(part.OpaqueModel != 0 && part.MeshMaterialIndex < 0)) continue;
                    var model = conversion.Models[part.ModelIndex];
                    var frame = (int)Math.Floor(Math.Max(0, particle.AppearancePhase) * model.AnimationCycles * models[part.ModelIndex].Length)
                        % models[part.ModelIndex].Length;
                    var points = models[part.ModelIndex][frame].GetCorners().Select(point => Vector3.Transform(point, particle.Transform));
                    Include(BoundingBox.CreateFromPoints(points), part.Distortion == 0);
                }
            }
            else
            {
                var radius = .75f * Math.Max(particle.Transform.Right.Length(), particle.Transform.Up.Length());
                if (emitter.Kind == 2) radius = Math.Max(radius, particle.Transform.Backward.Length() * .75f);
                Include(new(particle.Transform.Translation - new Vector3(radius), particle.Transform.Translation + new Vector3(radius)), emitter.Distortion == 0);
            }
        }
        return (colour, ground, fallback);

        void Include(BoundingBox bounds, bool hasColour)
        {
            if (!Finite(bounds.Min) || !Finite(bounds.Max)) return;
            Merge(ref fallback, bounds);
            if (bounds.Max.Y < 0) return;
            // The independent editor supplies a flat receiver at the effect root, not a falling-pose camera target.
            bounds.Min.Y = Math.Max(0, bounds.Min.Y);
            Merge(ref ground, bounds);
            if (hasColour) Merge(ref colour, bounds);
        }
    }

    private static void Merge(ref BoundingBox? target, BoundingBox? bounds)
    {
        if (bounds is { } value) target = target is { } previous ? BoundingBox.CreateMerged(previous, value) : value;
    }
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
