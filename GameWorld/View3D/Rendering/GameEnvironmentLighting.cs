using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Rendering;

// Authored weather units are independent of the model viewport's studio lights.
public sealed class GameEnvironmentLighting
{
    public string SourcePath { get; private init; } = "";
    public string SourceSha256 { get; private init; } = "";
    public Vector3 PositiveX { get; private init; }
    public Vector3 NegativeX { get; private init; }
    public Vector3 PositiveY { get; private init; }
    public Vector3 NegativeY { get; private init; }
    public Vector3 PositiveZ { get; private init; }
    public Vector3 NegativeZ { get; private init; }
    public Vector3 SunDirection { get; private init; }
    public Vector3 SunColour { get; private init; }
    public float SunScale { get; private init; }
    public float Brightness { get; private init; }
    public float? ToneMappingBrightness { get; private init; }
    public float? ToneMappingBurn { get; private init; }
    public bool? AutomaticExposure { get; private init; }
    public float? AutomaticExposureTarget { get; private init; }
    public float? BloomThreshold { get; private init; }
    public float? BloomStrength { get; private init; }
    public float? BloomParticleStrength { get; private init; }
    public float? BloomEmissiveStrength { get; private init; }
    public float? BloomWaterStrength { get; private init; }
    public float? UniformContrast { get; private init; }
    public bool UnsupportedPostProcessing { get; private init; }

    private GameEnvironmentLighting() { }

    public static GameEnvironmentLighting Read(byte[] bytes, string sourcePath)
    {
        using var stream = new MemoryStream(bytes, false);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var root = XElement.Load(reader);
        if (root.Name != "environment_file") throw new InvalidDataException("Expected a game environment file.");
        var lighting = root.Elements("lighting").Single();
        if ((string?)lighting.Attribute("use_custom_ambient_cube_colours") != "true"
            || Number("irradiance_power", 1) != 1 || Number("ambient_fudge_factor", 1) != 1)
            throw new NotSupportedException("Generated ambient cubes and irradiance remapping are not calibrated.");
        var scale = Number("ambient_cube_hdr_scaler") * Number("ambient_scale");
        var azimuth = MathHelper.ToRadians(Number("sun_direction_azimuth", minimum: -3600));
        var elevation = MathHelper.ToRadians(Number("sun_direction_elevation", minimum: -3600));
        var tone = root.Elements("tone_mapping").SingleOrDefault();
        var bloom = root.Elements("bloom").SingleOrDefault();
        var post = root.Elements("post_processing").SingleOrDefault();
        var contrast = ReadUniformContrast();
        // The legacy enabled flag is not a verified bypass; target_exposure belongs to Auto Exposure.
        bool? automaticExposure = (string?)tone?.Attribute("auto_exposure") switch
        {
            null => null, "false" or "0" => false, "true" or "1" => true,
            _ => throw new InvalidDataException("Invalid weather field: auto_exposure"),
        };
        return new()
        {
            SourcePath = sourcePath, SourceSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            // Native weather face names have the opposite X sign to the corrected world normal.
            PositiveX = Colour("ambient_cube_left") * scale, NegativeX = Colour("ambient_cube_right") * scale,
            PositiveY = Colour("ambient_cube_top") * scale, NegativeY = Colour("ambient_cube_bottom") * scale,
            PositiveZ = Colour("ambient_cube_front") * scale, NegativeZ = Colour("ambient_cube_back") * scale,
            SunDirection = new(MathF.Sin(azimuth) * MathF.Cos(elevation), MathF.Sin(elevation), MathF.Cos(azimuth) * MathF.Cos(elevation)),
            SunColour = Colour("sun_colour"), SunScale = Number("sun_colour_scale"),
            Brightness = Number("vfx_global_brightness_multiplier", 1),
            ToneMappingBrightness = ToneNumber("brightness"), ToneMappingBurn = ToneNumber("burn"),
            AutomaticExposure = automaticExposure, AutomaticExposureTarget = ToneNumber("target_exposure"),
            BloomThreshold = BloomNumber("bloom_threshold", -1000000), BloomStrength = BloomNumber("bloom_strength"),
            BloomParticleStrength = BloomNumber("bloom_particle_strength"), BloomEmissiveStrength = BloomNumber("bloom_emissive_strength"),
            BloomWaterStrength = BloomNumber("bloom_water_strength"),
            UniformContrast = contrast, UnsupportedPostProcessing = post != null && contrast == null,
        };

        float? ReadUniformContrast()
        {
            if (post == null) return null;
            float? Value(string name)
            {
                var text = (string?)post.Attribute(name);
                if (text == null) return null;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                    throw new InvalidDataException("Invalid weather field: post_processing/" + name);
                return value;
            }
            var red = Value("contrast_r"); var green = Value("contrast_g"); var blue = Value("contrast_b");
            var neutral = new[] { "brightness_r", "brightness_g", "brightness_b", "saturation_r", "saturation_g", "saturation_b" }
                .Select(Value).ToArray();
            var hue = Value("hue");
            return red is >= 1 && red == green && red == blue && neutral.All(value => value == 1) && hue == 0
                && (string?)post.Attribute("lut_texture_file") == "default" && (string?)post.Attribute("lut_texture_file_2") == "default"
                ? red : null;
        }

        float? BloomNumber(string name, float minimum = 0)
        {
            var text = (string?)bloom?.Attribute(name);
            if (text == null) return null;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value) || value < minimum || value > 1000000)
                throw new InvalidDataException("Invalid weather field: bloom/" + name);
            return value;
        }

        float? ToneNumber(string name)
        {
            var text = (string?)tone?.Attribute(name);
            if (text == null) return null;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                throw new InvalidDataException("Invalid weather field: tone_mapping/" + name);
            return value;
        }

        float Number(string name, float? fallback = null, float minimum = 0)
        {
            var text = (string?)lighting.Attribute(name);
            if (text == null && fallback.HasValue) return fallback.Value;
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value) || value < minimum || value > 1000000)
                throw new InvalidDataException("Invalid weather field: " + name);
            return value;
        }
        Vector3 Colour(string name)
        {
            var node = lighting.Elements(name).Single();
            float Channel(string channel)
            {
                if (!float.TryParse((string?)node.Attribute(channel), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !float.IsFinite(value) || value < 0 || value > 1) throw new InvalidDataException("Invalid weather colour: " + name);
                return value <= .04045f ? value / 12.92f : MathF.Pow((value + .055f) / 1.055f, 2.4f);
            }
            return new(Channel("r"), Channel("g"), Channel("b"));
        }
    }
}

// One explicit, read-only weather selection for particle previews in all editor scopes.
public sealed class GameWeatherStore
{
    public GameEnvironmentLighting? Current { get; private set; }
    public event Action? Changed;
    public void Set(GameEnvironmentLighting? weather)
    {
        Current = weather;
        Changed?.Invoke();
    }
}
