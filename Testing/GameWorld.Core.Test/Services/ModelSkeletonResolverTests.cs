using System.Buffers.Binary;
using System.Text;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.Vmd;

namespace Test.GameWorld.Core.Services;

public class ModelSkeletonResolverTests
{
    [TestCase("bigcat02")]
    [TestCase("BIGCAT02")]
    [TestCase("animations/skeletons/bigcat02.anim")]
    public void RigidModel_MatchesItsActualHeaderWithoutReadingGeometry(string target)
    {
        var source = new Mock<IDataSource>();
        source.SetupGet(value => value.Size).Returns(1_000_000);
        source.Setup(value => value.PeekData(RmvFileHeader.HeaderSize)).Returns(Header("bigcat02"));
        var file = new PackFile("unrelated_name.RIGID_MODEL_V2", source.Object);
        var resolver = CreateResolver();

        Assert.That(resolver.CreateFilter(target)(file), Is.True);
        source.Verify(value => value.ReadData(), Times.Never);
    }

    [Test]
    public void NestedVmdAndWsModel_MatchThePrimaryModelInsteadOfAnAttachment()
    {
        var body = Model("body.rigid_model_v2", "bigcat02");
        var attachment = Model("attachment.rigid_model_v2", "humanoid01");
        var wsModel = Xml("body.wsmodel", "<model><geometry>models/body.rigid_model_v2</geometry></model>");
        var inner = Xml("inner.variantmeshdefinition", """
            <VARIANT_MESH model="models/body.wsmodel">
              <SLOT name="attachment" attach_point="hand">
                <VARIANT_MESH model="models/attachment.rigid_model_v2" />
              </SLOT>
            </VARIANT_MESH>
            """);
        var outer = Xml("different_name.variantmeshdefinition", """
            <VARIANT_MESH><SLOT name="body">
              <VARIANT_MESH_REFERENCE definition="models/inner.variantmeshdefinition" />
            </SLOT></VARIANT_MESH>
            """);
        var resolver = CreateResolver(body, attachment, wsModel, inner);

        Assert.Multiple(() =>
        {
            Assert.That(resolver.CreateFilter("bigcat02")(outer), Is.True);
            Assert.That(resolver.CreateFilter("humanoid01")(outer), Is.False);
        });
    }

