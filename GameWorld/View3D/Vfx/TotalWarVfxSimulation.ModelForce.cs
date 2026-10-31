using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    private bool TryStepModelConstantForce(Particle particle, double age, double delta)
    {
        var state = particle.Emitter; var definition = state.Definition;
        var basis = particle.Root;
        var current = CurrentRoot(state, _simulatedTime);
        if (definition.Kind != 1 || definition.SimulationSpace == 1 || state.Parent != null
            || !definition.Force.TryConstantVector(out var force)
            || !TryModelForceRoot(basis, out var scale, out var rotation)
            || !TryModelForceRoot(current, out var currentScale, out _)
            || Math.Abs(scale - currentScale) > .00001f
            || !float.IsFinite(state.TimeScale) || !(state.TimeScale > 0))
        {
            ForceBasisUnsupported = true;
            return false;
        }
        // Rigid forces are world-axis acceleration, scaled with geometry, independently of birth rotation.
        // The matching positive uniform scale cancels when storing that acceleration in local space.
        var acceleration = Vector3.Transform(force * .01f, Quaternion.Conjugate(rotation));
        acceleration *= state.TimeScale;
        acceleration *= state.TimeScale;
        if (!float.IsFinite(acceleration.X) || !float.IsFinite(acceleration.Y) || !float.IsFinite(acceleration.Z))
        {
            ForceBasisUnsupported = true;
            return false;
        }
        var step = (float)delta;
        var damping = Math.Max(0, definition.Deceleration.Sample("x", age / particle.Life, particle.RandomAlpha));
        particle.Velocity *= MathF.Exp(-damping * (step * state.TimeScale));
        // Native body integration damps first, then applies the half-squared source-time displacement.
        particle.Position += particle.Velocity * step + acceleration * ((step * .5f) * step);
        particle.Velocity += acceleration * step;
        return true;
    }

    private static bool TryModelForceRoot(Matrix root, out float scale, out Quaternion rotation)
    {
        scale = 0; rotation = Quaternion.Identity;
        if (!FiniteModelTerrainRoot(root)) return false;
        var right = root.Right.LengthSquared(); var up = root.Up.LengthSquared(); var backward = root.Backward.LengthSquared();
        var determinant = root.Determinant();
        if (!(right > 0) || !(up > 0) || !(backward > 0) || !(determinant > 0)
            || !float.IsFinite(right) || !float.IsFinite(up) || !float.IsFinite(backward) || !float.IsFinite(determinant)
            || !root.Decompose(out var axes, out rotation, out var translation)
            || !PositiveUniformScale(axes)
            || !float.IsFinite(rotation.X) || !float.IsFinite(rotation.Y) || !float.IsFinite(rotation.Z) || !float.IsFinite(rotation.W)
            || !MatrixClose(root, Matrix.CreateScale(axes) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation)))
            return false;
        scale = axes.X;
        return true;
    }
}
