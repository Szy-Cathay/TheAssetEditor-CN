using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.GameFormats.RigidModel;
using System.IO;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.LoadAndSave
{
    [TestFixture]
    internal class PrimitiveSaveRoundTripTests : LoadAndSaveBase
    {
        [Test]
        public void CreateBox_SaveAndReload_PreservesGeometryAndMaterial()
        {
            var (runner, editor) = CreateKitbashTool(TestFiles.RomePack_MeshDecal);
            var commandFactory =
                runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
            commandFactory.Create<ConstructBoxUiCommand>().Execute();
            var createdMesh = GetMainNode(editor)
                .GetLodNodes()
                .SelectMany(lod => lod.GetAllModels(false))
                .Single(mesh => mesh.Name == "primitive_box");
            var expectedVertexFormat = createdMesh.Geometry.VertexFormat switch
            {
                UiVertexFormat.Static => VertexFormat.Static,
                UiVertexFormat.Weighted => VertexFormat.Weighted,
                UiVertexFormat.Cinematic => VertexFormat.Cinematic,
                _ => throw new InvalidOperationException()
            };
            var settings =
                runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            settings.IsUserInitialized = true;

            var saveResult = commandFactory
                .Create<SaveCommand>()
                .ExecuteWithResult();

            Assert.That(saveResult, Is.Not.Null);
            Assert.That(saveResult!.Status, Is.True);
            var outputPack = runner.PackFileService.GetEditablePack();
            var savedFile = runner.PackFileService.FindFile(
                settings.OutputName,
                outputPack);
            Assert.That(savedFile, Is.Not.Null);

            var reloaded = ModelFactory
                .Create()
                .Load(savedFile!.DataSource.ReadData());
            var primitive = reloaded.ModelList[0]
                .Single(model => model.Material.ModelName == "primitive_box");

            Assert.Multiple(() =>
            {
                Assert.That(primitive.Mesh.VertexList, Has.Length.EqualTo(726));
                Assert.That(primitive.Mesh.IndexList, Has.Length.EqualTo(3600));
                Assert.That(primitive.Material.BinaryVertexFormat, Is.EqualTo(expectedVertexFormat));
                Assert.That(primitive.Material.GetAllTextures(), Is.Empty);
                Assert.That(primitive.CommonHeader.BoundingBox.Width, Is.EqualTo(1f).Within(0.001f));
                Assert.That(primitive.CommonHeader.BoundingBox.Height, Is.EqualTo(1f).Within(0.001f));
                Assert.That(primitive.CommonHeader.BoundingBox.Depth, Is.EqualTo(1f).Within(0.001f));
            });
        }

        [Test]
        public void CreateBox_SaveAndReloadWarhammer3_PreservesSkinningAndWsModel()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPack = runner.LoadPackFile(TestFiles.KarlPackFile, true);
            var originalModel = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl);
            Assert.That(originalModel, Is.Not.Null);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute(originalModel!) as KitbasherViewModel;
            Assert.That(editor, Is.Not.Null);

            var commandFactory =
                runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
            commandFactory.Create<ConstructBoxUiCommand>().Execute();
            var createdMesh = GetMainNode(editor!)
                .GetLodNodes()
                .SelectMany(lod => lod.GetAllModels(false))
                .Single(mesh => mesh.Name == "primitive_box");
            var expectedVertexFormat = createdMesh.Geometry.VertexFormat switch
            {
                UiVertexFormat.Static => VertexFormat.Static,
                UiVertexFormat.Weighted => VertexFormat.Weighted,
                UiVertexFormat.Cinematic => VertexFormat.Cinematic,
                _ => throw new InvalidOperationException()
            };
            var expectedSkeletonName = createdMesh.Geometry.SkeletonName;
            var settings =
                runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            settings.IsUserInitialized = true;

            var saveResult = commandFactory
                .Create<SaveCommand>()
                .ExecuteWithResult();

            Assert.That(saveResult, Is.Not.Null);
            Assert.That(saveResult!.Status, Is.True);
            var savedModel = runner.PackFileService.FindFile(settings.OutputName, outputPack);
            var savedWsModel = runner.PackFileService.FindFile(
                Path.ChangeExtension(settings.OutputName, ".wsmodel"),
                outputPack);
            Assert.That(savedModel, Is.Not.Null);
            Assert.That(savedWsModel, Is.Not.Null);

            var reloaded = ModelFactory
                .Create()
                .Load(savedModel!.DataSource.ReadData());
            var primitive = reloaded.ModelList[0]
                .Single(model => model.Material.ModelName == "primitive_box");

            Assert.Multiple(() =>
            {
                Assert.That(reloaded.Header.SkeletonName, Is.EqualTo(expectedSkeletonName));
                Assert.That(primitive.Mesh.VertexList, Has.Length.EqualTo(726));
                Assert.That(primitive.Mesh.IndexList, Has.Length.EqualTo(3600));
                Assert.That(primitive.Material.BinaryVertexFormat, Is.EqualTo(expectedVertexFormat));
                Assert.That(primitive.Material.GetAllTextures(), Is.Empty);
                Assert.That(primitive.Mesh.VertexList.All(x => x.BoneIndex[0] == 0), Is.True);
                Assert.That(primitive.Mesh.VertexList.All(x => x.BoneWeight[0] == 1f), Is.True);
                Assert.That(
                    outputPack!.FileList.Keys.Any(x =>
                        x.Contains("\\materials\\", StringComparison.OrdinalIgnoreCase) &&
                        x.Contains("primitive_box", StringComparison.OrdinalIgnoreCase)),
                    Is.True);
            });
        }
    }
}
