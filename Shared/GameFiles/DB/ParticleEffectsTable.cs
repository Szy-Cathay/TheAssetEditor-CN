namespace Shared.GameFormats.DB;

public static class ParticleEffectsTable
{
    public static IReadOnlyList<string> Read(ReadOnlySpan<byte> data)
    {
        var reader = new EffectDbTableReader(data);
        var count = reader.ReadHeader(0, 2);
        var keys = new List<string>();
        for (var i = 0; i < count; i++)
            keys.Add(reader.ReadString());
        reader.RequireEnd();
        return keys.AsReadOnly();
    }
}
