using Editors.AnimationVisualEditors.AnimationWorkbench;
using GameWorld.Core.Animation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.GameFormats.RigidModel.Types;
using System.Text;
using System.Security.Cryptography;
using Test.TestingUtility.Shared;

namespace Test.E2EVerification;

[NonParallelizable]
public class TrustedAnimationPreviewRealAssetTests
{
    [Test]
    [Explicit]
    public void Yangjian_DefaultPoseRendersModelAndSkeletonIndependently()
    {
        var assetRoot = Environment.GetEnvironmentVariable(
            "AE_TRUSTED_PREVIEW_ASSET_ROOT");
        Assert.That(assetRoot, Is.Not.Null.And.Not.Empty,
            "Set AE_TRUSTED_PREVIEW_ASSET_ROOT to the WH3 acceptance asset folder.");
        Assert.That(Directory.Exists(assetRoot), Is.True,
            $"Real WH3 acceptance asset folder was not found: {assetRoot}");

        TestContext.Progress.WriteLine("Creating test services.");
        var runner = new AssetEditorTestRunner();
        runner.PackFileService.EnforceGameFilesMustBeLoaded = false;
        TestContext.Progress.WriteLine("Loading folder resources.");
        runner.PackFileService.AddContainer(
            CreateAcceptanceAssetContainer(assetRoot!));
        var model = runner.PackFileService.FindFile(
            @"test\yangjian.rigid_model_v2");
        var skeleton = runner.PackFileService.FindFile(
            @"animations\skeletons\yangjian_skeleton.anim");
        Assert.Multiple(() =>
        {
            Assert.That(model, Is.Not.Null);
            Assert.That(skeleton, Is.Not.Null);
        });

        TestContext.Progress.WriteLine("Creating trusted viewport.");
        using var viewport = runner.ServiceProvider
            .GetRequiredService<ITrustedAnimationPreviewViewport>();
        TestContext.Progress.WriteLine("Loading real model and skeleton.");
        var result = viewport.Load(model!, skeleton!);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.True, result.Diagnostic);
            Assert.That(result.MeshCount, Is.GreaterThan(0));
        });

        TestContext.Progress.WriteLine("Rendering hidden baseline.");
        var game = (WpfGameMock)viewport.GameWorld;
        viewport.SetModelVisible(false);
        viewport.SetSkeletonVisible(false);
        var baseline = RenderFrame(game);

        TestContext.Progress.WriteLine("Rendering model-only frame.");
        viewport.SetModelVisible(true);
        var modelOnly = RenderFrame(game);

        TestContext.Progress.WriteLine("Rendering skeleton-only frame.");
        viewport.SetModelVisible(false);
        viewport.SetSkeletonVisible(true);
        var skeletonOnly = RenderFrame(game);

        Assert.Multiple(() =>
        {
            Assert.That(
                CountDifferentPixels(baseline, modelOnly),
                Is.GreaterThan(100),
                "The real viewport did not draw the model default pose.");
            Assert.That(
                CountDifferentPixels(baseline, skeletonOnly),
                Is.GreaterThan(10),
                "The real viewport did not draw the resolved skeleton.");
        });
    }

    [Test]
    [Explicit]
    public void Yangjian_RealVersionSevenMotionHasCompleteReadOnlyPlayback()
    {
        var assetRoot = Environment.GetEnvironmentVariable(
            "AE_TRUSTED_PREVIEW_ASSET_ROOT");
        Assert.That(assetRoot, Is.Not.Null.And.Not.Empty);
        var modelDiskPath = Path.Combine(
            assetRoot!,
            "test",
            "yangjian.rigid_model_v2");
        var skeletonDiskPath = Path.Combine(
            assetRoot,
            "animations",
            "skeletons",
            "yangjian_skeleton.anim");
        var animationDiskPath = Path.Combine(
            assetRoot,
            "test",
            "yangjian_as_mgd_yangjian_01_bstd_01.anim");
        var originalHashes = new[]
        {
            HashFile(modelDiskPath),
            HashFile(skeletonDiskPath),
            HashFile(animationDiskPath),
        };

        var runner = new AssetEditorTestRunner();
        runner.PackFileService.EnforceGameFilesMustBeLoaded = false;
        runner.PackFileService.AddContainer(
            CreateAcceptanceAssetContainer(assetRoot));
        var model = runner.PackFileService.FindFile(
            @"test\yangjian.rigid_model_v2");
        var skeleton = runner.PackFileService.FindFile(
            @"animations\skeletons\yangjian_skeleton.anim");
        var animation = runner.PackFileService.FindFile(
            @"test\yangjian_as_mgd_yangjian_01_bstd_01.anim");
        Assert.Multiple(() =>
        {
            Assert.That(model, Is.Not.Null);
            Assert.That(skeleton, Is.Not.Null);
            Assert.That(animation, Is.Not.Null);
        });

        using var viewport = runner.ServiceProvider
            .GetRequiredService<ITrustedAnimationPreviewViewport>();
        var modelResult = viewport.Load(model!, skeleton!);
        Assert.That(modelResult.IsSuccess, Is.True, modelResult.Diagnostic);
        var game = (WpfGameMock)viewport.GameWorld;
        var defaultPose = RenderFrame(game);

        var sourceAnimation = AnimationFile.Create(animation!);
        Assert.That(sourceAnimation.Header.Version, Is.EqualTo(7));
        var animationResult = viewport.LoadAnimation(
            sourceAnimation,
            @"test\yangjian_as_mgd_yangjian_01_bstd_01.anim");
        Assert.That(
            animationResult.IsSuccess,
            Is.True,
            animationResult.Diagnostic);
        var frameZeroState = viewport.PlaybackState;
        var frameZero = RenderFrame(game);

        viewport.NextFrame();
        var nextFrameState = viewport.PlaybackState;
        var nextFrame = RenderFrame(game);
        viewport.PreviousFrame();
        var previousFrameState = viewport.PlaybackState;
        var returnedFrameZero = RenderFrame(game);
        viewport.SetLooping(false);
        var noLoopState = viewport.PlaybackState;

        viewport.Seek(frameZeroState.DurationSeconds / 2);
        var middleState = viewport.PlaybackState;
        var middle = RenderFrame(game);
        viewport.Seek(frameZeroState.DurationSeconds / 2);
        var repeatedMiddle = RenderFrame(game);

        viewport.Seek(0);
        viewport.Play();
        for (var index = 0; index < 8; index++)
            RenderFrame(game);
        viewport.Pause();
        var pausedState = viewport.PlaybackState;
        var paused = RenderFrame(game);

        viewport.SetSkeletonVisible(false);
        var modelOnly = RenderFrame(game);
        viewport.SetModelVisible(false);
        viewport.SetSkeletonVisible(true);
        var skeletonOnly = RenderFrame(game);

        Assert.Multiple(() =>
        {
            Assert.That(frameZeroState.HasAnimation, Is.True);
            Assert.That(frameZeroState.IsPlaying, Is.False);
            Assert.That(frameZeroState.CurrentFrame, Is.Zero);
            Assert.That(frameZeroState.FrameCount, Is.GreaterThan(1));
            Assert.That(frameZeroState.DurationSeconds, Is.GreaterThan(0));
            Assert.That(frameZeroState.FramesPerSecond,
                Is.EqualTo(sourceAnimation.Header.FrameRate));
            Assert.That(nextFrameState.CurrentFrame, Is.EqualTo(1));
            Assert.That(previousFrameState.CurrentFrame, Is.Zero);
            Assert.That(noLoopState.IsLooping, Is.False);
            Assert.That(middleState.CurrentFrame, Is.GreaterThan(0));
            Assert.That(pausedState.IsPlaying, Is.False);
            Assert.That(pausedState.CurrentTimeSeconds, Is.GreaterThan(0));
            Assert.That(
                CountDifferentPixels(frameZero, middle),
                Is.GreaterThan(100),
                "Seeking did not change the real skinned pose.");
            Assert.That(
                CountDifferentPixels(frameZero, nextFrame),
                Is.GreaterThan(0),
                "Next frame did not change the real skinned pose.");
            Assert.That(
                CountDifferentPixels(frameZero, returnedFrameZero),
                Is.LessThan(500),
                "Previous frame did not deterministically return to frame 0.");
            Assert.That(
                CountDifferentPixels(middle, repeatedMiddle),
                Is.LessThan(500),
                "Repeated seeking changed a visible part of the pose.");
            Assert.That(
                CountDifferentPixels(defaultPose, paused),
                Is.GreaterThan(100),
                "Play then pause did not produce a real animated pose.");
            Assert.That(
                CountDifferentPixels(modelOnly, skeletonOnly),
                Is.GreaterThan(100));
            Assert.That(new[]
            {
                HashFile(modelDiskPath),
                HashFile(skeletonDiskPath),
                HashFile(animationDiskPath),
            }, Is.EqualTo(originalHashes));
        });
    }

    [Test]
    [Explicit]
    public void Yangjian_RealViewportHandlesStaticAndMultipartAnimations()
    {
        var assetRoot = Environment.GetEnvironmentVariable(
            "AE_TRUSTED_PREVIEW_ASSET_ROOT");
        Assert.That(assetRoot, Is.Not.Null.And.Not.Empty);
        var modelDiskPath = Path.Combine(
            assetRoot!,
            "test",
            "yangjian.rigid_model_v2");
        var skeletonDiskPath = Path.Combine(
            assetRoot,
            "animations",
            "skeletons",
            "yangjian_skeleton.anim");
        var animationDiskPath = Path.Combine(
            assetRoot,
            "test",
            "yangjian_as_mgd_yangjian_01_bstd_01.anim");
        var originalHashes = new[]
        {
            HashFile(modelDiskPath),
            HashFile(skeletonDiskPath),
            HashFile(animationDiskPath),
        };

        var runner = new AssetEditorTestRunner();
        runner.PackFileService.EnforceGameFilesMustBeLoaded = false;
        runner.PackFileService.AddContainer(
            CreateAcceptanceAssetContainer(assetRoot));
        var model = runner.PackFileService.FindFile(
            @"test\yangjian.rigid_model_v2");
        var skeleton = runner.PackFileService.FindFile(
            @"animations\skeletons\yangjian_skeleton.anim");
        var animation = runner.PackFileService.FindFile(
            @"test\yangjian_as_mgd_yangjian_01_bstd_01.anim");
        Assert.Multiple(() =>
        {
            Assert.That(model, Is.Not.Null);
            Assert.That(skeleton, Is.Not.Null);
            Assert.That(animation, Is.Not.Null);
        });

        var skeletonFile = AnimationFile.Create(skeleton!);
        var sourceAnimation = AnimationFile.Create(animation!);
        var gameSkeleton = new GameSkeleton(
            skeletonFile,
            new AnimationPlayer());
        var staticPose = CreateStaticPose(sourceAnimation, gameSkeleton);
        var multipart = CreateMultipart(sourceAnimation, gameSkeleton);
        using var viewport = runner.ServiceProvider
            .GetRequiredService<ITrustedAnimationPreviewViewport>();
        var modelResult = viewport.Load(model!, skeleton);
        Assert.That(modelResult.IsSuccess, Is.True, modelResult.Diagnostic);
        viewport.SetModelVisible(true);
        viewport.SetSkeletonVisible(false);
        viewport.ShowFront();

        var staticResult = viewport.LoadAnimation(
            staticPose,
            @"test\yangjian_static_pose.anim");
        Assert.That(staticResult.IsSuccess, Is.True, staticResult.Diagnostic);
        var staticState = viewport.PlaybackState;
        var staticFrame = RenderFrame((WpfGameMock)viewport.GameWorld);

        var multipartResult = viewport.LoadAnimation(
            multipart,
            @"test\yangjian_multipart.anim");
        Assert.That(
            multipartResult.IsSuccess,
            Is.True,
            multipartResult.Diagnostic);
        var multipartFrameZero = RenderFrame(
            (WpfGameMock)viewport.GameWorld);
        var multipartState = viewport.PlaybackState;
        viewport.NextFrame();
        var multipartFrameOne = RenderFrame(
            (WpfGameMock)viewport.GameWorld);

        Assert.Multiple(() =>
        {
            Assert.That(staticState.HasStaticFrame, Is.True);
            Assert.That(staticState.IsStaticPose, Is.True);
            Assert.That(staticState.FrameCount, Is.EqualTo(1));
            Assert.That(staticState.DurationSeconds, Is.Zero);
            Assert.That(staticState.CurrentFrame, Is.Zero);
            Assert.That(multipartState.PartCount, Is.EqualTo(2));
            Assert.That(multipartState.FrameCount,
                Is.EqualTo(sourceAnimation.AnimationParts.Sum(
                    part => part.DynamicFrames.Count)));
            Assert.That(multipartState.FramesPerSecond,
                Is.EqualTo(sourceAnimation.Header.FrameRate));
            Assert.That(
                CountDifferentPixels(staticFrame, multipartFrameZero),
                Is.LessThan(500),
                "Switching animations changed the camera or visibility state.");
            Assert.That(
                CountDifferentPixels(multipartFrameZero, multipartFrameOne),
                Is.GreaterThan(0),
                "Multipart next-frame playback did not change the pose.");
            Assert.That(new[]
            {
                HashFile(modelDiskPath),
                HashFile(skeletonDiskPath),
                HashFile(animationDiskPath),
            }, Is.EqualTo(originalHashes));
        });
    }

    [Test]
    [Explicit]
    public void Yangjian_WsModelResolvesMaterialsTexturesAndAnimates()
    {
        var assetRoot = Environment.GetEnvironmentVariable(
            "AE_TRUSTED_PREVIEW_ASSET_ROOT");
        Assert.That(assetRoot, Is.Not.Null.And.Not.Empty);
        var modelDiskPath = Path.Combine(
            assetRoot!,
            "test",
            "yangjian.rigid_model_v2");
        var skeletonDiskPath = Path.Combine(
            assetRoot,
            "animations",
            "skeletons",
            "yangjian_skeleton.anim");
        var animationDiskPath = Path.Combine(
            assetRoot,
            "test",
            "yangjian_as_mgd_yangjian_01_bstd_01.anim");
        var originalHashes = new[]
        {
            HashFile(modelDiskPath),
            HashFile(skeletonDiskPath),
            HashFile(animationDiskPath),
        };

        TestContext.Progress.WriteLine("Creating real wsmodel fixture.");
        var runner = new AssetEditorTestRunner();
        runner.PackFileService.EnforceGameFilesMustBeLoaded = false;
        var container = CreateAcceptanceAssetContainer(assetRoot);
        var model = container.FileList[@"test\yangjian.rigid_model_v2"];
        var wsModel = AddWsModelResources(container, model);
        runner.PackFileService.AddContainer(container);
        var skeleton = runner.PackFileService.FindFile(
            @"animations\skeletons\yangjian_skeleton.anim");
        var animation = runner.PackFileService.FindFile(
            @"test\yangjian_as_mgd_yangjian_01_bstd_01.anim");
        Assert.Multiple(() =>
        {
            Assert.That(skeleton, Is.Not.Null);
            Assert.That(animation, Is.Not.Null);
        });

        var resolver = new TrustedWsModelResolver(
            runner.PackFileService,
            new TrustedRigidModelInspector());
        TestContext.Progress.WriteLine("Resolving complete wsmodel graph.");
        var resolutionResult = resolver.ResolveAsync(
                wsModel,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        Assert.That(
            resolutionResult.IsSuccess,
            Is.True,
            resolutionResult.Diagnostic);
        var resolution = resolutionResult.Resolution!;
        var parsedModel = ModelFactory.Create().Load(
            model.DataSource.ReadData());
        var expectedMaterialCount = parsedModel.ModelList.Sum(
            lod => lod.Length);

        TestContext.Progress.WriteLine("Loading complete wsmodel viewport.");
        using var viewport = runner.ServiceProvider
            .GetRequiredService<ITrustedAnimationPreviewViewport>();
        var modelResult = viewport.Load(wsModel, resolution.Skeleton);
        Assert.That(modelResult.IsSuccess, Is.True, modelResult.Diagnostic);
        viewport.SetModelVisible(true);
        viewport.SetSkeletonVisible(false);
        viewport.ShowFront();
        var frameZero = RenderFrame((WpfGameMock)viewport.GameWorld);

        TestContext.Progress.WriteLine("Animating complete wsmodel.");
        var sourceAnimation = AnimationFile.Create(animation!);
        var animationResult = viewport.LoadAnimation(
            sourceAnimation,
            @"test\yangjian_as_mgd_yangjian_01_bstd_01.anim");
        Assert.That(
            animationResult.IsSuccess,
            Is.True,
            animationResult.Diagnostic);
        viewport.Seek(sourceAnimation.Header.AnimationTotalPlayTimeInSec / 2);
        var middle = RenderFrame((WpfGameMock)viewport.GameWorld);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.GeometryCount, Is.EqualTo(1));
            Assert.That(resolution.StaticAttachmentCount, Is.Zero);
            Assert.That(resolution.Dependencies.Count(item =>
                    item.Kind == TrustedModelDependencyKind.Material),
                Is.EqualTo(expectedMaterialCount));
            Assert.That(resolution.Dependencies.Count(item =>
                    item.Kind == TrustedModelDependencyKind.Texture),
                Is.GreaterThan(0));
            Assert.That(modelResult.MeshCount,
                Is.EqualTo(parsedModel.ModelList[0].Length));
            Assert.That(
                CountDifferentPixels(frameZero, middle),
                Is.GreaterThan(100),
                "The complete wsmodel did not animate in the real viewport.");
            Assert.That(new[]
            {
                HashFile(modelDiskPath),
                HashFile(skeletonDiskPath),
                HashFile(animationDiskPath),
            }, Is.EqualTo(originalHashes));
        });
    }

    private static PackFile AddWsModelResources(
        PackFileContainer container,
        PackFile modelFile)
    {
        var model = ModelFactory.Create().Load(
            modelFile.DataSource.ReadData());
        var materialEntries = new StringBuilder();
        for (var lodIndex = 0;
             lodIndex < model.ModelList.Length;
             lodIndex++)
        {
            for (var partIndex = 0;
                 partIndex < model.ModelList[lodIndex].Length;
                 partIndex++)
            {
                var materialPath =
                    $@"test\ws_materials\yangjian_{lodIndex}_{partIndex}.xml";
                var textures = new StringBuilder();
                foreach (var texture in model.ModelList[lodIndex][partIndex]
                             .Material.GetAllTextures()
                             .Where(texture =>
                                 !string.IsNullOrWhiteSpace(texture.Path))
                             .DistinctBy(texture =>
                                 (texture.TexureType, texture.Path)))
                {
                    textures.Append("<texture><slot>")
                        .Append(GetTextureSlot(texture.TexureType))
                        .Append("</slot><source>")
                        .Append(System.Security.SecurityElement.Escape(
                            texture.Path))
                        .Append("</source></texture>");
                }
                var materialXml =
                    "<material><name>weighted_standard_4</name>" +
                    $"<textures>{textures}</textures></material>";
                container.FileList[materialPath.ToLowerInvariant()] =
                    PackFile.CreateFromBytes(
                        Path.GetFileName(materialPath),
                        Encoding.UTF8.GetBytes(materialXml));
                materialEntries
                    .Append($"<material lod_index=\"{lodIndex}\" ")
                    .Append($"part_index=\"{partIndex}\">")
                    .Append(materialPath)
                    .Append("</material>");
            }
        }

        var wsModelXml =
            "<model><geometry>test\\yangjian.rigid_model_v2</geometry>" +
            $"<materials>{materialEntries}</materials></model>";
        var wsModel = PackFile.CreateFromBytes(
            "yangjian.wsmodel",
            Encoding.UTF8.GetBytes(wsModelXml));
        container.FileList[@"test\yangjian.wsmodel"] = wsModel;
        return wsModel;
    }

    private static string GetTextureSlot(TextureType type) => type switch
    {
        TextureType.BaseColour => "base_colour",
        TextureType.MaterialMap => "material_map",
        TextureType.Blood => "xml_blood_map",
        TextureType.EmissiveDistortion => "t_xml_emissive_distortion",
        TextureType.Emissive => "t_xml_emissive_texture",
        TextureType.Distortion => "t_xml_distortion",
        TextureType.DistortionNoise => "t_xml_distortion_noise",
        _ => type.ToString(),
    };

    private static PackFileContainer CreateAcceptanceAssetContainer(
        string assetRoot)
    {
        var relativePaths = new List<string>
        {
            @"test\yangjian.rigid_model_v2",
            @"animations\skeletons\yangjian_skeleton.anim",
        };
        relativePaths.AddRange(Directory.EnumerateFiles(
                Path.Combine(assetRoot, "test", "tex"),
                "*",
                SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(assetRoot, path)));
        relativePaths.AddRange(Directory.EnumerateFiles(
                Path.Combine(assetRoot, "test"),
                "*.anim",
                SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetRelativePath(assetRoot, path)));

        var container = new PackFileContainer("trusted-preview-real-assets")
        {
            SystemFilePath = assetRoot,
            IsCaPackFile = true,
        };
        foreach (var relativePath in relativePaths)
        {
            var fullPath = Path.Combine(assetRoot, relativePath);
            var resourcePath = relativePath
                .Replace('/', '\\')
                .ToLowerInvariant();
            container.FileList[resourcePath] =
                PackFile.CreateFromFileSystem(
                    Path.GetFileName(fullPath),
                    fullPath);
        }
        return container;
    }

    private static AnimationFile CreateStaticPose(
        AnimationFile source,
        GameSkeleton skeleton)
    {
        var expanded = new AnimationClip(source, skeleton)
            .DynamicFrames[0];
        var output = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader
            {
                Version = 8,
                SkeletonName = source.Header.SkeletonName,
                FrameRate = source.Header.FrameRate,
                AnimationTotalPlayTimeInSec = 0,
            },
            Bones = source.Bones,
        };
        var part = new AnimationFile.AnimationPart
        {
            StaticFrame = new AnimationFile.Frame(),
        };
        for (var boneIndex = 0;
             boneIndex < source.Bones.Length;
             boneIndex++)
        {
            part.TranslationMappings.Add(
                new AnimationFile.AnimationBoneMapping(
                    10000 + boneIndex));
            part.RotationMappings.Add(
                new AnimationFile.AnimationBoneMapping(
                    10000 + boneIndex));
            var position = expanded.Position[boneIndex];
            var rotation = expanded.Rotation[boneIndex];
            part.StaticFrame.Transforms.Add(new RmvVector3(
                position.X,
                position.Y,
                position.Z));
            part.StaticFrame.Quaternion.Add(new RmvVector4(
                rotation.X,
                rotation.Y,
                rotation.Z,
                rotation.W));
        }
        output.AnimationParts.Add(part);
        return output;
    }

    private static AnimationFile CreateMultipart(
        AnimationFile source,
        GameSkeleton skeleton)
    {
        var output = new AnimationClip(source, skeleton)
            .ConvertToFileFormat(skeleton, 8);
        var original = output.AnimationParts.Single();
        var split = Math.Max(1, original.DynamicFrames.Count / 2);
        var first = ClonePartWithoutFrames(original);
        first.DynamicFrames.AddRange(
            original.DynamicFrames.Take(split));
        var second = ClonePartWithoutFrames(original);
        second.DynamicFrames.AddRange(
            original.DynamicFrames.Skip(split));
        output.AnimationParts = [first, second];
        output.Header.FrameRate = source.Header.FrameRate;
        output.Header.AnimationTotalPlayTimeInSec =
            source.Header.AnimationTotalPlayTimeInSec;
        return output;
    }

    private static AnimationFile.AnimationPart ClonePartWithoutFrames(
        AnimationFile.AnimationPart source) => new()
        {
            StaticFrame = source.StaticFrame,
            TranslationMappings = source.TranslationMappings
                .Select(mapping => mapping.Clone())
                .ToList(),
            RotationMappings = source.RotationMappings
                .Select(mapping => mapping.Clone())
                .ToList(),
        };

    private static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static Color[] RenderFrame(WpfGameMock game)
    {
        const int size = 512;
        var device = game.GraphicsDevice;
        using var target = new RenderTarget2D(
            device,
            size,
            size,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        var previousViewport = device.Viewport;
        try
        {
            device.SetRenderTarget(target);
            device.Viewport = new Viewport(0, 0, size, size);
            var gameTime = new GameTime(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1.0 / 60.0));
            foreach (var updateable in game.Components
                         .OfType<IUpdateable>()
                         .Where(component => component.Enabled)
                         .OrderBy(component => component.UpdateOrder))
            {
                updateable.Update(gameTime);
            }
            foreach (var drawable in game.Components
                         .OfType<IDrawable>()
                         .Where(component => component.Visible)
                         .OrderBy(component => component.DrawOrder))
            {
                drawable.Draw(gameTime);
            }
        }
        finally
        {
            device.SetRenderTarget(null);
            device.Viewport = previousViewport;
        }

        var pixels = new Color[size * size];
        target.GetData(pixels);
        return pixels;
    }

    private static int CountDifferentPixels(
        IReadOnlyList<Color> first,
        IReadOnlyList<Color> second)
    {
        var count = 0;
        for (var index = 0; index < first.Count; index++)
        {
            if (first[index] != second[index])
                count++;
        }
        return count;
    }
}
