using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.WsModel;

namespace GameWorld.Core.Test.Rendering.Materials.Serialization
{
    internal class WsModelMaterialFileTests
    {
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
