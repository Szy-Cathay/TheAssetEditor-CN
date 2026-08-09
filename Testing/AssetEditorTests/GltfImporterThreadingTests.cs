using System.Collections.ObjectModel;
using System.Numerics;
using System.Windows.Data;
using Editors.ImportExport.Importing;
using Editors.ImportExport.Importing.Importers.GltfToRmv;
using Editors.ImportExport.Importing.Importers.GltfToRmv.Helper;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Schema2;

using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class GltfImporterThreadingTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCase(PackWritePath.Model)]
    [TestCase(PackWritePath.Animation)]
    [TestCase(PackWritePath.Texture)]
    public async Task Import_FromWorkerThread_UpdatesPackOnApplicationDispatcher(
        PackWritePath writePath)
    {
        var glbPath = CreateGlb(writePath);
        try
        {
            var completion = new TaskCompletionSource<(Exception?, int, ImportResult?)>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            WpfTestApplicationHost.Invoke(_application =>
            {
                var uiCollection = new ObservableCollection<int>();
                var collectionView = CollectionViewSource.GetDefaultView(
                    uiCollection);
                var packFileService = new Mock<IPackFileService>();
                packFileService
                    .Setup(service => service.AddFilesToPack(
                        It.IsAny<PackFileContainer>(),
                        It.IsAny<List<NewPackFileEntry>>(),
                        It.IsAny<bool>()))
                    .Callback(() => uiCollection.Add(1));
                var materialBuilder = new RmvMaterialBuilder();
                var skeletonLookup = new Mock<ISkeletonAnimationLookUpHelper>();
                if (writePath == PackWritePath.Animation)
                {
                    skeletonLookup
                        .Setup(service => service.GetSkeletonFileFromName(
                            "test_skeleton"))
                        .Returns(CreateSkeleton());
                }
                var importer = new GltfImporter(
                    packFileService.Object,
                    skeletonLookup.Object,
                    materialBuilder);
                var settings = new GltfImporterSettings(
                    glbPath,
                    "models",
                    new PackFileContainer("test"),
                    GameTypeEnum.Warhammer3,
                    writePath == PackWritePath.Model,
                    writePath == PackWritePath.Texture,
                    false,
                    false,
                    writePath == PackWritePath.Animation,
                    20,
                    true);

                _ = RunImportAsync();

                async Task RunImportAsync()
                {
                    Exception? exception = null;
                    ImportResult? importResult = null;
                    try
                    {
                        importResult = await Task.Run(() => importer.Import(settings));
                    }
                    catch (Exception caughtException)
                    {
                        exception = caughtException;
                    }

                    GC.KeepAlive(collectionView);
                    completion.TrySetResult((exception, uiCollection.Count, importResult));
                }
            });

            var result = await completion.Task.WaitAsync(
                TimeSpan.FromSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(result.Item1, Is.Null);
                Assert.That(
                    result.Item3?.Succeeded,
                    Is.True,
                    result.Item3?.Exception?.ToString() ??
                    string.Join(Environment.NewLine, result.Item3?.Errors ?? []));
                Assert.That(result.Item2, Is.GreaterThan(0));
            });
        }
        finally
        {
            File.Delete(glbPath);
        }
    }

    private static string CreateGlb(PackWritePath writePath)
    {
        var imagePath = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.png");
        var material = new MaterialBuilder("material")
            .WithMetallicRoughness();
        if (writePath == PackWritePath.Texture)
        {
            using var bitmap = new System.Drawing.Bitmap(
                2,
                2,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            bitmap.SetPixel(0, 0, System.Drawing.Color.White);
            bitmap.Save(imagePath, System.Drawing.Imaging.ImageFormat.Png);
            material.WithChannelImage(KnownChannel.BaseColor, imagePath);
        }

        var geometry = new MeshBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>("mesh");
        var primitive = geometry.UsePrimitive(material);
        primitive.AddTriangle(
            CreateVertex(0, 0),
            CreateVertex(1, 0),
            CreateVertex(0, 1));

        var modelRoot = ModelRoot.CreateModel();
        var mesh = modelRoot.CreateMesh(geometry);
        var scene = modelRoot.UseScene("default");
        scene.CreateNode("mesh_node").WithMesh(mesh);
        if (writePath == PackWritePath.Animation)
        {
            scene.CreateNode("//skeleton//test_skeleton");
            var boneNode = scene.CreateNode("root");
            var animation = modelRoot.CreateAnimation("idle");
            animation.CreateTranslationChannel(
                boneNode,
                new Dictionary<float, Vector3>
                {
                    [0] = Vector3.Zero,
                    [0.1f] = Vector3.One,
                });
        }

        var path = Path.Combine(
            Path.GetTempPath(),
            $"{Guid.NewGuid():N}.glb");
        try
        {
            modelRoot.SaveGLB(path);
        }
        finally
        {
            File.Delete(imagePath);
        }

        return path;
    }

    private static AnimationFile CreateSkeleton() => new()
    {
        Header = new AnimationFile.AnimationHeader
        {
            SkeletonName = "test_skeleton",
        },
        Bones =
        [
            new AnimationFile.BoneInfo
            {
                Id = 0,
                ParentId = -1,
                Name = "root",
            },
        ],
        AnimationParts =
        [
            new AnimationFile.AnimationPart
            {
                DynamicFrames =
                [
                    new AnimationFile.Frame
                    {
                        Transforms = [new RmvVector3(0, 0, 0)],
                        Quaternion = [new RmvVector4(0, 0, 0, 1)],
                    },
                ],
            },
        ],
    };

    private static VertexBuilder<
        VertexPositionNormalTangent,
        VertexTexture1,
        VertexJoints4> CreateVertex(float x, float y)
    {
        var vertex = new VertexBuilder<
            VertexPositionNormalTangent,
            VertexTexture1,
            VertexJoints4>();
        vertex.Geometry.Position = new Vector3(x, y, 0);
        vertex.Geometry.Normal = Vector3.UnitZ;
        vertex.Geometry.Tangent = new Vector4(1, 0, 0, 1);
        vertex.Material.TexCoord = new Vector2(x, y);
        vertex.Skinning.SetBindings((0, 1), (0, 0), (0, 0), (0, 0));
        return vertex;
    }

    public enum PackWritePath
    {
        Model,
        Animation,
        Texture,
    }
}
