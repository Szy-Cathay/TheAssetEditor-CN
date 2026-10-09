using System.Text;
using System.Xml.Linq;
using Editors.VfxEditor;
using Moq;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Vfx;

namespace AssetEditorTests;

[TestClass]
public class TerryPreviewTests
{
    private static byte[] Effect(string references = "", string content = "") => Encoding.UTF8.GetBytes(
        $"<vfx revision=\"2\" enable_in_ted=\"false\"><emitters>{content}</emitters></vfx><vfx_references>{references}</vfx_references><editor_data><future value=\"keep\"/></editor_data>");
    private static string Reference(string name) => $"<vfx inst_name=\"{name}\" vfx_ref=\"{name}\"><offset x=\"3\"/></vfx>";
    private static TerryPreviewResource Resource(byte[] bytes, bool official = false) => new(PackFile.CreateFromBytes("resource", bytes), official);
    private static TerryPreviewBuilder Builder()
    {
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        return new(localization);
    }

    [TestMethod]
    public void ArbitraryEffect_UsesMemorySnapshotAndRecursivelyAliasesSharedReferences()
    {
        var source = Effect(Reference("any_fire") + Reference("any_smoke"), "<edited value=\"42\"/>");
        var original = source.ToArray();
        var resources = new Dictionary<string, TerryPreviewResource>
        {
            ["vfx\\any_fire.xml"] = Resource(Effect(Reference("shared"))),
            ["vfx\\any_smoke.xml"] = Resource(Effect(Reference("shared")), true),
            ["vfx\\shared.xml"] = Resource(Effect()),
        };
        var package = Builder().Build(source, "vfx\\completely_different_skill.xml", path => resources.GetValueOrDefault(path));
        Assert.AreEqual(4, package.EffectCount);
        Assert.AreEqual(4, package.Files.Count);
        var root = VfxDocument.Read(package.Files["vfx\\" + TerryPreviewBuilder.EffectName + ".xml"]);
        Assert.AreEqual("42", root.Root.Descendants("edited").Single().Attribute("value")!.Value);
        Assert.AreEqual("true", root.Root.Element("vfx")!.Attribute("enable_in_ted")!.Value);
        Assert.AreEqual("keep", root.Root.Descendants("future").Single().Attribute("value")!.Value);
        foreach (var bytes in package.Files.Values)
            foreach (var reference in VfxDocument.Read(bytes).Root.Elements("vfx_references").Elements("vfx"))
                Assert.IsTrue(package.Files.ContainsKey("vfx\\" + reference.Attribute("vfx_ref")!.Value + ".xml"));
        CollectionAssert.AreEqual(original, source);
        Assert.IsTrue(package.Files.Keys.All(path => path.StartsWith("vfx\\ae_cn_preview_")));
    }

