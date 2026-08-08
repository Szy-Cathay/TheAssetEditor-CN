using System.IO;
using System.Reflection;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Services;
using Test.KitbashEditor.LoadAndSave;

namespace Test.KitbashEditor.PhotoStudio;

[TestFixture]
[NonParallelizable]
internal class PhotoStudioRenderIntegrationTests :
    LoadAndSaveBase
{
    [Test]
    public void RenderEngine_ExportsTransparentOneAndTwoTimesPng()
    {
        var (runner, editor) = CreateKitbashTool(
            TestFiles.RomePack_MeshHelmet);
        var renderEngine =
            runner.GetRequiredServiceInCurrentEditorScope<
                RenderEngineComponent>();
        var selectionOverlay =
            runner.GetRequiredServiceInCurrentEditorScope<
                KitbashSceneComponentSet>()
            .SelectionOverlay;
        var game =
            runner.GetRequiredServiceInCurrentEditorScope<
                IWpfGame>();
        var device = game.GraphicsDevice;
        var outputFolder = Path.Combine(
            Path.GetTempPath(),
            $"PhotoStudioRenderIntegrationTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputFolder);

        using var backTarget = new RenderTarget2D(
            device,
            128,
            96,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        try
        {
            var oneTimes = Capture(
                renderEngine,
                selectionOverlay,
                editor.SceneExplorer.SceneManager,
                device,
                backTarget,
                outputFolder,
                "PhotoStudioAutomated_1x",
                1.0f);
            var twoTimes = Capture(
                renderEngine,
                selectionOverlay,
                editor.SceneExplorer.SceneManager,
                device,
                backTarget,
                outputFolder,
                "PhotoStudioAutomated_2x",
                2.0f);

            AssertImage(
                device,
                oneTimes,
                128,
                96);
            AssertImage(
                device,
                twoTimes,
                256,
                192);
        }
        finally
        {
            device.SetRenderTarget(null);
            Directory.Delete(outputFolder, true);
        }
    }

    [Test]
    public void RenderEngine_RejectedCaptureReportsFailure()
    {
        var (runner, _) = CreateKitbashTool(
            TestFiles.RomePack_MeshHelmet);
        var renderEngine =
            runner.GetRequiredServiceInCurrentEditorScope<
                RenderEngineComponent>();
        Exception? reportedException = null;
        renderEngine.SaveNextFrame(
            new SaveRenderImageSettings(
                "Oversized",
                false,
                2.0f,
                Path.GetTempPath())
            {
                FailureHandler =
                    exception => reportedException = exception
            });
        var handlePendingCapture =
            typeof(RenderEngineComponent).GetMethod(
                "HandlePendingCapture",
                BindingFlags.Instance |
                    BindingFlags.NonPublic);
        Assert.That(handlePendingCapture, Is.Not.Null);

        handlePendingCapture!.Invoke(
            renderEngine,
            [5000, 3000]);

        Assert.That(
            reportedException,
            Is.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void RenderEngine_SaveFailureReportsFailure()
    {
        var (runner, editor) = CreateKitbashTool(
            TestFiles.RomePack_MeshHelmet);
        var renderEngine =
            runner.GetRequiredServiceInCurrentEditorScope<
                RenderEngineComponent>();
        var selectionOverlay =
            runner.GetRequiredServiceInCurrentEditorScope<
                KitbashSceneComponentSet>()
            .SelectionOverlay;
        var game =
            runner.GetRequiredServiceInCurrentEditorScope<
                IWpfGame>();
        var device = game.GraphicsDevice;
        var outputRoot = Path.Combine(
            Path.GetTempPath(),
            $"PhotoStudioSaveFailure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputRoot);
        var blockedOutput = Path.Combine(
            outputRoot,
            "not-a-directory");
        File.WriteAllText(blockedOutput, "blocker");
        using var backTarget = new RenderTarget2D(
            device,
            64,
            64,
            false,
            SurfaceFormat.Color,
            DepthFormat.Depth24);
        Exception? reportedException = null;

        try
        {
            var gameTime = new GameTime();
            renderEngine.Update(gameTime);
            editor.SceneExplorer.SceneManager.Draw(gameTime);
            selectionOverlay.Draw(gameTime);
            renderEngine.SaveNextFrame(
                new SaveRenderImageSettings(
                    "SaveFailure",
                    false,
                    1.0f,
                    blockedOutput)
                {
                    FailureHandler =
                        exception => reportedException = exception
                });
            device.SetRenderTarget(backTarget);
            renderEngine.Draw(gameTime);
            device.SetRenderTarget(null);

            Assert.That(reportedException, Is.Not.Null);
        }
        finally
        {
            device.SetRenderTarget(null);
            Directory.Delete(outputRoot, true);
        }
    }

    private static string Capture(
        RenderEngineComponent renderEngine,
        KitbashSelectionOverlayComponent selectionOverlay,
        GameWorld.Core.Components.SceneManager sceneManager,
        GraphicsDevice device,
        RenderTarget2D backTarget,
        string outputFolder,
        string name,
        float scale)
    {
        var gameTime = new GameTime();
        renderEngine.Update(gameTime);
        sceneManager.Draw(gameTime);
        selectionOverlay.Draw(gameTime);
        renderEngine.SaveNextFrame(
            new SaveRenderImageSettings(
                name,
                false,
                scale,
                outputFolder));
        device.SetRenderTarget(backTarget);
        renderEngine.Draw(gameTime);
        device.SetRenderTarget(null);

        return Directory.GetFiles(
                outputFolder,
                $"{name}_*.png")
            .Single();
    }

    private static void AssertImage(
        GraphicsDevice device,
        string path,
        int expectedWidth,
        int expectedHeight)
    {
        using var stream = File.OpenRead(path);
        using var texture =
            Texture2D.FromStream(device, stream);
        var pixels = new Color[
            texture.Width * texture.Height];
        texture.GetData(pixels);

        Assert.Multiple(() =>
        {
            Assert.That(
                texture.Width,
                Is.EqualTo(expectedWidth));
            Assert.That(
                texture.Height,
                Is.EqualTo(expectedHeight));
            Assert.That(
                pixels.Count(pixel => pixel.A == 0),
                Is.GreaterThan(0));
            Assert.That(
                pixels.Count(pixel => pixel.A != 0),
                Is.GreaterThan(0));
        });
    }
}
