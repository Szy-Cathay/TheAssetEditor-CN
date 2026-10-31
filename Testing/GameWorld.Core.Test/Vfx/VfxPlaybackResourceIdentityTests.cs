using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Vfx;

public class VfxPlaybackResourceIdentityTests
{
    [Test]
    public void IdenticalPayloadsRemainEqualAcrossNewArraysAndObjects()
    {
        var original = Conversion();
        var copied = Conversion();
        copied.TotalEmitterCount = 12;
        copied.DisabledEmitterCount = 2;
        copied.Diagnostics.Add(new("DIAG", "", "", "Vfx.Preview.Candidate"));
        Assert.That(VfxPlaybackResourceIdentity.Create(copied), Is.EqualTo(VfxPlaybackResourceIdentity.Create(original)),
            "Only uploaded resource payloads constrain reuse; simulation and render-layer parameters are reconfigured separately.");
    }

    [TestCase("texture-byte")]
    [TestCase("texture-usage")]
    [TestCase("texture-slot")]
    [TestCase("texture-path")]
    [TestCase("model-position")]
    [TestCase("model-normal")]
    [TestCase("model-colour")]
    [TestCase("model-uv")]
    [TestCase("model-index")]
    [TestCase("animation-payload")]
    [TestCase("animation-frame-count")]
    [TestCase("animation-cycles")]
    [TestCase("animation-null")]
    [TestCase("model-opaque")]
    [TestCase("model-rigid")]
    [TestCase("model-fallback")]
    [TestCase("material-slot")]
    [TestCase("material-parameter")]
    [TestCase("material-float-bits")]
    public void EveryUploadDependencyAndSlotInvalidatesTheCapturedIdentity(string change)
    {
        var conversion = Conversion();
        var before = VfxPlaybackResourceIdentity.Create(conversion);
        var model = conversion.Models[0];
        switch (change)
        {
            case "texture-byte": conversion.Textures[0][1]++; break;
            case "texture-usage": conversion.TextureUsages[0] = VfxTextureUsage.MaterialValue; break;
            case "texture-slot": conversion.Textures.Add([1]); conversion.TextureUsages.Add(VfxTextureUsage.Diffuse); conversion.TexturePaths.Add("other"); break;
            case "texture-path": conversion.TexturePaths[0] = "other"; break;
            case "model-position": model.Vertices[0].Position.X++; break;
            case "model-normal": model.Vertices[0].Normal.Y++; break;
            case "model-colour": model.Vertices[0].Colour.Z++; break;
            case "model-uv": model.Vertices[0].UV.X++; break;
            case "model-index": model.Indices[0]++; break;
            case "animation-payload": model.AnimationVertices![0].Tangent.X++; break;
            case "animation-frame-count": conversion.Models[0] = model with { AnimationFrameCount = 2 }; break;
            case "animation-cycles": conversion.Models[0] = model with { AnimationCycles = 2 }; break;
            case "animation-null": conversion.Models[0] = model with { AnimationVertices = null }; break;
            case "model-opaque": conversion.Models[0] = model with { OpaqueMaterial = true }; break;
            case "model-rigid": conversion.Models[0] = model with { RigidAnimation = true }; break;
            case "model-fallback": conversion.Models[0] = model with { ApproximateMaterialShader = "unknown" }; break;
            case "material-slot": conversion.MeshMaterials[0].TextureIndexes[0]++; break;
            case "material-parameter": conversion.MeshMaterials[0].Parameters[0]++; break;
            case "material-float-bits": conversion.MeshMaterials[0].Parameters[1] = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)); break;
            default: Assert.Fail(change); break;
        }
        Assert.That(VfxPlaybackResourceIdentity.Create(conversion), Is.Not.EqualTo(before),
            "The captured fingerprint must also detect mutation of arrays returned by the same resource resolver.");
    }

    [TestCase("MissingResource")]
    [TestCase("InvalidData")]
    [TestCase("GeometryUnsupported")]
    [TestCase("MaterialUnsupported")]
    [TestCase("TextureUnsupported")]
    [TestCase("ShadingNormalUnsupported")]
    [TestCase("LayerTextureUnavailable")]
    public void FailedResourcesCannotEnterTheFastPathEvenAlongsideADeterministicModelFallback(string diagnostic)
    {
        var conversion = Conversion();
        conversion.Models[0] = conversion.Models[0] with { ApproximateMaterialShader = "shaders/rigid_character_emissive.xml.shader" };
        conversion.Diagnostics.Add(new("DIAG", "model", "shaders/rigid_character_emissive.xml.shader", "Vfx.Preview.ModelMaterialFallback"));
        Assert.That(VfxPlaybackResourceIdentity.CanReuse(conversion), Is.True);
        conversion.Diagnostics.Add(new("DIAG", "", "", "Vfx.Preview." + diagnostic));
        Assert.That(VfxPlaybackResourceIdentity.CanReuse(conversion), Is.False);
    }

    [Test]
    public void ADeterministicDiffuseModelFallbackRetainsItsDiagnosticAndDifferentShaderIdentityCannotMatch()
    {
        var conversion = Conversion();
        const string shader = "shaders/rigid_character_emissive.xml.shader";
        conversion.Models[0] = conversion.Models[0] with { ApproximateMaterialShader = shader };
        var diagnostic = new VfxPreviewDiagnostic("DIAG", "model", shader, "Vfx.Preview.ModelMaterialFallback");
        conversion.Diagnostics.Add(diagnostic);
        var identity = VfxPlaybackResourceIdentity.Create(conversion);
        Assert.That(VfxPlaybackResourceIdentity.CanReuse(conversion), Is.True);
        Assert.That(conversion.Diagnostics, Is.EqualTo(new[] { diagnostic }));
        Assert.That(conversion.Models[0].ApproximateMaterialShader, Is.EqualTo(shader));
        conversion.Models[0] = conversion.Models[0] with { ApproximateMaterialShader = "shaders/another_original.xml.shader" };
        Assert.That(VfxPlaybackResourceIdentity.Create(conversion), Is.Not.EqualTo(identity));
        Assert.That(conversion.Diagnostics, Is.EqualTo(new[] { diagnostic }), "Resource identity checks must not suppress or rewrite the existing fallback diagnosis.");
    }

    private static VfxConversion Conversion()
    {
        var conversion = new VfxConversion();
        conversion.Textures.Add([1, 2, 3]);
        conversion.TextureUsages.Add(VfxTextureUsage.Diffuse);
        conversion.TexturePaths.Add("DIAG-resource");
        conversion.Models.Add(new([new EffekseerVertex { Position = Vector3.One }], [0], [new EffekseerVertex()], 1, 1));
        conversion.MeshMaterials.Add(new([0], [1, 0]));
        return conversion;
    }
}
