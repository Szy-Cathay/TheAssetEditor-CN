using System.Xml.Serialization;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Editors.AnimationFragmentEditor.AnimationPack.ViewModels;
using Editors.AnimationFragmentEditor.CampaignAnimBin;
using GameWorld.Core.Services;
using Moq;
using Shared.ByteParsing;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace AssetEditorTests;

[TestClass]
public class AnimPackRoundtripRegressionTests
{
    [TestMethod]
    public void TableRoundtrip_PreservesEmptySlotsAndUnmountTable()
    {
        var source = Bin();
        source.Unknown = "custom_unmount";
        source.AnimationTableEntries.Add(new() { AnimationId = 1 });
        source.AnimationTableEntries.Add(new()
        {
            AnimationId = 2,
            AnimationRefs = [new() { AnimationFile = "run.anim" }],
        });
        var editor = Table();
        editor.LoadFromBinary(source.ToByteArray(), source.FileName);

        var reloaded = FromTable(editor);

        Assert.AreEqual(2, reloaded.AnimationTableEntries.Count);
        Assert.AreEqual(0, reloaded.AnimationTableEntries[0].AnimationRefs.Count);
        Assert.AreEqual("custom_unmount", reloaded.Unknown);
    }

    [TestMethod]
    public void TableEdit_LaterCandidateUpdatesSharedSlotParameters()
    {
        var source = Bin();
        source.AnimationTableEntries.Add(new()
        {
            AnimationId = 1, BlendIn = 0.2f, SelectionWeight = 1,
            AnimationRefs = [new() { AnimationFile = "a.anim" }, new() { AnimationFile = "b.anim" }],
        });
        var editor = Table();
        editor.LoadFromBinary(source.ToByteArray(), source.FileName);
        editor.Rows[1].BlendInTime = 0.9f;
        editor.Rows[1].SelectionWeight = 4;
        editor.Rows[1].Wb5 = true;

        var reloaded = FromTable(editor);

        Assert.AreEqual(0.9f, reloaded.AnimationTableEntries[0].BlendIn);
        Assert.AreEqual(4f, reloaded.AnimationTableEntries[0].SelectionWeight);
        Assert.AreEqual(32, reloaded.AnimationTableEntries[0].WeaponBools);
        Assert.AreEqual(2, reloaded.AnimationTableEntries[0].AnimationRefs.Count);
    }

    [TestMethod]
    public void HeaderEdit_CanUndoToLoadedState()
    {
        var editor = Table();
        editor.LoadFromBinary(Bin().ToByteArray(), "sample.bin");

        editor.MountBin = "griffon";

        Assert.IsTrue(editor.UndoCommand.CanExecute(null));
        editor.UndoCommand.Execute(null);
        Assert.AreEqual("", editor.MountBin);
        Assert.IsFalse(editor.IsDirty);
    }

    [TestMethod]
    public void CampaignRoundtrip_PreservesIntegerAndCaseSensitiveValues()
    {
        var source = new CampaignAnimationBin
        {
            Version = 3, Reference = "sample", SkeletonName = "skeleton",
            Status = [new()
            {
                Name = "status_normal",
                Unknown = [new() { Animation = "Case.anim", AnimationMeta = "", SoundMeta = "", Type = "MixedCase", Value = 7 }],
                Transitions = [new() { Animation = "Transition.anim", AnimationMeta = "", SoundMeta = "", Type = "Idle", TransitionTo = "Battle" }],
            }],
        };

        var reloaded = CampaignAnimationBinLoader.Load(new ByteChunk(CampaignAnimationBinLoader.Write(source, "sample")));

        Assert.AreEqual(7, reloaded.Status[0].Unknown[0].Value);
        Assert.AreEqual("Case.anim", reloaded.Status[0].Unknown[0].Animation);
        Assert.AreEqual("MixedCase", reloaded.Status[0].Unknown[0].Type);
        Assert.AreEqual("Battle", reloaded.Status[0].Transitions[0].TransitionTo);
    }

