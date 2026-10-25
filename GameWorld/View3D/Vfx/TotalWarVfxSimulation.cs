using GameWorld.Core.Rendering;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

// Fixed simulation ticks keep playback, seek and unsaved reload independent of UI frame rate.
internal sealed class TotalWarVfxSimulation(VfxConversion conversion)
{
    internal const int ParticleLimit = 65536;
    private const double Step = 1.0 / 120;
    private readonly List<Particle> _particles = [];
    private readonly List<EmitterState> _emitters = [];
    private readonly Dictionary<VfxPreviewEmitter, int> _definitionIndexes = [];
    private float? _groundHeight;
    private float _cycleDuration;
    private double _requestedTime;
    private double _simulatedTime;
    private Matrix _attachment = Matrix.Identity;
    private Func<float, Matrix>? _attachmentAtTime;
    private bool _useCurrentAttachmentForFrame;
    private bool _continueMotionTrails;
    private bool _initialized;
    public int ParticleCount => _particles.Count;
    public bool HasMotionTrailEmitters => conversion.Emitters.Any(emitter => emitter.IsMotionTrail);
    public float Time => (float)_requestedTime;
    public float CycleDuration
    {
        get
        {
            if (!_initialized) Reset();
            return _cycleDuration;
        }
    }
    public bool CapacityReached { get; private set; }
    public bool RateLimited { get; private set; }
    public bool VelocityScaleUnsupported { get; private set; }
    public bool ForceBasisUnsupported { get; private set; }
    public bool ModelSpinBasisUnsupported { get; private set; }
    public bool ModelSpinRangeUnsupported { get; private set; }
    public bool TerrainBasisUnsupported { get; private set; }

    public void Advance(float seconds, Matrix attachment) => Seek((float)_requestedTime + seconds, attachment);

    public void Seek(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime = null)
        => SeekCore(seconds, attachment, attachmentAtTime, false);

    public void SeekWithCurrentAttachment(float seconds, Matrix attachment, Func<float, Matrix> attachmentAtTime)
        => SeekCore(seconds, attachment, attachmentAtTime, true);

    public void SeekContinuousTrail(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime = null)
        => SeekCore(seconds, attachment, attachmentAtTime, true, continueMotionTrails: true);

    private void SeekCore(float seconds, Matrix attachment, Func<float, Matrix>? attachmentAtTime, bool useCurrentAttachmentForFrame,
        bool continueMotionTrails = false)
    {
        if (!float.IsFinite(seconds) || seconds < 0 || !continueMotionTrails && seconds > 600) throw new ArgumentOutOfRangeException(nameof(seconds));
        var sameTime = _initialized && seconds == _requestedTime;
        _attachment = attachment;
        _attachmentAtTime = attachmentAtTime;
        _useCurrentAttachmentForFrame = useCurrentAttachmentForFrame;
        _continueMotionTrails = continueMotionTrails;
        if (!_initialized || seconds + 0.00001 < _requestedTime) Reset();
        _requestedTime = seconds;
        while (_simulatedTime + Step <= _requestedTime + 0.000001)
        {
            _simulatedTime += Step;
            Tick(Step);
        }
        if (sameTime && useCurrentAttachmentForFrame)
            foreach (var emitter in _emitters) UpdateMotionRoot(emitter, seconds, currentFrame: true, preserveVelocity: true);
    }

    private void Reset()
    {
        _particles.Clear();
        CapacityReached = false;
        RateLimited = false;
        VelocityScaleUnsupported = false;
        ForceBasisUnsupported = false;
        ModelSpinBasisUnsupported = false;
        ModelSpinRangeUnsupported = false;
        _emitters.Clear(); _definitionIndexes.Clear();
        for (var index = 0; index < conversion.Emitters.Count; ++index) _definitionIndexes.Add(conversion.Emitters[index], index);
        var rootIndex = 0;
        foreach (var definition in conversion.Emitters.Where(e => !e.IsEventTemplate))
            _emitters.Add(new(definition, 1940 + rootIndex++ * 7919, _definitionIndexes[definition]));
        var end = _emitters.Select(emitter => emitter.Start + (emitter.Definition.Infinite ? 0 : emitter.Duration)
            + EventTail(emitter.Definition, emitter.LifeBias) / emitter.TimeScale).DefaultIfEmpty(0).Max();
        _cycleDuration = Math.Clamp(Math.Max(_emitters.Any(e => e.Definition.Infinite) ? 8 : .5f, end + .25f), .5f, 600);
        _simulatedTime = Math.Floor(Math.Min(0, _emitters.Select(x => (double)x.Start).DefaultIfEmpty(0).Min()) / Step) * Step;
        _requestedTime = 0;
        _initialized = true;
        Tick(0);
    }

    private void Tick(double delta)
    {
        for (var index = _particles.Count - 1; index >= 0; --index)
        {
            var particle = _particles[index];
            var definition = particle.Emitter.Definition;
            var age = AppearanceAge(particle, definition, _simulatedTime);
            if (age >= particle.Life)
            {
                if (particle.EventInstances is { } instances)
                    foreach (var child in instances) FreezeEvent(child);
                particle.Alive = false;
                TriggerEvents(particle, true);
                particle.Emitter.ActiveCount--;
                _particles.RemoveAt(index);
                continue;
            }
            if (definition.DynamicSpinDamping)
            {
                (particle.SpinRotation, particle.AngularSpeed) = DynamicSpinAt(particle, _simulatedTime);
                particle.SpinTime = _simulatedTime;
            }
            else if (definition.ComplexSpinDamping)
            {
                (particle.SpinRotation, particle.AngularVelocity) = ComplexSpinAt(particle, _simulatedTime);
                particle.SpinTime = _simulatedTime;
            }
            else if (definition.ModelQuaternionSpin)
            {
                (particle.SpinRotation, particle.AngularVelocity, particle.SpinRootRotation) = ModelSpinAt(particle, _simulatedTime);
                particle.SpinTime = _simulatedTime;
            }
            var forceSupported = (definition.CalibratedVerticalAcceleration != 0 || definition.ParticleForceCurve)
                && ForceBasisSupported(particle, definition);
            if (definition.ConstantForceDamping != null && forceSupported)
            {
                (particle.Position, particle.Velocity) = ConstantMotion(particle, definition, age);
                continue;
            }
            if (forceSupported)
            {
                var acceleration = definition.ParticleForceCurve
                    ? definition.Force.Vector(age / particle.Life, new Vector3(particle.RandomAlpha)) * .01f
                    : new Vector3(0, definition.CalibratedVerticalAcceleration, 0);
                particle.Velocity += acceleration * (float)delta;
            }
            var damping = Math.Max(0, definition.Deceleration.Sample("x", age / particle.Life, particle.RandomAlpha));
            particle.Velocity *= MathF.Exp(-damping * (float)delta);
            particle.Position += particle.Velocity * (float)delta;
            if (definition.ConstantForceDamping != null)
            {
                particle.MotionStart = age;
                particle.MotionOrigin = particle.Position;
                particle.MotionInitialVelocity = particle.Velocity;
            }
        }
        for (var index = 0; index < _emitters.Count; ++index)
        {
            var emitter = _emitters[index];
            UpdateMotionRoot(emitter, _simulatedTime);
            if (emitter.Parent is { Alive: false } && emitter.Event is { TrackLifetime: true, OnDeath: false }) continue;
            var localTime = _simulatedTime - emitter.Start;
            if (localTime < -0.000001) continue;
            // Continue only motion-driven root emission; particle and event lifetimes remain authored.
            var continuousTrail = _continueMotionTrails && emitter.Parent == null && emitter.Definition.IsMotionTrail;
            var emittingContinuously = emitter.Definition.Infinite || continuousTrail;
            var emissionDelta = delta;
            if (!emittingContinuously)
            {
                // Integrate only the part of this tick inside the authored finite interval.
                var intervalStart = Math.Max(0, localTime - delta);
                var intervalEnd = Math.Clamp(localTime, 0, emitter.Duration);
                if (localTime > emitter.Duration && intervalEnd <= intervalStart) continue;
                emissionDelta = Math.Max(0, intervalEnd - intervalStart);
            }
            if (emitter.Definition.BurstInterval > 0)
            {
                var intervalEnd = Math.Max(0, emittingContinuously ? localTime : Math.Min(localTime, emitter.Duration));
                var intervalStart = Math.Max(0, localTime - delta);
                emissionDelta = Math.Max(0, BurstActiveTime(intervalEnd, emitter) - BurstActiveTime(intervalStart, emitter));
            }
            var phase = Phase(localTime, emitter.Duration,
                emitter.Definition.LoopEmission || continuousTrail && !emitter.Definition.Infinite);
            if (emitter.Definition.SpawnSpacing > 0)
            {
                EmitByDistance(index, phase, delta, emissionDelta);
                continue;
            }
            var sampledRate = emitter.Definition.Emission.Sample("x", phase, emitter.RandomRate) * emitter.RateScale * emitter.TimeScale;
            if (!float.IsFinite(sampledRate) || sampledRate > 10000) RateLimited = true;
            var rate = float.IsFinite(sampledRate) ? Math.Clamp(sampledRate, 0, 10000) : 0;
            if (!emitter.Started)
            {
                emitter.Started = true;
                if (rate > 0) Spawn(index, phase);
            }
            emitter.Accumulator += rate * emissionDelta;
            while (emitter.Accumulator >= (emittingContinuously ? 1 : 1 - 0.000000001))
            {
                emitter.Accumulator = Math.Max(0, emitter.Accumulator - 1);
                Spawn(index, phase);
                if (emitter.ActiveCount >= emitter.Definition.MaximumParticles || _particles.Count >= ParticleLimit)
                {
                    emitter.Accumulator = 0;
                    break;
                }
            }
        }
        _emitters.RemoveAll(state => state.Parent != null && state.ActiveCount == 0
            && (!state.Definition.Infinite && _simulatedTime > state.Start + state.Duration
                || state.Event is { TrackLifetime: true, OnDeath: false } && !state.Parent.Alive));
    }

