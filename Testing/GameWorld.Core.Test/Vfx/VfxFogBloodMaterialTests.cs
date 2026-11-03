#nullable enable
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public partial class VfxFogBloodMaterialTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void OriginalParametersAndSlots_MatchIndependentShippedShaderReflection(bool blood)
    {
        var fixture=Original(blood); var bytes=(byte[])fixture.Original.Clone();
        var mapped=VfxCustomSpriteMaterial.Read(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(mapped.Material.Parameters,Is.EqualTo(fixture.Expected));
            Assert.That(mapped.Material.TexturePaths,Is.EqualTo(fixture.Textures));
            Assert.That(mapped.Material.UsesVertexColour,Is.True);
            Assert.That(mapped.SoftDistance,Is.Zero,"The custom pixel shader owns its unquantized depth parameters.");
            Assert.That(mapped.Diagnostic,Is.EqualTo("Vfx.Preview.CustomMboitMaterialCandidate"));
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes)),Is.EqualTo(fixture.Sha256));
            Assert.That(bytes,Is.EqualTo(fixture.Original));
        });
    }

    [TestCase(false,0,true)]
    [TestCase(false,1,true)]
    [TestCase(false,2,false)]
    [TestCase(true,0,true)]
    [TestCase(true,1,false)]
    [TestCase(true,2,false)]
    public void OrdinaryTextureModifier_IsOnlyIgnoredForTheEmptyNativeFogNode(bool blood,int textureMode,bool accepted)
    {
        var fixture=Original(blood);var source=Source(fixture,textureMode);
        var conversion=TotalWarVfxConverter.Convert(new(source,"DIAG/fog-blood-modifier-contract.xml",path =>
            path.Replace('\\','/').Equals(fixture.Path,StringComparison.OrdinalIgnoreCase)?fixture.Original:VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters.Count,Is.EqualTo(accepted?1:0),string.Join(';',conversion.Diagnostics));
        if(!accepted)Assert.That(conversion.Diagnostics.Any(d=>d.Field=="ps_custom_shader" && d.ReasonKey=="Vfx.Preview.MaterialUnsupported"),Is.True);
        Assert.That(VfxDocument.Read(source).Write(),Is.EqualTo(source));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActualFrameAndUpload_PreserveTheFixedParticleUnaryAndRawMaterialAlpha(bool blood)
    {
        var fixture=Original(blood);var source=Source(fixture,0);
        var conversion=TotalWarVfxConverter.Convert(new(source,"DIAG/fog-blood-frame.xml",path =>
            path.Replace('\\','/').Equals(fixture.Path,StringComparison.OrdinalIgnoreCase)?fixture.Original:VfxModelVariantTests.Resolve(path)));
        var simulation=new TotalWarVfxSimulation(conversion);simulation.Seek(.2f,Matrix.Identity);
        var camera=new CommonShaderParameters(Matrix.Identity,Matrix.Identity,new(0,0,20),Vector3.Zero,0,0,0,1,Vector3.One,[]);
        var packets=simulation.ProjectedFrame(camera,false);Assert.That(packets,Has.Length.EqualTo(1));
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        var particle=((IEnumerable)typeof(TotalWarVfxSimulation).GetField("_particles",flags)!.GetValue(simulation)!).Cast<object>().Single();
        var unary=(float)particle.GetType().GetField("RandomAlpha",flags)!.GetValue(particle)!;
        Assert.That(packets[0].Render.NormalRotation,Is.EqualTo(new Vector4(unary,0,0,1)));
        Assert.That(packets[0].Render.MaterialTime,Is.EqualTo(.2f));
        Assert.That(packets[0].Render.Colour.W,Is.EqualTo(2.5f));
        if(!blood)Assert.That(packets[0].Render.ParticleSize,Is.EqualTo(4));
        using var preview=new EffekseerVfxPreview(null!);
        typeof(EffekseerVfxPreview).GetField("_conversion",flags)!.SetValue(preview,conversion);
        var built=preview.BuildRenderLayers(new byte?[conversion.Textures.Count]);
        Assert.That(built.FullLayers,Has.Length.EqualTo(1));Assert.That(built.HalfLayers,Is.Empty);
        Assert.That(built.Layers[built.FullLayers[0].LayerIndex].MeshMaterialIndex,Is.EqualTo(0));
        Assert.That(built.FullLayers[0].LegacyCoverageIndex,Is.EqualTo(-1));
        var count=(int)typeof(EffekseerVfxPreview).GetMethod("PrepareSubmission",flags)!.Invoke(preview,[packets])!;
        var uploaded=(EffekseerParticle[])typeof(EffekseerVfxPreview).GetField("_submissionParticles",flags)!.GetValue(preview)!;
        Assert.That(count,Is.EqualTo(1));Assert.That(uploaded[0].Colour.W,Is.EqualTo(2.5f));
        Assert.That(uploaded[0].NormalRotation.X,Is.EqualTo(unary));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingDuplicateOrInvalidMaterialInput_RemainsAnExplicitFailure(bool blood)
    {
        var fixture=Original(blood);
        for(var kind=0;kind<4;kind++)
        {
            using var stream=new MemoryStream(fixture.Original);var root=XElement.Load(stream);
            var first=root.Element("params")!.Elements("param").First();
            if(kind==0)first.Remove();
            else if(kind==1)first.Parent!.Add(new XElement(first));
            else if(kind==2)first.Element("value")!.Value="NaN";
            else root.Element("textures")!.Elements("texture").Last().Remove();
            Assert.Throws<VfxMappingException>(()=>VfxFogBloodMaterial.Read(root),$"blood={blood}, malformed={kind}");
        }
    }

    private static byte[] Source(Fixture fixture,int textureMode)
    {
        var document=VfxDocument.Read(VfxParticleEventMotionTests.Child());var emitter=document.Root.Descendants("emitter").Single();
        VfxParticleEventMotionTests.Set(emitter,"ParticleInitialScale",VfxParticleEventMotionTests.VectorCurve(new(4,1,1)));
        VfxParticleEventMotionTests.Set(emitter,"ParticleInitialAlpha",VfxLibraryVisibilityTests.Constant("x",2.5f));
        var quality=new XElement("quality",new XAttribute("value",0),
            new XElement("modifier",new XAttribute("fragment","vs_translation"),
                new XElement("property",new XAttribute("name","restriction"),new XAttribute("value",0))),
            new XElement("modifier",new XAttribute("fragment","ps_custom_shader"),
                new XElement("property",new XAttribute("name","material"),new XAttribute("path",fixture.Path))));
        if(textureMode!=0)quality.Add(new XElement("modifier",new XAttribute("fragment","ps_texture"),
            new XElement("property",new XAttribute("name","diffuse"),new XAttribute("path",textureMode==1?"":"vfx/textures/explicit.dds"))));
        emitter.Parent!.Parent!.Add(new XElement("behaviour_chains",new XElement("behaviour_chain",new XAttribute("id",(string)emitter.Attribute("id")!),quality)));
        return document.Write();
    }
}
