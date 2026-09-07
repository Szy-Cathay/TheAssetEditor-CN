using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering;

public sealed class ViewportLightingResources(GraphicsDevice device) : IDisposable
{
    private readonly Dictionary<ViewportSolidLighting, Texture2D> _matcaps = [];
    private readonly Dictionary<ViewportEnvironment, (TextureCube Diffuse, TextureCube Specular)> _environments = [];

    public Texture2D GetMatcap(ViewportSolidLighting lighting)
    {
        if (_matcaps.TryGetValue(lighting, out var texture))
            return texture;
        const int size = 128;
        var pixels = new Color[size * size];
        var key = Vector3.Normalize(new Vector3(-0.4f, 0.65f, 1));
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var point = new Vector2((x + 0.5f) * 2 / size - 1, 1 - (y + 0.5f) * 2 / size);
            if (point.LengthSquared() > 1)
                point.Normalize();
            var normal = new Vector3(point, MathF.Sqrt(MathF.Max(0, 1 - point.LengthSquared())));
            Vector3 colour;
            if (lighting == ViewportSolidLighting.Metal)
            {
                var reflection = Vector3.Reflect(-Vector3.UnitZ, normal);
                colour = EnvironmentColour(ViewportEnvironment.Studio, reflection) * 0.65f + new Vector3(0.12f);
                colour /= Vector3.One + colour;
            }
            else
            {
                var diffuse = 0.18f + 0.72f * MathF.Max(0, Vector3.Dot(normal, key));
                var specular = 0.12f * MathF.Pow(MathF.Max(0, Vector3.Dot(normal, Vector3.Normalize(key + Vector3.UnitZ))), 32);
                colour = new Vector3(0.64f, 0.29f, 0.16f) * diffuse + new Vector3(specular);
            }
            pixels[y * size + x] = new Color(Gamma(colour));
        }
        texture = new Texture2D(device, size, size);
        texture.SetData(pixels);
        _matcaps.Add(lighting, texture);
        return texture;
    }

    public (TextureCube Diffuse, TextureCube Specular) GetEnvironment(ViewportEnvironment environment)
    {
        if (_environments.TryGetValue(environment, out var maps))
            return maps;
        var diffuse = new TextureCube(device, 16, false, SurfaceFormat.Vector4);
        var specular = new TextureCube(device, 64, true, SurfaceFormat.Vector4);
        try
        {
            FillCube(diffuse, environment, diffuse: true);
            FillCube(specular, environment, diffuse: false);
            maps = (diffuse, specular);
            _environments.Add(environment, maps);
            return maps;
        }
        catch
        {
            diffuse.Dispose();
            specular.Dispose();
            throw;
        }
    }

    private static void FillCube(TextureCube texture, ViewportEnvironment environment, bool diffuse)
    {
        for (var level = 0; level < texture.LevelCount; level++)
        {
            var size = Math.Max(1, texture.Size >> level);
            var roughness = diffuse ? 1 : (float)level / Math.Max(1, texture.LevelCount - 1);
            var pixels = new Vector4[size * size];
            foreach (CubeMapFace face in Enum.GetValues<CubeMapFace>())
            {
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var u = (x + 0.5f) * 2 / size - 1;
                    var v = (y + 0.5f) * 2 / size - 1;
                    var direction = Vector3.Normalize(face switch
                    {
                        CubeMapFace.PositiveX => new Vector3(1, -v, -u),
                        CubeMapFace.NegativeX => new Vector3(-1, -v, u),
                        CubeMapFace.PositiveY => new Vector3(u, 1, v),
                        CubeMapFace.NegativeY => new Vector3(u, -1, -v),
                        CubeMapFace.PositiveZ => new Vector3(u, -v, 1),
                        _ => new Vector3(-u, -v, -1)
                    });
                    // Match the radiance range expected by the game's environment lighting.
                    pixels[y * size + x] = new Vector4(FilterEnvironment(environment, direction, roughness, diffuse) * 8, 1);
                }
                texture.SetData(face, level, null, pixels, 0, pixels.Length);
            }
        }
    }

    private static Vector3 FilterEnvironment(ViewportEnvironment environment, Vector3 normal, float roughness, bool diffuse)
    {
        if (roughness == 0)
            return EnvironmentColour(environment, normal);
        const int samples = 48;
        var tangent = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) < 0.95f ? Vector3.UnitY : Vector3.UnitZ, normal));
        var bitangent = Vector3.Cross(normal, tangent);
        var result = Vector3.Zero;
        var weight = 0f;
        var alpha = roughness * roughness;
        for (var i = 0; i < samples; i++)
        {
            var u = (i + 0.5f) / samples;
            var angle = i * 2.39996323f;
            var cosine = diffuse ? MathF.Sqrt(1 - u) : MathF.Sqrt((1 - u) / (1 + (alpha * alpha - 1) * u));
            var sine = MathF.Sqrt(MathF.Max(0, 1 - cosine * cosine));
            var half = normal * cosine + (tangent * MathF.Cos(angle) + bitangent * MathF.Sin(angle)) * sine;
            var direction = diffuse ? half : Vector3.Reflect(-normal, half);
            var sampleWeight = diffuse ? 1 : MathF.Max(0, Vector3.Dot(normal, direction));
            result += EnvironmentColour(environment, direction) * sampleWeight;
            weight += sampleWeight;
        }
        return result / MathF.Max(weight, 0.001f);
    }

    private static Vector3 EnvironmentColour(ViewportEnvironment environment, Vector3 direction)
    {
        var sky = MathHelper.Clamp(direction.Y * 0.5f + 0.5f, 0, 1);
        if (environment == ViewportEnvironment.Overcast)
            return Vector3.Lerp(new Vector3(0.12f, 0.13f, 0.14f), new Vector3(0.9f, 1.0f, 1.15f), sky * sky);
        var key = MathF.Max(0, Vector3.Dot(direction, Vector3.Normalize(new Vector3(-0.6f, 0.65f, 0.5f))));
        if (environment == ViewportEnvironment.Sunset)
            return Vector3.Lerp(new Vector3(0.13f, 0.065f, 0.035f), new Vector3(0.32f, 0.36f, 0.55f), sky) +
                new Vector3(5, 2.5f, 0.8f) * MathF.Pow(key, 96);
        var fill = MathF.Max(0, Vector3.Dot(direction, Vector3.Normalize(new Vector3(0.75f, 0.3f, -0.55f))));
        return new Vector3(0.09f + sky * 0.16f) + new Vector3(4) * MathF.Pow(key, 32) +
            new Vector3(1.3f, 1.5f, 1.8f) * MathF.Pow(fill, 24);
    }

    private static Vector3 Gamma(Vector3 value) => new(
        MathF.Pow(MathHelper.Clamp(value.X, 0, 1), 1 / 2.2f),
        MathF.Pow(MathHelper.Clamp(value.Y, 0, 1), 1 / 2.2f),
        MathF.Pow(MathHelper.Clamp(value.Z, 0, 1), 1 / 2.2f));

    public void Dispose()
    {
        foreach (var texture in _matcaps.Values)
            texture.Dispose();
        foreach (var maps in _environments.Values)
        {
            maps.Diffuse.Dispose();
            maps.Specular.Dispose();
        }
        _matcaps.Clear();
        _environments.Clear();
    }
}
