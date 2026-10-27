using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

// The rigid CPU controller transports positions separately from particle spin and animation clips.
internal sealed class VfxModelNativeMotion
{
    private readonly VfxModelNativeCurve[]? _position;
    private readonly VfxModelNativeOrientation? _orientation;

    private VfxModelNativeMotion(VfxModelNativeCurve[]? position, VfxModelNativeOrientation? orientation)
        => (_position, _orientation) = (position, orientation);

    public static VfxModelNativeMotion Create(string? position, VfxModelNativeOrientation? orientation)
        => new(position == null ? null : [VfxModelNativeCurve.Create(position, "EmitterPosition", "x"),
            VfxModelNativeCurve.Create(position, "EmitterPosition", "y"),
            VfxModelNativeCurve.Create(position, "EmitterPosition", "z")], orientation);

    public Vector3 Position(double phase, float unary) => _position == null ? Vector3.Zero
        : new(_position[0].Sample(phase, unary), _position[1].Sample(phase, unary), _position[2].Sample(phase, unary));

    public Quaternion Orientation(double phase, float unary) => _orientation?.Sample(phase, unary) ?? Quaternion.Identity;

    public static (Vector3 Position, Vector3 Velocity) Transport(Vector3 position, Vector3 velocity,
        Matrix previousNode, Matrix currentNode, Vector3 previousEmitterPosition, Vector3 currentEmitterPosition,
        Quaternion previousEmitterOrientation, Quaternion currentEmitterOrientation)
    {
        previousNode.Decompose(out _, out var previousRotation, out _);
        currentNode.Decompose(out _, out var currentRotation, out _);
        var previous = Matrix.CreateFromQuaternion(Quaternion.Normalize(previousRotation));
        var current = Matrix.CreateFromQuaternion(Quaternion.Normalize(currentRotation));
        var inverseCurrent = Matrix.Transpose(current);
        var nodeDelta = Matrix.Transpose(previous) * current;
        var emitterDelta = Matrix.Transpose(Matrix.CreateFromQuaternion(previousEmitterOrientation))
            * Matrix.CreateFromQuaternion(currentEmitterOrientation);
        // Native 6CD870: undo current node, transport around the previous emitter, then apply node delta.
        var local = Vector3.TransformNormal(position - previousNode.Translation, inverseCurrent) - previousEmitterPosition;
        local = Vector3.TransformNormal(local, emitterDelta) + currentEmitterPosition;
        position = Vector3.TransformNormal(local, current * nodeDelta) + currentNode.Translation;
        velocity = Vector3.TransformNormal(velocity, inverseCurrent * emitterDelta * current * nodeDelta);
        return (position, velocity);
    }
}
