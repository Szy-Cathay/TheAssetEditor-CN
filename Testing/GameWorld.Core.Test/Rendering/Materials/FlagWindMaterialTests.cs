using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Shaders;
using Shared.Core.Settings;
using Shared.GameFormats.WsModel;

namespace GameWorld.Core.Test.Rendering.Materials;

public class FlagWindMaterialTests
{
    [TestCase("weighted2_character_flag", .1f, 0f)]
    [TestCase("weighted4_character_flag", 1f, -.3f)]
    public void OfficialFlagShader_UsesAuthoredParametersAndDistinctNormalSlots(string shader, float vertexScale, float normalRotation)
    {
        var material = Create(GameTypeEnum.ThreeKingdoms, shader);
        var flag = material.GetCapability<FlagWindCapability>();
        Assert.Multiple(() =>
        {
            Assert.That(flag.Enabled, Is.True);
            Assert.That(flag.WindInfluenceStrength, Is.EqualTo(.6f));
            Assert.That(flag.NormalTextureRotate, Is.EqualTo(normalRotation));
            Assert.That(flag.VertexWindScale, Is.EqualTo(vertexScale));
            Assert.That(flag.ClothNormal1Path, Is.EqualTo("first_cloth.dds"));
            Assert.That(flag.ClothNormal2Path, Is.EqualTo("second_cloth.dds"));
            Assert.That(material.GetCapability<MetalRoughCapability>().NormalMap.TexturePath,
                Is.EqualTo("surface_normal.dds"));
            Assert.That(flag.AreEqual(material.Clone().GetCapability<FlagWindCapability>()).Result, Is.True);
        });
    }

    [TestCase(GameTypeEnum.Warhammer3, "weighted4_character_flag")]
    [TestCase(GameTypeEnum.ThreeKingdoms, "weighted4_character")]
    [TestCase(GameTypeEnum.ThreeKingdoms, "weighted4_character_flag_alpha")]
    [TestCase(GameTypeEnum.ThreeKingdoms, "battle_flag_prop")]
    public void OtherGamesAndUnverifiedFlagShaders_DoNotEnableTheNativeFlagPath(GameTypeEnum game, string shader)
    {
        Assert.That(Create(game, shader).GetCapability<FlagWindCapability>().Enabled, Is.False);
    }

    [Test]
    public void ReinitializingForAnOrdinaryMaterial_DisablesPreviousFlagState()
    {
        var flag = Create(GameTypeEnum.ThreeKingdoms, "weighted4_character_flag")
            .GetCapability<FlagWindCapability>();
        flag.Initialize(new WsModelMaterialFile { ShaderPath = "shaders/weighted4_character.xml.shader" }, null!);
        Assert.That(flag.Enabled, Is.False);
    }

    private static CapabilityMaterial Create(GameTypeEnum game, string shader)
    {
        var xml = $$"""
            <material>
              <name>weighted4_alpha_off</name>
              <shader>shaders/{{shader}}.xml.shader</shader>
              <textures>
                <texture><slot version="2">s_xml_normal</slot><source>surface_normal.dds</source></texture>
                <texture><slot version="2">s_xml_cloth_normal_1</slot><source>first_cloth.dds</source></texture>
                <texture><slot version="2">s_xml_cloth_normal_2</slot><source>second_cloth.dds</source></texture>
              </textures>
              <params>
                <param><name>wind_influence_strength</name><type>float</type><value>0.6</value></param>
                <param><name>normal_texture_rotate</name><type>float</type><value>-0.3</value></param>
              </params>
            </material>
            """;
        return new CapabilityMaterialFactory(new ApplicationSettingsService(game), null!)
            .Create(null!, new WsModelMaterialFile(xml));
    }
}