    [TestMethod]
    public void AnimPackLoad_RoutesCampaignFileToCampaignParser()
    {
        var source = new CampaignAnimationBin { Version = 3, Reference = "sample", SkeletonName = "skeleton" };
        var database = new AnimationPackFileDatabase("sample.animpack");
        database.AddFile(new UnknownAnimFile("animations/campaign/database/bin/sample.bin", CampaignAnimationBinLoader.Write(source, "sample")));
        var file = PackFile.CreateFromBytes("sample.animpack", AnimationPackSerializer.ConvertToBytes(database));
        var service = new Mock<IPackFileService>();
        service.Setup(p => p.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(file.Name);

        var loaded = AnimationPackSerializer.Load(file, service.Object);

        Assert.AreEqual("CampaignAnimationPackFile", loaded.Files.Single().GetType().Name);
        CollectionAssert.AreEqual(CampaignAnimationBinLoader.Write(source, "sample"), loaded.Files.Single().ToByteArray());
    }

    [TestMethod]
    public void AnimPackLoad_RejectsImpossibleChildCountBeforeAllocating()
    {
        var file = PackFile.CreateFromBytes("invalid.animpack", BitConverter.GetBytes(int.MaxValue));
        var service = new Mock<IPackFileService>();
        service.Setup(p => p.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(file.Name);

        Assert.ThrowsException<InvalidDataException>(() => AnimationPackSerializer.Load(file, service.Object));
    }

    [TestMethod]
    public void HeaderUndoRedo_AfterApplyTracksTheNewSavedBaseline()
    {
        var editor = Table();
        editor.LoadFromBinary(Bin().ToByteArray(), "sample.bin");
        editor.MountBin = "griffon";
        editor.IsDirty = false;
        editor.UndoCommand.Execute(null);
        Assert.IsTrue(editor.IsDirty);
        Assert.AreEqual("", editor.MountBin);
        editor.RedoCommand.Execute(null);
        Assert.AreEqual("griffon", editor.MountBin);
        Assert.IsFalse(editor.IsDirty);
    }

    [TestMethod]
    public void JoiningExistingSlot_AdoptsItsSharedParameters()
    {
        var source = Bin();
        source.AnimationTableEntries.Add(new() { AnimationId = 1, BlendIn = 0.7f, SelectionWeight = 5, AnimationRefs = [new() { AnimationFile = "Case.anim" }] });
        source.AnimationTableEntries.Add(new() { AnimationId = 2, BlendIn = 0.2f, SelectionWeight = 1, AnimationRefs = [new() { AnimationFile = "run.anim" }] });
        var editor = Table(); editor.LoadFromBinary(source.ToByteArray(), source.FileName);
        editor.Rows[1].SlotName = editor.Rows[0].SlotName;
        Assert.AreEqual(0.7f, editor.Rows[1].BlendInTime);
        Assert.AreEqual(5f, editor.Rows[1].SelectionWeight);
        var loaded = FromTable(editor);
        Assert.AreEqual(1, loaded.AnimationTableEntries.Count);
        Assert.AreEqual("Case.anim", loaded.AnimationTableEntries[0].AnimationRefs[0].AnimationFile);
    }

    [DataTestMethod]
    [DataRow(2, false, 348)]
    [DataRow(3, true, 557)]
    [DataRow(3, true, 0)]
    public void CampaignRoundtrip_PreservesLegacyAndExtendedActionLayout(int version, bool extra, int id)
    {
        var source = new CampaignAnimationBin
        {
            Version = version, Reference = "sample", SkeletonName = "skeleton",
            Status = [new() { Name = "status_battle", Action = [new()
            { Animation = "Case.anim", Type = "global", Meta = "", SoundMeta = "sound.snd.meta", ActionType = "battle_draw", ActionId = id,
                HasExtraString = extra, ExtraString = "global", BlendTime = 0, Unknown = false }],
                Unk3 = [new() { Animation = "Cast.anim", Type = "global", AnimationMeta = "", SoundMeta = "", Value0 = 0.3f, Value1 = 1, Value2 = 5, Value3 = 7 }] }],
        };
        var loaded = CampaignAnimationBinLoader.Load(new ByteChunk(CampaignAnimationBinLoader.Write(source, "sample")));
        Assert.AreEqual(version, loaded.Version);
        Assert.AreEqual(extra, loaded.Status[0].Action[0].HasExtraString);
        Assert.AreEqual(id, loaded.Status[0].Action[0].ActionId);
        Assert.AreEqual("sound.snd.meta", loaded.Status[0].Action[0].SoundMeta);
        Assert.AreEqual(7, loaded.Status[0].Unk3[0].Value3);
    }

    [TestMethod]
    public void CampaignInvalidInput_RemainsBlockedAfterChangingCategoryAndCanUndo()
    {
        var model = new CampaignAnimationBin { Version = 3, Reference = "sample", SkeletonName = "skeleton",
            Status = [new() { Name = "global" }, new() { Name = "status_normal", Idle = [new()
                { Animation = "idle.anim", Type = "global", MetaFile = "", SoundMeta = "", BlendTime = 0.3f, Weight = 1 }] }] };
        var editor = new CampaignTableEditorViewModel(Mock.Of<IPackFileService>(), Mock.Of<IStandardDialogs>());
        editor.LoadFromBinary(CampaignAnimationBinLoader.Write(model, "sample"), "sample.bin");
        editor.Rows[0].Fields[0].Value = "invalid";
        editor.SelectedCategory = editor.Categories.Single(c => c.Name == "Action");
        Assert.IsTrue(editor.HasInvalidFields);
        Assert.IsTrue(editor.IsDirty);
        Assert.IsNull(editor.SaveToBinary("sample.bin", out var error));
        Assert.IsNotNull(error);
        editor.UndoCommand.Execute(null);
        Assert.IsFalse(editor.HasInvalidFields);
        Assert.IsFalse(editor.IsDirty);
    }

    [TestMethod]
    public void CampaignInvalidXmlCategory_IsRejectedBeforeDataCouldBeDropped()
    {
        var source = new CampaignAnimationBin { Version = 3, Reference = "sample", SkeletonName = "skeleton",
            Status = [new() { Name = "global", Idle = [new()] }] };
        Assert.IsFalse(Validator.ValidateAnimationData(source, Mock.Of<IPackFileService>(), "sample.bin"));
    }

    [TestMethod]
    public void FragmentTableRoundtrip_PreservesAdditionalFieldsAndSkeletonAssignments()
    {
        var source = new AnimationFragmentFile("sample.frg", null!, GameTypeEnum.Warhammer3)
        {
            Skeletons = new Shared.GameFormats.DB.StringArrayTable("RootSkeleton", "OtherSkeleton", "RootSkeleton"),
            Fragments = [new() { Slot = DefaultAnimationSlotTypeHelper.GetFromId(1), RecordId = 29,
                Skeleton = "OtherSkeleton", AnimationFile = "Case.anim", Comment = "保留备注", Ignore = true, Unknown0 = 7, WeaponBone = 129 }],
        };
        var editor = Table(); editor.LoadFromBinary(source.ToByteArray(), source.FileName);
        var bytes = editor.SaveToBinary(source.FileName, out var error);
        Assert.IsNull(error);
        Assert.IsNotNull(bytes);
        var loaded = new AnimationFragmentFile(source.FileName, bytes, GameTypeEnum.Warhammer3);
        CollectionAssert.AreEqual(source.Skeletons.Values, loaded.Skeletons.Values);
        Assert.AreEqual(29, loaded.Fragments[0].RecordId);
        Assert.AreEqual(7, loaded.Fragments[0].Unknown0);
        Assert.AreEqual(129, loaded.Fragments[0].WeaponBone);
        Assert.IsTrue(loaded.Fragments[0].Ignore);
        Assert.AreEqual("保留备注", loaded.Fragments[0].Comment);
        Assert.AreEqual("Case.anim", loaded.Fragments[0].AnimationFile);
        editor.Skeleton = "NewRoot";
        editor.UndoCommand.Execute(null);
        Assert.AreEqual("RootSkeleton", editor.Skeleton);
        Assert.IsFalse(editor.IsDirty);
    }

    [TestMethod]
    public void ClassicXmlRoundtrip_PreservesFragmentParameters()
    {
        var source = new AnimationBin("classic.bin");
        source.AnimationTableEntries.Add(new("Set", "Skeleton") { FragmentReferences = [new() { Name = "Case.frg", Unknown = 7 }, new() { Name = "Case.frg", Unknown = 9 }] });
        var converter = new Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinConverter.AnimationBinFileToXmlConverter();
        var bytes = converter.ToBytes(converter.GetText(source.ToByteArray()), source.FileName, Mock.Of<IPackFileService>(), out var error);
        Assert.IsNull(error);
        var loaded = new AnimationBin(source.FileName, bytes);
        Assert.AreEqual(7, loaded.AnimationTableEntries[0].FragmentReferences[0].Unknown);
        Assert.AreEqual(9, loaded.AnimationTableEntries[0].FragmentReferences[1].Unknown);
        Assert.AreEqual("Case.frg", loaded.AnimationTableEntries[0].FragmentReferences[0].Name);
    }

    private static AnimSetTableEditorViewModel Table()
    {
        var service = new Mock<IPackFileService>();
        service.Setup(p => p.GetAllPackfileContainers()).Returns([]);
        return new(service.Object, Mock.Of<ISkeletonAnimationLookUpHelper>(),
            new MetaDataFileParser(Mock.Of<IMetaDataDatabase>()), null!, GameTypeEnum.Warhammer3);
    }

    private static AnimationBinWh3 Bin() => new("sample.bin")
    {
        Name = "sample", SkeletonName = "humanoid01", LocomotionGraph = "graph.xml",
    };

    private static AnimationBinWh3 FromTable(AnimSetTableEditorViewModel table)
    {
        using var reader = new StringReader(table.BuildXmlString());
        var xml = (XmlFormat)new XmlSerializer(typeof(XmlFormat)).Deserialize(reader)!;
        return new("sample.bin", new Wh3ConversionProbe().Write(xml));
    }

    private sealed class Wh3ConversionProbe() : AnimationBinWh3FileToXmlConverter(null!, null!, null!)
    {
        public byte[] Write(XmlFormat xml) => ConvertXmlToBinary(xml, "sample.bin");
    }
}
