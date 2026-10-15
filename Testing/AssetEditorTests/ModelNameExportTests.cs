using System.Numerics;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf;
using Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using NUnit.Framework;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class ModelNameExportTests
{
    [Test]
    public void ChineseName_GltfImportRmvSaveReloadAndGltfExport_PreservesName()
    {
        new LocalizationManager().LoadLanguage();
        const string name = "胸甲_左侧";
        var geometry = new MeshBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(name);
        var triangle = geometry.UsePrimitive(new MaterialBuilder("material"));
        triangle.AddTriangle(
            new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(new VertexPositionNormal(Vector3.Zero, Vector3.UnitZ)),
            new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(new VertexPositionNormal(Vector3.UnitX, Vector3.UnitZ)),
            new VertexBuilder<VertexPositionNormal, VertexTexture1, VertexEmpty>(new VertexPositionNormal(Vector3.UnitY, Vector3.UnitZ)));
        var gltf = ModelRoot.CreateModel();
        gltf.UseScene("default").CreateNode(name).WithMesh(gltf.CreateMesh(geometry));
        var settings = new GltfImporterSettings("scene.gltf", "models", new PackFileContainer("test"),
            GameTypeEnum.Warhammer3, true, false, false, false, false, 20);
        var imported = RmvMeshBuilder.Build(settings, gltf, null, "")!;
        var factory = ModelFactory.Create();
        var reloaded = factory.Load(factory.Save(imported));
        Assert.That(reloaded.ModelList[0][0].Material.ModelName, Is.EqualTo(name));
        var exportedMesh = new GltfMeshBuilder().Build(reloaded, [],
            new RmvToGltfExporterSettings(null!, [], "", false, false, false, false, false), false).Single();
        var exported = ModelRoot.CreateModel();
        exported.UseScene("default").CreateNode(name).WithMesh(exported.CreateMesh(exportedMesh));
        var path = Path.Combine(Path.GetTempPath(), "aecn-name-" + Guid.NewGuid() + ".glb");
        try
        {
            exported.SaveGLB(path);
            var reopened = ModelRoot.Load(path);
            Assert.That(reopened.LogicalMeshes.Single().Name, Is.EqualTo(name));
            var roundTrip = RmvMeshBuilder.Build(settings, reopened, null, "")!;
            Assert.That(factory.Load(factory.Save(roundTrip)).ModelList[0][0].Material.ModelName, Is.EqualTo(name));
        }
        finally { File.Delete(path); }
    }
}