    [TestMethod]
    public void CustomModel_CollectsMaterialsAndTexturesButReusesOfficialTextures()
    {
        var resources = new Dictionary<string, TerryPreviewResource>
        {
            ["vfx\\models\\custom.wsmodel"] = Resource(Encoding.UTF8.GetBytes("<model><materials><material>materials/custom.xml.material</material></materials></model>")),
            ["materials\\custom.xml.material"] = Resource(Encoding.UTF8.GetBytes("<material><textures><texture><source>vfx/textures/custom.dds</source></texture><texture><source>vfx/textures/stock.dds</source></texture></textures></material>")),
            ["vfx\\textures\\custom.dds"] = Resource([1, 2, 3]),
            ["vfx\\textures\\stock.dds"] = Resource([4, 5, 6], true),
        };
        var package = Builder().Build(Effect(content: "<property name=\"Model\" value=\"vfx/models/custom.wsmodel\"/>"),
            "vfx\\arbitrary.xml", path => resources.GetValueOrDefault(path));
        Assert.AreEqual(4, package.Files.Count);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, package.Files["vfx\\textures\\custom.dds"]);
        Assert.IsFalse(package.Files.ContainsKey("vfx\\textures\\stock.dds"));
    }

    [TestMethod]
    public void MissingNestedTexture_ReportsTheActualMissingPath()
    {
        var exception = Assert.ThrowsException<InvalidDataException>(() => Builder().Build(
            Effect(content: "<property value=\"vfx/textures/missing.dds\"/>"), "vfx\\sample.xml", _ => null));
        StringAssert.Contains(exception.Message, "vfx\\textures\\missing.dds");
    }

    [TestMethod]
    public void ReferenceBackToCurrentEffect_IsRejected()
    {
        var bytes = Effect(Reference("loop"));
        var exception = Assert.ThrowsException<InvalidDataException>(() => Builder().Build(bytes, "vfx\\loop.xml", _ => Resource(bytes)));
        StringAssert.Contains(exception.Message, "循环");
    }

    [TestMethod]
    public void InvalidResourcePath_IsRejectedBeforeLookup()
    {
        var calls = 0;
        Assert.ThrowsException<InvalidDataException>(() => Builder().Build(Effect(Reference("../outside")), "vfx\\sample.xml", _ => { calls++; return null; }));
        Assert.AreEqual(0, calls);
    }

    [TestMethod]
    public void NewComposition_DoesNotNeedASavedFilename()
    {
        var package = Builder().Build(VfxDocument.CreateComposition().Write(), "", _ => null);
        Assert.IsTrue(package.Files.ContainsKey("vfx\\" + TerryPreviewBuilder.EffectName + ".xml"));
    }

    [TestMethod]
    public void RealModEffect_BuildsWithGameDependenciesAndRoundTripsTheMoviePack()
    {
        var sample = Environment.GetEnvironmentVariable("ASSETEDITOR_TERRY_SAMPLE");
        var gameData = Environment.GetEnvironmentVariable("ASSETEDITOR_TERRY_GAME_DATA");
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_TERRY_TEST_OUTPUT");
        if (string.IsNullOrEmpty(sample) || string.IsNullOrEmpty(gameData) || string.IsNullOrEmpty(output))
        {
            Assert.Inconclusive("Set ASSETEDITOR_TERRY_SAMPLE, ASSETEDITOR_TERRY_GAME_DATA and ASSETEDITOR_TERRY_TEST_OUTPUT for real resource validation.");
            return;
        }
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        settings.CurrentSettings.GameDirectories.Add(new() { Game = GameTypeEnum.Warhammer3, Path = gameData });
        var loader = new PackFileContainerLoader(settings);
        var originals = loader.LoadAllCaFiles(GameTypeEnum.Warhammer3)!;
        var root = Directory.GetParent(Path.GetDirectoryName(sample)!)!.FullName;
        var source = File.ReadAllBytes(sample);
        var package = Builder().Build(source, Path.GetRelativePath(root, sample), path =>
        {
            var disk = Path.Combine(root, path);
            if (File.Exists(disk)) return new(PackFile.CreateFromFileSystem(Path.GetFileName(disk), disk), false);
            return originals.FileList.TryGetValue(path, out var file) ? new(file, true) : null;
        });
        Directory.CreateDirectory(output);
        var packPath = Path.Combine(output, "preview.pack");
        PackFileServiceUtility.ExportSnapshot(packPath, package.Files, GameInformationDatabase.Games[GameTypeEnum.Warhammer3], PackFileCAType.MOVIE);
        var saved = loader.Load(packPath)!;
        Assert.AreEqual(PackFileCAType.MOVIE, saved.Header.PackFileType);
        foreach (var entry in package.Files) CollectionAssert.AreEqual(entry.Value, saved.FileList[entry.Key].DataSource.ReadData());
        CollectionAssert.AreEqual(source, File.ReadAllBytes(sample));
        Console.WriteLine($"Built {package.EffectCount} VFX with {package.Files.Count} files, source unchanged: {sample}");
    }

    [TestMethod]
    public async Task ExistingUnownedPreviewPack_IsNeverOverwrittenAndDataSettingIsAccepted()
    {
        var root = Path.Combine(Path.GetTempPath(), "ae-terry-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "assembly_kit", "binaries"));
        File.WriteAllBytes(Path.Combine(root, "assembly_kit", "binaries", "tweak.modder.x64.exe"), []);
        var target = Path.Combine(root, "data", TerryPreviewService.PackName);
        byte[] original = [1, 7, 9];
        File.WriteAllBytes(target, original);
        try
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            settings.CurrentSettings.GameDirectories.Add(new() { Game = GameTypeEnum.Warhammer3, Path = Path.Combine(root, "data") });
            var files = new Mock<IPackFileService>();
            files.Setup(x => x.GetAllPackfileContainers()).Returns([]);
            var localization = new LocalizationManager();
            localization.LoadLanguage();
            var service = new TerryPreviewService(files.Object, settings, localization);
            var exception = await Assert.ThrowsExceptionAsync<IOException>(() => service.PreviewAsync(
                VfxDocument.CreateComposition().Write(), "", null, CancellationToken.None));
            StringAssert.Contains(exception.Message, "已停止覆盖");
            CollectionAssert.AreEqual(original, File.ReadAllBytes(target));
        }
        finally { Directory.Delete(root, true); }
    }
}
