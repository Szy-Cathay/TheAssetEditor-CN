using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders;

public partial class ViewportSolidShadingPixelTests
{
    [TestCaseSource(nameof(s_effectAssets))]
    public void Wireframe_WholeObjectMaskDoesNotColourUnselectedEditElements(string asset)
    {
        var game = new WpfGameMock();
        using var effect = game.Content.Load<Effect>(asset).Clone();
        using var diffuse = CreateTexture(game.GraphicsDevice, Color.White);
        var editColour = Draw(false);
        var objectColour = Draw(true);
        Assert.That(Math.Abs(editColour.X - editColour.Z), Is.LessThan(0.05f));
        Assert.That(objectColour.X - objectColour.Z, Is.GreaterThan(0.8f));

        Vector3 Draw(bool objectSelection) => Render(game.GraphicsDevice, effect, diffuse, shader =>
        {
            shader.Parameters["SelectionMaskEnabled"].SetValue(true);
            shader.Parameters["ViewportWireframe"].SetValue(true);
            shader.Parameters["ViewportWireframeObjectSelection"].SetValue(objectSelection);
        });
    }

    [TestCaseSource(nameof(s_effectAssets))]
    public void Matcaps_ChangeRenderedSurfaceAndRestoreStudioWithoutTextureLeak(string asset)
    {
        var game = new WpfGameMock();
        using var effect = game.Content.Load<Effect>(asset).Clone();
        using var diffuse = CreateTexture(game.GraphicsDevice, Color.White);
        using var lighting = new ViewportLightingResources(game.GraphicsDevice);
        var studio = Render(game.GraphicsDevice, effect, diffuse);
        foreach (var preset in new[] { ViewportSolidLighting.Clay, ViewportSolidLighting.Metal })
        {
            var texture = lighting.GetMatcap(preset);
            var shaded = Render(game.GraphicsDevice, effect, diffuse, shader =>
            {
                shader.Parameters["ViewportSolidLighting"].SetValue((int)preset);
                shader.Parameters["ViewportMatcap"].SetValue(texture);
            });
            Assert.That(Vector3.Distance(studio, shaded), Is.GreaterThan(0.1f));
            Assert.That(lighting.GetMatcap(preset), Is.SameAs(texture));
        }
        Assert.That(Render(game.GraphicsDevice, effect, diffuse), Is.EqualTo(studio));
    }

    [TestCase("Shaders\\Pbr\\MetalRoughness\\MetalRoughness_main", false)]
    [TestCase("Shaders\\Pbr\\MetalRoughness\\MetalRoughness_main", true)]
    [TestCase("Shaders\\Pbr\\SpecGloss\\SpecGloss_main", false)]
    [TestCase("Shaders\\Pbr\\SpecGloss\\SpecGloss_main", true)]
    public void SurfaceDetail_DarkensContactsAndCavitiesButLeavesFlatPlanesUnchanged(string asset, bool perspective)
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        using var effect = game.Content.Load<Effect>(asset).Clone();
        using var diffuse = CreateTexture(device, Color.White);
        using var geometry = new Texture2D(device, RenderTargetSize, RenderTargetSize, false, SurfaceFormat.Vector4);
        var projection = perspective ? Matrix.CreatePerspectiveFieldOfView(MathHelper.PiOver4, 1, 0.1f, 10) :
            Matrix.CreateOrthographic(2.5f, 2.5f, 0.1f, 10);
        var baseline = Draw(0, 0);
        Fill((x, y) => 3);
        Assert.That(Vector3.Distance(Draw(1, 1), baseline), Is.LessThanOrEqualTo(1f / 255));
        Fill((x, y) => Math.Abs(x - 16) <= 3 && Math.Abs(y - 16) <= 3 ? 3 : 2.7f);
        Assert.That(Draw(0, 1).Length(), Is.LessThan(baseline.Length() - 0.01f), "Nearby surfaces must add contact occlusion.");
        Fill((x, y) => x == 16 && y == 16 ? 3 : 2.92f);
        Assert.That(Draw(1, 0).Length(), Is.LessThan(baseline.Length() - 0.01f), "Concave detail must darken independently of contact shadows.");

