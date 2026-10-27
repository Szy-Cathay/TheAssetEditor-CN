#nullable enable
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSpriteLowOpacityCullingTests
{
    [TestCase(.005f, false)]
    [TestCase(.02f, true)]
    public void DefaultNativeThreshold_CullsWeakVertexAlphaButKeepsTheHigherControl(float alpha, bool visible)
    {
        var simulation = Simulation(alpha);
        Assert.That(simulation.Frame(Camera()), Has.Length.EqualTo(visible ? 1 : 0));
        Assert.That(simulation.ParticleCount, Is.EqualTo(1), "Vertex culling must not remove or shorten the simulated particle.");
    }

    [Test]
    public void FirstGate_UsesTintBeforeTheHighInitialAlphaCanRaiseFinalOpacity()
    {
        var simulation = Simulation(initialAlpha: 10, tintAlpha: .005f);
        Assert.That(simulation.Frame(Camera()), Is.Empty, "Native VS81 tests Tint before sampling InitialAlpha and life Alpha.");
    }

    [Test]
    public void SecondGate_UsesTheLifeAlphaEvenWhenTintPassedTheFirstGate()
    {
        Assert.That(Simulation(lifeAlpha: .005f).Frame(Camera()), Is.Empty);
    }

    [Test]
    public void StrictFirstGate_CullsTintExactlyEqualToTheExplicitThreshold()
    {
        Assert.That(Simulation(initialAlpha: 2, tintAlpha: .125f).Frame(Camera(.125f)), Is.Empty);
    }

    [Test]
    public void StrictSecondGate_CullsFinalAlphaExactlyEqualToTheExplicitThreshold()
    {
        Assert.That(Simulation(initialAlpha: .125f).Frame(Camera(.125f)), Is.Empty);
    }

    [Test]
    public void ExplicitThreshold_KeepsARepresentableValueImmediatelyAboveIt()
    {
        var alpha = MathF.BitIncrement(.125f);
        Assert.That(Simulation(alpha).Frame(Camera(.125f)).Single().Colour.W, Is.EqualTo(alpha));
    }

    [Test]
    public void NegativeDiagnosticThreshold_PreservesWeakRawVertexOpacity()
    {
        Assert.That(Simulation(.005f).Frame(Camera(-1)).Single().Colour.W, Is.EqualTo(.005f));
    }

    [Test]
    public void FirstGate_AppliesDistanceFadeBeforeHighInitialAlpha()
    {
        var simulation = Simulation(initialAlpha: 10, distances: new(0, 200, 300, 400));
        Assert.That(simulation.Frame(Camera()), Is.Empty, "Camera distance1 yields Tint*fade=.005 but final alpha=.05.");
    }

    [Test]
    public void SecondGate_AppliesDistanceFadeToTheFinalVertexAlpha()
    {
        var simulation = Simulation(initialAlpha: .02f, distances: new(0, 4, 8, 10));
        Assert.That(simulation.Frame(Camera()), Is.Empty, "Tint*fade=.25 passes; final alpha=.005 does not.");
    }

    [TestCase(1)]
    [TestCase(2)]
    public void UncalibratedModelAndDecalRenderKinds_RetainTheirSeparateAlphaRules(int kind)
    {
        // Isolate the gate's render-kind admission without inventing a native model/decal opacity contract.
        var simulation = Simulation(.005f, kind: kind);
        Assert.That(simulation.Frame(Camera()).Single().Colour.W, Is.EqualTo(.005f));
    }

    [Test]
    public void CustomSpriteMaterial_IsOutsideTheCalibratedOrdinaryShaderGate()
    {
        Assert.That(Simulation(.005f, meshMaterialIndex: 0).Frame(Camera()).Single().Colour.W, Is.EqualTo(.005f));
    }

    [Test]
    public void TransparentDiffuseTexels_DoNotBecomeAVertexShaderOpacityGate()
    {
        var conversion = Conversion();
        var texture = VfxModelVariantPixelTests.Dds(Color.White);
        for (var index = 131; index < texture.Length; index += 4) texture[index] = 0;
        conversion.Textures[0] = texture;
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.1f, Matrix.Identity);
        Assert.That(simulation.Frame(Camera()).Single().Colour.W, Is.EqualTo(1), "Texture opacity is sampled later in the pixel shader.");
    }

    internal static TotalWarVfxSimulation Simulation(float initialAlpha = 1, float lifeAlpha = 1, float tintAlpha = 1,
        Vector4? distances = null, int kind = 0, int meshMaterialIndex = -1)
    {
        var conversion = Conversion(initialAlpha, lifeAlpha, tintAlpha, distances);
        var layer = conversion.Emitters.Single().Render; layer.Kind = kind; layer.MeshMaterialIndex = meshMaterialIndex;
        conversion.Emitters.Single().Render = layer;
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.1f, Matrix.Identity);
        return simulation;
    }

    internal static VfxConversion Conversion(float initialAlpha = 1, float lifeAlpha = 1, float tintAlpha = 1,
        Vector4? distances = null)
    {
        var child = VfxDocument.Read(VfxLibraryVisibilityTests.Source($"""
            <modifier fragment="vs_vertex_alpha"><property name="curve_alpha">{VfxLibraryVisibilityTests.Constant("alpha", lifeAlpha)}</property></modifier>
            """));
        var emitter = child.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleInitialAlpha"), VfxLibraryVisibilityTests.Constant("x", initialAlpha)));
        if (distances is { } cull)
            emitter.Add(new XElement("property", new XAttribute("name", "ParticleCullDistances"), new XAttribute("x", cull.X),
                new XAttribute("y", cull.Y), new XAttribute("z", cull.Z), new XAttribute("w", cull.W)));
        var root = VfxDocument.CreateComposition(); var reference = root.AddReference("child", child); reference.Apply();
        var row = reference.Selection.Elements("emitter").Single();
        row.SetAttributeValue("tint_min_a", tintAlpha); row.SetAttributeValue("tint_max_a", tintAlpha);
        return TotalWarVfxConverter.Convert(new(root.Write(), "vfx/low-opacity-diag.xml",
            path => path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child.Write() : [1]));
    }

    internal static CommonShaderParameters Camera(float threshold = .01f) => new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 1), Vector3.Zero, 0, 0, 0, 1, Vector3.One, [], VfxLowOpacityCulling: threshold);
}
