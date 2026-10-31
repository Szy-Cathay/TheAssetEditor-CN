using System.Text;
using GameWorld.Core.Animation;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Test.TestUtility;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Test.TestingUtility.TestUtility;

namespace GameWorld.Core.Test.Services;

[TestFixture]
[NonParallelizable]
public class ComplexMeshLoaderAttachmentTests
{
    [TestCase("be_prop_0", "", "be_prop_0")]
    [TestCase("be_prop_0", "be_prop_3", "be_prop_3")]
    [TestCase("", "", "")]
    public void Load_NestedSlotsKeepTheEffectiveAttachmentOnTheActualMesh(
        string outerAttachment, string nestedAttachment, string expectedAttachment)
    {
        const string model = "variantmeshes/_variantmodels/man/helmets/carthaginian_pylos.rigid_model_v2";
        const string nestedPath = "nested.variantmeshdefinition";
        var modelFile = PackFile.CreateFromBytes(model, File.ReadAllBytes(PathHelper.GetDataFile(
            model, "Data/Rome_Man_And_Shield_Pack")));
        var nestedFile = PackFile.CreateFromBytes(nestedPath, Encoding.UTF8.GetBytes($"""
            <VARIANT_MESH>
              <SLOT name="equipment" attach_point="{nestedAttachment}">
                <VARIANT_MESH>
                  <SLOT name="mesh">
                    <VARIANT_MESH model="{model}" />
                  </SLOT>
                </VARIANT_MESH>
              </SLOT>
            </VARIANT_MESH>
            """));
        var root = PackFile.CreateFromBytes("root.variantmeshdefinition", Encoding.UTF8.GetBytes($"""
            <VARIANT_MESH>
              <SLOT name="weapon" attach_point="{outerAttachment}">
                <VARIANT_MESH_REFERENCE definition="{nestedPath}" />
              </SLOT>
            </VARIANT_MESH>
            """));
        var files = new Mock<IPackFileService>();
        files.Setup(service => service.FindFile(nestedPath, null)).Returns(nestedFile);
        files.Setup(service => service.FindFile(model, null)).Returns(modelFile);
        files.Setup(service => service.GetFullPath(modelFile, null)).Returns(model);
        var geometryContexts = new TestGeometryGraphicsContextFactory();
        var materials = new CapabilityMaterialFactory(
            new ApplicationSettingsService(GameTypeEnum.Rome2), Mock.Of<IScopedResourceLibrary>());
        var nodes = new Rmv2ModelNodeLoader(new MeshBuilderService(geometryContexts),
            files.Object, materials, Mock.Of<IStandardDialogs>());
        var loader = new ComplexMeshLoader(nodes, files.Object);

        var loaded = loader.Load(root, new AnimationPlayer(), true, true);

        var mesh = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(loaded).Single();
        Assert.That(mesh.Geometry.VertexArray, Is.Not.Empty);
        Assert.That(geometryContexts.CreatedContexts.Single().VertexBufferRebuildCount, Is.EqualTo(1));
        if (expectedAttachment.Length == 0)
            Assert.That(mesh.AttachmentPointName, Is.Null.Or.Empty);
        else
            Assert.That(mesh.AttachmentPointName, Is.EqualTo(expectedAttachment));
    }
}
