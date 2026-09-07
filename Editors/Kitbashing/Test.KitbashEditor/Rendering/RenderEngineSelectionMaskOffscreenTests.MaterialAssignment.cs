using GameWorld.Core.Commands;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Settings;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Test.TestingUtility.Shared;

namespace GameWorld.Core.Test.Rendering;

public partial class RenderEngineSelectionMaskOffscreenTests
{
    [Test]
    public void MaterialAssignment_ChangesRenderedPixelsAndUndoRestoresThem()
    {
        const int size = 96;
        var game = new WpfGameMock();
        var device = game.GraphicsDevice;
        var resolver = Mock.Of<IDeviceResolver>(value => value.Device == device);
        var camera = new ArcBallCamera(resolver, Mock.Of<IKeyboardComponent>(), Mock.Of<IMouseComponent>());
        camera.Initialize();
        var resources = new ResourceLibrary(Mock.Of<IPackFileService>());
        resources.Initialize(device, game.Content);
        var events = Mock.Of<IEventHub>();
        using var red = new Texture2D(device, 1, 1);
        using var green = new Texture2D(device, 1, 1);
        red.SetData(new[] { Color.Red });
        green.SetData(new[] { Color.Lime });
        var scoped = new Mock<IScopedResourceLibrary>();
        scoped.Setup(x => x.GetStaticEffect(It.IsAny<ShaderTypes>())).Returns((ShaderTypes type) => resources.GetStaticEffect(type));
        scoped.Setup(x => x.LoadTexture("red", false, false)).Returns(red);
        scoped.Setup(x => x.LoadTexture("green", false, false)).Returns(green);
        using var renderer = new RenderEngineComponent(game, resources, camera, resolver,
            new ApplicationSettingsService(), new SceneRenderParametersStore(), events,
            new GridComponent(camera, resources, resolver));
        renderer.Initialize();
        using var geometry = CreateMesh(device, false);
        var original = new SelectionOutlineCapabilityMaterial(scoped.Object);
        original.GetCapability<SpecGlossCapability>().DiffuseMap.TexturePath = "red";
        original.GetCapability<SpecGlossCapability>().DiffuseMap.UseTexture = true;
        var replacement = original.Clone();
        replacement.GetCapability<SpecGlossCapability>().DiffuseMap.TexturePath = "green";
        var node = new Rmv2MeshNode(geometry, Mock.Of<IRmvMaterial>(), original, null!);
        using var target = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        using var mask = new RenderTarget2D(device, size, size, false, SurfaceFormat.Color, DepthFormat.Depth24);
        device.SetRenderTarget(target);
        renderer.Draw(new GameTime());
        device.SetRenderTarget(null);
        var before = Draw("material-before");
        Assert.That(before.Count(x => x.R > x.G + 20), Is.GreaterThan(100));
        var commandType = typeof(Editors.KitbasherEditor.ChildEditors.ReRiggingTool.ReRiggingViewModel).Assembly
            .GetType("Editors.KitbasherEditor.Commands.AssignMaterialFromOtherMeshCommand")!;
        var command = (ICommand)Activator.CreateInstance(commandType, true)!;
        commandType.GetMethod("Configure")!.Invoke(command, [replacement, new List<Rmv2MeshNode> { node }]);
        command.Execute();
        var after = Draw("material-after");
        Assert.That(after.Count(x => x.G > x.R + 20), Is.GreaterThan(100));
        Assert.That(resources.GetStaticEffect(ShaderTypes.Pbr_SpecGloss).Parameters["DiffuseTexture"].GetValueTexture2D(), Is.SameAs(green));
        command.Undo();
        Assert.That(Draw("material-undo"), Is.EqualTo(before));
        ((IRedoableCommand)command).Redo();
        Assert.That(Draw("material-redo"), Is.EqualTo(after));

        Color[] Draw(string name)
        {
            renderer.Update(new GameTime());
            node.Render(renderer, Matrix.Identity);
            device.SetRenderTargets(new RenderTargetBinding(target), new RenderTargetBinding(mask));
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Transparent, 1, 0);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullNone;
            InvokeRender3DObjects(renderer);
            device.SetRenderTarget(null);
            var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                System.IO.Directory.CreateDirectory(output);
                using var stream = System.IO.File.Create(System.IO.Path.Combine(output, name + ".png"));
                target.SaveAsPng(stream, size, size);
            }
            var pixels = new Color[size * size];
            target.GetData(pixels);
            return pixels;
        }
    }
}
