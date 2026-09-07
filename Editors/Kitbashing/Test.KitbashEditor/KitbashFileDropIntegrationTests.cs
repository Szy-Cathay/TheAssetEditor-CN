using System.Xml.Linq;
using Editors.KitbasherEditor.ViewModels;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Test.KitbashEditor.LoadAndSave;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor;

[NonParallelizable]
public class KitbashFileDropIntegrationTests
{
    [Test]
    public void Drop_AllModelFormats_LoadGeometryAndKeepExistingModelAndSourceFiles()
    {
        var runner = new AssetEditorTestRunner();
        runner.CreateCaContainer();
        var output = runner.LoadPackFile(TestFiles.KarlPackFile)!;
        var rmv = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl)!;
        var wsmodel = runner.PackFileService.FindFile(TestFiles.WsFilePathKarl)!;
        // Exercise VMD -> wsmodel -> real RMV2 and material dependencies.
        var vmdXml = new XElement("VARIANT_MESH",
            new XElement("SLOT", new XAttribute("name", "body"),
                new XElement("VARIANT_MESH", new XAttribute("model", TestFiles.WsFilePathKarl))));
        var vmd = PackFile.CreateFromASCII("drop.variantmeshdefinition", vmdXml.ToString());
        runner.PackFileService.AddFilesToPack(output, [new NewPackFileEntry("", vmd)]);
        var editor = (KitbasherViewModel)runner.CommandFactory.Create<OpenEditorCommand>().Execute(rmv)!;

        try
        {
            var scene = editor.SceneExplorer.SceneManager;
            var main = scene.GetNodeByName<MainEditableNode>(SpecialNodes.EditableModel);
            var editableMeshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(main).ToArray();
            var references = scene.GetNodeByName<GroupNode>(SpecialNodes.ReferenceMeshs);
            var sourceFiles = new[] { rmv, wsmodel, vmd };
            var outputCount = output.FileList.Count;

            Assert.That(editableMeshes, Is.Not.Empty);
            Assert.That(references.Children, Is.Empty);
            foreach (var source in sourceFiles)
            {
                var owner = runner.PackFileService.GetPackFileContainer(source)!;
                var sourcePath = runner.PackFileService.GetFullPath(source);
                var sourceBytes = source.DataSource.ReadData();
                var node = new TreeNode(source.Name, NodeType.File, owner, null, source);
                var count = references.Children.Count;

                Assert.That(editor.AllowDrop(node), Is.True);
                Assert.That(editor.Drop(node), Is.True);
                Assert.That(references.Children, Has.Count.EqualTo(count + 1));
                var importedMeshes = SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(references.Children.Last());
                Assert.That(importedMeshes, Has.Count.EqualTo(4), source.Name);
                Assert.That(importedMeshes.All(mesh => mesh.Geometry.VertexArray.Length > 0 && mesh.Geometry.IndexArray.Length > 0), Is.True);
                Assert.That(importedMeshes.All(mesh => !mesh.IsEditable && !mesh.IsSelectable), Is.True);
                Assert.That(SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(main), Is.EqualTo(editableMeshes));
                Assert.That(runner.PackFileService.FindFile(sourcePath, owner), Is.SameAs(source));
                Assert.That(source.DataSource.ReadData(), Is.EqualTo(sourceBytes));
                Assert.That(output.FileList, Has.Count.EqualTo(outputCount));
                Assert.That(editor.HasUnsavedChanges, Is.False);
            }

            Assert.That(references.Children[0], Is.TypeOf<Rmv2ModelNode>());
            Assert.That(references.Children[1], Is.TypeOf<WsModelGroup>());
            Assert.That(references.Children[2], Is.TypeOf<VariantMeshNode>());
        }
        finally
        {
            editor.Close();
            editor.SceneExplorer.SceneManager.Dispose();
        }
    }

    [TestCase(NodeType.File, "texture.dds", true)]
    [TestCase(NodeType.Directory, "folder.wsmodel", true)]
    [TestCase(NodeType.File, "missing.wsmodel", false)]
    public void Drop_InvalidNode_DoesNotRunImport(NodeType type, string name, bool hasFile)
    {
        var factory = new Mock<IUiCommandFactory>(MockBehavior.Strict);
        var handler = new KitbashViewDropHandler(factory.Object);
        var file = hasFile ? PackFile.CreateFromBytes(name, []) : null;
        var node = new TreeNode(name, type, new PackFileContainer("Test"), null, file);

        Assert.That(handler.Drop(node), Is.False);
        factory.VerifyNoOtherCalls();
    }
}
