using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.WsModel;

namespace GameWorld.Core.Test.Rendering.Materials.Serialization
{
    internal class WsModelMaterialFileTests
    {
        [Test]
        public void Constructor_PreservesDistinctNamedNormalTextureSlots()
        {
            var material = new WsModelMaterialFile("""
                <material><textures>
                  <texture><slot>s_xml_cloth_normal_1</slot><source>cloth_one.dds</source></texture>
                  <texture><slot>s_xml_cloth_normal_2</slot><source>cloth_two.dds</source></texture>
                  <texture><slot>s_xml_normal</slot><source>surface.dds</source></texture>
                </textures></material>
                """);

            Assert.Multiple(() =>
            {
                Assert.That(material.TextureSlots["S_XML_CLOTH_NORMAL_1"], Is.EqualTo("cloth_one.dds"));
                Assert.That(material.TextureSlots["s_xml_cloth_normal_2"], Is.EqualTo("cloth_two.dds"));
                Assert.That(material.TextureSlots["s_xml_normal"], Is.EqualTo("surface.dds"));
                Assert.That(material.Textures[TextureType.Normal], Is.EqualTo("surface.dds"));
                Assert.That(material.ReferencedTexturePaths, Is.EqualTo(new[] { "cloth_one.dds", "cloth_two.dds", "surface.dds" }));
            });
        }

        [TestCase("s_xml_surface_map")]
        [TestCase("t_xml_material_map")]
        public void Constructor_MapsMaterialTextureSlots(string slotName)
        {
            const string texturePath = "VariantMeshes/_VariantModels/character/material_map.dds";
            var materialXml = $"""
                <material>
                  <textures>
                    <texture>
                      <slot version="2">{slotName}</slot>
                      <source>{texturePath}</source>
                    </texture>
                  </textures>
                </material>
                """;

            var material = new WsModelMaterialFile(materialXml);

            Assert.That(
                material.Textures[TextureType.MaterialMap],
                Is.EqualTo(texturePath));
        }
    }
}
