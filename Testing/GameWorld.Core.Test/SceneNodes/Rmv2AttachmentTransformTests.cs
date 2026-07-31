using System.Reflection;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;

namespace Testing.GameWorld.Core.SceneNodes;

[TestFixture]
internal class Rmv2AttachmentTransformTests
{
    [Test]
    public void GetRenderWorldMatrix_ComposesPivotBeforeModelAndOuterWorldAfterModel()
    {
        var mesh = CreateEmptyMesh();
        mesh.PivotPoint = new Vector3(2, 0, 0);
        mesh.ModelMatrix = Matrix.CreateScale(3) * Matrix.CreateTranslation(5, 0, 0);
        mesh.AttachmentOuterWorld = Matrix.CreateTranslation(7, 0, 0);

        var world = mesh.GetRenderWorldMatrix();

        Assert.That(world.Translation.X, Is.EqualTo(18).Within(0.001f));
    }

    private static Rmv2MeshNode CreateEmptyMesh() =>
        (Rmv2MeshNode)Activator.CreateInstance(
            typeof(Rmv2MeshNode),
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            args: null,
            culture: null)!;
}
