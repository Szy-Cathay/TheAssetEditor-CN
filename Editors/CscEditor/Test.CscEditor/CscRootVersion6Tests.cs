using Editors.CscEditor.Data;
using Shared.GameFormats.Csc;
using Shared.GameFormats.Esf;

namespace Test.CscEditor;

public class CscRootVersion6Tests
{
    [Test]
    public void Version6_header_extension_survives_known_field_edit_and_stable_save()
    {
        var document = Version6Document();
        var scene = CscScene.Load(WriteDocument(document));
        var elementId = scene.Elements[0].Id;
        scene.Duration += 2;
        scene.Elements[0].Position!.Channels[0].OffsetAll(.25f);

        var written = CscSceneWriter.Write(scene);
        var reloaded = CscScene.Load(written);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.RootVersion, Is.EqualTo(6));
            Assert.That(reloaded.HeaderFields, Has.Count.EqualTo(12));
            Assert.That(reloaded.HeaderFields[10].Value, Is.EqualTo(.75f));
            Assert.That(reloaded.HeaderFields[11].Value, Is.True);
            Assert.That(reloaded.Duration, Is.EqualTo(scene.Duration));
            Assert.That(reloaded.FindElement(elementId)!.EvaluatePosition(0), Is.EqualTo(scene.Elements[0].EvaluatePosition(0)));
            Assert.That(CscSceneWriter.Write(reloaded), Is.EqualTo(written));
        });
    }

    [Test]
    public void Version6_with_unrecognised_extension_field_type_is_rejected()
    {
        var document = Version6Document();
        document.Root.Groups![0][11] = EsfNode.Leaf(EsfNodeKind.Ascii, "unknown extension");

        Assert.That(RootStructureDetector.Detect(document.Root.Children.ToList(), 6), Is.Null);
        Assert.Throws<InvalidDataException>(() => CscScene.Load(WriteDocument(document)));
    }

    [Test]
    public void Unknown_root_version_does_not_inherit_version6_support()
    {
        var fields = Version6Document().Root.Children.ToList();
        Assert.That(RootStructureDetector.Detect(fields, 7), Is.Null);
    }

    [Test]
    public void Official_version6_failures_round_trip_with_original_header_extension()
    {
        var directory = Environment.GetEnvironmentVariable("AE_CSC_V6_SAMPLE_DIR");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            Assert.Ignore("Set AE_CSC_V6_SAMPLE_DIR to bounded manifest-verified official ROOT v6 originals.");
        var files = Directory.GetFiles(directory!, "*.csc", SearchOption.AllDirectories);
        Assert.That(files, Is.Not.Empty);
        foreach (var file in files)
        {
            var original = CscScene.Load(File.ReadAllBytes(file));
            Assert.That(original.RootVersion, Is.EqualTo(6), file);
            var extension = original.HeaderFields.Skip(10).Select(field => field.Value).ToArray();
            original.Duration += .125f;
            var written = CscSceneWriter.Write(original);
            var reloaded = CscScene.Load(written);
            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Duration, Is.EqualTo(original.Duration), file);
                Assert.That(reloaded.HeaderFields.Skip(10).Select(field => field.Value), Is.EqualTo(extension), file);
                Assert.That(CscSceneWriter.Write(reloaded), Is.EqualTo(written), file);
            });
        }
    }

    private static EsfDocument Version6Document()
    {
        using var stream = new MemoryStream(CscSceneRoundTripTests.BuildSceneWithManifestBytes());
        var document = EsfReader.Read(stream);
        var fields = document.Root.Children.ToList();
        fields.InsertRange(10, [EsfNode.Leaf(EsfNodeKind.F32, .75f), EsfNode.Leaf(EsfNodeKind.Bool, true)]);
        return new EsfDocument
        {
            Signature = document.Signature,
            Unknown1 = document.Unknown1,
            CreationDate = document.CreationDate,
            Root = EsfNode.NewRecord(document.Root.Name!, 6, document.Root.RecordFlags, [fields]),
        };
    }

    private static byte[] WriteDocument(EsfDocument document)
    {
        using var stream = new MemoryStream();
        EsfWriter.Write(document, stream);
        return stream.ToArray();
    }
}