    private static double BurstActiveTime(double time, EmitterState emitter)
    {
        // Native BurstInterval is the quiet gap after each active BurstIntervalLength window.
        var length = (double)emitter.Definition.BurstLength;
        var period = length + emitter.Definition.BurstInterval;
        var authoredTime = time * emitter.TimeScale;
        var cycles = Math.Floor(authoredTime / period);
        return (cycles * length + Math.Min(authoredTime - cycles * period, length)) / emitter.TimeScale;
    }

    private void EmitByDistance(int emitterIndex, double phase, double delta, double activeDelta)
    {
        var emitter = _emitters[emitterIndex];
        var current = CurrentRoot(emitter, _simulatedTime);
        var previous = emitter.DistanceRoot ?? current;
        emitter.DistanceRoot = current;
        if (delta <= 0 || activeDelta <= 0) return;
        var distance = Vector3.Distance(previous.Translation, current.Translation);
        if (!float.IsFinite(distance)) { RateLimited = true; return; }
        distance *= (float)Math.Clamp(activeDelta / delta, 0, 1);
        if (distance <= 0) return;
        var spacing = emitter.Definition.SpawnSpacing;
        var travelled = spacing - emitter.DistanceRemainder;
        var budget = Math.Max(1, (int)Math.Ceiling(10000 * delta));
        var emitted = 0;
        while (travelled <= distance + spacing * .00001)
        {
            if (emitted++ >= budget) { RateLimited = true; break; }
            var fraction = (float)Math.Clamp(travelled / distance * activeDelta / delta, 0, 1);
            Spawn(emitterIndex, phase, Matrix.Lerp(previous, current, fraction));
            travelled += spacing;
            if (emitter.ActiveCount >= emitter.Definition.MaximumParticles || _particles.Count >= ParticleLimit) break;
        }
        emitter.DistanceRemainder = (emitter.DistanceRemainder + distance) % spacing;
        if (spacing - emitter.DistanceRemainder < spacing * .00001) emitter.DistanceRemainder = 0;
    }

    private static float EventTail(VfxPreviewEmitter definition, float lifeBias = 0)
    {
        var life = Math.Max(0, definition.Life.Maximum + lifeBias);
        var end = life;
        foreach (var particleEvent in definition.Events)
            foreach (var child in particleEvent.Children)
            {
                var state = new EmitterState(child, 1940, 0);
                end = Math.Max(end, (particleEvent.OnDeath ? life : particleEvent.Delay) + state.Start
                    + (child.Infinite ? 8 : state.Duration) + EventTail(child, state.LifeBias) / state.TimeScale);
            }
        return end;
    }

    private void TriggerEvents(Particle particle, bool death)
    {
        foreach (var particleEvent in particle.Emitter.Definition.Events.Where(e => e.OnDeath == death))
            foreach (var child in particleEvent.Children)
            {
                if (!_definitionIndexes.TryGetValue(child, out var definitionIndex)) continue;
                if (_emitters.Count >= 4096) { CapacityReached = true; return; }
                var state = new EmitterState(child, particle.Emitter.Random.Next(), definitionIndex, particle, particleEvent,
                    _simulatedTime + particleEvent.Delay);
                state.SpawnAnchor = EventAnchor(state, _simulatedTime, out _);
                if (!particle.Alive) FreezeEvent(state);
                (particle.EventInstances ??= []).Add(state);
                _emitters.Add(state);
            }
    }

