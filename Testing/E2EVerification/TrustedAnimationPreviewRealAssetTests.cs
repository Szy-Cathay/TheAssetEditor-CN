using Editors.AnimationVisualEditors.AnimationWorkbench;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.PackFiles.Models;
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
