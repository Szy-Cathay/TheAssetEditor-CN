namespace Shared.GameFormats.Esf;

public readonly record struct Coord2d(float X, float Y)
{
    public override string ToString() => $"({X}, {Y})";
}

public readonly record struct Coord3d(float X, float Y, float Z)
{
    public override string ToString() => $"({X}, {Y}, {Z})";
}
