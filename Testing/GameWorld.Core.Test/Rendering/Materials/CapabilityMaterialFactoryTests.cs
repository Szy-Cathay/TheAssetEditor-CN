using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Rendering.Materials.Serialization;
using GameWorld.Core.Test.TestUtility;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Types;
using Shared.GameFormats.WsModel;

namespace GameWorld.Core.Test.Rendering.Materials
{
    internal class CapabilityMaterialFactoryTests
    {

        [Test]
        public void Create_FromRmv_Wh3_Default()
        {
            var rmvMaterial = RmvMaterialHelper
                .Create(ModelMaterialEnum.weighted)
                .SetAlpha(true)
                .AssignMaterials([TextureType.Normal, TextureType.MaterialMap]);

            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var abstractMaterialFactory = new CapabilityMaterialFactory(appSettings, null);
            var material = abstractMaterialFactory.Create(rmvMaterial, null);

            Assert.That(material, Is.TypeOf<Core.Rendering.Materials.Shaders.MetalRough.DefaultMaterial>());

            var defaultCapabiliy = material.TryGetCapability<MetalRoughCapability>();
            Assert.That(defaultCapabiliy, Is.Not.Null);

            Assert.That(defaultCapabiliy.MaterialMap.TexturePath, Is.EqualTo($"texturePath/{TextureType.MaterialMap}.dds"));
            Assert.That(defaultCapabiliy.MaterialMap.UseTexture, Is.True);

            Assert.That(defaultCapabiliy.NormalMap.TexturePath, Is.EqualTo($"texturePath/{TextureType.Normal}.dds"));
            Assert.That(defaultCapabiliy.NormalMap.UseTexture, Is.True);

            Assert.That(defaultCapabiliy.BaseColour.TexturePath, Is.EqualTo(""));
            Assert.That(defaultCapabiliy.BaseColour.UseTexture, Is.False);

            Assert.That(defaultCapabiliy.UseAlpha, Is.True);
        }

        [Test]
        public void Create_FromLegacyRmv_Wh3_MapsLegacyTextureNames()
        {
            var rmvMaterial = RmvMaterialHelper
                .Create(ModelMaterialEnum.weighted)
                .AssignMaterials([TextureType.Diffuse, TextureType.Specular, TextureType.Normal]);
            rmvMaterial.SetTexture(
                TextureType.Diffuse,
                @"rigidmodels\buildings\textures\building_diffuse.dds");
            rmvMaterial.SetTexture(
                TextureType.Specular,
                @"rigidmodels\buildings\textures\building_specular.dds");
            rmvMaterial.SetTexture(
                TextureType.Normal,
                @"rigidmodels\buildings\textures\building_normal.dds");

            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var materialFactory = new CapabilityMaterialFactory(appSettings, null);
            var material = materialFactory.Create(rmvMaterial, null);

            var capability = material.GetCapability<MetalRoughCapability>();
            Assert.Multiple(() =>
            {
                Assert.That(capability.BaseColour.UseTexture, Is.True);
                Assert.That(
                    capability.BaseColour.TexturePath,
                    Is.EqualTo(@"rigidmodels\buildings\textures\building_base_colour.dds"));
                Assert.That(capability.MaterialMap.UseTexture, Is.True);
                Assert.That(
                    capability.MaterialMap.TexturePath,
                    Is.EqualTo(@"rigidmodels\buildings\textures\building_material_map.dds"));
                Assert.That(capability.NormalMap.UseTexture, Is.True);
                Assert.That(
                    capability.NormalMap.TexturePath,
                    Is.EqualTo(@"rigidmodels\buildings\textures\building_normal.dds"));
            });
        }

        [Test]
        public void Create_FromLegacyDefaultRmv_Wh3_PreservesNormalPathForSerialization()
        {
            const string normalPath =
                @"rigidmodels\buildings\textures\flatnormal.dds";
            const string specularPath =
                @"rigidmodels\buildings\textures\test_black.dds";
            var rmvMaterial = RmvMaterialHelper.Create(ModelMaterialEnum.weighted);
            rmvMaterial.SetTexture(TextureType.Normal, normalPath);
            rmvMaterial.SetTexture(TextureType.Specular, specularPath);

            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var materialFactory = new CapabilityMaterialFactory(appSettings, null);
            var material = materialFactory.Create(rmvMaterial, null);
            var capability = material.GetCapability<MetalRoughCapability>();

            var serializedMaterial = new MaterialToRmvSerializer()
                .CreateMaterialFromCapabilityMaterial(material);
            var serializedNormal = serializedMaterial.GetTexture(TextureType.Normal);

            Assert.Multiple(() =>
            {
                Assert.That(capability.NormalMap.TexturePath, Is.EqualTo(normalPath));
                Assert.That(capability.NormalMap.UseTexture, Is.True);
                Assert.That(capability.MaterialMap.UseTexture, Is.False);
                Assert.That(serializedNormal, Is.Not.Null);
                Assert.That(serializedNormal!.Value.Path, Is.EqualTo(normalPath));
            });
        }

