#nullable enable
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vfx;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Vfx;

public class VfxMixedModelMaterialTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void MixedParts_KeepTheirOwnBindingsAndShareOneUnmodifiedParticle(bool mappedFirst)
    {
        var source = Source(); var conversion = TotalWarVfxConverter.Convert(new(source, "DIAG/mixed-model.xml", Resolver(mappedFirst)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field+":"+d.ReasonKey)));
        var parts = conversion.Emitters.Single().RenderParts(0).ToArray();
        Assert.That(parts, Has.Length.EqualTo(2));
        Assert.That(parts.Count(part => part.MeshMaterialIndex >= 0), Is.EqualTo(1));
        Assert.That(parts[mappedFirst ? 0 : 1].MeshMaterialIndex, Is.EqualTo(0));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.2f, Matrix.Identity);
        Assert.That(simulation.ParticleCount, Is.EqualTo(1));
        var frame = simulation.Frame(Camera);
        Assert.That(frame, Has.Length.EqualTo(1));
        Assert.That(frame[0].Colour.W, Is.EqualTo(2.5f));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    [Apartment(System.Threading.ApartmentState.STA)]
    public void MixedParts_ReachSeparateGpuLayersAndOnlyThePlainPartClampsAlpha(bool mappedFirst)
    {
        if (Environment.GetEnvironmentVariable("ASSETEDITOR_VFX_HEADLESS_TEST") != "1") Assert.Ignore("Native GPU is opt-in.");
        var graphics = new GraphicsDeviceServiceMock();
        try
        {
            var device = graphics.GraphicsDevice;
            using var preview = new EffekseerVfxPreview(device);
            preview.Reload(new(Source(), "DIAG/mixed-model.xml", Resolver(mappedFirst)));
            Assert.That(preview.EmitterCount, Is.EqualTo(1));
            preview.Seek(.2f, Matrix.Identity);
            using var target = new RenderTarget2D(device,128,64,false,SurfaceFormat.Vector4,DepthFormat.Depth24);
            device.SetRenderTarget(target); device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black,1,0);
            preview.Draw(Camera); device.SetRenderTarget(null);
            var submitted = (List<EffekseerParticle>)typeof(EffekseerVfxPreview)
                .GetField("_renderedParticles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(preview)!;
            Assert.That(submitted.Select(particle => particle.Colour.W).Order(), Is.EqualTo(new[] {1f,2.5f}));
            var pixels = new Vector4[128*64]; target.GetData(pixels);
            Assert.That(pixels.Count(pixel => pixel.X > .2f && pixel.Y < .01f), Is.GreaterThan(100));
            Assert.That(pixels.Count(pixel => pixel.Y > .2f && pixel.X < .01f), Is.GreaterThan(100));
        }
        finally { graphics.Release(); }
    }

    private static readonly CommonShaderParameters Camera = new(Matrix.CreateLookAt(new(1,.5f,10),new(1,.5f,0),Vector3.Up),
        Matrix.CreateOrthographic(2.6f,1.3f,.1f,100),new(1,.5f,10),Vector3.Zero,0,0,0,1,Vector3.One,[]);

    private static byte[] Source()
    {
        var root = VfxDocument.Read(VfxOpaqueModelMaterialTests.Source(Vector3.One,2.5f));
        root.Root.Descendants("emitter").Single().Add(new XElement("property",new XAttribute("name","MaximumParticles"),new XAttribute("value",1)));
        return root.Write();
    }

    private static Func<string,byte[]?> Resolver(bool mappedFirst)
    {
        var first = ModelFactory.Create().Load(VfxModelVariantTests.Model(0));
        first.ModelList[0] = [first.ModelList[0][0],ModelFactory.Create().Load(VfxModelVariantTests.Model(1)).ModelList[0][0]];
        first.RecalculateOffsets(); var model = ModelFactory.Create().Save(first);
        var wrapper = Encoding.UTF8.GetBytes($"<model version=\"1\"><geometry>vfx/mixed.rigid_model_v2</geometry><materials><material lod_index=\"0\" part_index=\"{(mappedFirst?0:1)}\">vfx/mapped.xml.material</material></materials></model>");
        var material = Encoding.UTF8.GetBytes("<material><shader>shaders/basic_additive.xml.shader</shader><textures><texture><slot>t_xml_base_glow_texture</slot><source>vfx/red.dds</source></texture><texture><slot>t_xml_scrolling_detail</slot><source>vfx/white.dds</source></texture></textures><params><param><name>glow_colour</name><type>float3</type><value>1,1,1</value></param><param><name>glow_strength</name><type>float</type><value>0.1</value></param></params></material>");
        return path => path.Replace('\\','/') switch
        {
            "vfx/character.wsmodel" => wrapper, "vfx/mixed.rigid_model_v2" => model, "vfx/mapped.xml.material" => material,
            "vfx/red.dds" => VfxModelVariantPixelTests.Dds(Color.Red), "vfx/white.dds" => VfxModelVariantPixelTests.Dds(Color.White),
            "vfx/texture-0.dds" or "vfx/texture-1.dds" => VfxModelVariantPixelTests.Dds(Color.Lime), _=>null,
        };
    }
}
