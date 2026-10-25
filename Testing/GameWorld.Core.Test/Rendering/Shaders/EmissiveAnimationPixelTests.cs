#nullable enable
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering.Shaders;

[NonParallelizable]
public class EmissiveAnimationPixelTests
{
    [TestCase(0f, .83743978f)]
    [TestCase(1.570796327f, 1.25564363f)]
    [TestCase(4.71238898f, .49776867f)]
    public void AuthoredPulse_ShiftsTheMaskedGradientUsingNativeRadians(float time, float expected)
    {
        using var fixture = new Fixture();
        fixture.Emission.SetData([new Color(64, 0, 0)]);
        fixture.Capability.EmissivePulseSpeed = 1;
        fixture.Capability.EmissivePulseStrength = 1;
        var result = fixture.Render(time);
        // Native shader equations encode these phases as 7, 9 and 5 in the 10-bit emission channel.
        Assert.That(result.X, Is.EqualTo(expected).Within(.00001f));
        Assert.That(result.Y, Is.EqualTo(result.X).Within(.00001f));
    }

    [Test]
    public void NativeCharacterEmission_ReconstructsColourBeforeToneMapping()
    {
        using var fixture = new Fixture();
        fixture.Capability.EmissiveTint = new(2, .25f, 0);
        fixture.Capability.GradientColours = [Vector3.One, Vector3.One, Vector3.One, Vector3.One];
        var linear = fixture.Render(0);
        Assert.That(linear.X, Is.EqualTo(255f * 25 / 1023).Within(.0001f));
        Assert.That(linear.Y, Is.EqualTo(linear.X * .3f).Within(.0001f));
        Assert.That(linear.Z, Is.EqualTo(linear.X * .2f).Within(.0001f));
        var displayed = fixture.Render(0, toneMapped: true);
        Assert.That(displayed.X, Is.InRange(0.9f, 1f));
        Assert.That(displayed.Y, Is.LessThan(displayed.X - .01f));
        Assert.That(displayed.Z, Is.LessThan(displayed.Y - .01f),
            "Adding emission after tone mapping clips the channels together and destroys the colour.");
    }

    [Test]
    public void PanningAndDistortion_UseAuthoredUvChannelsAndRemainLiveAfterEditing()
    {
        using var fixture = new Fixture(true);
        fixture.Capability.EmissiveDirection = Vector2.UnitX;
        fixture.Capability.EmissiveSpeed = 1;
        var first = fixture.Render(0);
        var shifted = fixture.Render(.5f);
        var looped = fixture.Render(1);
        Assert.That(shifted.X - first.X, Is.GreaterThan(.5f));
        Assert.That(looped.X, Is.EqualTo(first.X).Within(.00001f));
        fixture.Capability.EmissiveSpeed = 0;
        Assert.That(fixture.Render(.5f).X, Is.EqualTo(first.X).Within(.00001f));
        fixture.Capability.EmissiveDistortion.UseTexture = true;
        fixture.Capability.EmissiveDistortStrength = .5f;
        fixture.Distortion.SetData([Color.Red]);
        Assert.That(fixture.Render(0).X, Is.EqualTo(shifted.X).Within(.00001f));
        fixture.Distortion.SetData([Color.Lime]);
        Assert.That(fixture.Render(0).X, Is.EqualTo(first.X).Within(.00001f),
            "Native distortion uses red/blue; green must not shift U.");
    }

