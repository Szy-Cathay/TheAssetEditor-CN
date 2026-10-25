using System.Text;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class TotalWarVfxConverterTests
{
    [Test]
    public void InitialSpeed_ReportsItsUncalibratedUnitsWithoutChangingTheSource()
    {
        var bytes = Encoding.UTF8.GetBytes(Document(Emitter("known",
            $"<property name=\"ParticleInitialSpeed\">{Constant("x")}</property>"), Behaviour("known")));
        var result = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics.Any(x => x.Field == "ParticleInitialSpeed" && x.ReasonKey == "Vfx.Preview.InitialMotionUncalibrated"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void UnknownGameFields_AreDiagnosedAndNeverRemoved()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Document(Emitter("known"), Behaviour("known")))).ToArray();
        var result = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics.Any(x => x.Field == "future" && x.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void HalfResolutionPass_IsDiagnosedInsteadOfBeingPresentedAsAnEquivalentFullResolutionPass()
    {
        var bytes = Encoding.UTF8.GetBytes(Document(Emitter("known"), Behaviour("known")).Replace("</vfx>", """
            <effect_render_stacks><effect_render_stack id="known"><property name="scene_view_type" quality="0" value="HALFRES_DEFERRED_ALPHA"/></effect_render_stack></effect_render_stacks></vfx>
            """));
        var result = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.Diagnostics.Any(x => x.Field == "scene_view_type" && x.ReasonKey == "Vfx.Preview.HalfResolutionUnsupported"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void UnknownCurveMode_DisablesOnlyAffectedLayer()
    {
        var nonlinear = "x_min,0,1;0,0,future_mode,future_mode,0,0,0,0,0;1,1,future_mode,future_mode,0,0,0,0,0:x,0;0,0";
        var text = Document(Emitter("known") + Emitter("unknown", $"<property name=\"ParticleScale\" quality=\"0\">{nonlinear}</property>"),
            Behaviour("known") + Behaviour("unknown"));
        var bytes = Encoding.UTF8.GetBytes(text);
        var result = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.TotalEmitterCount, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Any(x => x.Layer == "unknown" && x.ReasonKey == "Vfx.Preview.InterpolationUnsupported"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void RequestedQuality_UsesItsOverrideAndLeavesBaseDataUntouched()
    {
        var bytes = Encoding.UTF8.GetBytes(Document(Emitter("known", "<property name=\"Enable\" quality=\"1\" value=\"false\"/>"), Behaviour("known")));
        Assert.Multiple(() =>
        {
            Assert.That(Convert(bytes).Emitters, Has.Count.EqualTo(1));
            Assert.That(Convert(bytes, "1").Emitters, Is.Empty);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [TestCase("0", 0)]
    [TestCase("1", 1)]
    [TestCase("2", 1)]
    [TestCase("3", 0)]
    public void RenderPass_UsesSelectedQualityBeforeBaseFallback(string quality, int visible)
    {
        var bytes = Encoding.UTF8.GetBytes(Document(Emitter("known"), Behaviour("known")).Replace("</vfx>", """
            <effect_render_stacks><effect_render_stack id="known">
            <property name="scene_view_type" quality="0" value="VFXUI"/>
            <property name="scene_view_type" quality="1" value="HALFRES_DEFERRED_ALPHA"/>
            <property name="scene_view_type" quality="2" value="FULLRES_DEFERRED_ALPHA"/>
            </effect_render_stack></effect_render_stacks></vfx>
            """));
        var result = Convert(bytes, quality);
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(visible));
            Assert.That(result.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.PassUnsupported"), Is.EqualTo(visible == 0));
            Assert.That(result.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.HalfResolutionUnsupported"), Is.EqualTo(quality == "1"));
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void MissingRealTexture_IsReportedWithoutInventingAReplacement()
    {
        var result = TotalWarVfxConverter.Convert(new(Encoding.UTF8.GetBytes(Document(Emitter("known"), Behaviour("known"))), "sample.xml", _ => null));
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Is.Empty);
            Assert.That(result.Textures, Is.Empty);
            Assert.That(result.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.MissingResource"), Is.True);
        });
    }

    [Test]
    public void MissingParticleLifetime_UsesNativeDefaultWithoutChangingSource()
    {
        var document = VfxDocument.Read(Encoding.UTF8.GetBytes(Document(Emitter("known"), Behaviour("known"))));
        document.Root.Descendants("property").Single(x => (string)x.Attribute("name")! == "ParticleLifeTime").Remove();
        var bytes = document.Write(); var result = Convert(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(result.Emitters, Has.Count.EqualTo(1));
            Assert.That(result.Emitters[0].Life.Sample("x", .5, .5f), Is.EqualTo(1));
            Assert.That(result.Diagnostics.Any(x => x.Field == "ParticleLifeTime" && x.ReasonKey == "Vfx.Preview.DefaultEmitterCandidate"), Is.True);
            Assert.That(VfxDocument.Read(bytes).Write(), Is.EqualTo(bytes));
        });
    }

    [Test]
    public void Composition_ExpandsRealReferenceOverridesWithoutChangingEitherSourceDocument()
    {
        var child = Encoding.UTF8.GetBytes(Document(Emitter("known"), Behaviour("known")));
        var composition = VfxDocument.CreateComposition();
        composition.AddReference("child", VfxDocument.Read(child)).Apply();
        var reference = composition.Root.Element("vfx_references")!.Element("vfx")!;
        reference.SetAttributeValue("pos_x", "3"); reference.SetAttributeValue("scale_min", "2"); reference.SetAttributeValue("scale_max", "2");
        reference.SetAttributeValue("future_attribute", "preserved");
        var original = composition.Write();
        var conversion = TotalWarVfxConverter.Convert(new(original, "vfx/composition.xml", path => path == @"vfx\child.xml" ? child : [1]));
        var simulation = new TotalWarVfxSimulation(conversion);
        simulation.Seek(0, Microsoft.Xna.Framework.Matrix.Identity);
        var camera = new GameWorld.Core.Rendering.CommonShaderParameters(Microsoft.Xna.Framework.Matrix.Identity, Microsoft.Xna.Framework.Matrix.Identity,
            new(0, 0, 10), Microsoft.Xna.Framework.Vector3.Zero, 0, 0, 0, 1, Microsoft.Xna.Framework.Vector3.One, []);
        var particle = simulation.Frame(camera).Single();
        Assert.Multiple(() =>
        {
            Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
            Assert.That(particle.Transform.Translation.X, Is.EqualTo(3));
            Assert.That(particle.Transform.Right.Length(), Is.EqualTo(2));
            Assert.That(conversion.Diagnostics.Any(x => x.Field == "future_attribute" && x.ReasonKey == "Vfx.Preview.Unmapped"), Is.True);
            Assert.That(VfxDocument.Read(child).Write(), Is.EqualTo(child));
            Assert.That(composition.Write(), Is.EqualTo(original));
        });
        reference.Element("emitter")!.SetAttributeValue("enable", "false");
        var disabled = TotalWarVfxConverter.Convert(new(composition.Write(), "vfx/composition.xml", path => path == @"vfx\child.xml" ? child : [1]));
        Assert.That(disabled.Emitters, Is.Empty);
        Assert.That(disabled.DisabledEmitterCount, Is.EqualTo(1));
    }

    [Test]
    public void RecursiveReference_IsDiagnosedInsteadOfRecursingOrDroppingTheGameData()
    {
        var composition = VfxDocument.CreateComposition();
        composition.AddReference("loop", VfxDocument.Read(Encoding.UTF8.GetBytes(Document(Emitter("known"), Behaviour("known"))))).Apply();
        var bytes = composition.Write();
        var conversion = TotalWarVfxConverter.Convert(new(bytes, @"vfx\loop.xml", _ => bytes));
        Assert.That(conversion.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.ReferenceCycle"), Is.True);
        Assert.That(composition.Write(), Is.EqualTo(bytes));
    }

    [Test]
    public void ExtremeReferenceStart_IsRejectedBeforeItCanCreateAnUnboundedPrewarmLoop()
    {
        var child = Encoding.UTF8.GetBytes(Document(Emitter("known"), Behaviour("known")));
        var composition = VfxDocument.CreateComposition();
        composition.AddReference("child", VfxDocument.Read(child)).Apply();
        var node = composition.Root.Element("vfx_references")!.Element("vfx")!;
        node.SetAttributeValue("start_time_min", "-1e30"); node.SetAttributeValue("start_time_max", "-1e30");
        var bytes = composition.Write();
        var result = TotalWarVfxConverter.Convert(new(bytes, "vfx/test.xml", path => path == @"vfx\child.xml" ? child : [1]));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(x => x.ReasonKey == "Vfx.Preview.RangeUnsupported"), Is.True);
        Assert.That(composition.Write(), Is.EqualTo(bytes));
    }

    private static VfxConversion Convert(byte[] bytes, string quality = "0") =>
        TotalWarVfxConverter.Convert(new(bytes, "sample.xml", _ => [1], quality));
    private static string Document(string emitters, string behaviours) => $"""
        <vfx revision="2"><emitters>{emitters}</emitters><behaviour_chains>{behaviours}</behaviour_chains></vfx>
        """;
    private static string Emitter(string id, string extra = "") => $"""
        <emitter id="{id}" type="continuous_mesh"><future unknown="preserve"><payload><![CDATA[a&b]]></payload></future>
        <property name="EmissionRateCurve" quality="0">{Constant("x")}</property>
        <property name="ParticleLifeTime" quality="0">{Constant("x")}</property>
        <property name="ParticleScale" quality="0">{Scale}</property>
        <property name="InfiniteLife" quality="0" value="true"/>{extra}</emitter>
        """;
    private static string Constant(string channel) => $"{channel}_min,0,0;0,1,linear,linear,0,0,0,0,1:{channel},0;0,0";
    private const string Scale = "x_min,0,0;0,1,linear,linear,0,0,0,0,1|y_min,0,0;0,1,linear,linear,0,0,0,0,1|z_min,0,0;0,1,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2";
    private static string Behaviour(string id) => $"""
        <behaviour_chain id="{id}"><quality value="0"><modifier fragment="ps_texture">
        <texture_property name="diffuse" path="vfx/textures/real.dds"/></modifier></quality></behaviour_chain>
        """;
}
