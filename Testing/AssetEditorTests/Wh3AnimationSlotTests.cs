using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommonControls.Editors.AnimationPack;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Editors.AnimationFragmentEditor.AnimationPack.ViewModels;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class Wh3AnimationSlotTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestInitialize]
    public void LoadLanguage() => new LocalizationManager().LoadLanguage();

    // RPFM schemas 5d841c5c2a73d27495c6fed7282dc011c5517e1c, WH3 9.0.
    [DataTestMethod]
    [DataRow(0, "MISSING_ANIM")]
    [DataRow(1, "STAND")]
    [DataRow(1182, "RIDER_REBIRTH_FLYING")]
    [DataRow(1183, "TELEPORT_ARRIVAL")]
    [DataRow(1184, "TELEPORT_DEPARTURE")]
    [DataRow(1185, "WAVERING_STAND_LOOK_FORWARD_1")]
    [DataRow(1600, "ENGINE_CREW_LOADING_IDLE_1")]
    [DataRow(1603, "ENGINE_CREW_LOADING_IDLE_4")]
    [DataRow(1694, "STAND_TO_ENGINE_CREW_FIRING_IDLE_5")]
    [DataRow(1705, "RIDER_REFUSE_CAN_THROW_RIDER_4")]
    [DataRow(1706, "RIDER_STAND")]
    [DataRow(1711, "RIDER_WALK_1")]
    [DataRow(1722, "RIDER_RUN_1")]
    [DataRow(1756, "RIDER_FLY_STAND")]
    [DataRow(1901, "RIDER_ATTACK_1")]
    [DataRow(2291, "BEAM_TEST_LOOPED")]
    public void Wh3SlotMapping_UsesCurrentGameIds(int id, string name)
    {
        var helper = new BaseAnimationSlotHelper(GameTypeEnum.Warhammer3);

        Assert.AreEqual(name, helper.TryGetFromId(id)?.Value);
        Assert.AreEqual(id, helper.GetfromValue(name.ToLowerInvariant())?.Id);
    }

    [TestMethod]
    public void AnimPackAndGenericPreview_ResolveRiderSlotsWithoutChangingIds()
    {
        var source = RiderBin();
        var database = new AnimationPackFileDatabase("rider.animpack");
        database.AddFile(source);
        var bytes = AnimationPackSerializer.ConvertToBytes(database);
        var file = PackFile.CreateFromBytes(database.FileName, bytes);
        var service = new Mock<IPackFileService>();
        service.Setup(p => p.GetFullPath(file, It.IsAny<PackFileContainer?>())).Returns(file.Name);

        var loaded = AnimationPackSerializer.Load(file, service.Object, GameTypeEnum.Warhammer3);
        var bin = (AnimationBinWh3)loaded.Files.Single();
        var entries = ((IAnimationBinGenericFormat)bin).Entries;

        CollectionAssert.AreEqual(new[] { "RIDER_STAND", "RIDER_WALK_1", "RIDER_RUN_1" }, entries.Select(e => e.SlotName).ToArray());
        CollectionAssert.AreEqual(new[] { 1706, 1711, 1722 }, entries.Select(e => e.SlotIndex).ToArray());
        CollectionAssert.AreEqual(bytes, AnimationPackSerializer.ConvertToBytes(loaded));
    }

    [TestMethod]
    public void TableAndXml_RoundtripCurrentSlotsWithoutChangingBinary()
    {
        var source = RiderBin();
        var bytes = source.ToByteArray();
        var service = Mock.Of<IPackFileService>();
        var parser = new MetaDataFileParser(Mock.Of<IMetaDataDatabase>());
        var lookup = Mock.Of<ISkeletonAnimationLookUpHelper>();
        var editor = new AnimSetTableEditorViewModel(service, lookup, parser, null!, GameTypeEnum.Warhammer3);
        editor.LoadFromBinary(bytes, source.FileName);

        CollectionAssert.AreEqual(new[] { "RIDER_STAND", "RIDER_WALK_1", "RIDER_RUN_1" }, editor.Rows.Select(r => r.SlotName).ToArray());
        Assert.IsFalse(editor.IsDirty);
        var saved = editor.SaveToBinary(source.FileName, out var tableError);
        Assert.IsNull(tableError);
        CollectionAssert.AreEqual(bytes, saved!);

        var converter = new AnimationBinWh3FileToXmlConverter(lookup, parser, null!);
        var xml = converter.GetText(bytes);
        StringAssert.Contains(xml, "RIDER_STAND");
        var xmlSaved = converter.ToBytes(xml, source.FileName, service, out var xmlError);
        Assert.IsNull(xmlError);
        CollectionAssert.AreEqual(bytes, xmlSaved);
    }

    [TestMethod]
    public void EditingRiderSlot_SavesCurrentIdAndUndoRestoresOriginal()
    {
        var source = RiderBin();
        var editor = Table(source);

        editor.Rows[0].SlotName = "RIDER_ATTACK_1";

        Assert.AreEqual(1901, editor.Rows[0].SlotIndex);
        var saved = editor.SaveToBinary(source.FileName, out var error);
        Assert.IsNull(error);
        Assert.IsNotNull(saved);
        var reloaded = new AnimationBinWh3(source.FileName, saved);
        Assert.AreEqual(1901u, reloaded.AnimationTableEntries[0].AnimationId);
        Assert.AreEqual("RIDER_ATTACK_1", ((IAnimationBinGenericFormat)reloaded).Entries[0].SlotName);

        editor.UndoCommand.Execute(null);
        Assert.AreEqual(1706, editor.Rows[0].SlotIndex);
        Assert.AreEqual("RIDER_STAND", editor.Rows[0].SlotName);
    }

    [TestMethod]
    public void ProductionTable_RendersCurrentRiderSlotNames()
    {
        byte[]? renderedPng = null;
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var editor = Table(RiderBin());
            var view = new AnimSetTableEditorView { DataContext = editor };
            var window = new Window
            {
                Content = view, Width = 1280, Height = 720,
                Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var labels = Descendants<TextBlock>(view).Where(t => t.IsVisible).Select(t => t.Text).ToArray();
                foreach (var slot in new[] { "RIDER_STAND", "RIDER_WALK_1", "RIDER_RUN_1" })
                    CollectionAssert.Contains(labels, slot);

                var image = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(view);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = new MemoryStream();
                encoder.Save(stream);
                renderedPng = stream.ToArray();
            }
            finally { window.Close(); }
        });
        Assert.IsNotNull(renderedPng);
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "wh3-animation-slots");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "rider-slots.png");
        File.WriteAllBytes(path, renderedPng);
        TestContext.WriteLine(path);
        TestContext.AddResultFile(path);
    }

    private static AnimSetTableEditorViewModel Table(AnimationBinWh3 source)
    {
        var editor = new AnimSetTableEditorViewModel(Mock.Of<IPackFileService>(), Mock.Of<ISkeletonAnimationLookUpHelper>(),
            new MetaDataFileParser(Mock.Of<IMetaDataDatabase>()), null!, GameTypeEnum.Warhammer3);
        editor.LoadFromBinary(source.ToByteArray(), source.FileName);
        return editor;
    }

    private static AnimationBinWh3 RiderBin() => new("animations/database/battle/bin/rider.bin")
    {
        Name = "rider", SkeletonName = "humanoid01", LocomotionGraph = "graph.xml",
        AnimationTableEntries = new[] { (1706u, "stand"), (1711u, "walk"), (1722u, "run") }.Select(slot => new AnimationBinEntry
        {
            AnimationId = slot.Item1, BlendIn = 0.3f, SelectionWeight = 1,
            AnimationRefs = [new() { AnimationFile = $"animations/battle/rider_{slot.Item2}.anim" }],
        }).ToList(),
    };

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T value) yield return value;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
