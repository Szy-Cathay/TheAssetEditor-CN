using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    private const long MaximumCheckpointBudget = 16 * 1024 * 1024;
    private const double CheckpointInterval = .25;
    private bool _seekCheckpointsEnabled, _seekCheckpointsNeedReset;
    private int _seekCheckpointLimit = 8;
    private long _seekCheckpointStateLimit = MaximumCheckpointBudget;
    private long _seekCheckpointRandomLimit = MaximumCheckpointBudget;
    private long _seekCheckpointBytes;
    private int _seekCheckpointRestores;
    private double _nextSeekCheckpointTime;
    private Matrix _seekCheckpointAttachment;
    private VfxSeekRandomBudget? _seekRandomBudget;
    private readonly List<SeekCheckpoint> _seekCheckpoints = [];

    internal bool SeekCheckpointingEnabled => _seekCheckpointsEnabled;
    internal int SeekCheckpointCount => _seekCheckpoints.Count;
    internal int SeekCheckpointRestoreCount => _seekCheckpointRestores;
    internal long SeekCheckpointStateBytes => _seekCheckpointBytes;
    internal long SeekCheckpointRandomBytes => _seekRandomBudget?.UsedBytes ?? 0;

    // The caller certifies an immutable, constant external attachment history before the first Reset.
    internal bool EnableSeekCheckpoints(int maxCheckpoints = 8,
        long stateBudgetBytes = MaximumCheckpointBudget, long randomBudgetBytes = MaximumCheckpointBudget)
    {
        if (maxCheckpoints is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(maxCheckpoints));
        if (stateBudgetBytes is < 1 or > MaximumCheckpointBudget) throw new ArgumentOutOfRangeException(nameof(stateBudgetBytes));
        if (randomBudgetBytes is < 1 or > MaximumCheckpointBudget) throw new ArgumentOutOfRangeException(nameof(randomBudgetBytes));
        if (_seekCheckpointsEnabled) return true;
        if (_initialized || HasMotionTrailEmitters) return false;
        _seekCheckpointLimit = maxCheckpoints;
        _seekCheckpointStateLimit = stateBudgetBytes;
        _seekCheckpointRandomLimit = randomBudgetBytes;
        _seekCheckpointsEnabled = true;
        _seekRandomBudget = new(randomBudgetBytes);
        return true;
    }

    internal void DisableSeekCheckpoints()
    {
        if (!_seekCheckpointsEnabled && _seekRandomBudget == null) return;
        _seekCheckpointsEnabled = false;
        ClearSeekCheckpoints();
        _seekRandomBudget?.Disable();
        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<object>(_emitters.Cast<object>().Concat(_particles));
        while (pending.TryDequeue(out var item))
        {
            if (!visited.Add(item)) continue;
            if (item is EmitterState emitter)
            {
                if (emitter.Random is VfxSeekRandom recorded) emitter.Random = recorded.DetachExact();
                if (emitter.Parent != null) pending.Enqueue(emitter.Parent);
            }
            else if (item is Particle particle)
            {
                pending.Enqueue(particle.Emitter);
                if (particle.EventInstances != null)
                    foreach (var child in particle.EventInstances) pending.Enqueue(child);
            }
        }
        _seekRandomBudget = null;
    }

    internal bool HasSeekCheckpoint(float seconds)
        => _seekCheckpointsEnabled && !_seekCheckpointsNeedReset && _seekRandomBudget?.Enabled == true
            && !CheckpointUnsupported() && _seekCheckpoints.Any(state => state.SimulatedTime <= seconds + .000001);

    private Random CreateSeekRandom(int seed) => _seekCheckpointsEnabled && _seekRandomBudget is { Enabled: true } budget
        ? VfxSeekRandom.Create(seed, budget) : new Random(seed);

    private void ClearSeekCheckpoints()
    {
        _seekCheckpoints.Clear();
        _seekCheckpointBytes = 0;
    }

    private void InvalidateSeekCheckpoints()
    {
        if (!_seekCheckpointsEnabled) return;
        ClearSeekCheckpoints();
        _seekCheckpointsNeedReset = true;
    }

    private void CheckSeekCheckpointContext(Matrix attachment, bool continuous, bool frameSamples)
    {
        if (_seekCheckpointsEnabled && (continuous || frameSamples || _initialized && attachment != _seekCheckpointAttachment))
            InvalidateSeekCheckpoints();
    }

    private void ResetSeekCheckpointGeneration()
    {
        ClearSeekCheckpoints();
        _seekCheckpointsNeedReset = false;
        _seekCheckpointAttachment = _attachment;
        _nextSeekCheckpointTime = 0;
        _seekRandomBudget = _seekCheckpointsEnabled ? new(_seekCheckpointRandomLimit) : null;
    }

    private bool CheckpointUnsupported()
        // Force fallback is deterministic; both its per-particle basis result and diagnostic are captured.
        => VelocityScaleUnsupported || SpriteCullScaleUnsupported || ModelSpinBasisUnsupported
            || ModelMotionScaleUnsupported || SpriteSpinBasisUnsupported || ModelSpinRangeUnsupported
            || SpriteSpinRangeUnsupported || TerrainBasisUnsupported
            || _emitters.Any(state => state.ModelAngularUnsupported || state.SpriteAngularUnsupported);

    private void CaptureSeekCheckpoint()
    {
        if (!_seekCheckpointsEnabled) return;
        if (_seekRandomBudget?.Enabled != true) { DisableSeekCheckpoints(); return; }
        if (_seekCheckpointsNeedReset || _simulatedTime < -.000001 || _simulatedTime + .000001 < _nextSeekCheckpointTime) return;
        if (CheckpointUnsupported()) { DisableSeekCheckpoints(); return; }
        _nextSeekCheckpointTime = (Math.Floor((_simulatedTime + .000001) / CheckpointInterval) + 1) * CheckpointInterval;
        if (_seekCheckpoints.Any(state => Math.Abs(state.SimulatedTime - _simulatedTime) < .000001)) return;
        if (!TryCloneGraph(_particles, _emitters, _seekCheckpointStateLimit,
                out var particles, out var emitters, out var bytes)) { DisableSeekCheckpoints(); return; }
        bytes += 512 + _referenceRandoms.Count * 96L;
        if (bytes > _seekCheckpointStateLimit) { DisableSeekCheckpoints(); return; }
        while (_seekCheckpoints.Count >= _seekCheckpointLimit || _seekCheckpointBytes + bytes > _seekCheckpointStateLimit)
        {
            var index = _seekCheckpoints.FindIndex(state => state.SimulatedTime > .000001);
            if (index < 0)
            {
                // Keep the expensive prewarmed zero state if another complete state cannot fit beside it.
                if (_seekCheckpoints.Count > 0) return;
                break;
            }
            _seekCheckpointBytes -= _seekCheckpoints[index].Bytes;
            _seekCheckpoints.RemoveAt(index);
        }
        _seekCheckpoints.Add(new(particles, emitters, _referenceRandoms.ToArray(), _simulatedTime,
            Math.Max(0, _simulatedTime), _cycleDuration, _groundHeight, _emissionEndTimeSeconds, Flags(), bytes));
        _seekCheckpointBytes += bytes;
    }

    private bool TryRestoreSeekCheckpoint(float seconds)
    {
        if (!HasSeekCheckpoint(seconds)) return false;
        var state = _seekCheckpoints.Where(value => value.SimulatedTime <= seconds + .000001)
            .MaxBy(value => value.SimulatedTime)!;
        if (!TryCloneGraph(state.Particles, state.Emitters, _seekCheckpointStateLimit,
                out var particles, out var emitters, out _)) { DisableSeekCheckpoints(); return false; }
        ClearAppearanceEntries();
        _particles.Clear(); _particles.AddRange(particles);
        _emitters.Clear(); _emitters.AddRange(emitters);
        _referenceRandoms.Clear();
        foreach (var item in state.ReferenceRandoms) _referenceRandoms.Add(item.Key, item.Value);
        _simulatedTime = state.SimulatedTime;
        _requestedTime = state.RequestedTime;
        _cycleDuration = state.CycleDuration;
        _groundHeight = state.GroundHeight;
        _emissionEndTimeSeconds = state.EmissionEnd;
        RestoreFlags(state.Flags);
        _spriteCullScales.Clear(); _spriteFrameUnsupported.Clear();
        _initialized = true;
        _nextSeekCheckpointTime = (Math.Floor((_simulatedTime + .000001) / CheckpointInterval) + 1) * CheckpointInterval;
        _seekCheckpointRestores++;
        return true;
    }

    private static bool TryCloneGraph(IReadOnlyList<Particle> sourceParticles, IReadOnlyList<EmitterState> sourceEmitters,
        long budget, out Particle[] particles, out EmitterState[] emitters, out long bytes)
    {
        particles = []; emitters = []; bytes = 0;
        var particleSet = new HashSet<Particle>(ReferenceEqualityComparer.Instance);
        var emitterSet = new HashSet<EmitterState>(ReferenceEqualityComparer.Instance);
        var pending = new Queue<object>();
        foreach (var particle in sourceParticles) pending.Enqueue(particle);
        foreach (var emitter in sourceEmitters) pending.Enqueue(emitter);
        while (pending.TryDequeue(out var item))
        {
            if (item is Particle particle)
            {
                if (!particleSet.Add(particle)) continue;
                bytes += 640 + (particle.EventInstances?.Count ?? 0) * 16L;
                pending.Enqueue(particle.Emitter);
                if (particle.EventInstances != null)
                    foreach (var child in particle.EventInstances) pending.Enqueue(child);
            }
            else if (item is EmitterState emitter)
            {
                if (!emitterSet.Add(emitter)) continue;
                if (emitter.Random is not VfxSeekRandom { CanClone: true }) return false;
                bytes += 1536;
                if (emitter.Parent != null) pending.Enqueue(emitter.Parent);
            }
            if (bytes > budget) return false;
        }
        var particleMap = new Dictionary<Particle, Particle>(ReferenceEqualityComparer.Instance);
        var emitterMap = new Dictionary<EmitterState, EmitterState>(ReferenceEqualityComparer.Instance);
        foreach (var particle in particleSet) particleMap.Add(particle, particle.CloneFields());
        foreach (var emitter in emitterSet) emitterMap.Add(emitter, emitter.CloneFields());
        foreach (var emitter in emitterSet)
        {
            var copy = emitterMap[emitter];
            copy.Parent = emitter.Parent == null ? null : particleMap[emitter.Parent];
            copy.Random = ((VfxSeekRandom)emitter.Random).CloneCursor();
        }
        foreach (var particle in particleSet)
        {
            var copy = particleMap[particle];
            copy.Emitter = emitterMap[particle.Emitter];
            copy.EventInstances = particle.EventInstances?.Select(child => emitterMap[child]).ToList();
        }
        particles = sourceParticles.Select(particle => particleMap[particle]).ToArray();
        emitters = sourceEmitters.Select(emitter => emitterMap[emitter]).ToArray();
        return true;
    }

    private SeekFlags Flags() => new(CapacityReached, RateLimited, VelocityScaleUnsupported, SpriteCullScaleUnsupported,
        ForceBasisUnsupported, ModelSpinBasisUnsupported, ModelMotionScaleUnsupported, SpriteSpinBasisUnsupported,
        ModelSpinRangeUnsupported, SpriteSpinRangeUnsupported, TerrainBasisUnsupported);

    private void RestoreFlags(SeekFlags flags)
    {
        CapacityReached = flags.Capacity; RateLimited = flags.Rate;
        VelocityScaleUnsupported = flags.Velocity; SpriteCullScaleUnsupported = flags.Cull;
        ForceBasisUnsupported = flags.Force; ModelSpinBasisUnsupported = flags.ModelSpinBasis;
        ModelMotionScaleUnsupported = flags.ModelMotion; SpriteSpinBasisUnsupported = flags.SpriteSpinBasis;
        ModelSpinRangeUnsupported = flags.ModelSpinRange; SpriteSpinRangeUnsupported = flags.SpriteSpinRange;
        TerrainBasisUnsupported = flags.Terrain;
    }

    private readonly record struct SeekFlags(bool Capacity, bool Rate, bool Velocity, bool Cull, bool Force,
        bool ModelSpinBasis, bool ModelMotion, bool SpriteSpinBasis, bool ModelSpinRange, bool SpriteSpinRange, bool Terrain);

    private sealed record SeekCheckpoint(Particle[] Particles, EmitterState[] Emitters,
        KeyValuePair<object, ReferenceRandoms>[] ReferenceRandoms, double SimulatedTime, double RequestedTime,
        float CycleDuration, float? GroundHeight, float? EmissionEnd, SeekFlags Flags, long Bytes);
}
