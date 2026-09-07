using System.Text.RegularExpressions;
using Test.TestingUtility.TestUtility;

namespace GameWorld.Core.Test.Rendering.Shaders
{
    [TestFixture]
    internal class PbrLightingShaderContractTests
    {
        private const string MetalRoughnessMain =
            "ContentProject/Content/Shaders/Pbr/MetalRoughness/MetalRoughness_main.fx";
        private const string SpecGlossMain =
            "ContentProject/Content/Shaders/Pbr/SpecGloss/SpecGloss_main.fx";
        private const string MetalRoughnessHelper =
            "ContentProject/Content/Shaders/Pbr/Helpers/CAMetalRoughnessHelper.hlsli";
        private const string SpecGlossHelper =
            "ContentProject/Content/Shaders/Pbr/Helpers/CASpecGlossHelper.hlsli";

        [TestCase(MetalRoughnessMain)]
        [TestCase(SpecGlossMain)]
        public void MainShader_UsesSharedCameraLightUnlessLocalEnvironmentIsEnabled(string relativePath)
        {
            var source = ReadNormalizedSource(relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(
                    source,
                    Does.Contain("float3 L_main = normalize(CameraPos - input.worldPosition);"));
                Assert.That(source, Does.Contain("float unchartedSunFactor = 3.0f;"));
                Assert.That(
                    source,
                    Does.Contain(
                        "float3 lightCol_main = get_sun_colour() * unchartedSunFactor;"));
                Assert.That(
                    source,
                    Does.Contain("lightCol_main, L_main, normalizedViewDirection"));
                Assert.That(
                    source,
                    Does.Contain("float3 hdr_linear_col = env_light + (ViewportEnvironmentEnabled ? 0 : combined_dir_light);"));
                Assert.That(source, Does.Contain("hdr_linear_col *= Constant_LightColour;"));
                Assert.That(
                    source,
                    Does.Contain("Uncharted2ToneMapping(hdr_linear_col)"));
            });

            var environmentIndex = RequiredIndexOf(
                source,
                "standard_lighting_model_environment_light_SM4_private");
            var directionalIndex = RequiredIndexOf(
                source,
                "standard_lighting_model_directional_light_SM4_private");
            var combineIndex = RequiredIndexOf(
                source,
                "float3 hdr_linear_col = env_light + (ViewportEnvironmentEnabled ? 0 : combined_dir_light);");
            var lightColourIndex = RequiredIndexOf(
                source,
                "hdr_linear_col *= Constant_LightColour;");
            var toneMapIndex = RequiredIndexOf(
                source,
                "Uncharted2ToneMapping(hdr_linear_col)");

            Assert.Multiple(() =>
            {
                Assert.That(environmentIndex, Is.LessThan(directionalIndex));
                Assert.That(directionalIndex, Is.LessThan(combineIndex));
                Assert.That(combineIndex, Is.LessThan(lightColourIndex));
                Assert.That(lightColourIndex, Is.LessThan(toneMapIndex));
            });
        }

        [Test]
        public void SpecGlossMain_DoesNotUseLegacyFixedLightOrToneMap()
        {
            var source = ReadNormalizedSource(SpecGlossMain);

            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Not.Contain("rotatedNormalizedLightDirection"));
                Assert.That(source, Does.Not.Contain("DirLightTransform"));
                Assert.That(source, Does.Not.Contain("tone_map_linear_hdr_pixel_value"));
                Assert.That(source, Does.Not.Contain("hdr_linear_col * exposure"));
            });
        }

        [TestCase(MetalRoughnessHelper)]
        [TestCase(SpecGlossHelper)]
        public void Helper_UsesSharedSunAndEnvironmentCalibration(string relativePath)
        {
            var source = ReadNormalizedSource(relativePath);

            Assert.Multiple(() =>
            {
                Assert.That(source, Does.Contain("float3 get_sun_colour()"));
                Assert.That(
                    source,
                    Does.Contain(
                        "const float default_max_sun_colour_scale = 20000.0f * LightMult;"));
                Assert.That(
                    source,
                    Does.Contain("const float specularCubeMapBrightness = 0.261f;"));
                Assert.That(
                    source,
                    Does.Contain("const float diffuseCubeMapBrightness = 0.261f;"));
            });
        }

        private static string ReadNormalizedSource(string relativePath)
        {
            var gameWorldPath = PathHelper.GetDataFolder("GameWorld");
            var fullPath = Path.Combine(gameWorldPath, relativePath);
            var source = File.ReadAllText(fullPath);
            return Regex.Replace(source, @"\s+", " ");
        }

        private static int RequiredIndexOf(string source, string value)
        {
            var index = source.IndexOf(value, StringComparison.Ordinal);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), $"Missing shader expression: {value}");
            return index;
        }
    }
}
