using Editors.KitbasherEditor.Commands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;

namespace Test.KitbashEditor.Commands
{
    [TestFixture]
    internal class ConstructPrimitiveCommandTests
    {
        [TestCase(PrimitiveType.Box, "primitive_box", 726, 3600)]
        [TestCase(PrimitiveType.Plane, "primitive_plane", 121, 600)]
        [TestCase(PrimitiveType.Sphere, "primitive_sphere", 231, 1080)]
        public void Execute_EmptyScene_CreatesSelectedPrimitiveAndLod0(
            PrimitiveType primitiveType,
            string expectedName,
            int expectedVertexCount,
            int expectedIndexCount)
        {
            var context = CreateContext();
            var command = context.CreateCommand(primitiveType);

            var executed = context.CommandExecutor.ExecuteCommand(command);

            var lod = context.MainNode.GetLodNodes().Single();
            var mesh = lod.GetAllModels(false).Single();
            Assert.Multiple(() =>
            {
                Assert.That(executed, Is.True);
                Assert.That(lod.LodValue, Is.Zero);
                Assert.That(mesh.Name, Is.EqualTo(expectedName));
                Assert.That(mesh.Geometry.VertexArray, Has.Length.EqualTo(expectedVertexCount));
                Assert.That(mesh.Geometry.IndexArray, Has.Length.EqualTo(expectedIndexCount));
                Assert.That(mesh.Geometry.VertexFormat, Is.EqualTo(UiVertexFormat.Static));
                Assert.That(mesh.Geometry.SkeletonName, Is.Empty);
                Assert.That(mesh.RmvMaterial.MaterialId, Is.EqualTo(ModelMaterialEnum.default_type));
                Assert.That(mesh.RmvMaterial.GetAllTextures(), Is.Empty);
                Assert.That(mesh.Material.Type, Is.EqualTo(CapabilityMaterialsEnum.MetalRoughPbr_Default));
                Assert.That(
                    context.SelectionManager
                        .GetState<ObjectSelectionState>()
                        .GetSingleSelectedObject(),
                    Is.SameAs(mesh));
                Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.GreaterThan(0));
            });
        }

        [Test]
        public void UndoAndRedo_ReuseCreatedLodAndMeshAndRestoreSelection()
        {
            var context = CreateContext();
            var command = context.CreateCommand(PrimitiveType.Box);
            context.CommandExecutor.ExecuteCommand(command);
            var createdLod = context.MainNode.GetLodNodes().Single();
            var createdMesh = createdLod.GetAllModels(false).Single();
            var dirtyState = context.CommandExecutor.CurrentDocumentStateId;

            var undone = context.CommandExecutor.Undo();

            Assert.Multiple(() =>
            {
                Assert.That(undone, Is.True);
                Assert.That(context.MainNode.GetLodNodes(), Is.Empty);
                Assert.That(
                    context.SelectionManager
                        .GetState<ObjectSelectionState>()
                        .CurrentSelection(),
                    Is.Empty);
                Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.Zero);
            });

            var redone = context.CommandExecutor.Redo();

            Assert.Multiple(() =>
            {
                Assert.That(redone, Is.True);
                Assert.That(context.MainNode.GetLodNodes().Single(), Is.SameAs(createdLod));
                Assert.That(
                    context.MainNode.GetLodNodes().Single().GetAllModels(false).Single(),
                    Is.SameAs(createdMesh));
                Assert.That(
                    context.SelectionManager
                        .GetState<ObjectSelectionState>()
                        .GetSingleSelectedObject(),
                    Is.SameAs(createdMesh));
                Assert.That(context.CommandExecutor.CurrentDocumentStateId, Is.EqualTo(dirtyState));
            });
        }

        [Test]
        public void Execute_WithTemplateMesh_InheritsGeometryContractButCreatesPlainMaterial()
        {
            var context = CreateContext();
            var lod = context.MainNode.AddObject(new Rmv2LodNode("Lod 0", 0));
            var templateMaterial = MaterialFactory
                .Create()
                .CreateMaterial(ModelMaterialEnum.weighted_skin_decal_dirtmap);
            templateMaterial.ModelName = "template";
            templateMaterial.UpdateInternalState(UiVertexFormat.Cinematic);
            templateMaterial.SetTexture(TextureType.Diffuse, "textures\\template.dds");
            var templateMaterialId = templateMaterial.MaterialId;
            var templateGeometry = context.PrimitiveConstructor.CreatePlane(
                UiVertexFormat.Cinematic,
                "template_skeleton",
                resolution: 1);
            var templateNode = lod.AddObject(
                new Rmv2MeshNode(
                    templateGeometry,
                    templateMaterial,
                    context.MaterialFactory.Create(templateMaterial),
                    null!));
            var command = context.CreateCommand(PrimitiveType.Plane);

            var executed = context.CommandExecutor.ExecuteCommand(command);

            var createdMesh = lod.GetAllModels(false).Single(x => !ReferenceEquals(x, templateNode));
            Assert.Multiple(() =>
            {
                Assert.That(executed, Is.True);
                Assert.That(createdMesh.Geometry.VertexFormat, Is.EqualTo(UiVertexFormat.Cinematic));
                Assert.That(createdMesh.Geometry.SkeletonName, Is.EqualTo("template_skeleton"));
                Assert.That(createdMesh.RmvMaterial.MaterialId, Is.EqualTo(ModelMaterialEnum.weighted));
                Assert.That(createdMesh.RmvMaterial.GetAllTextures(), Is.Empty);
                Assert.That(templateMaterial.MaterialId, Is.EqualTo(templateMaterialId));
                Assert.That(
                    templateMaterial.GetTexture(TextureType.Diffuse)?.Path,
                    Is.EqualTo("textures\\template.dds"));
            });
        }

        private static TestContext CreateContext()
        {
            var eventHub = Mock.Of<IEventHub>();
            var sceneManager = new SceneManager(null!, null!, eventHub);
            var selectionManager = new SelectionManager(eventHub, null!, null!, null!);
            selectionManager.CreateSelectionSate(GeometrySelectionMode.Object, null!, false);
            var mainNode = sceneManager.RootNode.AddObject(
                new MainEditableNode(
                    SpecialNodes.EditableModel,
                    null!,
                    Mock.Of<IPackFileService>()));
            var geometryFactory = new TestGeometryGraphicsContextFactory();
            var primitiveConstructor = new PrimitiveConstructor(geometryFactory);
            var materialFactory = new CapabilityMaterialFactory(
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                null!);
            var commandExecutor = new CommandExecutor(eventHub);

            return new TestContext(
                sceneManager,
                selectionManager,
                mainNode,
                primitiveConstructor,
                materialFactory,
                commandExecutor);
        }

        private sealed record TestContext(
            SceneManager SceneManager,
            SelectionManager SelectionManager,
            MainEditableNode MainNode,
            PrimitiveConstructor PrimitiveConstructor,
            CapabilityMaterialFactory MaterialFactory,
            CommandExecutor CommandExecutor)
        {
            public ConstructPrimitiveCommand CreateCommand(PrimitiveType primitiveType)
            {
                var command = new ConstructPrimitiveCommand(
                    SceneManager,
                    SelectionManager,
                    MaterialFactory,
                    PrimitiveConstructor);
                command.Configure(primitiveType);
                return command;
            }
        }

        private sealed class TestGeometryGraphicsContextFactory : IGeometryGraphicsContextFactory
        {
            public IGraphicsCardGeometry Create()
            {
                return Mock.Of<IGraphicsCardGeometry>();
            }
        }
    }
}
