using System.Globalization;
using System.IO;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.WsModel;

namespace GameWorld.Core.Rendering.Materials.Capabilities;

public sealed class FlagWindCapability : ICapability
{
    public bool AllowNativeFlag { get; set; }
    public bool Enabled { get; set; }
    public float WindInfluenceStrength { get; set; } = .1f;
    public float NormalTextureRotate { get; set; }
    public float VertexWindScale { get; set; } = 1;
    public string ClothNormal1Path { get; set; } = "";
    public string ClothNormal2Path { get; set; } = "";

    public void Initialize(WsModelMaterialFile? wsModelMaterial, IRmvMaterial rmvMaterial)
    {
        Enabled = false;
        if (!AllowNativeFlag || wsModelMaterial == null)
            return;

        var shader = wsModelMaterial.ShaderPath.Replace('\\', '/');
        if (!shader.Equals("shaders/weighted2_character_flag.xml.shader", StringComparison.OrdinalIgnoreCase)
            && !shader.Equals("shaders/weighted4_character_flag.xml.shader", StringComparison.OrdinalIgnoreCase))
            return;

        var weighted2 = shader.Equals("shaders/weighted2_character_flag.xml.shader", StringComparison.OrdinalIgnoreCase);
        WindInfluenceStrength = ReadParameter("wind_influence_strength", .1f);
        NormalTextureRotate = weighted2 ? 0 : ReadParameter("normal_texture_rotate", 0);
        VertexWindScale = weighted2 ? .1f : 1;
        ClothNormal1Path = ReadTexture("s_xml_cloth_normal_1");
        ClothNormal2Path = ReadTexture("s_xml_cloth_normal_2");
        Enabled = true;

        float ReadParameter(string name, float fallback)
        {
            var parameter = wsModelMaterial.Parameters.FirstOrDefault(p => p.Name == name);
            if (parameter == null)
                return fallback;
            if (!float.TryParse(parameter.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value))
                throw new InvalidDataException($"Invalid native flag material parameter: {name}");
            return value;
        }

        string ReadTexture(string slot)
        {
            if (wsModelMaterial.TextureSlots.TryGetValue(slot, out var path) && !string.IsNullOrWhiteSpace(path))
                return path;
            throw new InvalidDataException($"Native flag material is missing texture slot: {slot}");
        }
    }

    public void Apply(Effect effect, IScopedResourceLibrary resourceLibrary)
    {
        effect.Parameters["FlagWindEnabled"].SetValue(Enabled);
        if (!Enabled)
            return;

        effect.Parameters["FlagWindStrength"].SetValue(WindInfluenceStrength);
        effect.Parameters["FlagNormalTextureRotate"].SetValue(NormalTextureRotate);
        effect.Parameters["FlagVertexWindScale"].SetValue(VertexWindScale);
        effect.Parameters["FlagClothNormal1"].SetValue(resourceLibrary.LoadTexture(ClothNormal1Path));
        effect.Parameters["FlagClothNormal2"].SetValue(resourceLibrary.LoadTexture(ClothNormal2Path));
    }

    public ICapability Clone() => new FlagWindCapability
    {
        AllowNativeFlag = AllowNativeFlag,
        Enabled = Enabled,
        WindInfluenceStrength = WindInfluenceStrength,
        NormalTextureRotate = NormalTextureRotate,
        VertexWindScale = VertexWindScale,
        ClothNormal1Path = ClothNormal1Path,
        ClothNormal2Path = ClothNormal2Path,
    };

    public (bool Result, string Message) AreEqual(ICapability otherCap)
    {
        var equal = otherCap is FlagWindCapability other
            && AllowNativeFlag == other.AllowNativeFlag && Enabled == other.Enabled
            && WindInfluenceStrength == other.WindInfluenceStrength
            && NormalTextureRotate == other.NormalTextureRotate
            && VertexWindScale == other.VertexWindScale
            && ClothNormal1Path == other.ClothNormal1Path && ClothNormal2Path == other.ClothNormal2Path;
        return (equal, equal ? "" : "Different native flag material settings");
    }
}
