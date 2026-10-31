using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.GameFormats.RigidModel.MaterialHeaders;

namespace Testing.GameWorld.Core.Components.Gizmo;

public partial class TransformGestureTests
{
    [TestCase(TransformSpace.World)]
    [TestCase(TransformSpace.Local)]
    public void MeshAxes_UseActualRotatedParentFrameAndUndoRestoresGeometry(TransformSpace space)
    {
        using var context = CreateBlenderInputContext();
        SetBlenderTestView(context);
        var material = new Mock<IRmvMaterial>();
        material.SetupGet(value => value.ModelName).Returns("LocalMesh");
        material.SetupGet(value => value.PivotPoint).Returns(Vector3.Zero);
        var node = new Rmv2MeshNode(context.Mesh, material.Object, null!, null!)
        {
            ModelMatrix = Matrix.CreateScale(2, 3, 4) * Matrix.CreateRotationZ(0.4f),
        };
        var parent = new GroupNode
        {
            ModelMatrix = Matrix.CreateRotationY(0.7f) * Matrix.CreateTranslation(4, 5, 6),
        };
        parent.AddObject(node);
        var selection = new ObjectSelectionState();
        selection.ModifySelectionSingleObject(node, onlyRemove: false);
        context.SelectionManager.SetState(selection);
        var initial = context.Mesh.VertexArray.ToArray();
        var world = node.GetRenderWorldMatrix();
        Assert.That(world.Decompose(out _, out var rotation, out _), Is.True);
        var expectedBasis = Matrix.CreateFromQuaternion(rotation);
        Assert.That(Vector3.Distance(Vector3.Transform(Vector3.UnitX,
            context.Component.Gizmo.Selection.Single().Orientation), expectedBasis.Right), Is.LessThan(0.0001f));
        Assert.That(context.Component.TrySetTransformSpace(space), Is.True);
        var delta = space == TransformSpace.Local ? expectedBasis.Right * 2 : Vector3.UnitX * 2;

        ReleaseKey(context, Keys.G);
        ReleaseKey(context, Keys.X);
        ReleaseKey(context, Keys.D2);

        for (var index = 0; index < initial.Length; index++)
        {
            var before = Vector3.Transform(new Vector3(initial[index].Position.X,
                initial[index].Position.Y, initial[index].Position.Z), world);
            var after = Vector3.Transform(new Vector3(context.Mesh.VertexArray[index].Position.X,
                context.Mesh.VertexArray[index].Position.Y, context.Mesh.VertexArray[index].Position.Z), world);
            Assert.That(Vector3.Distance(after, before + delta), Is.LessThan(0.0001f));
        }
        Assert.That(context.Component.TrySetTransformSpace(space == TransformSpace.Local ? TransformSpace.World : TransformSpace.Local), Is.False);
        ReleaseKey(context, Keys.Enter);
        Assert.That(context.CommandExecutor.Undo(), Is.True);
        var restored = context.Mesh.VertexArray.ToArray();
        for (var index = 0; index < initial.Length; index++)
        {
            // Undo replays float matrices; all non-position data must remain exact.
            Assert.That(Vector4.Distance(restored[index].Position, initial[index].Position), Is.LessThan(0.00001f));
            restored[index].Position = initial[index].Position;
        }
        Assert.That(restored, Is.EqualTo(initial));
        Assert.That(context.CommandExecutor.CanUndo(), Is.False);
        Assert.That(context.CommandExecutor.Redo(), Is.True);
        for (var index = 0; index < initial.Length; index++)
        {
            var before = Vector3.Transform(initial[index].Position3(), world);
            var after = Vector3.Transform(context.Mesh.VertexArray[index].Position3(), world);
            Assert.That(Vector3.Distance(after, before + delta), Is.LessThan(0.00001f));
        }
    }
}
