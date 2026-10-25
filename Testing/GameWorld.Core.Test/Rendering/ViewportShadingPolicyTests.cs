using GameWorld.Core.Components.Rendering;
using Microsoft.Xna.Framework.Graphics;
using GameWorld.Core.Rendering;
using System.Text;

namespace GameWorld.Core.Test.Rendering;

public class ViewportShadingPolicyTests
{
    [TestCase("bloom_emissive_strength='0'", false)]
    [TestCase("bloom_emissive_strength='0.1'", true)]
    [TestCase("bloom_particle_strength='0'", true)]
    [TestCase("", true)]
    public void AuthoredBloomOff_DisablesOnlyModelBloom(string attributes, bool expected)
    {
        var weather = GameEnvironmentLighting.Read(Encoding.UTF8.GetBytes($"""
            <environment_file><lighting use_custom_ambient_cube_colours="true" ambient_cube_hdr_scaler="1" ambient_scale="1"
                sun_direction_azimuth="0" sun_direction_elevation="0" sun_colour_scale="1">
              <ambient_cube_left r="0" g="0" b="0"/><ambient_cube_right r="0" g="0" b="0"/>
              <ambient_cube_top r="0" g="0" b="0"/><ambient_cube_bottom r="0" g="0" b="0"/>
              <ambient_cube_front r="0" g="0" b="0"/><ambient_cube_back r="0" g="0" b="0"/>
              <sun_colour r="1" g="1" b="1"/>
            </lighting><bloom {attributes}/></environment_file>
            """), "preview.environment");
        Assert.That(ViewportShadingPolicy.AllowsEmissiveBloom(weather), Is.EqualTo(expected));
        Assert.That(ViewportShadingPolicy.AllowsEmissiveBloom(null), Is.True);
        Assert.That(ViewportShadingPolicy.Resolve(ViewportShadingMode.MaterialPreview).SurfaceTechnique,
            Is.EqualTo(RenderingTechnique.Normal), "Disabling bloom must retain the animated material surface.");
    }

    [TestCase(
        ViewportShadingMode.MaterialPreview,
        RenderingTechnique.Normal,
        FillMode.Solid,
        true)]
    [TestCase(
        ViewportShadingMode.Solid,
        RenderingTechnique.Solid,
        FillMode.Solid,
        false)]
    [TestCase(
        ViewportShadingMode.Wireframe,
        RenderingTechnique.Solid,
        FillMode.WireFrame,
        false)]
    public void Resolve_SeparatesEditingAndPreviewPipelines(
        ViewportShadingMode mode,
        RenderingTechnique expectedTechnique,
        FillMode expectedFillMode,
        bool expectedBloom)
    {
        var result = ViewportShadingPolicy.Resolve(mode);

        Assert.Multiple(() =>
        {
            Assert.That(
                result.SurfaceTechnique,
                Is.EqualTo(expectedTechnique));
            Assert.That(
                result.FillMode,
                Is.EqualTo(expectedFillMode));
            Assert.That(
                result.EnableBloom,
                Is.EqualTo(expectedBloom));
        });
    }

    [TestCase(
        ViewportShadingMode.MaterialPreview,
        ViewportShadingMode.Wireframe)]
    [TestCase(
        ViewportShadingMode.Wireframe,
        ViewportShadingMode.Solid)]
    [TestCase(
        ViewportShadingMode.Solid,
        ViewportShadingMode.MaterialPreview)]
    public void Next_UsesStableThreeModeCycle(
        ViewportShadingMode current,
        ViewportShadingMode expected)
    {
        Assert.That(
            ViewportShadingPolicy.Next(current),
            Is.EqualTo(expected));
    }
}
