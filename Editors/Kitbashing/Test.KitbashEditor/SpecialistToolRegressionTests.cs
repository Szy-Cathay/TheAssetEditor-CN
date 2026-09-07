using System.Reflection;
using System.Threading;
using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using Editors.KitbasherEditor.ChildEditors.PinTool;
using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using Editors.KitbasherEditor.ChildEditors.ReRiggingTool;
using Editors.KitbasherEditor.ChildEditors.VertexDebugger;
using GameWorld.Core.Animation;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Face;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.Ui.Editors.BoneMapping;

namespace Test.KitbashEditor
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public class SpecialistToolRegressionTests
    {
        const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [OneTimeSetUp]
        public void LoadLocalization()
        {
            var localization = new LocalizationManager();
            localization.LoadLanguage();
        }

        [TestCase("duplicate")]
        [TestCase("split")]
        [TestCase("faces")]
        [TestCase("combine")]
        [TestCase("reduce")]
        [TestCase("pose")]
        [TestCase("remap")]
        [TestCase("pin")]
        [TestCase("skinwrap")]
        public void Redo_AfterTransform_RestoresTheVisibleEditedMesh(string operation)
        {
            var selection = Selection();
            var executor = new CommandExecutor(Mock.Of<IEventHub>());
            var parent = new GroupNode();
            var mesh = parent.AddObject(GridMesh());
            Select(selection, mesh);
            var command = CreateCommand(operation, mesh, parent, selection);
            Assert.That(executor.ExecuteCommand(command), Is.True);
            var editedMesh = operation switch
            {
                "duplicate" or "faces" => parent.Children.OfType<Rmv2MeshNode>().Single(x => x != mesh),
                "split" => parent.Children.OfType<GroupNode>().Single().Children.OfType<Rmv2MeshNode>().First(),
                "combine" => parent.Children.OfType<Rmv2MeshNode>().Single(),
                _ => mesh
            };
            var editedGeometry = editedMesh.Geometry;
            Move(editedMesh, selection, executor);
            var expected = editedGeometry.VertexArray.ToArray();

            for (var repeat = 0; repeat < 2; repeat++)
            {
                Assert.That(executor.Undo(), Is.True);
                Assert.That(executor.Undo(), Is.True);
                Assert.That(executor.Redo(), Is.True);
                Assert.That(executor.Redo(), Is.True);
                Assert.That(editedMesh.Parent.Children, Does.Contain(editedMesh));
                Assert.That(editedMesh.Geometry, Is.SameAs(editedGeometry));
                Assert.That(editedMesh.Geometry.VertexArray, Is.EqualTo(expected));
                Assert.That(parent.Children.SelectMany(x => x is GroupNode ? x.Children : new List<GameWorld.Core.SceneNodes.ISceneNode> { x }), Does.Contain(editedMesh));
            }
        }

        [TestCase("pose")]
        [TestCase("remap")]
        public void Undo_RestoresGeometryUsedByEarlierTransformCommands(string operation)
        {
            var selection = Selection();
            var executor = new CommandExecutor(Mock.Of<IEventHub>());
            var mesh = Mesh();
            var original = mesh.Geometry;
            var vertices = original.VertexArray.ToArray();
            Move(mesh, selection, executor);
            Assert.That(executor.ExecuteCommand(CreateCommand(operation, mesh, new GroupNode(), selection)), Is.True);
            Assert.That(executor.Undo(), Is.True);
            Assert.That(executor.Undo(), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(mesh.Geometry, Is.SameAs(original));
                Assert.That(mesh.Geometry.VertexArray, Is.EqualTo(vertices));
                Assert.That(executor.CurrentDocumentStateId, Is.Zero);
            });
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SplitFaces_PreservesSelectedFacesAndRemainder(bool allFaces)
        {
            var parent = new GroupNode();
            var mesh = parent.AddObject(Mesh());
            var original = mesh.Geometry;
            var selection = Selection();
            selection.SetState(new FaceSelectionState { RenderObject = mesh, SelectedFaces = [0] });
            var command = new DuplicateFacesCommand(selection);
            command.Configure(mesh, allFaces ? [0, 3] : [0], true);
            command.Execute();
            Assert.That(parent.Children.OfType<Rmv2MeshNode>().Sum(x => x.Geometry.IndexArray.Length), Is.EqualTo(6));
            Assert.That(parent.Children.Contains(mesh), Is.EqualTo(!allFaces));
            command.Undo();
            Assert.That(parent.Children, Is.EqualTo(new[] { mesh }));
            Assert.That(mesh.Geometry, Is.SameAs(original));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SplitMesh_BridgeTriangleUnitesBothComponentsExactlyOnce(bool weld)
        {
            var mesh = Mesh([Vertex(0), Vertex(1), Vertex(1, 1), Vertex(2, 1), Vertex(2), Vertex(3), Vertex(1.5f, 2)], [0, 1, 2, 3, 4, 5, 2, 3, 6]);
            var output = MeshSplitterService.SplitMesh(mesh.Geometry, weld);
            Assert.That(output, Has.Count.EqualTo(1));
            Assert.That(output.Sum(x => x.IndexArray.Length), Is.EqualTo(9));
        }

        [Test]
        public void Combine_PreservesWorldPositionsAndSourceGeometry()
        {
            var parent = new GroupNode { ModelMatrix = Matrix.CreateRotationZ(0.5f) * Matrix.CreateTranslation(3, 4, 5) };
            var a = parent.AddObject(Mesh());
            var b = parent.AddObject(Mesh());
            a.Position = new Vector3(2, 0, 0);
            a.ModelMatrix = Matrix.CreateRotationY(0.3f) * Matrix.CreateTranslation(2, 0, 0);
            b.Position = new Vector3(10, 0, 0);
            b.Scale = new Vector3(2, 3, 1);
            var before = b.Geometry.VertexArray.ToArray();
            var expected = new[] { a, b }.SelectMany(x => x.Geometry.VertexArray.Select(v => Vector3.Transform(v.Position3(), x.GetRenderWorldMatrix()))).ToArray();
            var merged = ModelCombiner.CombineMeshes([a, b]).Single();
            var actual = merged.Geometry.VertexArray.Select(v => Vector3.Transform(v.Position3(), merged.GetRenderWorldMatrix())).ToArray();
            for (var i = 0; i < expected.Length; i++)
                Assert.That(Vector3.Distance(actual[i], expected[i]), Is.LessThan(0.0001f));
            Assert.That(b.Geometry.VertexArray, Is.EqualTo(before));
        }

        [Test]
        public void Combine_RejectsIndexOverflowBeforeChangingTheScene()
        {
            var parent = new GroupNode();
            var a = parent.AddObject(Mesh(Enumerable.Range(0, 40000).Select(x => Vertex(x)).ToArray(), [0, 1, 2]));
            var b = parent.AddObject(Mesh(Enumerable.Range(0, 40000).Select(x => Vertex(x)).ToArray(), [39997, 39998, 39999]));
            var original = a.Geometry.VertexArray;
            Assert.That(ModelCombiner.HasPotentialCombineMeshes([a, b], out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => ModelCombiner.CombineMeshes([a, b]));
            Assert.Throws<InvalidOperationException>(() => a.Geometry.Merge([b.Geometry]));
            Assert.That(a.Geometry.VertexArray, Is.SameAs(original));
            Assert.That(parent.Children, Is.EqualTo(new[] { a, b }));
        }

        [Test]
        public void MaximumSizeMesh_CanStillExtractItsLastTriangle()
        {
            var mesh = Mesh(Enumerable.Range(0, 65536).Select(x => Vertex(x)).ToArray(), [65533, 65534, 65535]);
            using var extracted = mesh.Geometry.CloneSubMesh([65533, 65534, 65535]);
            Assert.That(extracted.VertexCount(), Is.EqualTo(3));
            Assert.That(extracted.VertexArray[^1].Position.X, Is.EqualTo(65535));
            mesh.Geometry.RemoveUnusedVertexes([65533, 65534, 65535]);
            Assert.That(mesh.Geometry.VertexArray, Is.EqualTo(extracted.VertexArray));
        }

        [Test]
        public void CombineUndo_KeepsMeshesThatWereNotCombinable()
        {
            var parent = new GroupNode();
            var a = parent.AddObject(Mesh());
            var b = parent.AddObject(Mesh());
            var separate = parent.AddObject(Mesh(format: UiVertexFormat.Static));
            var selection = Selection();
            Select(selection, a, b, separate);
            var command = new CombineMeshCommand(selection);
            command.Configure([a, b, separate]);
            command.Execute();
            Assert.That(parent.Children, Has.Count.EqualTo(2));
            command.Undo();
            Assert.That(parent.Children, Is.EquivalentTo(new[] { a, b, separate }));
        }

        [Test]
        public void Combine_DoesNotBakeOffsetsIntoAnIncompatibleAnimationSpace()
        {
            var a = Mesh();
            var b = Mesh();
            a.AnimationPlayer = b.AnimationPlayer = new AnimationPlayer { IsEnabled = true };
            b.Position = Vector3.UnitX;
            Assert.That(ModelCombiner.HasPotentialCombineMeshes([a, b], out _), Is.False);
            Assert.That(ModelCombiner.CombineMeshes([a, b]), Is.EquivalentTo(new[] { a, b }));
        }

        [Test]
        public void Combine_RejectsZeroScaleOnAnyInput()
        {
            var a = Mesh();
            var b = Mesh();
            b.Scale = Vector3.Zero;
            var original = b.Geometry;
            Assert.Throws<InvalidOperationException>(() => ModelCombiner.CombineMeshes([a, b]));
            Assert.That(b.Geometry, Is.SameAs(original));
        }

        [Test]
        public void ReduceTenPercent_TargetsTriangleCount()
        {
            var mesh = GridMesh();
            var command = new ReduceMeshCommand(Selection());
            command.Configure([mesh], 0.9f);
            command.Execute();
            Assert.That(mesh.Geometry.IndexArray.Length / 3, Is.InRange(178, 180));
        }

        [Test]
        public void StaticConversion_MixedInputs_PreservesStaticVertices()
        {
            var parent = new GroupNode();
            var staticMesh = parent.AddObject(Mesh(format: UiVertexFormat.Static));
            var animatedMesh = parent.AddObject(Mesh());
            var frame = IdentityFrame();
            frame.BoneTransforms[0].WorldTransform = Matrix.CreateTranslation(0, 2, 0);
            var command = new CreateStaticMeshFromAnimationCommand();
            command.Configure(parent, [staticMesh, animatedMesh], frame);
            command.Execute();
            var output = parent.Children.OfType<GroupNode>().Single().Children.OfType<Rmv2MeshNode>().ToArray();
            Assert.That(output[0].Geometry.VertexArray.Select(x => x.Position), Is.EqualTo(staticMesh.Geometry.VertexArray.Select(x => x.Position)));
            Assert.That(output[1].Geometry.VertexArray[0].Position.Y, Is.EqualTo(2));
            Assert.That(animatedMesh.Geometry.VertexArray[0].Position.Y, Is.Zero);
            Assert.That(output.All(x => x.Geometry.VertexFormat == UiVertexFormat.Static), Is.True);
        }

        [Test]
        public void Pin_RejectsStaticSourceWithoutChangingTargets()
        {
            var target = Mesh();
            var original = target.Geometry;
            var command = new PinMeshToVertexCommand(Selection());
            command.Configure([target], Mesh(format: UiVertexFormat.Static), 0);
            Assert.Throws<InvalidOperationException>(command.Execute);
            Assert.That(target.Geometry, Is.SameAs(original));
        }

        [Test]
        public void SkinWrap_UsesWorldSpaceIncludingParentTransforms()
        {
            var parent = new GroupNode { ModelMatrix = Matrix.CreateTranslation(100, 0, 0) };
            var source = parent.AddObject(Mesh([Vertex(0, 0, 1), Vertex(1, 0, 1), Vertex(0, 1, 1), Vertex(100, 0, 2), Vertex(101, 0, 2), Vertex(100, 1, 2)], [0, 1, 2, 3, 4, 5]));
            var targetParent = new GroupNode { ModelMatrix = Matrix.CreateTranslation(50, 0, 0) };
            var target = targetParent.AddObject(Mesh([Vertex(50.2f, 0.2f)], [], UiVertexFormat.Static));
            var command = new SkinWrapRiggingCommand(Selection());
            command.Configure([target], source);
            command.Execute();
            Assert.That(target.Geometry.VertexArray[0].BlendIndices.X, Is.EqualTo(1));
        }

        [TestCase(UiVertexFormat.Weighted, 2)]
        [TestCase(UiVertexFormat.Cinematic, 3)]
        public void SkinWrap_NormalizesOnlyWeightsThatTheVertexFormatCanStore(UiVertexFormat format, int count)
        {
            var source = Mesh([Vertex(0, 0, 1), Vertex(1, 0, 2), Vertex(0, 1, 3)], [0, 1, 2], format);
            var target = Mesh([Vertex(1f / 3, 1f / 3)], [], UiVertexFormat.Static);
            var command = new SkinWrapRiggingCommand(Selection());
            command.Configure([target], source);
            command.Execute();
            var weights = target.Geometry.VertexArray[0].BlendWeights;
            var values = new[] { weights.X, weights.Y, weights.Z, weights.W };
            Assert.That(values.Count(x => x > 0), Is.EqualTo(count));
            Assert.That(values.Take(target.Geometry.WeightCount).Sum(), Is.EqualTo(1).Within(0.0001f));
        }

        [Test]
        public void MaterialAssignment_RefreshesCachedRenderMaterialOnExecuteUndoAndRedo()
        {
            var mesh = Mesh();
            var renderer = Renderer();
            mesh.Render(renderer, Matrix.Identity);
            var type = typeof(ReRiggingViewModel).Assembly.GetType("Editors.KitbasherEditor.Commands.AssignMaterialFromOtherMeshCommand")!;
            var command = (ICommand)Activator.CreateInstance(type, true)!;
            type.GetMethod("Configure")!.Invoke(command, [new TestMaterial("new"), new List<Rmv2MeshNode> { mesh }]);
            object? assignedMaterial = null;
            foreach (var action in new Action[] { command.Execute, command.Undo, ((IRedoableCommand)command).Redo })
            {
                action();
                mesh.Render(renderer, Matrix.Identity);
                var item = typeof(Rmv2MeshNode).GetField("_pooledRenderItem", Hidden)!.GetValue(mesh)!;
                Assert.That(item.GetType().GetField("_shader", Hidden)!.GetValue(item), Is.SameAs(mesh.Material));
                if (action == (Action)command.Execute)
                    assignedMaterial = mesh.Material;
            }
            Assert.That(mesh.Material, Is.SameAs(assignedMaterial));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void VertexDebugger_HighlightAndDirectionsFollowWorldPosition(bool animated)
        {
            var renderer = Renderer();
            var selection = Selection();
            var parent = new GroupNode { ModelMatrix = Matrix.CreateTranslation(5, 0, 0) };
            var mesh = parent.AddObject(Mesh());
            mesh.Position = new Vector3(10, 0, 0);
            if (animated)
            {
                var player = new AnimationPlayer();
                var skeleton = new GameSkeleton(SkeletonFile(), player);
                var clip = new AnimationClip();
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame
                {
                    Position = [new Vector3(2, 0, 0)],
                    Rotation = [Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathHelper.PiOver2)],
                    Scale = [Vector3.One]
                });
                player.SetAnimation(clip, skeleton);
                player.IsEnabled = true;
                player.Refresh();
                mesh.AnimationPlayer = player;
            }
            var state = new VertexSelectionState(mesh, 0);
            state.ModifySelection([0], false);
            selection.SetState(state);
            using var vm = new VertexDebuggerViewModel(renderer, selection, Mock.Of<IEventHub>());
            vm.Initialize();
            vm.SelectedVertex = vm.VertexList[0];
            vm.Draw(new GameTime());
            var lines = (List<VertexPositionColor>)typeof(RenderEngineComponent).GetField("_renderLines", Hidden)!.GetValue(renderer)!;
            var expectedX = animated ? 17 : 15;
            Assert.That(lines.Take(24).Average(x => x.Position.X), Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(lines[24].Position.X, Is.EqualTo(expectedX).Within(0.0001f));
            if (animated)
                Assert.That(lines[29].Position.Y, Is.EqualTo(vm.DebugScale.Value).Within(0.0001f));
        }

        [Test]
        public void MeshFitter_ApplyThenOk_CommitsOnlyOnce()
        {
            var file = SkeletonFile();
            var player = new AnimationPlayer();
            var skeleton = new GameSkeleton(file, player);
            var scene = new SceneManager(Renderer(), null!, Mock.Of<IEventHub>());
            scene.RootNode.AddObject(new MainEditableNode(SpecialNodes.EditableModel, new SkeletonNode(skeleton), null!));
            var mesh = Mesh();
            mesh.AnimationPlayer = player;
            var original = mesh.Geometry;
            var selection = Selection();
            Select(selection, mesh);
            var executor = new CommandExecutor(Mock.Of<IEventHub>());
            var provider = new Mock<IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(CreateAnimatedMeshPoseCommand))).Returns(() => new CreateAnimatedMeshPoseCommand());
            using var vm = new MeshFitterViewModel(new CommandFactory(provider.Object, executor), new AnimationsContainerComponent(), scene);
            vm.Initialize(new RemappedAnimatedBoneConfiguration
            {
                MeshSkeletonName = "source_skeleton", ParnetModelSkeletonName = "target_skeleton",
                MeshBones = AnimatedBoneHelper.CreateFromSkeleton(file), ParentModelBones = AnimatedBoneHelper.CreateFromSkeleton(file)
            }, [mesh], skeleton, file);
            vm.ScaleFactor.Value = 2;
            vm.OnApplyButton();
            Assert.That(vm.OnOkButton(), Is.True);
            vm.Dispose();
            Assert.That(mesh.Geometry.VertexArray[1].Position.X, Is.EqualTo(2));
            Assert.That(mesh.AnimationPlayer, Is.SameAs(player));
            Assert.That(executor.Undo(), Is.True);
            Assert.That(mesh.Geometry, Is.SameAs(original));
            Assert.That(executor.CurrentDocumentStateId, Is.Zero);
        }

        static ICommand CreateCommand(string operation, Rmv2MeshNode mesh, GroupNode parent, SelectionManager selection)
        {
            switch (operation)
            {
                case "duplicate":
                    var duplicate = new DuplicateObjectCommand(selection);
                    duplicate.Configure([mesh]);
                    return duplicate;
                case "split":
                    var split = new DivideObjectIntoSubmeshesCommand(selection);
                    split.Configure(mesh, false);
                    return split;
                case "faces":
                    var faces = new DuplicateFacesCommand(selection);
                    faces.Configure(mesh, [0], true);
                    return faces;
                case "combine":
                    var combine = new CombineMeshCommand(selection);
                    combine.Configure([mesh, parent.AddObject(Mesh())]);
                    return combine;
                case "reduce":
                    var reduce = new ReduceMeshCommand(selection);
                    reduce.Configure([mesh], 0.9f);
                    return reduce;
                case "pose":
                    var pose = new CreateAnimatedMeshPoseCommand();
                    pose.Configure([mesh], IdentityFrame());
                    return pose;
                case "remap":
                    var remap = new RemapBoneIndexesCommand();
                    remap.Configure([mesh], [new IndexRemapping(0, 1, true)], "target_skeleton");
                    return remap;
                case "pin":
                    var pin = new PinMeshToVertexCommand(selection);
                    pin.Configure([mesh], Mesh(), 0);
                    return pin;
                case "skinwrap":
                    var skinwrap = new SkinWrapRiggingCommand(selection);
                    skinwrap.Configure([mesh], Mesh());
                    return skinwrap;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        static SelectionManager Selection() => new(Mock.Of<IEventHub>());
        static void Select(SelectionManager selection, params Rmv2MeshNode[] meshes)
        {
            var state = new ObjectSelectionState();
            state.ModifySelection(meshes, false);
            selection.SetState(state);
        }

        static void Move(Rmv2MeshNode mesh, SelectionManager selection, CommandExecutor executor)
        {
            Select(selection, mesh);
            var provider = new Mock<IServiceProvider>();
            provider.Setup(x => x.GetService(typeof(TransformVertexCommand))).Returns(() => new TransformVertexCommand(selection));
            using var wrapper = new TransformGizmoWrapper(new CommandFactory(provider.Object, executor), [mesh.Geometry], selection.GetState());
            wrapper.BeginTransform();
            wrapper.GizmoTranslateEvent(Vector3.UnitX, PivotType.WorldOrigin);
            wrapper.CommitTransform(executor);
        }

        static VertexPositionNormalTextureCustom Vertex(float x, float y = 0, int bone = 0) => new()
        {
            Position = new Vector4(x, y, 0, 1), Normal = Vector3.UnitZ, Tangent = Vector3.UnitX, BiNormal = Vector3.UnitY,
            BlendIndices = new Vector4(bone, 0, 0, 0), BlendWeights = new Vector4(1, 0, 0, 0)
        };

        static Rmv2MeshNode Mesh(VertexPositionNormalTextureCustom[]? vertices = null, ushort[]? indices = null, UiVertexFormat format = UiVertexFormat.Cinematic)
        {
            var graphics = new Mock<IGraphicsCardGeometry>();
            graphics.Setup(x => x.Clone()).Returns(graphics.Object);
            var geometry = new MeshObject(graphics.Object, "source_skeleton")
            {
                VertexArray = vertices ?? [Vertex(0), Vertex(1), Vertex(0, 1), Vertex(1, 1)],
                IndexArray = indices ?? [0, 1, 2, 1, 3, 2]
            };
            geometry.ChangeVertexType(format, false);
            geometry.BuildBoundingBox();
            var material = new Mock<IRmvMaterial>();
            material.SetupGet(x => x.ModelName).Returns("Mesh");
            material.Setup(x => x.Clone()).Returns(material.Object);
            return new Rmv2MeshNode(geometry, material.Object, new TestMaterial("original"), null!);
        }

        static Rmv2MeshNode GridMesh()
        {
            var vertices = new List<VertexPositionNormalTextureCustom>();
            var indices = new List<ushort>();
            for (var y = 0; y <= 10; y++)
                for (var x = 0; x <= 10; x++)
                    vertices.Add(Vertex(x, y));
            for (var y = 0; y < 10; y++)
                for (var x = 0; x < 10; x++)
                {
                    var a = (ushort)(y * 11 + x);
                    indices.AddRange([a, (ushort)(a + 1), (ushort)(a + 11), (ushort)(a + 1), (ushort)(a + 12), (ushort)(a + 11)]);
                }
            return Mesh(vertices.ToArray(), indices.ToArray());
        }

        static AnimationFile SkeletonFile() => new()
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "source_skeleton" },
            Bones = [new AnimationFile.BoneInfo { Id = 0, Name = "root", ParentId = -1 }],
            AnimationParts = [new AnimationFile.AnimationPart { DynamicFrames = [new AnimationFile.Frame { Transforms = [new RmvVector3(0, 0, 0)], Quaternion = [new RmvVector4(0, 0, 0, 1)] }] }]
        };

        static AnimationFrame IdentityFrame() => new() { BoneTransforms = [new AnimationFrame.BoneKeyFrame { WorldTransform = Matrix.Identity, Rotation = Quaternion.Identity, Scale = Vector3.One }] };
        static RenderEngineComponent Renderer() => new(null!, null!, null!, null!, new ApplicationSettingsService(), new SceneRenderParametersStore(), Mock.Of<IEventHub>(), new GridComponent(null!, null!, null!));

        sealed class TestMaterial(string label) : CapabilityMaterial((CapabilityMaterialsEnum)0, (ShaderTypes)0, null!)
        {
            protected override CapabilityMaterial CreateCloneInstance() => new TestMaterial(label);
        }
    }
}
