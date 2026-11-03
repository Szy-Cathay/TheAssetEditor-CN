#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;

namespace GameWorld.Core.Test.Vfx;

public class VfxFungiMeshMaterialTests
{
    internal const string MaterialPath = "vfx/materials/wh2_dlc15_magic_fungi.xml.material";
    // Unchanged manifest original; the compressed payload is filled from the bounded official export.
    private const string CompressedOriginal = "H4sIAAAAAAACCs1YTY/aMBC9V+p/WO15RUKAVZBoDr33UvVuDfYkWPVHajvA7q+vnQSWVrvbmCa0B4Q/hnlv7DfjERsJDg0HUXz8cLdRILE47DLCBJ2viISKU1I2quKzoxSbpN0PhnYHDE3RfdnE8Ioz4rhyyIhEB4Kg5NbyPZJ9eSQg6h0Q2did0VoGX7Pup5uk99Q61cYRvUdjOMMiW6387i9Lwcjh0TUGbZicZ+3EOxDa3Xljy7X6dJ/dF454KLIFi4RqoZuA521O5n6BYvE1kP+iGQqbfG64YFxVNvnW4yQVB+XO3C+dzRizgWLrpaWTXPIZSE5o7c8M6teoUS2lVqeQk9bWm44LnP0z5MWtkRl3OzQDUDvDUTDPicC4Qer85hB8LKERjhx23OHYNKzzWTWMh9J8XLmfafTWAzhoU4Hydagy+GQpCCTpfBQuEuz3+f9XEU4V+a2iEMfu0tso9JQ2EsTfE+v8jELJr3i7oOw6QlenHGs1Po6iljmk6222JukizciSZjlZb+c5wS1dP1LcpjmjA5gduGKW6LJ/gFuCo/B7hHy+Zumc0Hydk+Vj7vlBoJtCCUtYZMuyjOdna6SNgD+o/zxpn+5NDQZk/4q34x6xbTF+L1bEOoOqcruXFiSE/FRjUQoNzvsO4255D6LBwid2N+iIvEC8g1Y3wqeyDwfZTYCig0pn16DdIKDb3I/jwpeUNzCy10AeroNR7g2QxauXkq0e0ln4pNlqOF7pG1/CFdGNi1BApP+uypJaH0LfM8HldN1+ODKyB//QdI3FJIJ2XHqlteVlklD6Z8Q3G3LCnLl8rNp2JgJltroWx2ghIhInnS28nJfXoU2bphdIAihKVDHpc8UJ6rK06AhI3cRAxYekLeWiTaBYYfgSFI3mhS7rKcM5ktLgjykRnuJCWF0BEBfCNfJ6nvoenke6h34YerZNcvG30U/oRz/3QxIAAA==";
    internal const string OriginalHash = "92d2d81472c2b621b46f9e0f89cac9e425d5703ac82ee1a9c8d130424adfa63e";
    internal static byte[] Original()
    {
        using var input = new MemoryStream(Convert.FromBase64String(CompressedOriginal));
        using var unzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(); unzip.CopyTo(output); return output.ToArray();
    }

    internal static readonly float[] Expected = [1,1,.1f,1,1,1,1,.25f,.5f,.025f,0,1,.1f,1,1,1.5f,.3f,.4f,1,1,.5f,1,.2f,1,1,5,.5f,1,1];
    internal static readonly string[] Textures =
    [
        "RigidModels/Buildings/Textures/giant_mushroom_base_colour.dds", "commontextures/dither.dds",
        "commontextures/default_white.dds", "commontextures/noise_colour.dds", "commontextures/organic_greyscale_01.dds",
        "RigidModels/Buildings/Textures/giant_mushroom_base_colour.dds", "RigidModels/Buildings/Textures/giant_mushroom_material_map.dds",
        "RigidModels/Buildings/Textures/giant_mushroom_normal.dds", "commontextures/default_noise1.dds",
    ];