        Vector3 Draw(float cavity, float shadow) => Render(device, effect, diffuse, shader =>
        {
            shader.Parameters["Projection"].SetValue(projection);
            shader.Parameters["ViewportInverseProjection"].SetValue(Matrix.Invert(projection));
            shader.Parameters["ViewportSize"].SetValue(new Vector2(RenderTargetSize));
            shader.Parameters["ViewportGeometry"].SetValue(geometry);
            shader.Parameters["ViewportGeometryEnabled"].SetValue(cavity > 0 || shadow > 0);
            shader.Parameters["ViewportCavityStrength"].SetValue(cavity);
            shader.Parameters["ViewportShadowStrength"].SetValue(shadow);
        });

        void Fill(Func<int, int, float> depth)
        {
            for (var i = 0; i < 16; i++) device.Textures[i] = null;
            geometry.SetData(Enumerable.Range(0, RenderTargetSize * RenderTargetSize)
                .Select(i => new Vector4(0, 0, 1, depth(i % RenderTargetSize, i / RenderTargetSize))).ToArray());
        }
    }

    [TestCaseSource(nameof(s_effectAssets))]
    public void PreviewEnvironment_UploadsThroughCapabilityChangesPixelsAndResetsForOtherViews(string asset)
    {
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        using var effect = game.Content.Load<Effect>(asset).Clone();
        using var diffuse = CreateTexture(device, Color.White);
        using var lighting = new ViewportLightingResources(device);
        using var stock = new TextureCube(device, 1, false, SurfaceFormat.Color);
        foreach (var face in Enum.GetValues<CubeMapFace>()) stock.SetData(face, new[] { Color.Black });
        var baseline = Draw(null);
        foreach (var environment in new[] { ViewportEnvironment.Studio, ViewportEnvironment.Overcast, ViewportEnvironment.Sunset })
        {
            var maps = lighting.GetEnvironment(environment);
            var lit = Draw(maps);
            Assert.That(Vector3.Distance(lit, baseline), Is.GreaterThan(0.01f), $"{environment} must affect real material pixels.");
            Assert.That(lighting.GetEnvironment(environment), Is.EqualTo(maps));
        }
        Assert.That(Draw(null), Is.EqualTo(baseline), "Photo capture and other views must recover their original lighting.");
        Assert.That(effect.Parameters["ViewportEnvironmentEnabled"].GetValueBoolean(), Is.False);
        var studio = lighting.GetEnvironment(ViewportEnvironment.Studio);
        Assert.That(Vector3.Distance(Draw(studio), Draw(studio, MathHelper.Pi)), Is.GreaterThan(0.01f),
            "Rotating the environment must change the actual lighting, not only the slider value.");

        Vector3 Draw((TextureCube Diffuse, TextureCube Specular)? maps, float rotation = 0) => Render(device, effect, diffuse, shader =>
        {
            var parameters = new CommonShaderParameters(
                Matrix.CreateLookAt(new Vector3(0, 0, 3), Vector3.Zero, Vector3.Up),
                Matrix.CreateOrthographic(2.5f, 2.5f, 0.1f, 10), new Vector3(0, 0, 3), Vector3.Zero,
                rotation, 0, 0, 0.1f, Vector3.One, [], ViewportShading: new() { UseLocalLighting = maps != null },
                ViewportDiffuse: maps?.Diffuse, ViewportSpecular: maps?.Specular);
            var capability = new CommonShaderParametersCapability();
            capability.Assign(parameters, Matrix.Identity);
            ((CommonShaderParametersCapability)capability.Clone()).Apply(shader, null!);
            shader.Parameters["tex_cube_diffuse"].SetValue(stock);
            shader.Parameters["tex_cube_specular"].SetValue(stock);
        }, "BasicColorDrawing");
    }
}
