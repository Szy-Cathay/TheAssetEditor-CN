using Editors.AnimationVisualEditors.AnimationWorkbench;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
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
    public void Yangjian_RealMotionThroughVersionEightCodecIsStableAndReadOnly()
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
        sourceAnimation.Header.Version = 8;
        var versionEightPackFile = PackFile.CreateFromBytes(
            "yangjian_bstd_v8_in_memory.anim",
            AnimationFile.ConvertToBytes(sourceAnimation));
        var parsedAnimation = AnimationFile.Create(versionEightPackFile);
        Assert.That(parsedAnimation.Header.Version, Is.EqualTo(8));
        var animationResult = viewport.LoadAnimation(
            parsedAnimation,
            @"test\yangjian_as_mgd_yangjian_01_bstd_01.anim");
        Assert.That(
            animationResult.IsSuccess,
            Is.True,
            animationResult.Diagnostic);
        var frameZeroState = viewport.PlaybackState;
        var frameZero = RenderFrame(game);

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
            Assert.That(middleState.CurrentFrame, Is.GreaterThan(0));
            Assert.That(pausedState.IsPlaying, Is.False);
            Assert.That(pausedState.CurrentTimeSeconds, Is.GreaterThan(0));
            Assert.That(
                CountDifferentPixels(frameZero, middle),
                Is.GreaterThan(100),
                "Seeking did not change the real skinned pose.");
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