    [Test]
    public void VmdSlot_UsesTheFirstLoadedVariantInsteadOfAnyMatchingAlternative()
    {
        var first = Model("first.rigid_model_v2", "humanoid01");
        var second = Model("second.rigid_model_v2", "bigcat02");
        var vmd = Xml("variants.variantmeshdefinition", """
            <VARIANT_MESH><SLOT name="body">
              <VARIANT_MESH model="models/first.rigid_model_v2" />
              <VARIANT_MESH model="models/second.rigid_model_v2" />
            </SLOT></VARIANT_MESH>
            """);
        var resolver = CreateResolver(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(resolver.CreateFilter("humanoid01")(vmd), Is.True);
            Assert.That(resolver.CreateFilter("bigcat02")(vmd), Is.False);
        });
    }

    [Test]
    public void WsModelWithMultipleGeometries_UsesTheFirstSkeleton()
    {
        var first = Model("first.rigid_model_v2", "humanoid01");
        var second = Model("second.rigid_model_v2", "bigcat02");
        var wsModel = Xml("combined.wsmodel", """
            <model><geometry>models/first.rigid_model_v2</geometry>
            <geometry>models/second.rigid_model_v2</geometry></model>
            """);
        var resolver = CreateResolver(first, second);

        Assert.Multiple(() =>
        {
            Assert.That(resolver.CreateFilter("humanoid01")(wsModel), Is.True);
            Assert.That(resolver.CreateFilter("bigcat02")(wsModel), Is.False);
        });
    }

    [Test]
    public void CyclicReferences_DoNotHangOrBecomeMatchingModels()
    {
        var first = Xml("first.wsmodel", "<model><geometry>models/second.wsmodel</geometry></model>");
        var second = Xml("second.wsmodel", "<model><geometry>models/first.wsmodel</geometry></model>");
        var filter = CreateResolver(first, second).CreateFilter("bigcat02");

        Assert.Multiple(() =>
        {
            Assert.That(filter(first), Is.False);
            Assert.That(filter(second), Is.False);
        });
    }

    [Test]
    public void MissingMalformedOrStaticModels_AreNotReportedAsCompatible()
    {
        var missing = Xml("missing.wsmodel", "<model><geometry>missing.rigid_model_v2</geometry></model>");
        var malformed = Xml("broken.variantmeshdefinition", "<broken");
        var truncated = PackFile.CreateFromBytes("short.rigid_model_v2", [1, 2, 3]);
        var staticModel = Model("static.rigid_model_v2", "");
        var filter = CreateResolver().CreateFilter("bigcat02");

        Assert.Multiple(() =>
        {
            Assert.That(filter(missing), Is.False);
            Assert.That(filter(malformed), Is.False);
            Assert.That(filter(truncated), Is.False);
            Assert.That(filter(staticModel), Is.False);
        });
    }

    [Test]
    public void SharedReferences_AreReadOnceWithinAFilterAndRefreshedForTheNextFilter()
    {
        var source = new Mock<IDataSource>();
        source.SetupGet(value => value.Size).Returns(RmvFileHeader.HeaderSize);
        source.Setup(value => value.PeekData(RmvFileHeader.HeaderSize)).Returns(Header("bigcat02"));
        var model = new PackFile("shared.rigid_model_v2", source.Object);
        var first = Xml("one.wsmodel", "<model><geometry>models/shared.rigid_model_v2</geometry></model>");
        var second = Xml("two.wsmodel", "<model><geometry>models/shared.rigid_model_v2</geometry></model>");
        var resolver = CreateResolver(model);
        var filter = resolver.CreateFilter("bigcat02");

        Assert.That(filter(first) && filter(second), Is.True);
        source.Verify(value => value.PeekData(RmvFileHeader.HeaderSize), Times.Once);

        model.DataSource = new MemorySource(Header("humanoid01"));
        Assert.Multiple(() =>
        {
            Assert.That(resolver.CreateFilter("bigcat02")(first), Is.False);
            Assert.That(resolver.CreateFilter("humanoid01")(second), Is.True);
        });
    }

    [Test]
    public void CachedVmdParser_KeepsStrictErrorsSeparateFromNormalLoading()
    {
        const string xml = "<VARIANT_MESH model=\"models/body.wsmodel\" unknown=\"preserve\" />";
        Assert.Throws<InvalidOperationException>(() => VariantMeshDefinitionLoader.Load(xml, strict: true));
        Assert.That(VariantMeshDefinitionLoader.Load(xml).ModelReference, Is.EqualTo("models/body.wsmodel"));
    }

    [TestCase(CompressionFormat.Zstd)]
    [TestCase(CompressionFormat.Lz4)]
    [TestCase(CompressionFormat.Lzma1)]
    public void CompressedModel_UsesTheUncompressedHeaderLength(CompressionFormat format)
    {
        var header = Header("bigcat02");
        var bytes = FileCompression.Compress(header, format);
        Assert.That(bytes.Length, Is.LessThan(RmvFileHeader.HeaderSize));
        var path = Path.GetTempFileName();
        File.WriteAllBytes(path, bytes);
        var parent = new PackedFileSourceParent { FilePath = path };
        try
        {
            var file = new PackFile("compressed.rigid_model_v2", new PackedFileSource(
                parent, 0, bytes.Length, false, true, format, (uint)header.Length));
            Assert.That(CreateResolver().CreateFilter("bigcat02")(file), Is.True);
        }
        finally
        {
            parent.CloseStream();
            File.Delete(path);
        }
    }

    private static ModelSkeletonResolver CreateResolver(params PackFile[] files)
    {
        var references = files.ToDictionary(file => "models\\" + file.Name, StringComparer.OrdinalIgnoreCase);
        var service = new Mock<IPackFileService>();
        service.Setup(value => value.FindFile(It.IsAny<string>(), It.IsAny<PackFileContainer>()))
            .Returns((string path, PackFileContainer _) => references.GetValueOrDefault(path.Replace('/', '\\')));
        return new ModelSkeletonResolver(service.Object);
    }

    private static PackFile Model(string name, string skeleton) => PackFile.CreateFromBytes(name, Header(skeleton));
    private static PackFile Xml(string name, string content) => PackFile.CreateFromASCII(name, content);

    private static byte[] Header(string skeleton)
    {
        var bytes = new byte[RmvFileHeader.HeaderSize];
        Encoding.ASCII.GetBytes("RMV2").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 7);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 1);
        Encoding.UTF8.GetBytes(skeleton).CopyTo(bytes, 12);
        return bytes;
    }
}
