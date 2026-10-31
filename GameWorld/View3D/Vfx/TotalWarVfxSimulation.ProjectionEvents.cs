using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    private readonly Dictionary<EmitterState, Vector4> _projectionEventTints = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ProjectionEventKey, (Matrix Anchor, bool Valid, byte Diagnostics)> _projectionEventAnchors = [];
    private readonly Dictionary<ProjectionEventKey, (Vector3 Scale, byte Diagnostics)> _projectionEventScales = [];
    private bool _projectingEventTints;
    private long _projectionEventTintTime;
    private int _projectionAngularRevision;
    internal bool ProjectionMemoEnabled { get; set; }
    private readonly record struct ProjectionEventKey(EmitterState Emitter, long Time, bool CurrentFrame, int AngularRevision);
    private bool CanMemoProjection => ProjectionMemoEnabled && _projectingEventTints;

    // These flags only accumulate during a projection. Reusing a value must retain their side effects.
    private byte ProjectionDiagnostics => (byte)((ModelSpinBasisUnsupported ? 1 : 0)
        | (ModelMotionScaleUnsupported ? 2 : 0) | (ModelSpinRangeUnsupported ? 4 : 0) | (ForceBasisUnsupported ? 8 : 0));

    private void RestoreProjectionDiagnostics(byte diagnostics)
    {
        ModelSpinBasisUnsupported |= (diagnostics & 1) != 0;
        ModelMotionScaleUnsupported |= (diagnostics & 2) != 0;
        ModelSpinRangeUnsupported |= (diagnostics & 4) != 0;
        ForceBasisUnsupported |= (diagnostics & 8) != 0;
    }

    // Parent appearance is immutable during one projection, including camera-only redraws.
    private readonly struct ProjectionEventScope : IDisposable
    {
        private readonly TotalWarVfxSimulation _owner;
        public ProjectionEventScope(TotalWarVfxSimulation owner)
        {
            _owner = owner;
            owner._projectionEventTints.Clear();
            owner._projectionEventAnchors.Clear();
            owner._projectionEventScales.Clear();
            owner._projectionAngularRevision = 0;
            owner._projectionEventTintTime = BitConverter.DoubleToInt64Bits(owner._requestedTime);
            owner._projectingEventTints = owner.ProjectionMemoEnabled;
        }

        public void Dispose()
        {
            _owner._projectingEventTints = false;
            _owner._projectionEventTints.Clear();
            _owner._projectionEventAnchors.Clear();
            _owner._projectionEventScales.Clear();
        }
    }
}
