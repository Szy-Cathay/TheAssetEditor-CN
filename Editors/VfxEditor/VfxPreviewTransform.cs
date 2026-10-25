using GameWorld.Core.Components.Gizmo;
using Microsoft.Xna.Framework;

namespace Editors.VfxEditor;

// A transient preview attachment, never an authored emitter or a document command.
public sealed class VfxPreviewTransform : ITransformable
{
    private Matrix? _initial;
    public Matrix World { get; private set; } = Matrix.Identity;
    public bool IsTransformActive => _initial.HasValue;
    public Vector3 Position { get => World.Translation; set { var matrix = World; matrix.Translation = value; World = matrix; } }
    public Vector3 Scale
    {
        get { World.Decompose(out var scale, out _, out _); return scale; }
        set => World = Matrix.CreateScale(value) * Matrix.CreateFromQuaternion(Orientation) * Matrix.CreateTranslation(Position);
    }
    public Quaternion Orientation
    {
        get { World.Decompose(out _, out var rotation, out _); return rotation; }
        set => World = Matrix.CreateScale(Scale) * Matrix.CreateFromQuaternion(value) * Matrix.CreateTranslation(Position);
    }
    public Vector3 GetObjectCentre() => Position;
    public void Set(Matrix matrix) => World = matrix;
    public void Begin() => _initial ??= World;
    public void Commit() => _initial = null;
    public void Cancel()
    {
        if (_initial is { } initial) World = initial;
        _initial = null;
    }
    public bool Replace(ModalPreviewReplacement replacement)
    {
        if (_initial is not { } initial) return false;
        var pivot = replacement.Pivot == PivotType.WorldOrigin ? Vector3.Zero : initial.Translation;
        var transform = replacement.Kind switch
        {
            ModalPreviewReplacementKind.RestoreOnly => Matrix.Identity,
            ModalPreviewReplacementKind.Translate => Matrix.CreateTranslation(replacement.VectorValue),
            ModalPreviewReplacementKind.Rotate => Matrix.CreateTranslation(-pivot) * replacement.RotationValue * Matrix.CreateTranslation(pivot),
            ModalPreviewReplacementKind.Scale => Matrix.CreateTranslation(-pivot) * Matrix.Invert(replacement.ScaleOrientation)
                * Matrix.CreateScale(replacement.VectorValue + Vector3.One) * replacement.ScaleOrientation * Matrix.CreateTranslation(pivot),
            _ => throw new ArgumentOutOfRangeException(nameof(replacement)),
        };
        var candidate = initial * transform;
        // Singular attachments cannot be sampled or inverted safely during a gesture.
        var values = new[] { candidate.M11, candidate.M12, candidate.M13, candidate.M21, candidate.M22, candidate.M23,
            candidate.M31, candidate.M32, candidate.M33, candidate.M41, candidate.M42, candidate.M43 };
        if (values.Any(value => !float.IsFinite(value)) || Math.Abs(candidate.Determinant()) < .000001f)
        {
            World = initial;
            return false;
        }
        World = candidate;
        return true;
    }
}