    [Test]
    public void OriginalMaterial_PreservesEveryReflectedOffsetAndLogicalSlot()
    {
        var source = Original(); var before = source.ToArray();
        var material = VfxScrollingMeshMaterial.Read(source);
        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(source)), Is.EqualTo(OriginalHash));
            Assert.That(material.Parameters, Has.Length.EqualTo(80));
            Assert.That(material.Parameters.Take(29), Is.EqualTo(Expected), "Independent official DXBC AUTO_PARAMETERS order, 116 bytes.");
            Assert.That(material.Parameters.Skip(29), Is.All.Zero, "Padding is preview transport, not fabricated native parameters.");
            Assert.That(material.TexturePaths, Is.EqualTo(Textures), "The unused blood/winds slots cannot replace any sampled input.");
            Assert.That(material.UsesVertexColour, Is.True, "Original red drives vertex displacement independently of particle tint.");
            Assert.That(source, Is.EqualTo(before));
        });
        TestContext.Progress.WriteLine($"Official material={MaterialPath}, SHA256={OriginalHash}; native VS/GBUFFER/UI material equations, not full game deferred-lighting acceptance.");
    }

    [Test]
    public void OriginalMaterial_ReachesTheActualConverterAsOpaquePbr()
    {
        var source = VfxOpaqueModelMaterialTests.Source(new(.1f,.9f,.3f), .615f);
        var material = Original();
        var conversion = TotalWarVfxConverter.Convert(new(source, "DIAG/fungi-material-host.xml", path =>
            path.Replace('\\','/') == "vfx/character.xml.material" ? material
                : VfxOpaqueModelMaterialTests.Resolve(path, VfxModelVariantPixelTests.Dds(Color.White))));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1), string.Join(';', conversion.Diagnostics.Select(d => d.Field+":"+d.ReasonKey)));
        var layer = conversion.Emitters.Single().Render;
        Assert.Multiple(() =>
        {
            Assert.That(layer.MeshMaterialIndex, Is.EqualTo(0));
            Assert.That(layer.OpaqueModel, Is.EqualTo(1));
            Assert.That(conversion.MeshMaterials.Single().Parameters.Take(29), Is.EqualTo(Expected));
            Assert.That(conversion.MeshMaterials.Single().TextureIndexes.Select(i => conversion.TexturePaths[i]), Is.EqualTo(Textures));
            Assert.That(conversion.Models.Single().ApproximateMaterialShader, Is.Null);
            Assert.That(conversion.Diagnostics.Any(d => d.ReasonKey is "Vfx.Preview.MaterialUnsupported" or "Vfx.Preview.ModelMaterialFallback"), Is.False);
            Assert.That(Convert.ToHexStringLower(SHA256.HashData(material)), Is.EqualTo(OriginalHash));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingOrDuplicateNativeParameter_RemainsDiagnosed(bool duplicate)
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(Original()));
        var field = root.Element("params")!.Elements("param").Single(p => p.Element("name")!.Value == "time_source");
        if (duplicate) field.Parent!.Add(new XElement(field)); else field.Remove();
        var failure = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(failure!.Reason, Is.EqualTo("Vfx.Preview.AmbiguousField"));
    }

    [TestCase("material_map")]
    [TestCase("normal")]
    [TestCase("vertex_disp_texture")]
    public void MissingExtraSrv_IsNotHiddenByAWhiteTexture(string slot)
    {
        var root = XElement.Parse(Encoding.UTF8.GetString(Original()));
        root.Element("textures")!.Elements("texture").Single(t => t.Element("slot")!.Value == "t_xml_"+slot).Remove();
        var failure = Assert.Throws<VfxMappingException>(() => VfxScrollingMeshMaterial.Read(Encoding.UTF8.GetBytes(root.ToString())));
        Assert.That(failure!.Reason, Is.EqualTo("Vfx.Preview.MissingResource"));
    }
}