    [Test]
    public void ZeroMaskAndDisabledCapability_DoNotLeakAnotherMaterialsAnimation()
    {
        using var fixture = new Fixture();
        fixture.Capability.EmissivePulseSpeed = 2;
        fixture.Capability.EmissivePulseStrength = 1;
        fixture.Mask.SetData([new Color(0, 0, 0, 0)]);
        Assert.That(fixture.Render(1).X, Is.Zero.Within(.00001f));
        fixture.Mask.SetData([Color.White]);
        Assert.That(fixture.Render(1).X, Is.GreaterThan(.5f));
        Assert.That(fixture.Render(1, false).X, Is.Zero.Within(.00001f));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly WpfGameMock _game = new();
        private readonly Effect _effect;
        private readonly RenderTarget2D _target;
        private readonly Mock<IScopedResourceLibrary> _resources = new();
        public Texture2D Emission { get; }
        public Texture2D Distortion { get; }
        public Texture2D Mask { get; }
        public EmissiveCapability Capability { get; } = new()
        {
            EmissiveTint = Vector3.One, EmissiveStrength = 1,
            EmissiveFresnelStrength = 0, EmissivePulseSpeed = 0, EmissivePulseStrength = 0,
            EmissiveSpeed = 0, EmissiveDistortStrength = 0,
            GradientColours = [Vector3.Zero, new(1f / 3), new(2f / 3), Vector3.One],
            GradientTimes = [0, 1f / 3, 2f / 3, 1],
        };

        public Fixture(bool stripes = false)
        {
            var device = _game.GraphicsDevice;
            _effect = _game.Content.Load<Effect>("Shaders/Pbr/MetalRoughness/MetalRoughness_main").Clone();
            _target = new(device, 32, 32, false, SurfaceFormat.Vector4, DepthFormat.Depth24);
            Emission = new(device, stripes ? 2 : 1, 1);
            Emission.SetData(stripes ? new[] { Color.Black, Color.White } : [Color.White]);
            Distortion = new(device, 1, 1); Distortion.SetData([Color.Black]);
            Mask = new(device, 1, 1); Mask.SetData([Color.White]);
            Capability.Emissive.UseTexture = true; Capability.Emissive.TexturePath = "emission";
            Capability.EmissiveDistortion.TexturePath = "distortion";
            _resources.Setup(r => r.LoadTexture("emission", false, false)).Returns(Emission);
            _resources.Setup(r => r.LoadTexture("distortion", false, false)).Returns(Distortion);
            _effect.Parameters["UseDiffuse"].SetValue(false);
            _effect.Parameters["UseNormal"].SetValue(false);
            _effect.Parameters["UseGloss"].SetValue(false);
            _effect.Parameters["UseAlpha"].SetValue(false);
            _effect.Parameters["UseMask"].SetValue(true);
            _effect.Parameters["MaskTexture"].SetValue(Mask);
            _effect.Parameters["CapabilityFlag_ApplyAnimation"].SetValue(false);
            _effect.Parameters["CapabilityFlag_ApplyTinting"].SetValue(false);
        }

        public Vector4 Render(float time, bool enabled = true, bool toneMapped = false)
        {
            var eye = new Vector3(0, 0, 3);
            var common = new CommonShaderParametersCapability();
            common.Assign(new(Matrix.CreateLookAt(eye, Vector3.Zero, Vector3.Up), Matrix.CreateOrthographic(2.5f, 2.5f, .1f, 10),
                eye, Vector3.Zero, 0, 0, 0, 1, Vector3.Zero, [], MaterialTimeSeconds: time), Matrix.Identity);
            common.Apply(_effect, _resources.Object);
            Capability.Apply(_effect, _resources.Object);
            _effect.Parameters["CapabilityFlag_ApplyEmissive"].SetValue(enabled);
            var device = _game.GraphicsDevice;
            device.SetRenderTarget(_target); device.Clear(Color.Black);
            device.BlendState = BlendState.Opaque; device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            _effect.CurrentTechnique = _effect.Techniques[toneMapped ? "BasicColorDrawing" : "GlowDrawing"];
            _effect.CurrentTechnique.Passes[0].Apply();
            VertexPositionNormalTextureCustom Vertex(float x, float y) => new()
            {
                Position = new(x, y, 0, 1), Normal = Vector3.UnitZ, Tangent = Vector3.UnitX, BiNormal = Vector3.UnitY,
                TextureCoordinate = new(.25f, .5f), TextureCoordinate1 = Vector2.Zero,
            };
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, new[] { Vertex(-1, -1), Vertex(-1, 1), Vertex(1, -1), Vertex(1, 1) }, 0, 2);
            device.SetRenderTarget(null);
            var pixels = new Vector4[32 * 32]; _target.GetData(pixels); return pixels[16 * 32 + 16];
        }

        public void Dispose()
        {
            _effect.Dispose(); _target.Dispose(); Emission.Dispose(); Distortion.Dispose(); Mask.Dispose();
        }
    }
}
