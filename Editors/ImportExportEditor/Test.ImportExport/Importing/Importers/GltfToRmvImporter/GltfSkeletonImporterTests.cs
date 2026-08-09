using System.Numerics;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using SharpGLTF.Schema2;

namespace Test.ImportExport.Importing.Importers.GltfImporterTest;

public class GltfSkeletonImporterTests
{
    [Test]
    public void Build_PreservesJointOrderHierarchyAndBindPose()
    {
        var modelRoot = ModelRoot.CreateModel();
        var scene = modelRoot.UseScene("default");
        var root = scene.CreateNode("root");
        root.LocalMatrix =
            Matrix4x4.CreateFromQuaternion(Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.25f)) *
            Matrix4x4.CreateTranslation(-1, 2, 3);
        var child = root.CreateNode("child");
        child.LocalMatrix = Matrix4x4.CreateTranslation(-4, 5, 6);
        modelRoot.CreateSkin("test_skin").BindJoints(Matrix4x4.Identity, root, child);

        var result = GltfSkeletonImporter.Build(
            modelRoot,
            "test_skeleton",
            mirrorMesh: true);

        var frame = result.AnimationParts[0].DynamicFrames[0];
        Assert.Multiple(() =>
        {
            Assert.That(result.Header.SkeletonName, Is.EqualTo("test_skeleton"));
            Assert.That(result.Bones.Select(bone => bone.Name), Is.EqualTo(new[] { "root", "child" }));
            Assert.That(result.Bones.Select(bone => bone.ParentId), Is.EqualTo(new[] { -1, 0 }));
            Assert.That(result.AnimationParts[0].DynamicFrames, Has.Count.EqualTo(2));
            Assert.That(frame.Transforms[0].X, Is.EqualTo(1).Within(0.0001f));
            Assert.That(frame.Transforms[0].Y, Is.EqualTo(2).Within(0.0001f));
            Assert.That(frame.Transforms[0].Z, Is.EqualTo(3).Within(0.0001f));
            Assert.That(frame.Transforms[1].X, Is.EqualTo(4).Within(0.0001f));
            Assert.That(frame.Transforms[1].Y, Is.EqualTo(5).Within(0.0001f));
            Assert.That(frame.Transforms[1].Z, Is.EqualTo(6).Within(0.0001f));
        });
    }
}