    private void Spawn(int emitterIndex, double phase, Matrix? distanceRoot = null)
    {
        var state = _emitters[emitterIndex];
        var definition = state.Definition;
        if (state.ActiveCount >= definition.MaximumParticles) return;
        if (_particles.Count >= ParticleLimit) { CapacityReached = true; return; }
        var random = state.Random;
        var life = (definition.Life.Sample("x", phase, random.NextSingle()) + state.LifeBias) / state.TimeScale;
        if (!(life > 0) || !float.IsFinite(life)) return;
        var orientation = definition.Orientation.Vector(phase, RandomVector(random));
        var rotation = Rotation(orientation);
        var position = definition.Position.Vector(phase, RandomVector(random));
        var box = definition.SpawnBox.Vector(phase, RandomVector(random));
        var randomBox = RandomVector(random) - new Vector3(0.5f);
        position += Vector3.TransformNormal(box * randomBox, rotation);
        var inner = definition.SpawnRadius.Sample("inner_radius", phase, random.NextSingle());
        var outer = definition.SpawnRadius.Sample("outer_radius", phase, random.NextSingle());
        // Native fixed negative radii still emit a ring in the emitter plane.
        if (inner < 0 && outer == inner)
            inner = outer = -inner;
        inner = Math.Max(0, inner);
        outer = Math.Max(inner, outer);
        if (outer > 0)
        {
            var radius = MathF.Sqrt(inner * inner + (outer * outer - inner * inner) * random.NextSingle());
            var angle = random.NextSingle() * MathHelper.TwoPi;
            position += Vector3.TransformNormal(new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius), rotation);
        }
        var azimuth = random.NextSingle();
        var theta = MathHelper.ToRadians(definition.SpawnAngle.Sample(azimuth));
        var phi = MathHelper.ToRadians(definition.SpawnSpread.Sample(random.NextSingle()));
        // Preserve the float pole residue: native horizontal sprites still form a plane near vertical.
        var direction = definition.ZeroSpawnArc
            ? new Vector3(MathF.Cos(theta) * MathF.Cos(MathHelper.PiOver2), 1, MathF.Sin(theta) * MathF.Cos(MathHelper.PiOver2))
            : definition.DefaultVerticalSpawn || definition.LegacySpreadSpawn
            ? new Vector3(MathF.Cos(phi) * MathF.Cos(theta), MathF.Sin(phi), MathF.Cos(phi) * MathF.Sin(theta))
            : new Vector3(MathF.Sin(theta) * MathF.Cos(phi), MathF.Cos(theta), MathF.Sin(theta) * MathF.Sin(phi));
        var velocity = Vector3.TransformNormal(direction, rotation) * definition.Speed.Sample("x", phase, random.NextSingle()) * state.TimeScale;
        var birthRoot = distanceRoot ?? CurrentRoot(state, _simulatedTime);
        if (definition.SpawnInheritedSpeed != 0 && state.MotionVelocity.LengthSquared() > 0)
        {
            var speed = state.MotionVelocity.Length();
            var authoredDirection = Vector3.TransformNormal(Vector3.TransformNormal(direction, rotation), RotationOnly(birthRoot));
            var inherited = Vector3.Lerp(state.MotionVelocity / speed, authoredDirection, definition.SpawnInheritedDirectionBlend)
                * (speed * definition.SpawnInheritedSpeed);
            var determinant = birthRoot.Determinant();
            if (float.IsFinite(determinant) && determinant != 0)
                // Native integrates source time; managed velocities instead integrate the fixed preview clock.
                velocity += Vector3.TransformNormal(inherited, Matrix.Invert(birthRoot)) * state.TimeScale;
            else VelocityScaleUnsupported = true;
        }
        var atlas = definition.Atlas;
        var atlasCount = atlas.RowRestricted ? atlas.Columns : atlas.Columns * atlas.Rows;
        var cell = atlas.Random ? random.Next(atlasCount) : atlas.Cell;
        if (atlas.Random && !atlas.Animated) cell = (cell + atlas.Cell) % atlasCount;
        var row = atlas.RowRestricted ? (atlas.RandomRow ? random.Next(atlas.Rows) : atlas.Row) : 0;
        _particles.Add(new Particle
        {
            Emitter = state, Birth = _simulatedTime, Life = life, Position = position, Velocity = velocity,
            MotionOrigin = position, MotionInitialVelocity = velocity,
            Root = birthRoot,
            InitialScale = definition.InitialScale.Vector(phase, RandomVector(random)),
            InitialAlpha = definition.InitialAlpha.Sample("x", phase, random.NextSingle()) * state.Tint.W,
            Orientation = Vector3.Lerp(definition.OrientationMin, definition.OrientationMax, random.NextSingle()),
            RandomScale = RandomVector(random), RandomColour = RandomVector(random), RandomAlpha = random.NextSingle(),
            Cell = cell, Row = row, Loops = atlas.Loops.Sample(random.NextSingle()),
            ModelVariant = definition.ModelVariants.Count == 0 ? 0 : random.Next(definition.ModelVariants.Count + 1),
        });
        var spawned = _particles[^1];
        spawned.BirthRotation = ModelRotation(spawned.Orientation);
        if (definition.RelativeOrientation)
        {
            var emitterRotation = definition.ModelQuaternionSpin && definition.ModelEmitterOrientation is { } emitterCurve
                ? Matrix.CreateFromQuaternion(emitterCurve.Sample(Phase(spawned.Birth - state.Start, state.Duration, definition.ModelEmitterBirthLoop), state.RandomRate))
                : Rotation(orientation);
            spawned.BirthRotation *= emitterRotation * RotationOnly(birthRoot);
            // Keep the existing legacy/model representation separate from the sprite birth matrix.
            spawned.Orientation += orientation;
        }
        if (definition.DynamicSpinDamping)
        {
            // Reuse independent, unused colour channels without shifting non-angular birth randomness.
            var spinDirection = definition.SpinDirection.Z;
            var spinSpeed = !float.IsFinite(spinDirection) ? 0
                : (definition.SpinMin.Z + (definition.SpinMax.Z - definition.SpinMin.Z) * spawned.RandomColour.Y)
                    * (spinDirection == 0 ? spawned.RandomColour.Z < .5f ? -1 : 1 : spinDirection);
            spawned.Spin = new(0, 0, spinSpeed);
            spawned.SpinRotation = Quaternion.CreateFromRotationMatrix(spawned.BirthRotation);
            spawned.AngularSpeed = MathHelper.ToRadians(spinSpeed);
            spawned.SpinTime = spawned.Birth;
        }
        else if (definition.SpinMin != Vector3.Zero || definition.SpinMax != Vector3.Zero)
        {
            float Spin(float min, float max, float direction) => !float.IsFinite(direction) ? 0
                : (min + (max - min) * random.NextSingle()) * (direction == 0 ? random.Next(2) * 2 - 1 : direction);
            _particles[^1].Spin = new(Spin(definition.SpinMin.X, definition.SpinMax.X, definition.SpinDirection.X),
                Spin(definition.SpinMin.Y, definition.SpinMax.Y, definition.SpinDirection.Y),
                Spin(definition.SpinMin.Z, definition.SpinMax.Z, definition.SpinDirection.Z));
        }
        if (definition.ComplexSpinDamping || definition.ModelQuaternionSpin)
        {
            spawned.SpinRootRotation = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(RotationOnly(birthRoot)));
            spawned.SpinRotation = Quaternion.CreateFromRotationMatrix(spawned.BirthRotation);
            spawned.AngularVelocity = Vector3.TransformNormal(spawned.Spin * (MathHelper.Pi / 180), spawned.BirthRotation);
            spawned.SpinTime = spawned.Birth;
        }
        state.ActiveCount++;
        TriggerEvents(_particles[^1], false);
    }

    public EffekseerParticle[] Frame(CommonShaderParameters camera)
        => ProjectedFrame(camera).Select(particle => particle.Render).ToArray();

    public VfxPreviewParticle[] ProjectedFrame(CommonShaderParameters camera)
    {
        TerrainBasisUnsupported = false;
        if (_groundHeight != camera.VfxGroundHeight)
        {
            _groundHeight = camera.VfxGroundHeight;
            if (conversion.Emitters.Any(e => e.RequiresFlatTerrain && e.Events.Count > 0))
            {
                var time = (float)_requestedTime; Reset(); SeekCore(time, _attachment, _attachmentAtTime, _useCurrentAttachmentForFrame, _continueMotionTrails);
            }
        }
        var result = new List<VfxPreviewParticle>(_particles.Count);
        var inverseView = Matrix.Invert(camera.View);
        inverseView.Translation = Vector3.Zero;
        foreach (var particle in _particles)
        {
            var definition = particle.Emitter.Definition;
            if (definition.IsEventHelper || particle.Emitter.ModelAngularUnsupported) continue;
            if (particle.Emitter.Parent != null)
            {
                EventAnchor(particle.Emitter, _requestedTime, out var valid, currentFrame: true);
                if (!valid) { TerrainBasisUnsupported = true; continue; }
            }
            var age = Math.Clamp(AppearanceAge(particle, definition, _requestedTime), 0, particle.Life);
            var phase = age / particle.Life;
            var emitterState = particle.Emitter;
            var root = ParticleRoot(particle, _requestedTime, currentFrame: true);
            var position = particle.Position;
            var velocity = particle.Velocity;
            if (definition.ConstantForceDamping != null && ForceBasisSupported(particle, definition))
                (position, velocity) = ConstantMotion(particle, definition, age);
            var inheritedScale = EventScale(emitterState, _requestedTime, currentFrame: true);
            var worldPosition = Vector3.Transform(position * inheritedScale, root);
            // Native terrain birth keeps the emitter height offset while uniform scale changes geometry.
            var terrainBasis = definition.RequiresFlatTerrain && definition.Kind != 2 && definition.SpawnOnTerrain
                && root.M11 > 0 && float.IsFinite(root.M11) ? Matrix.CreateScale(1 / root.M11) * root : root;
            if (definition.RequiresFlatTerrain && (camera.VfxGroundHeight is not { } ground || !float.IsFinite(ground)
                || !MatrixClose(terrainBasis, Matrix.CreateTranslation(root.Translation))
                || definition.Kind == 2 && definition.SpawnOnTerrain && Math.Abs(worldPosition.Y - ground) > .00001f))
            {
                TerrainBasisUnsupported = true;
                continue;
            }
            if (definition.RequiresFlatTerrain && definition.Kind != 2 && definition.SpawnOnTerrain)
                worldPosition.Y = camera.VfxGroundHeight!.Value + position.Y;
            var distanceAlpha = 1f;
            if (definition.ParticleCullDistances is { } distances)
            {
                var distance = Vector3.Distance(camera.CameraPosition, worldPosition);
                if (distance < distances.X || distance > distances.W) continue;
                if (distance < distances.Y) distanceAlpha *= (distance - distances.X) / (distances.Y - distances.X);
                if (distance > distances.Z) distanceAlpha *= (distances.W - distance) / (distances.W - distances.Z);
                if (distanceAlpha <= 0) continue;
            }
            var worldVelocity = Vector3.TransformNormal(velocity, root);
            var scale = definition.Scale.Vector(phase, new(particle.RandomScale.X)) * particle.InitialScale;
            if (!root.Decompose(out var rootScale, out _, out _)) rootScale = Vector3.One;
            scale *= rootScale * inheritedScale;
            if (definition.Kind == 2 && (MathF.Abs(scale.X) < 0.0000001f || MathF.Abs(scale.Y) < 0.0000001f || MathF.Abs(scale.Z) < 0.0000001f))
                continue;
            if (definition.Kind == 0)
            {
                if (MathF.Abs(scale.X) < 0.0000001f || MathF.Abs(scale.Y) < 0.0000001f) continue;
                scale.Z = 1;
            }
            var spinAge = (_requestedTime - particle.Birth) * emitterState.TimeScale;
            var spinDamping = definition.SpinDeceleration.Sample("x", 0, particle.RandomAlpha);
            var spinTime = spinDamping > 0 ? (1 - Math.Exp(-spinDamping * spinAge)) / spinDamping : spinAge;
            var orientation = particle.Orientation + particle.Spin * (float)spinTime;
            var reflectedCamera = camera.Projection.M11 * camera.Projection.M22 - camera.Projection.M12 * camera.Projection.M21 < 0;
            var spriteRotation = definition.Kind == 0 ? SpriteRotation(particle, _requestedTime, root) : Matrix.Identity;
            var basis = definition.Alignment == 2 ? (definition.Kind == 0 ? spriteRotation
                    : definition.ModelQuaternionSpin ? Matrix.CreateFromQuaternion(ModelSpinAt(particle, _requestedTime, root).Rotation)
                    : (definition.Kind == 1 ? ModelRotation(orientation) : Rotation(orientation)) * RotationOnly(root)) :
                definition.Alignment == 5 ? Matrix.CreateRotationZ(MathHelper.ToRadians(orientation.Z))
                    * Matrix.CreateRotationX(MathHelper.PiOver2) * RotationOnly(root) :
                Matrix.CreateRotationZ(MathHelper.ToRadians(definition.Alignment == 0 && reflectedCamera ? -orientation.Z : orientation.Z)) * inverseView;
            if (emitterState.ModelAngularUnsupported) continue;
            var localTransform = Matrix.CreateScale(scale);
            if (definition.Kind == 0 && definition.Alignment is 0 or 5 or 6)
            {
                localTransform *= SpritePlane(spriteRotation, (definition.Alignment is 0 or 6) && reflectedCamera);
                basis = definition.Alignment == 5 ? Matrix.CreateRotationX(MathHelper.PiOver2) : inverseView;
            }
            var shadingBasis = spriteRotation * (definition.Alignment is 0 or 6 ? inverseView
                : definition.Alignment == 5 ? Matrix.CreateRotationX(MathHelper.PiOver2) : Matrix.Identity);
            var hasShadingBasis = definition.Alignment is 0 or 2 or 5 or 6;
            var velocityStretch = 1f;
            var distortionNormalBasis = Vector4.Zero;
            if (definition.Alignment == 6)
            {
                // Native restriction=1 adds rotated local Y in world space and local X in view space.
                if (definition.Kind != 0)
                    localTransform *= SpritePlane(Rotation(orientation) * RotationOnly(root), reflectedCamera);
                basis = Matrix.Identity;
                basis.Right = inverseView.Right; basis.Up = Vector3.Up;
                basis.Backward = Vector3.Cross(basis.Right, basis.Up);
            }
            var horizontalVelocityPlane = definition.Alignment is 4 or 7;
            if (horizontalVelocityPlane && (worldVelocity.LengthSquared() <= 0.000001f
                || Vector3.Cross(Vector3.Up, worldVelocity).LengthSquared() == 0)) continue;
            if (definition.Alignment is 1 or 3 or 4 or 7 && worldVelocity.LengthSquared() > 0.000001f
                && (horizontalVelocityPlane || Vector3.DistanceSquared(camera.CameraPosition, worldPosition) > 0.000001f))
            {
                var viewDirection = horizontalVelocityPlane ? Vector3.Up : Vector3.Normalize(camera.CameraPosition - worldPosition);
                var look = Vector3.Normalize(worldVelocity);
                var side = Vector3.Cross(viewDirection, look);
                var sideLength = side.Length();
                side = sideLength > (horizontalVelocityPlane ? 0 : 0.000001f) ? side / sideLength : inverseView.Right;
                if (definition.Alignment == 3)
                    side = Vector3.Lerp(inverseView.Right, side, Math.Clamp(sideLength * 100, 0, 1));
                var up = definition.Alignment == 3 ? Vector3.Normalize(Vector3.Cross(side, viewDirection)) : look;
                // Native yaw-only mode projects velocity onto the horizontal plane, but stretches by its full length.
                if (definition.Alignment == 7) up = Vector3.Normalize(Vector3.Cross(side, Vector3.Up));
                basis = Matrix.Identity;
                basis.Right = side; basis.Up = up; basis.Backward = Vector3.Cross(side, up);
                var stretch = 1f;
                if (MathF.Abs(rootScale.X - rootScale.Y) < 0.00001f && MathF.Abs(rootScale.X - rootScale.Z) < 0.00001f
                    && rootScale.X > 0.000001f)
                {
                    stretch = worldVelocity.Length() * definition.VelocityStretch.Sample("x", phase, particle.RandomAlpha) / rootScale.X;
                    stretch = Math.Min(definition.VelocityStretchCap, stretch + (definition.TrailMode ? 0 : 1));
                    if (definition.Alignment == 3) stretch = Math.Max(1, stretch * Vector3.Dot(look, up));
                }
                else VelocityScaleUnsupported = true;
                velocityStretch = stretch;
                // The game's legacy velocity shader rotates in the particle plane before stretching that plane.
                // Independent birth orientation follows only the later full-local rotation delta.
                var rotation = definition.Kind == 0 ? spriteRotation : Rotation(orientation);
                if (definition.Kind != 0)
                {
                    if (definition.RelativeOrientation) rotation *= RotationOnly(root);
                    else if (definition.SimulationSpace == 1)
                        rotation *= Matrix.Transpose(RotationOnly(particle.Root)) * RotationOnly(root);
                }
                shadingBasis = rotation * basis;
                hasShadingBasis = true;
                localTransform *= SpritePlane(rotation, false) * Matrix.CreateScale(1, stretch, 1);
            }
            if (definition.Kind == 0 && definition.MinimumPixelSize > 0)
            {
                var pixelScale = MinimumPixelScale(definition.MinimumPixelSize, scale, velocityStretch, worldPosition, camera);
                scale.X *= pixelScale; scale.Y *= pixelScale;
                localTransform = Matrix.CreateScale(pixelScale, pixelScale, 1) * localTransform;
            }
            // Native normals retain the full orientation even when geometry projects it onto a billboard plane.
            if (definition.Kind == 0 && hasShadingBasis && definition.Render.Distortion != 0)
                distortionNormalBasis = definition.Render.TextureIndex >= 0
                    ? new(shadingBasis.Right.X, shadingBasis.Right.Y, shadingBasis.Up.X, shadingBasis.Up.Y)
                    : new(shadingBasis.Backward, 1);
            var lightingRotation = Vector4.Zero;
            if (definition.Kind == 0 && hasShadingBasis && definition.Render.LightingMode != 0)
            {
                var normalBasis = definition.Alignment == 3 ? Matrix.CreateRotationY(MathHelper.Pi) * shadingBasis : shadingBasis;
                var normalRotation = Quaternion.CreateFromRotationMatrix(normalBasis);
                lightingRotation = new(normalRotation.X, normalRotation.Y, normalRotation.Z, normalRotation.W);
            }
            var colour = new Vector3(definition.Colour.Sample("r", phase, particle.RandomColour.X),
                definition.Colour.Sample("g", phase, particle.RandomColour.X), definition.Colour.Sample("b", phase, particle.RandomColour.X));
            colour *= definition.Colour.Sample("brightness", phase, particle.RandomColour.X);
            colour *= new Vector3(emitterState.Tint.X, emitterState.Tint.Y, emitterState.Tint.Z);
            var inheritedTint = EventTint(emitterState, _requestedTime);
            colour *= new Vector3(inheritedTint.X, inheritedTint.Y, inheritedTint.Z);
            var authoredAlpha = definition.AlphaAt(phase, particle.RandomAlpha) * particle.InitialAlpha * inheritedTint.W;
            var customSprite = definition.Kind == 0 && definition.Render.MeshMaterialIndex >= 0;
            var rawSpriteAlpha = customSprite || definition.Kind == 0 && (definition.PremultipliedAlpha || definition.Render.Distortion != 0);
            var rawMaterialAlpha = definition.Kind == 1 && definition.RenderParts(particle.ModelVariant)
                .Any(part => part.OpaqueModel != 0 && part.MeshMaterialIndex >= 0);
            var alpha = rawSpriteAlpha || rawMaterialAlpha ? Math.Max(0, authoredAlpha) : Math.Clamp(authoredAlpha, 0, 1);
            alpha *= distanceAlpha;
            var flipbook = definition.Atlas.Animated
                ? definition.Atlas.FixedSpeed
                    ? definition.Atlas.FrameAtSeconds(phase, age * emitterState.TimeScale, particle.RandomAlpha)
                    : definition.Atlas.FrameAtLife(phase, particle.RandomAlpha)
                : AtlasFrame(definition.Atlas, particle.Cell, particle.Loops, phase);
            var atlasRow = definition.Atlas.Animated ? definition.Atlas.RowAt(particle.RandomAlpha) : particle.Row;
            var uv = AtlasRectangle(definition.Atlas, particle.Cell, atlasRow, particle.Loops, phase, flipbook);
            // Native flips decorrelate the same unary random value without consuming another birth sample.
            var flipU = particle.RandomAlpha * 251; var flipV = particle.RandomAlpha * 149;
            if (flipU - MathF.Floor(flipU) > 1 - definition.UvFlipProbability.X) { uv.X += uv.Z; uv.Z = -uv.Z; }
            if (flipV - MathF.Floor(flipV) > 1 - definition.UvFlipProbability.Y) { uv.Y += uv.W; uv.W = -uv.W; }
            var native = new EffekseerParticle
            {
                Layer = emitterState.DefinitionIndex, Transform = localTransform * basis * Matrix.CreateTranslation(worldPosition),
                Colour = new(colour, alpha), UV = uv,
                Time = (float)age,
                MaterialTime = (float)_requestedTime,
                AppearancePhase = (float)phase,
                AlphaCrush = definition.AlphaCrush == null ? Vector2.Zero
                    : new(definition.AlphaCrush.Sample("progress", phase, particle.RandomAlpha), definition.SharpAlphaCrush ? 2 : 1),
                SoftParticleScale = Math.Min(scale.X, scale.Y),
                ParticleSize = definition.Kind == 0 && definition.Render.LightingMode != 0 ? scale.X
                    : customSprite ? Math.Min(Math.Abs(scale.X), Math.Abs(scale.Y))
                    : definition.Render.Distortion != 0 ? Math.Min(scale.X, scale.Y) : 0,
                Flipbook = flipbook,
                NormalRotation = definition.Render.Distortion != 0 ? distortionNormalBasis : lightingRotation,
            };
            result.Add(new(native, particle.ModelVariant));
        }
        if (ModelSpinRangeUnsupported)
        {
            var disabled = _emitters.Where(state => state.ModelAngularUnsupported).Select(state => state.DefinitionIndex).ToHashSet();
            result.RemoveAll(particle => disabled.Contains(particle.Render.Layer));
        }
        // Alpha particles within a material are drawn back to front; authored layer order is retained.
        return result.OrderBy(x => x.Render.Layer).ThenByDescending(x => Vector3.DistanceSquared(x.Render.Transform.Translation, camera.CameraPosition)).ToArray();
    }

    private static Matrix SpriteRotation(Particle particle, double time, Matrix root)
    {
        if (particle.Emitter.Definition.DynamicSpinDamping)
            return Matrix.CreateFromQuaternion(DynamicSpinAt(particle, time).Rotation);
        if (particle.Emitter.Definition.ComplexSpinDamping)
            return Matrix.CreateFromQuaternion(ComplexSpinAt(particle, time).Rotation);
        var age = Math.Max(0, time - particle.Birth) * particle.Emitter.TimeScale;
        var damping = particle.Emitter.Definition.SpinDeceleration.Sample("x", 0, particle.RandomAlpha);
        var spinTime = damping > 0 ? (1 - Math.Exp(-damping * age)) / damping : age;
        var speed = particle.Spin.Length();
        var rotation = speed > 0
            ? Matrix.CreateFromAxisAngle(particle.Spin / speed, MathHelper.ToRadians(speed * (float)spinTime)) * particle.BirthRotation
            : particle.BirthRotation;
        if (particle.Emitter.Definition.SimulationSpace == 1)
            rotation *= Matrix.Transpose(RotationOnly(particle.Root)) * RotationOnly(root);
        return rotation;
    }

    private static (Quaternion Rotation, float Speed) DynamicSpinAt(Particle particle, double time)
    {
        var sourceDelta = (float)(Math.Max(0, time - particle.SpinTime) * particle.Emitter.TimeScale);
        if (sourceDelta == 0) return (particle.SpinRotation, particle.AngularSpeed);
        var sourceAge = Math.Max(0, time - particle.Birth) * particle.Emitter.TimeScale;
        var phase = sourceAge / (particle.Life * particle.Emitter.TimeScale + .000001);
        var damping = particle.Emitter.Definition.SpinDeceleration.Sample("x", phase, particle.RandomAlpha);
        var speed = particle.AngularSpeed * MathF.Exp(-damping * sourceDelta);
        var q = particle.SpinRotation;
        var derivative = Quaternion.Multiply(new Quaternion(0, 0, speed, 0), q);
        var halfDelta = sourceDelta * .5f;
        // Native decays world Z first, then normalizes an explicit quaternion step.
        return (Quaternion.Normalize(new(q.X + derivative.X * halfDelta, q.Y + derivative.Y * halfDelta,
            q.Z + derivative.Z * halfDelta, q.W + derivative.W * halfDelta)), speed);
    }

    private static (Quaternion Rotation, Vector3 Velocity) ComplexSpinAt(Particle particle, double time)
    {
        var sourceDelta = (float)(Math.Max(0, time - particle.SpinTime) * particle.Emitter.TimeScale);
        if (sourceDelta == 0) return (particle.SpinRotation, particle.AngularVelocity);
        var sourceAge = Math.Max(0, time - particle.Birth) * particle.Emitter.TimeScale;
        var phase = sourceAge / (particle.Life * particle.Emitter.TimeScale + .000001);
        var damping = particle.Emitter.Definition.SpinDeceleration.Sample("x", phase, particle.RandomAlpha);
        var velocity = particle.AngularVelocity;
        velocity.Z *= MathF.Exp(-damping * sourceDelta);
        var q = particle.SpinRotation;
        var derivative = Quaternion.Multiply(new Quaternion(velocity, 0), q);
        var halfDelta = sourceDelta * .5f;
        return (Quaternion.Normalize(new(q.X + derivative.X * halfDelta, q.Y + derivative.Y * halfDelta,
            q.Z + derivative.Z * halfDelta, q.W + derivative.W * halfDelta)), velocity);
    }

    private (Quaternion Rotation, Vector3 Velocity, Quaternion RootRotation) ModelSpinAt(Particle particle, double time, Matrix? root = null)
    {
        var definition = particle.Emitter.Definition;
        if (particle.Emitter.ModelAngularUnsupported)
            return (particle.SpinRotation, particle.AngularVelocity, particle.SpinRootRotation);
        var sourceDelta = (float)(Math.Max(0, time - particle.SpinTime) * particle.Emitter.TimeScale);
        var velocity = particle.AngularVelocity;
        var q = particle.SpinRotation;
        if (sourceDelta > 0)
        {
            var sourceAge = Math.Max(0, time - particle.Birth) * particle.Emitter.TimeScale;
            var phase = sourceAge / (particle.Life * particle.Emitter.TimeScale + .000001);
            var damping = definition.ModelSpinDeceleration?.Sample(phase, particle.RandomAlpha)
                ?? definition.SpinDeceleration.Sample("x", phase, particle.RandomAlpha);
            // The native rigid CPU path damps XYZ together; the sprite CS only damps world Z.
            velocity *= MathF.Exp(-damping * sourceDelta);
            var derivative = Quaternion.Multiply(new Quaternion(velocity, 0), q);
            var halfDelta = sourceDelta * .5f;
            q = Quaternion.Normalize(new(q.X + derivative.X * halfDelta, q.Y + derivative.Y * halfDelta,
                q.Z + derivative.Z * halfDelta, q.W + derivative.W * halfDelta));
        }
        var rootRotation = particle.SpinRootRotation;
        if (definition.SimulationSpace == 1)
        {
            var currentRoot = root ?? CurrentRoot(particle.Emitter, time);
            var decomposed = currentRoot.Decompose(out var scale, out var rotation, out _);
            var nextRootRotation = decomposed ? Quaternion.Normalize(rotation) : Quaternion.Identity;
            if (!decomposed
                || scale.X <= 0 || scale.Y <= 0 || scale.Z <= 0
                || Math.Abs(scale.X - scale.Y) > .00001f || Math.Abs(scale.X - scale.Z) > .00001f
                || !MatrixClose(currentRoot, Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(nextRootRotation)
                    * Matrix.CreateTranslation(currentRoot.Translation)))
                ModelSpinBasisUnsupported = true;
            if (definition.ModelEmitterOrientation is { } emitterCurve)
            {
                var state = particle.Emitter;
                var previousEmitter = emitterCurve.Sample(Phase(particle.SpinTime - state.Start, state.Duration, definition.ModelEmitterFollowLoop), state.RandomRate);
                var currentEmitter = emitterCurve.Sample(Phase(time - state.Start, state.Duration, definition.ModelEmitterFollowLoop), state.RandomRate);
                var emitterDelta = time > particle.SpinTime
                    ? Quaternion.Multiply(currentEmitter, Quaternion.Conjugate(previousEmitter)) : Quaternion.Identity;
                var nodeDelta = Quaternion.Multiply(nextRootRotation, Quaternion.Conjugate(rootRotation));
                // Native emitter changes are conjugated by the current node, after its own delta.
                var delta = Quaternion.Normalize(Quaternion.Multiply(nodeDelta, Quaternion.Multiply(nextRootRotation,
                    Quaternion.Multiply(emitterDelta, Quaternion.Conjugate(nextRootRotation)))));
                q = Quaternion.Normalize(Quaternion.Multiply(delta, q));
                velocity = Vector3.Transform(velocity, delta);
            }
            else if (nextRootRotation != rootRotation)
            {
                // Native transports both world-space angular state fields after this step's own spin.
                var delta = Quaternion.Normalize(Quaternion.Multiply(nextRootRotation, Quaternion.Conjugate(rootRotation)));
                q = Quaternion.Normalize(Quaternion.Multiply(delta, q));
                velocity = Vector3.Transform(velocity, delta);
            }
            rootRotation = nextRootRotation;
        }
        if (!float.IsFinite(velocity.X) || !float.IsFinite(velocity.Y) || !float.IsFinite(velocity.Z)
            || !float.IsFinite(q.X) || !float.IsFinite(q.Y) || !float.IsFinite(q.Z) || !float.IsFinite(q.W)
            || !float.IsFinite(q.LengthSquared()) || q.LengthSquared() < .5f)
        {
            // Do not turn native float overflow into an invented angular-velocity cap.
            particle.Emitter.ModelAngularUnsupported = true;
            ModelSpinRangeUnsupported = true;
            return (particle.SpinRotation, Vector3.Zero, particle.SpinRootRotation);
        }
        return (q, velocity, rootRotation);
    }

    private static Matrix SpritePlane(Matrix rotation, bool reflected)
    {
        var plane = Matrix.Identity;
        plane.M11 = rotation.M11; plane.M12 = reflected ? -rotation.M12 : rotation.M12;
        plane.M21 = reflected ? -rotation.M21 : rotation.M21; plane.M22 = rotation.M22;
        return plane;
    }

    private void UpdateMotionRoot(EmitterState state, double time, bool currentFrame = false, bool preserveVelocity = false)
    {
        if (state.Definition.SpawnInheritedSpeed == 0) return;
        var root = CurrentRoot(state, time, currentFrame);
        var elapsed = time - state.MotionTime;
        if (!preserveVelocity && state.MotionRoot is { } previous && elapsed != 0)
            state.MotionVelocity = (root.Translation - previous.Translation) / (float)elapsed;
        state.MotionRoot = root;
        state.MotionTime = time;
    }

    private Matrix CurrentRoot(EmitterState state, double time, bool currentFrame = false) => state.ReferenceTransform * (state.Parent == null
        ? currentFrame && _useCurrentAttachmentForFrame ? _attachment : _attachmentAtTime?.Invoke(Math.Max(0, (float)time)) ?? _attachment
        : EventAnchor(state, time, out _, currentFrame));

    private Matrix ParticleRoot(Particle particle, double time, bool currentFrame = false)
    {
        var current = CurrentRoot(particle.Emitter, time, currentFrame);
        var space = particle.Emitter.Definition.SimulationSpace;
        var root = space == 1 ? current : particle.Root;
        if (space == 0) root.Translation = current.Translation;
        return root;
    }

    private Matrix EventAnchor(EmitterState state, double time, out bool valid, bool currentFrame = false)
    {
        if (state.FrozenAnchor is { } frozen) { valid = state.FrozenAnchorValid; return frozen; }
        valid = true;
        var parent = state.Parent!; var definition = parent.Emitter.Definition;
        if (parent.Emitter.Parent != null)
        {
            EventAnchor(parent.Emitter, time, out var ancestorValid, currentFrame);
            if (!ancestorValid) { valid = false; return Matrix.Identity; }
        }
        var root = ParticleRoot(parent, Math.Min(time, parent.Birth + parent.Life), currentFrame);
        // Visible rigid models retain the calibrated birth anchor; no-model helpers follow their motion.
        var position = definition.Kind == 1 && !definition.IsEventHelper ? parent.MotionOrigin : parent.Position;
        if (definition.ConstantForceDamping != null && ForceBasisSupported(parent, definition))
            (position, _) = ConstantMotion(parent, definition, Math.Min(parent.Life, time - parent.Birth));
        var world = Vector3.Transform(position * EventScale(parent.Emitter, time, currentFrame), root);
        if (definition.RequiresFlatTerrain && definition.SpawnOnTerrain)
        {
            var terrainBasis = root.M11 > 0 ? Matrix.CreateScale(1 / root.M11) * root : root;
            valid = _groundHeight is { } ground && float.IsFinite(ground)
                && MatrixClose(terrainBasis, Matrix.CreateTranslation(root.Translation));
            if (valid) world.Y = _groundHeight!.Value + position.Y;
        }
        var age = Math.Clamp(time - parent.Birth, 0, parent.Life) * parent.Emitter.TimeScale;
        var damping = definition.SpinDeceleration.Sample("x", 0, parent.RandomAlpha);
        var spinTime = damping > 0 ? (1 - Math.Exp(-damping * age)) / damping : age;
        var orientation = state.Event!.Orientation == "orientation"
            ? (definition.Kind == 1 ? ModelRotation(parent.Orientation + parent.Spin * (float)spinTime)
                : Rotation(parent.Orientation + parent.Spin * (float)spinTime)) * RotationOnly(root) : Matrix.Identity;
        var anchor = orientation * Matrix.CreateTranslation(world);
        if (!state.Event.TrackPosition && state.SpawnAnchor.HasValue) anchor.Translation = state.SpawnAnchor.Value.Translation;
        return anchor;
    }

    private Vector3 EventScale(EmitterState state, double time, bool currentFrame = false)
    {
        if (state.FrozenScale is { } frozen) return frozen;
        if (state.Parent is not { } parent || state.Event is not { TrackScale: true }) return Vector3.One;
        var definition = parent.Emitter.Definition;
        var phase = Math.Clamp(AppearanceAge(parent, definition, time) / parent.Life, 0, 1);
        var root = ParticleRoot(parent, time, currentFrame);
        if (!root.Decompose(out var rootScale, out _, out _)) rootScale = Vector3.One;
        return definition.Scale.Vector(phase, new(parent.RandomScale.X)) * parent.InitialScale * rootScale * EventScale(parent.Emitter, time, currentFrame);
    }

    private Vector4 EventTint(EmitterState state, double time)
    {
        if (state.FrozenTint is { } frozen) return frozen;
        if (state.Parent is not { } parent || state.Event is not { } particleEvent) return Vector4.One;
        var definition = parent.Emitter.Definition;
        var phase = Math.Clamp(AppearanceAge(parent, definition, time) / parent.Life, 0, 1);
        var inherited = EventTint(parent.Emitter, time); var tint = parent.Emitter.Tint;
        var colour = particleEvent.TrackColour ? new Vector3(definition.Colour.Sample("r", phase, parent.RandomColour.X),
            definition.Colour.Sample("g", phase, parent.RandomColour.X), definition.Colour.Sample("b", phase, parent.RandomColour.X))
            * definition.Colour.Sample("brightness", phase, parent.RandomColour.X)
            * new Vector3(tint.X * inherited.X, tint.Y * inherited.Y, tint.Z * inherited.Z) : Vector3.One;
        var alpha = particleEvent.TrackAlpha ? definition.AlphaAt(phase, parent.RandomAlpha) * parent.InitialAlpha * inherited.W : 1;
        return new(colour, alpha);
    }

    private void FreezeEvent(EmitterState state)
    {
        var anchor = EventAnchor(state, _simulatedTime, out var valid);
        state.FrozenScale = EventScale(state, _simulatedTime);
        state.FrozenTint = EventTint(state, _simulatedTime);
        state.FrozenAnchor = anchor; state.FrozenAnchorValid = valid;
    }

    private double AppearanceAge(Particle particle, VfxPreviewEmitter definition, double time)
    {
        var age = time - particle.Birth;
        if (definition.MeshLoop is not { } loop) return age;
        var emitter = particle.Emitter;
        return LoopedAge(age, particle.Life, loop, definition.Infinite ? null : emitter.Start + emitter.Duration - particle.Birth);
    }

    internal static double LoopedAge(double age, double life, Vector2 loop, double? stopAge = null)
    {
        var end = loop.Y * life; var start = loop.X * life;
        double ActiveAge(double value) => value < end ? value : start + (value - end) % (end - start);
        return stopAge is { } stop && age >= stop ? ActiveAge(stop) + age - stop : ActiveAge(age);
    }

    internal static Vector4 AtlasRectangle(VfxAtlas atlas, int startCell, int row, float loops, double phase, float? frameOverride = null)
    {
        var frame = (int)Math.Floor(frameOverride ?? AtlasFrame(atlas, startCell, loops, phase));
        frame += row * atlas.Columns;
        var width = 1f / (atlas.OuterColumns * atlas.Columns);
        var height = 1f / (atlas.OuterRows * atlas.Rows);
        return new((atlas.OuterCell % atlas.OuterColumns) / (float)atlas.OuterColumns + frame % atlas.Columns * width,
            (atlas.OuterCell / atlas.OuterColumns) / (float)atlas.OuterRows + frame / atlas.Columns * height, width, height);
    }

    internal static float AtlasFrame(VfxAtlas atlas, int startCell, float loops, double phase)
    {
        var count = atlas.RowRestricted ? atlas.Columns : atlas.Columns * atlas.Rows;
        var frame = startCell + (atlas.Animated ? phase * Math.Max(0, loops) * count : 0);
        return (float)(atlas.Loop ? (frame % count + count) % count : Math.Clamp(frame, 0, count - 1));
    }

    private static double Phase(double time, float duration, bool loop)
    {
        var phase = Math.Max(0, time) / duration;
        return loop ? phase - Math.Floor(phase) : Math.Clamp(phase, 0, 1);
    }

    private static float MinimumPixelScale(float limit, Vector3 scale, float stretch, Vector3 position, CommonShaderParameters camera)
    {
        if (!(camera.ViewportWidth > 0) || !float.IsFinite(camera.ViewportWidth) || !(scale.X > 0)) return 1;
        var distance = Vector3.Distance(position, camera.CameraPosition);
        var edge = Vector4.Transform(new Vector4(scale.X, scale.Y * stretch, distance, 1), camera.Projection);
        var origin = Vector4.Transform(new Vector4(0, 0, distance, 1), camera.Projection);
        if (edge.W == 0 || origin.W == 0) return 1;
        // Projection reflection changes screen direction, not the authored signed particle width.
        var pixels = Math.Abs(edge.X / edge.W - origin.X / origin.W) * camera.ViewportWidth * .5f;
        var factor = limit / pixels;
        return pixels > 0 && float.IsFinite(factor) ? Math.Max(1, factor) : 1;
    }

    private bool ForceBasisSupported(Particle particle, VfxPreviewEmitter definition)
    {
        var emitter = particle.Emitter;
        var basis = ParticleRoot(particle, _simulatedTime);
        var allowed = definition.ConstantForceDamping == null && float.IsFinite(basis.M11) && basis.M11 > 0
            ? Matrix.CreateScale(basis.M11) : Matrix.Identity;
        allowed.Translation = basis.Translation;
        var supported = Math.Abs(emitter.TimeScale - 1) <= 0.00001f && MatrixClose(basis, allowed)
            && (definition.ConstantForceDamping == null || emitter.LifeBias == 0);
        if (!supported) ForceBasisUnsupported = true;
        return supported;
    }

    private static (Vector3 Position, Vector3 Velocity) ConstantMotion(Particle particle, VfxPreviewEmitter definition, double age)
    {
        // This bounded native-control candidate does not change the shared simulation tick.
        const double interval = 1.0 / 60;
        var time = Math.Max(0, age - particle.MotionStart);
        var steps = Math.Floor(time / interval);
        var remainder = time - steps * interval;
        var decay = Math.Exp(-definition.ConstantForceDamping!.Value * interval);
        var power = Math.Pow(decay, steps);
        var terminal = definition.CalibratedVerticalAcceleration * interval * decay / (1 - decay);
        var initial = particle.MotionInitialVelocity.Y;
        var displacement = interval * (terminal * steps + (initial - terminal) * decay * (1 - power) / (1 - decay));
        var currentVelocity = terminal + (initial - terminal) * power;
        var nextVelocity = terminal + (initial - terminal) * power * decay;
        displacement += remainder * nextVelocity;
        var horizontalDisplacement = interval * decay * (1 - power) / (1 - decay) + remainder * power * decay;
        var horizontalVelocity = power * (1 + (decay - 1) * remainder / interval);
        return (particle.MotionOrigin + new Vector3((float)(particle.MotionInitialVelocity.X * horizontalDisplacement), (float)displacement, (float)(particle.MotionInitialVelocity.Z * horizontalDisplacement)),
            new Vector3((float)(particle.MotionInitialVelocity.X * horizontalVelocity), (float)(currentVelocity + (nextVelocity - currentVelocity) * remainder / interval), (float)(particle.MotionInitialVelocity.Z * horizontalVelocity)));
    }
    private static Vector3 RandomVector(Random random) => new(random.NextSingle(), random.NextSingle(), random.NextSingle());
    private static Matrix Rotation(Vector3 degrees) => Matrix.CreateFromYawPitchRoll(MathHelper.ToRadians(degrees.Y), MathHelper.ToRadians(degrees.X), MathHelper.ToRadians(degrees.Z));

    // Native rigid controls apply X, then Y, then Z; sprites and references retain their own convention.
    private static Matrix ModelRotation(Vector3 degrees) => Matrix.CreateRotationX(MathHelper.ToRadians(degrees.X))
        * Matrix.CreateRotationY(MathHelper.ToRadians(degrees.Y)) * Matrix.CreateRotationZ(MathHelper.ToRadians(degrees.Z));
    private static Matrix RotationOnly(Matrix matrix) => matrix.Decompose(out _, out var rotation, out _) ? Matrix.CreateFromQuaternion(rotation) : Matrix.Identity;
    private static bool MatrixClose(Matrix left, Matrix right) =>
        Math.Abs(left.M11 - right.M11) < 0.00001f && Math.Abs(left.M12 - right.M12) < 0.00001f && Math.Abs(left.M13 - right.M13) < 0.00001f && Math.Abs(left.M14 - right.M14) < 0.00001f
        && Math.Abs(left.M21 - right.M21) < 0.00001f && Math.Abs(left.M22 - right.M22) < 0.00001f && Math.Abs(left.M23 - right.M23) < 0.00001f && Math.Abs(left.M24 - right.M24) < 0.00001f
        && Math.Abs(left.M31 - right.M31) < 0.00001f && Math.Abs(left.M32 - right.M32) < 0.00001f && Math.Abs(left.M33 - right.M33) < 0.00001f && Math.Abs(left.M34 - right.M34) < 0.00001f
        && Math.Abs(left.M44 - right.M44) < 0.00001f;

    private sealed class EmitterState
    {
        public readonly VfxPreviewEmitter Definition;
        public readonly int DefinitionIndex;
        public readonly Particle? Parent;
        public readonly VfxPreviewParticleEvent? Event;
        public Matrix? SpawnAnchor;
        public Matrix? FrozenAnchor;
        public Vector3? FrozenScale;
        public Vector4? FrozenTint;
        public bool FrozenAnchorValid;
        public readonly Random Random;
        public readonly float Start, Duration, RandomRate, TimeScale, RateScale, LifeBias;
        public readonly Matrix ReferenceTransform;
        public readonly Vector4 Tint;
        public int ActiveCount;
        public bool ModelAngularUnsupported;
        public bool Started;
        public double Accumulator;
        public Matrix? DistanceRoot;
        public double DistanceRemainder;
        public Matrix? MotionRoot;
        public double MotionTime;
        public Vector3 MotionVelocity;
        public EmitterState(VfxPreviewEmitter definition, int seed, int definitionIndex, Particle? parent = null,
            VfxPreviewParticleEvent? particleEvent = null, double eventTime = 0)
        {
            Definition = definition; DefinitionIndex = definitionIndex; Parent = parent; Event = particleEvent; Random = new(seed);
            Start = definition.Start.Sample(Random.NextSingle()); Duration = definition.Duration.Sample(Random.NextSingle());
            RandomRate = Random.NextSingle();
            TimeScale = RateScale = 1; LifeBias = 0; ReferenceTransform = Matrix.Identity; Tint = Vector4.One;
            foreach (var reference in definition.References)
            {
                Start = reference.Start.Sample(Random.NextSingle()) + Start / reference.Speed;
                Duration = Math.Max(0.0001f, Duration + reference.DurationBias.Sample(Random.NextSingle())) / reference.Speed;
                LifeBias += reference.LifeBias.Sample(Random.NextSingle()) * TimeScale;
                TimeScale *= reference.Speed; RateScale *= reference.Rate.Sample(Random.NextSingle());
                ReferenceTransform *= Matrix.CreateScale(reference.Scale.Sample(Random.NextSingle())) * Rotation(reference.Rotation) * Matrix.CreateTranslation(reference.Position);
                Tint *= Vector4.Lerp(reference.TintMin, reference.TintMax, Random.NextSingle());
            }
            Start += (float)eventTime;
        }
    }
    private sealed class Particle
    {
        public EmitterState Emitter = null!;
        public List<EmitterState>? EventInstances;
        public bool Alive = true;
        public int Cell, Row, ModelVariant;
        public double Birth;
        public double MotionStart;
        public float Life, InitialAlpha, RandomAlpha, Loops;
        public Vector3 Position, Velocity, InitialScale, Orientation, Spin, RandomScale, RandomColour;
        public Vector3 MotionOrigin, MotionInitialVelocity;
        public Matrix Root;
        public Matrix BirthRotation;
        public Quaternion SpinRootRotation;
        public Quaternion SpinRotation;
        public float AngularSpeed;
        public Vector3 AngularVelocity;
        public double SpinTime;
    }
}
