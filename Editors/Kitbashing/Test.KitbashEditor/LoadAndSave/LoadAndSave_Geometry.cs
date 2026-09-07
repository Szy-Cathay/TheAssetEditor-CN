using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Editors.KitbasherEditor.UiCommands;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Lod;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.GameFormats.RigidModel;
using Test.TestingUtility.Shared;

namespace Test.KitbashEditor.LoadAndSave
{
    internal class LoadAndSave_Geometry
    {
        // No geo
        // Only visible
        // rmv8
        // Rmv7
        // Rmv6
        // Validate pivot and matrix shit




        [Test]
        public void Warhammer3_SaveKarl_Default()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(TestFiles.KarlPackFile, true);

            // Load the a rmv2 and open the kitbash editor
            var originalRmv2File = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(originalRmv2File);

            // Edit the save settings and trigger a save
            var saveSettings = runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            saveSettings.IsUserInitialized = true;

            var toolCommandFactory = runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
            toolCommandFactory.Create<SaveCommand>().Execute();

            // Verify output files
            Assert.That(outputPackFile!.FileList.Count, Is.EqualTo(2));

            // Verify the generated RMV2 file
            uint[] expectedMeshCountPerLod = [4, 4, 2, 2];
            VertexFormat[][] expectedVertexType = [
                [VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Weighted],
                [VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Weighted],
                [VertexFormat.Weighted, VertexFormat.Weighted ],
                [VertexFormat.Weighted, VertexFormat.Weighted ]];
            bool[][] alpha = [
                [false, true, false, true],
                [false, true, false, true],
                [false, false],
                [false, false]];

            // Assert
            var rmv2File = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl, outputPackFile);
            var rmv = RmvHelper.AssertFile(rmv2File, RmvVersionEnum.RMV2_V7, 4, "humanoid01");
            AssertDefaultLodReduction(rmv);
            RmvHelper.AssertMaterial(rmv, 4, expectedVertexType, alpha, ModelMaterialEnum.weighted);

            // Verify wsmodel
            var wsModelFile = runner.PackFileService.FindFile(TestFiles.WsFilePathKarl, outputPackFile);
            WsModelHelper.AssertFile(wsModelFile);
        }

        internal static void AssertDefaultLodReduction(RmvFile rmv)
        {
            int[] expectedMeshCounts = [4, 4, 2, 2];
            double[] expectedRatios = [1, 0.75, 0.5, 0.25];
            var baseIndexCount = rmv.ModelList[0].Sum(x => x.Mesh.IndexList.Length);
            Assert.That(baseIndexCount, Is.EqualTo(36150));
            Assert.That(rmv.ModelList, Has.Length.EqualTo(4));
            for (var lod = 0; lod < 4; lod++)
            {
                Assert.That(rmv.ModelList[lod], Has.Length.EqualTo(expectedMeshCounts[lod]));
                var indexCount = rmv.ModelList[lod].Sum(x => x.Mesh.IndexList.Length);
                Assert.That((double)indexCount / baseIndexCount, Is.EqualTo(expectedRatios[lod]).Within(0.01),
                    $"LOD {lod} must reduce triangles to the configured fraction, allowing protected borders.");
                foreach (var model in rmv.ModelList[lod])
                    Assert.That(model.Mesh.IndexList.All(index => index < model.Mesh.VertexList.Length), Is.True);
            }
        }

        [Test]
        public void Warhammer3_SaveKarl_Lod0ForAll()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(TestFiles.KarlPackFile, true);

            // Load the a rmv2 and open the kitbash editor
            var originalRmv2File = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl);
            runner.CommandFactory.Create<OpenEditorCommand>().Execute(originalRmv2File);

            // Edit the save settings and trigger a save
            var saveSettings = runner.GetRequiredServiceInCurrentEditorScope<GeometrySaveSettings>();
            saveSettings.IsUserInitialized = true;
            saveSettings.LodGenerationMethod = LodStrategy.Lod0ForAll;

            var toolCommandFactory = runner.GetRequiredServiceInCurrentEditorScope<IUiCommandFactory>();
            toolCommandFactory.Create<SaveCommand>().Execute();

            // Verify output files
            Assert.That(outputPackFile!.FileList.Count, Is.EqualTo(2));

            // Verify the generated RMV2 file
            uint[] expectedMeshCountPerLod = [4, 4, 4, 4];
            uint[] vertexCount = [36150, 36150, 36150, 36150];
            VertexFormat[][] expectedVertexType = [
                [VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Weighted],
                [VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Weighted],
                [VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Weighted],
                [VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Cinematic, VertexFormat.Weighted]];
            bool[][] alpha = [
                [false, true, false, true],
                [false, true, false, true],
                [false, true, false, true],
                [false, true, false, true]];

            // Assert
            var rmv2File = runner.PackFileService.FindFile(TestFiles.RmvFilePathKarl, outputPackFile);
            var rmv = RmvHelper.AssertFile(rmv2File, RmvVersionEnum.RMV2_V7, 4, "humanoid01");
            RmvHelper.AssertGeometryFile(rmv, 4, [4, 4, 4, 4], vertexCount);
            RmvHelper.AssertMaterial(rmv, 4, expectedVertexType, alpha, ModelMaterialEnum.weighted);

            // Verify wsmodel
            var wsModelFile = runner.PackFileService.FindFile(TestFiles.WsFilePathKarl, outputPackFile);
            WsModelHelper.AssertFile(wsModelFile);
        }
    }
}