        [Test]
        public void Create_FromWs_Wh3_Default()
        {
            var rmvMaterial = RmvMaterialHelper
                .Create(ModelMaterialEnum.weighted)
                .SetAlpha(true)
                .AssignMaterials([TextureType.Normal, TextureType.MaterialMap]);

            var wsMaterial = new WsModelMaterialFile()
            {
                Alpha = false,
                Name = "cth_celestial_general_body_01_weighted4_alpha_off.xml",
                ShaderPath = "shaders/weighted4_character.xml.shader",
                Textures = new()
                {
                    {TextureType.Normal, $"texturePath/wsmodel/{TextureType.Normal}.dds"},
                    {TextureType.MaterialMap, $"texturePath/wsmodel/{TextureType.MaterialMap}.dds"}
                }
            };

            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var abstractMaterialFactory = new CapabilityMaterialFactory(appSettings, null);
            var material = abstractMaterialFactory.Create(null, wsMaterial);

            Assert.That(material, Is.TypeOf<Core.Rendering.Materials.Shaders.MetalRough.DefaultMaterial>());

            var defaultCapabiliy = material.TryGetCapability<MetalRoughCapability>();
            Assert.That(defaultCapabiliy, Is.Not.Null);

            Assert.That(defaultCapabiliy.MaterialMap.TexturePath, Is.EqualTo($"texturePath/wsmodel/{TextureType.MaterialMap}.dds"));
            Assert.That(defaultCapabiliy.MaterialMap.UseTexture, Is.True);

            Assert.That(defaultCapabiliy.NormalMap.TexturePath, Is.EqualTo($"texturePath/wsmodel/{TextureType.Normal}.dds"));
            Assert.That(defaultCapabiliy.NormalMap.UseTexture, Is.True);

            Assert.That(defaultCapabiliy.BaseColour.TexturePath, Is.EqualTo(""));
            Assert.That(defaultCapabiliy.BaseColour.UseTexture, Is.False);

            Assert.That(defaultCapabiliy.UseAlpha, Is.False);
        }

        [Test]
        public void Create_FromWs_Wh3_Emissive()
        {
            var rmvMaterial = RmvMaterialHelper
                .Create(ModelMaterialEnum.weighted);

            var wsMaterial = new WsModelMaterialFile()
            {
                Name = "cth_celestial_general_body_01_weighted4_alpha_off.xml",
                ShaderPath = "shaders/weighted4_character_emissive.xml.shader",
            };

            var appSettings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var abstractMaterialFactory = new CapabilityMaterialFactory(appSettings, null);
            var material = abstractMaterialFactory.Create(rmvMaterial, wsMaterial);

            Assert.That(material, Is.TypeOf<Core.Rendering.Materials.Shaders.MetalRough.EmissiveMaterial>());

            var emissiveCapability = material.TryGetCapability<EmissiveCapability>();
            Assert.That(emissiveCapability, Is.Not.Null);
        }

        [Test]
        public void Create_FromRmv_Rome_Default()
        {
            var rmvMaterial = RmvMaterialHelper
                .Create(ModelMaterialEnum.weighted)
                .SetAlpha(true)
                .AssignMaterials([TextureType.Normal, TextureType.Gloss]);

            var appSettings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            var abstractMaterialFactory = new CapabilityMaterialFactory(appSettings, null);
            var material = abstractMaterialFactory.Create(rmvMaterial, null);

            Assert.That(material, Is.TypeOf<Core.Rendering.Materials.Shaders.SpecGloss.DefaultMaterial>());

            var defaultCapabiliy = material.TryGetCapability<SpecGlossCapability>();
            Assert.That(defaultCapabiliy, Is.Not.Null);

            Assert.That(defaultCapabiliy.GlossMap.TexturePath, Is.EqualTo($"texturePath/{TextureType.Gloss}.dds"));
            Assert.That(defaultCapabiliy.GlossMap.UseTexture, Is.True);

            Assert.That(defaultCapabiliy.NormalMap.TexturePath, Is.EqualTo($"texturePath/{TextureType.Normal}.dds"));
            Assert.That(defaultCapabiliy.NormalMap.UseTexture, Is.True);

            Assert.That(defaultCapabiliy.DiffuseMap.TexturePath, Is.EqualTo(""));
            Assert.That(defaultCapabiliy.DiffuseMap.UseTexture, Is.False);

            Assert.That(defaultCapabiliy.UseAlpha, Is.True);
        }

        [Test]
        public void Create_FromRmv_Rome_DirtAndDecal()
        {
            var rmvMaterial = RmvMaterialHelper
                .Create(ModelMaterialEnum.weighted_decal_dirtmap)
                .SetAlpha(true)
                .SetDecalAndDirt(true, true)
                .AssignMaterials([TextureType.Diffuse, TextureType.Decal_dirtmap, TextureType.Decal_mask, TextureType.Decal_dirtmask]);

            var appSettings = new ApplicationSettingsService(GameTypeEnum.Rome2);
            var abstractMaterialFactory = new CapabilityMaterialFactory(appSettings, null);
            var material = abstractMaterialFactory.Create(rmvMaterial, null);
            
            Assert.That(material, Is.TypeOf<Core.Rendering.Materials.Shaders.SpecGloss.AdvancedRmvMaterial>());
            
            var specGlossCap = material.TryGetCapability<SpecGlossCapability>();
            Assert.That(specGlossCap, Is.Not.Null);
            
            var dirtCap = material.TryGetCapability<AdvancedMaterialCapability>();
            Assert.That(dirtCap, Is.Not.Null);
        }
    }
}
