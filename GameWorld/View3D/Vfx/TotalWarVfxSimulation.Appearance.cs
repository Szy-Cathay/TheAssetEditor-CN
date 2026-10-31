using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

internal sealed partial class TotalWarVfxSimulation
{
    private AppearanceEntry[] _appearanceEntries = [];
    private int _appearanceCount;
    private readonly record struct LocalAppearance(Vector3 Scale, Vector3 Colour, float Alpha);
    private readonly record struct AppearanceEntry(Particle? Particle, long Age, long Phase, int SpritePhase, bool Complete, LocalAppearance Value);

    private void PrepareAppearanceEntries()
    {
        if (_appearanceEntries.Length < _particles.Count)
            Array.Resize(ref _appearanceEntries, Math.Min(ParticleLimit,
                Math.Max(_particles.Count, Math.Max(4, _appearanceEntries.Length * 2))));
        if (_appearanceCount > _particles.Count)
            Array.Clear(_appearanceEntries, _particles.Count, _appearanceCount - _particles.Count);
        _appearanceCount = _particles.Count;
    }

    private void ClearAppearanceEntries()
    {
        Array.Clear(_appearanceEntries, 0, _appearanceCount);
        _appearanceCount = 0;
    }

    private LocalAppearance Appearance(Particle particle, int index, double age, double phase, float spritePhase, bool complete = false)
    {
        ref var entry = ref _appearanceEntries[index];
        var ageBits = BitConverter.DoubleToInt64Bits(age);
        var phaseBits = BitConverter.DoubleToInt64Bits(phase);
        var spriteBits = BitConverter.SingleToInt32Bits(spritePhase);
        var same = ReferenceEquals(entry.Particle, particle) && entry.Age == ageBits && entry.Phase == phaseBits && entry.SpritePhase == spriteBits;
        if (same && (!complete || entry.Complete))
            return entry.Value;
        var definition = particle.Emitter.Definition;
        var scale = same ? entry.Value.Scale : (definition.ModelScaleTable is { } modelTable
            ? modelTable.Sample(age * particle.Emitter.TimeScale / (particle.Life * particle.Emitter.TimeScale + .000001), particle.RandomScale.X)
            : definition.SpriteScaleTable is { } table
            ? new Vector3(table.Sample("x", spritePhase, particle.RandomScale.X), table.Sample("y", spritePhase, particle.RandomScale.X),
                table.Sample("z", spritePhase, particle.RandomScale.X))
            : definition.Scale.Vector(phase, new(particle.RandomScale.X))) * particle.InitialScale;
        if (!complete)
        {
            var scaleOnly = new LocalAppearance(scale, Vector3.Zero, 0);
            entry = new(particle, ageBits, phaseBits, spriteBits, false, scaleOnly);
            return scaleOnly;
        }
        var colour = new Vector3(definition.Colour.Sample("r", phase, particle.RandomColour.X),
            definition.Colour.Sample("g", phase, particle.RandomColour.X), definition.Colour.Sample("b", phase, particle.RandomColour.X));
        colour *= definition.Colour.Sample("brightness", phase, particle.RandomColour.X);
        var alpha = (definition.SpriteAlphaTable?.Sample("x", spritePhase, particle.RandomAlpha)
            ?? definition.AlphaAt(phase, particle.RandomAlpha)) * particle.InitialAlpha;
        var value = new LocalAppearance(scale, colour, alpha);
        entry = new(particle, ageBits, phaseBits, spriteBits, true, value);
        return value;
    }
}
