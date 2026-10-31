using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationPack;
using Test.TestingUtility.TestUtility;

namespace Testing.GameWorld.Core.Animation;

[TestFixture]
internal class ThreeKingdomsAnimationFragmentTests
{
    private const string SourcePath = "animations/database/battle/fragments/inf_flag_default.txt";
    private const string FixtureDirectory = "Testing/GameWorld.Core.Test/Animation/Fixtures";

    [Test]
    public void OfficialOriginal_PreservesStandPersistentAndEveryActiveVariantWithoutWritingSource()
    {
        // Original 3K fragment, not a reduced or rewritten JSON diagnostic.
        var bytes = File.ReadAllBytes(PathHelper.GetDataFile("OfficialThreeKingdomsFlagFragment.txt", FixtureDirectory));
        const string sourceHash = "617D5475327ACF9A71531D6C840FF1CBE50464F999B44AF790D1545E57FDAD0C";
        Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(sourceHash));

        var fragment = ThreeKingdomsAnimationFragment.Read(SourcePath, bytes);
        var stand = fragment.Entries.Single(entry => entry.SlotName == "STAND");
        var persistent = fragment.Entries.Single(entry => entry.SlotName == "PERSISTENT_METADATA_ALIVE");
        var charges = fragment.Entries.Where(entry => entry.SlotName == "CHARGE").ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(fragment.Name, Is.EqualTo("inf_flag_default"));
            Assert.That(fragment.SkeletonName, Is.EqualTo("male01"));
            Assert.That(fragment.FileName, Is.EqualTo("inf_flag_default.txt"));
            Assert.That(fragment.FullPath, Is.EqualTo(SourcePath.Replace('/', '\\')));
            Assert.That(fragment.PackFileReference.FileName, Is.EqualTo(fragment.FullPath));
            Assert.That(fragment.PackFileReference.Files, Is.Empty);
            Assert.That(fragment, Is.Not.InstanceOf<Shared.GameFormats.AnimationPack.AnimPackFileTypes.IAnimationPackFile>());
            Assert.That(fragment.Entries, Has.Count.EqualTo(261));
            Assert.That(fragment.Diagnostics, Is.Empty);
            Assert.That(stand.Index, Is.EqualTo(-1));
            Assert.That(stand.SlotIndex, Is.EqualTo(AnimationSlotTypeHelper3k.GetfromValue("STAND").Id));
            Assert.That(stand.AnimationFile, Is.EqualTo("animations/battle/character/male01/infantry/inf_flag/default/inf_flag_default_01.anim"));
            Assert.That(stand.MetaFile, Is.EqualTo("animations/battle/character/male01/infantry/inf_flag/default/inf_flag_default_01.anm.meta"));
            Assert.That(stand.SoundFile, Is.EqualTo("animations/audio/battle/character/male01/infantry/inf_flag/default/inf_flag_default_01.{1gvuyg9}.snd.meta"));
            Assert.That(stand.BlendInTime, Is.EqualTo(0.15f));
            Assert.That(stand.SelectionWeight, Is.EqualTo(1));
            Assert.That(stand.WeaponBools, Is.EqualTo(3));
            Assert.That(persistent.AnimationFile, Is.EqualTo("animations/skeletons/male01.anim"));
            Assert.That(persistent.MetaFile, Is.EqualTo("animations/battle/persistent/inf_flag_default_persistent_metadata_alive_0.anm.meta"));
            Assert.That(persistent.SoundFile, Is.EqualTo("animations/audio/battle/persistent/cav_1h095_strategist_mandatory_persistent_metadata_alive_0.{1f9905o}.snd.meta"));
            Assert.That(charges.Select(entry => entry.Index), Is.EqualTo(new[] { 1, 2, 3, 4 }));
            Assert.That(charges.Select(entry => Path.GetFileName(entry.AnimationFile)), Is.EqualTo(new[]
            {
                "inf_1h095dun_charge_06.anim", "inf_1h095dun_charge_05.anim",
                "inf_1h095dun_charge_07.anim", "inf_1h095dun_charge_03.anim"
            }));
            Assert.That(fragment.Entries.Any(entry => entry.SlotName == "ATTACK_BRACING_3"), Is.False, "Authored disabled slot has an entry, but must not be advertised as active.");
            Assert.That(fragment.Entries.Any(entry => entry.SlotName == "ATTACK_FAILED_10"), Is.False, "Empty slot must not fabricate an animation.");
            Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo(sourceHash));
        });
    }

    [TestCase("animations/database/battle/fragments/example.txt", true)]
    [TestCase("ANIMATIONS\\DATABASE\\BATTLE\\FRAGMENTS\\EXAMPLE.TXT", true)]
    [TestCase("animations/database/battle/fragments/subfolder/example.txt", false)]
    [TestCase("animations/database/battle/fragments/../example.txt", false)]
    [TestCase("animations/database/battle/fragments/.txt", false)]
    [TestCase("animations/database/battle/fragments/example.bin", false)]
    [TestCase("animations/database/campaign/fragments/example.txt", false)]
    [TestCase("other/animations/database/battle/fragments/example.txt", false)]
    public void ResourcePath_AcceptsOnlyTheDirectOfficialFragmentDirectory(string path, bool expected)
    {
        Assert.That(ThreeKingdomsAnimationFragment.IsResourcePath(path), Is.EqualTo(expected));
    }

    [Test]
    public void EmptyAnimationAndEmptySlots_DoNotBecomeBindPoseOrImplicitPersistentMetadata()
    {
        var json = CreateMinimalJson();
        json["SlotData"]!["STAND"]!["Entries"]!["#1"]!["animation"] = "";
        var fragment = Read(json);
        Assert.Multiple(() =>
        {
            Assert.That(fragment.Entries, Is.Empty);
            Assert.That(fragment.Diagnostics, Has.Count.EqualTo(1));
        });
    }

    [TestCase("Ignore")]
    [TestCase("SingleFrameVariant")]
    public void UnverifiedSlotModes_AreDiagnosedInsteadOfPretendingToPlayTheFullClip(string flag)
    {
        var json = CreateMinimalJson();
        json["SlotData"]!["STAND"]!["Config"]![flag] = true;
        var fragment = Read(json);
        Assert.Multiple(() =>
        {
            Assert.That(fragment.Entries, Is.Empty);
            Assert.That(fragment.Diagnostics.Single(), Does.Contain(flag));
        });
    }

    [Test]
    public void CombinedFragments_AreRejectedWithoutInventingInheritanceRules()
    {
        var json = CreateMinimalJson();
        json["Properties"]!["Combined"] = true;
        Assert.Throws<InvalidDataException>(() => Read(json));
    }

    [Test]
    public void MetadataList_IsNotSilentlyTruncatedToOneFile()
    {
        var json = CreateMinimalJson();
        json["SlotData"]!["STAND"]!["Entries"]!["#1"]!["metadata"] = new JsonArray(
            "animations/battle/first.anm.meta", "animations/battle/second.anm.meta");
        Assert.Throws<InvalidDataException>(() => Read(json));
    }

    [Test]
    public void NullOrMalformedSlotStructure_IsRejectedRatherThanExposedAsAValidFragment()
    {
        var json = CreateMinimalJson();
        json["SlotData"]!["STAND"]!["Entries"]!["#1"]!["metadata"] = null;
        Assert.Throws<InvalidDataException>(() => Read(json));
    }

    [Test]
    public void Discovery_UsesActualPfsOverridePriorityAndDoesNotFallBackToTheOfficialDuplicate()
    {
        var official = CreateContainer("official", ca: true);
        var replacement = CreateContainer("replacement");
        AddJson(official, CreateMinimalJson("original"));
        AddJson(replacement, CreateMinimalJson("override"));
        var service = CreateService(official, replacement);

        var fragments = ThreeKingdomsAnimationFragment.LoadForSkeleton(service, "male01");
        Assert.That(fragments.Select(fragment => fragment.Name), Is.EqualTo(new[] { "override" }));

        replacement.FileList[SourcePath.Replace('/', '\\')].DataSource = new MemorySource(Encoding.UTF8.GetBytes("not JSON"));
        var failures = new List<string>();
        fragments = ThreeKingdomsAnimationFragment.LoadForSkeleton(service, "male01", onInvalid: (path, _) => failures.Add(path));
        Assert.Multiple(() =>
        {
            Assert.That(fragments, Is.Empty, "Invalid high-priority resource must not leak through to lower-priority content.");
            Assert.That(failures, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Discovery_FiltersSkeletonAndCaPacksWithoutParsingUnrelatedTextOrAnimpack()
    {
        var official = CreateContainer("official", ca: true);
        AddJson(official, CreateMinimalJson());
        official.FileList["animations\\database\\other.txt"] = PackFile.CreateFromASCII("other.txt", "not a fragment");
        official.FileList["animations\\normal.animpack"] = PackFile.CreateFromASCII("normal.animpack", "not JSON");
        var service = CreateService(official);
        var failures = new List<string>();
        Assert.Multiple(() =>
        {
            Assert.That(ThreeKingdomsAnimationFragment.LoadForSkeleton(service, "female01"), Is.Empty);
            Assert.That(ThreeKingdomsAnimationFragment.LoadForSkeleton(service, "male01", onlyPacksThatCanBeSaved: true), Is.Empty);
            Assert.That(ThreeKingdomsAnimationFragment.LoadForSkeleton(service, "male01", onInvalid: (path, _) => failures.Add(path)), Has.Count.EqualTo(1));
            Assert.That(failures, Is.Empty);
        });
    }

    [TestCase("combined")]
    [TestCase("structure")]
    [TestCase("oversize")]
    [TestCase("syntax")]
    public void Discovery_RejectsInvalidOverrideButStillLoadsOtherValidFragments(string invalidMode)
    {
        var official = CreateContainer("official", ca: true);
        var replacement = CreateContainer("replacement");
        AddJson(official, CreateMinimalJson("must_not_leak_through"));
        const string validPath = "animations\\database\\battle\\fragments\\valid.txt";
        official.FileList[validPath] = PackFile.CreateFromBytes(
            "valid.txt", Encoding.UTF8.GetBytes(CreateMinimalJson("valid").ToJsonString()));

        var invalid = CreateMinimalJson("invalid");
        if (invalidMode == "combined")
            invalid["Properties"]!["Combined"] = true;
        else if (invalidMode == "structure")
            invalid["SlotData"]!["STAND"]!["Config"] = null;
        var invalidBytes = Encoding.UTF8.GetBytes(invalidMode == "syntax" ? "not JSON" : invalid.ToJsonString());
        var invalidFile = PackFile.CreateFromBytes("inf_flag_default.txt", invalidBytes);
        var oversizedSource = new Mock<IDataSource>(MockBehavior.Strict);
        if (invalidMode == "oversize")
        {
            oversizedSource.SetupGet(source => source.Size).Returns(8 * 1024 * 1024 + 1L);
            invalidFile.DataSource = oversizedSource.Object;
        }
        replacement.FileList[SourcePath.Replace('/', '\\')] = invalidFile;
        var service = CreateService(official, replacement);
        var failures = new List<(string Path, Exception Error)>();

        var fragments = ThreeKingdomsAnimationFragment.LoadForSkeleton(
            service, "male01", onInvalid: (path, error) => failures.Add((path, error)));

        Assert.Multiple(() =>
        {
            Assert.That(fragments.Select(fragment => fragment.Name), Is.EqualTo(new[] { "valid" }),
                "A rejected fragment must neither abort loading other paths nor expose its lower-priority duplicate.");
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures.Single().Path, Is.EqualTo(SourcePath.Replace('/', '\\')));
            Assert.That(failures.Single().Error, invalidMode == "syntax"
                ? Is.InstanceOf<System.Text.Json.JsonException>()
                : Is.InstanceOf<InvalidDataException>());
        });
        oversizedSource.Verify(source => source.ReadData(), Times.Never,
            "The declared size guard must reject an oversized source before allocating its contents.");
    }

    private static ThreeKingdomsAnimationFragment Read(JsonNode json) =>
        ThreeKingdomsAnimationFragment.Read(SourcePath, Encoding.UTF8.GetBytes(json.ToJsonString()));

    private static JsonNode CreateMinimalJson(string name = "source") => JsonNode.Parse($$"""
        {
          "Properties": { "Combined": false, "FragmentName": "{{name}}", "SkeletonName": "male01" },
          "SlotData": {
            "STAND": {
              "Config": { "BlendInTime": 0.15, "Disable": false, "EquipmentDisplay": 3,
                          "Ignore": false, "SelectionWeight": 1, "SingleFrameVariant": false },
              "Entries": { "#1": { "animation": "animations/battle/stand.anim", "metadata": [] } }
            }
          }
        }
        """)!;

    private static PackFileContainer CreateContainer(string name, bool ca = false) => new(name)
    {
        SystemFilePath = $"C:\\tests\\{name}.pack", IsCaPackFile = ca
    };

    private static void AddJson(PackFileContainer container, JsonNode json) =>
        container.FileList[SourcePath.Replace('/', '\\')] = PackFile.CreateFromBytes("inf_flag_default.txt", Encoding.UTF8.GetBytes(json.ToJsonString()));

    private static IPackFileService CreateService(params PackFileContainer[] containers)
    {
        var service = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false };
        foreach (var container in containers)
            service.AddContainer(container);
        return service;
    }
}
