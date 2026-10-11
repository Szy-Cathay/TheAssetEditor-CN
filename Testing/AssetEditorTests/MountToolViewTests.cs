using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AnimationEditor.MountAnimationCreator;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.AnimationPlayer;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class MountToolViewTests
{
    [ClassInitialize]
    public static void Initialize(TestContext _) => new LocalizationManager().LoadLanguage();

    [TestMethod]
    public void ModelReplacement_InvalidatesAttachmentAndGeneratedPreview()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var (vm, pfs, selection, editor) = CreateEditor();
            var skeleton = MountToolTests.Skeleton("test", ("animroot", -1), ("root", 0));
            var skeletonFile = PackFile.CreateFromBytes("test.anim", AnimationFile.ConvertToBytes(MountToolTests.Clip(skeleton, 1, 1).ConvertToFileFormat(skeleton)));
            pfs.AddContainer(new PackFileContainer("source")
            {
                IsCaPackFile = true,
                FileList = new()
                {
                    [@"animations\skeletons\test.anim"] = skeletonFile,
                    ["mount.anim"] = PackFile.CreateFromBytes("mount.anim", AnimationFile.ConvertToBytes(MountToolTests.Clip(skeleton, 2, 1).ConvertToFileFormat(skeleton)))
                }
            });
            var rider = vm.GetSceneObjectFromId("rider")!.Data;
            var mount = vm.GetSceneObjectFromId("mount")!.Data;
            editor.SetSkeleton(rider, skeletonFile);
            editor.SetSkeleton(mount, skeletonFile);
            var geometry = new MeshObject(Mock.Of<IGraphicsCardGeometry>(), "test")
            {
                VertexArray = [new VertexPositionNormalTextureCustom { Position = new(0, 2, 0, 1), BlendWeights = new(1, 0, 0, 0) }],
                IndexArray = []
            };
            geometry.ChangeVertexType(UiVertexFormat.Weighted, false);
            var mesh = new Rmv2MeshNode(geometry, new WeightedMaterial(), null!, mount.Player);
            mount.ModelNode = mesh;
            mount.TriggerMeshChanged();
            ((VertexSelectionState)selection.CreateSelectionSate(GeometrySelectionMode.Vertex, mesh)).SetSelection([0]);
            vm.SetMountVertex();
            editor.SetAnimationClip(rider, MountToolTests.Clip(skeleton, 2, 1), "rider.anim");
            editor.SetAnimationClip(mount, MountToolTests.Clip(skeleton, 2, 1), "mount.anim");
            Assert.IsTrue(vm.CanPreview.Value);
            Assert.IsFalse(vm.CanSave.Value);
            var preview = vm.Player.PlayerItems.Single(x => x.Asset.Id == "mount-preview").Asset;
            Assert.IsNotNull(preview.AnimationClip);
            Assert.IsNotNull(preview.Skeleton);
            Assert.AreEqual(2, preview.AnimationClip.DynamicFrames.Count);

            var projectRoot = Path.Combine(Path.GetTempPath(), "AssetEditorMount-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(projectRoot);
            LibGit2Sharp.Repository.Init(projectRoot);
            try
            {
                using var project = FolderProjectContainer.Create(projectRoot, new FolderProjectSettings { Name = "Mount test" });
                pfs.AddEditableFolderProject(project);
                vm.SaveCurrentAnimationAction();
                Assert.IsTrue(File.Exists(Path.Combine(projectRoot, "new_rider.anim")));
                var carrier = new AnimationBinWh3("person.bin") { Name = "person", SkeletonName = "test" };
                carrier.AnimationTableEntries.Add(MountToolTests.Entry("WALK_1", "mount.anim"));
                vm.MountLinkController.AnimationSetForMount.UpdatePossibleValues([carrier]);
                vm.MountLinkController.AnimationSetForMount.SelectedItem = carrier;
                Assert.IsNull(vm.MountLinkController.AnimationSetForRider.SelectedItem);
                Assert.IsFalse(vm.CanBatchProcess.Value);
                var table = new AnimationBinWh3("animations/database/battle/bin/test.bin") { Name = "test", SkeletonName = "test" };
                table.AnimationTableEntries.Add(MountToolTests.Entry("RIDER_WALK_1", "original.anim", "original.anim"));
                table.AnimationTableEntries[0].AnimationRefs[1].AnimationMetaFile = "meta_0";
                table.AnimationTableEntries[0].AnimationRefs[1].AnimationSoundMetaFile = "sound_0";
                vm.MountLinkController.AnimationSetForRider.UpdatePossibleValues([table]);
                vm.MountLinkController.AnimationSetForRider.SelectedItem = table;
                Assert.IsTrue(vm.CanBatchProcess.Value);
                var database = new AnimationPackFileDatabase("test.animpack");
                database.AddFile(table);
                pfs.ApplyFileWrites(project, [new PackFileWrite("test.animpack", AnimationPackSerializer.ConvertToBytes(database))]);
                vm.RefreshViewAction();
                editor.SetAnimationClip(rider, MountToolTests.Clip(skeleton, 2, 1), "rider.anim");
                editor.SetAnimationClip(mount, MountToolTests.Clip(skeleton, 2, 1), "mount.anim");
                vm.ActiveOutputFragment.SelectedItem = vm.ActiveOutputFragment.PossibleValues.Single();
                vm.ActiveFragmentSlot.SelectedItem = vm.ActiveFragmentSlot.PossibleValues[1];
                Assert.IsTrue(vm.CanAddToFragment.Value);
                vm.AddAnimationToFragment();
                var saved = AnimationPackSerializer.Load(pfs.FindFile("test.animpack", project)!, pfs).Files.OfType<AnimationBinWh3>().Single();
                var refs = saved.AnimationTableEntries[0].AnimationRefs;
                Assert.AreEqual("original.anim", refs[0].AnimationFile);
                Assert.AreEqual("new_rider_1.anim", refs[1].AnimationFile);
                Assert.AreEqual("meta_0", refs[1].AnimationMetaFile);
                Assert.IsTrue(File.Exists(Path.Combine(projectRoot, refs[1].AnimationFile)));
            }
            finally { Directory.Delete(projectRoot, true); }

            mount.ModelNode = null!;
            mount.TriggerMeshChanged();
            Assert.IsFalse(vm.CanPreview.Value);
            Assert.IsFalse(vm.CanBatchProcess.Value);
            Assert.IsNull(preview.AnimationClip);
            Assert.IsFalse(preview.ShowMesh.Value);
            vm.Close();
        });
    }

    [DataTestMethod]
    [DataRow("DarkTheme")]
    [DataRow("LightTheme")]
    [DataRow("HighContrastDark")]
    [DataRow("HighContrastLight")]
    public void EditorView_RendersChineseControlsAndTableVariants(string theme)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var original = ThemesController.CurrentTheme;
            ThemesController.SetTheme(Enum.Parse<ThemeType>(theme));
            try
            {
                var (vm, _, _, _) = CreateEditor();
                Assert.AreEqual(2, vm.SceneObjects.Count);
                CollectionAssert.AreEqual(new[] { "骑手", "坐骑" }, vm.SceneObjects.Select(x => x.HeaderName).ToArray());
                Assert.AreEqual(3, vm.Player.PlayerItems.Count);
                var table = new AnimationBinWh3("animations/database/battle/bin/test.bin") { Name = "test_rider", SkeletonName = "test" };
                table.AnimationTableEntries.Add(MountToolTests.Entry("RIDER_WALK_1", "one.anim", "two.anim"));
                vm.ActiveOutputFragment.UpdatePossibleValues([table]);
                vm.ActiveOutputFragment.SelectedItem = table;
                vm.ActiveFragmentSlot.SelectedItem = vm.ActiveFragmentSlot.PossibleValues[1];
                var view = new AnimationEditor.MountAnimationCreator.EditorView { DataContext = vm };
                view.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Shared.Ui;component/Common/Styles/EditorWorkspaceStyles.xaml")
                });
                view.SetResourceReference(Control.ForegroundProperty, "AeBrush.TextPrimary");
                view.SetResourceReference(Control.FontFamilyProperty, "AppFontFamily");
                var root = new Border { Child = view, Background = (Brush)Application.Current.FindResource("AeBrush.Canvas") };
                root.Measure(new System.Windows.Size(650, 1100));
                root.Arrange(new Rect(0, 0, 650, 1100));
                root.UpdateLayout();
                var combos = Descendants<ComboBox>(root).ToArray();
                var tableCombo = combos.Single(x => ReferenceEquals(x.ItemsSource, vm.ActiveOutputFragment.Values));
                var slotCombo = combos.Single(x => ReferenceEquals(x.ItemsSource, vm.ActiveFragmentSlot.Values));
                tableCombo.SelectedItem = null;
                tableCombo.SelectedItem = table;
                slotCombo.SelectedItem = vm.ActiveFragmentSlot.PossibleValues[1];
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                root.UpdateLayout();
                Assert.AreEqual("test_rider", tableCombo.Text);
                Assert.AreEqual("[2] RIDER_WALK_1", slotCombo.Text);
                Assert.IsTrue(Descendants<TextBlock>(root).Any(x => x.Text.Contains("请先打开或新建 Mod 工程")));
                Assert.IsTrue(Descendants<TextBlock>(root).Any(x => x.Text.Contains("攻击、死亡必须使用真实动作") && x.Text.Contains("_mount")));
                var bitmap = new RenderTargetBitmap(650, 1100, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(root);
                var output = Environment.GetEnvironmentVariable("ASSETEDITOR_MOUNT_RENDER_OUTPUT");
                if (!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var stream = File.Create(Path.Combine(output, theme + ".png"));
                    encoder.Save(stream);
                }
                vm.Close();
            }
            finally { ThemesController.SetTheme(original); }
        });
    }

    private static (MountAnimationCreatorViewModel, PackFileService, SelectionManager, SceneObjectEditor) CreateEditor()
    {
        var pfs = new PackFileService(null) { EnforceGameFilesMustBeLoaded = false };
        var game = new Mock<IWpfGame>();
        game.Setup(x => x.AddComponent(It.IsAny<SceneObject>())).Returns((SceneObject value) => value);
        var hub = Mock.Of<IEventHub>();
        var dialogs = Mock.Of<IStandardDialogs>();
        var lookup = Mock.Of<ISkeletonAnimationLookUpHelper>();
        var scene = new SceneManager(null!, null!, hub);
        var editor = new SceneObjectEditor(game.Object, scene, pfs, new AnimationsContainerComponent(), null!, hub);
        var player = new AnimationPlayerViewModel();
        var uiCommands = Mock.Of<IUiCommandFactory>();
        var builder = new SceneObjectViewModelBuilder(player, editor, pfs, lookup, uiCommands, dialogs, new ModelSkeletonResolver(pfs));
        var parameters = new Mock<IEditorHostParameters>();
        parameters.SetupGet(x => x.GameWorld).Returns(game.Object);
        parameters.SetupGet(x => x.AnimationPlayerViewModel).Returns(player);
        parameters.SetupGet(x => x.SceneObjectEditor).Returns(editor);
        parameters.SetupGet(x => x.SceneObjectViewModelBuilder).Returns(builder);
        parameters.SetupGet(x => x.ComponentInserter).Returns(Mock.Of<IComponentInserter>());
        var selection = new SelectionManager(hub);
        var vm = new MountAnimationCreatorViewModel(parameters.Object, pfs, lookup, selection,
            new FileSaveService(pfs, dialogs), uiCommands, null!, null!, null!, dialogs, Mock.Of<IGlobalEventHub>());
        return (vm, pfs, selection, editor);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item)
            yield return item;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
}
