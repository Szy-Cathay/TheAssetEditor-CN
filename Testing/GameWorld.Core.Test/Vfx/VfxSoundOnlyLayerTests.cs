using System.Security.Cryptography;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxSoundOnlyLayerTests
{
    [TestCase("0")]
    [TestCase("1")]
    [TestCase("2")]
    public void CompleteOfficialSoundLayer_IsIdentifiedWithoutInventingMissingGeometry(string quality)
    {
        // Complete unmodified vfx_desc.pack source, not a visual emitter with its model removed.
        using var stream = typeof(VfxSoundOnlyLayerTests).Assembly.GetManifestResourceStream("Test.GameWorld.Core.Vfx.Fixtures.OfficialSoundOnlyVfx.xml")!;
        using var buffer = new MemoryStream(); stream.CopyTo(buffer); var original = buffer.ToArray();
        Assert.That(Convert.ToHexStringLower(SHA256.HashData(original)), Is.EqualTo("6cf8929b441d32c927c6c374078bf87fa6d0d7fe62585fcc58a162f104d3628b"));
        var result = TotalWarVfxConverter.Convert(new(original,"vfx/wh3_dlc26_sfx_murderous_exhortation_skull_explosion.xml",_=>throw new AssertionException("No visual dependency belongs to this sound-only layer."),quality));
        Assert.That(result.Emitters, Is.Empty);
        Assert.That(result.Diagnostics.Any(d=>d.Field=="SoundRecord" && d.ReasonKey=="Vfx.Preview.SoundOnlyLayer"), Is.True);
        Assert.That(result.Diagnostics.Any(d=>d.ReasonKey=="Vfx.Preview.GeometryUnsupported"), Is.False);
        Assert.That(VfxDocument.Read(original).Write(), Is.EqualTo(original));
    }

    [Test]
    public void SoundMetadata_DoesNotHideARealVisualEmitterOrBypassItsMissingGeometry()
    {
        var source=VfxDocument.Read(VfxModelVariantTests.Source(1));var emitter=source.Root.Descendants("emitter").Single();
        emitter.Add(new XElement("property",new XAttribute("name","SoundRecord"),new XAttribute("value","sound-diagnostic")));
        var converted=TotalWarVfxConverter.Convert(new(source.Write(),"DIAG/visual-with-sound.xml",VfxModelVariantTests.Resolve));
        Assert.That(converted.Emitters,Has.Count.EqualTo(1));
        emitter.Elements().Single(field=>(string?)field.Attribute("name")=="RigidData").Remove();
        var broken=TotalWarVfxConverter.Convert(new(source.Write(),"DIAG/broken-visual-with-sound.xml",VfxModelVariantTests.Resolve));
        Assert.That(broken.Emitters,Is.Empty);
        Assert.That(broken.Diagnostics.Any(d=>d.ReasonKey=="Vfx.Preview.SoundOnlyLayer"),Is.False);
        Assert.That(broken.Diagnostics.Any(d=>d.ReasonKey=="Vfx.Preview.GeometryUnsupported"),Is.True);
    }
}
