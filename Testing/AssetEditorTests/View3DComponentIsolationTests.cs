using System.Runtime.CompilerServices;
using System.Windows;
using AssetEditor.Services;
using Editors.KitbasherEditor.Components;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.Services;

namespace AssetEditorTests;

[TestClass]
public class View3DComponentIsolationTests
{
    [TestMethod]
    public void ComponentInserter_AddsOnlyCoreAndExplicitEditorComponents()
    {
        var game = new RecordingWpfGame();
        var coreComponents = CreateCoreComponentSet();
        var editorComponents = new IGameComponent[]
        {
            new TestGameComponent(),
            new TestGameComponent(),
        };

        new ComponentInserter(game).Execute(
            coreComponents,
            editorComponents);

        Assert.AreEqual(1, game.ForceEnsureCreatedCallCount);
        CollectionAssert.AreEqual(
            coreComponents.Components
                .Concat(editorComponents)
                .ToArray(),
            game.AddedComponents);
    }

    [TestMethod]
    public void ComponentInserter_WithReferenceSelection_DoesNotAddKitbashEditingComponents()
    {
        var game = new RecordingWpfGame();
        var coreComponents = CreateCoreComponentSet();
        var referenceComponents = new IGameComponent[]
        {
            CreateUninitialized<ReferenceObjectSelectionComponent>(),
            CreateUninitialized<ReferenceObjectSelectionOutlineComponent>(),
        };

        new ComponentInserter(game).Execute(
            coreComponents,
            referenceComponents);

        CollectionAssert.IsSubsetOf(
            referenceComponents,
            game.AddedComponents);
        Assert.IsFalse(
            game.AddedComponents.Any(
                component => component is SelectionManager));
        Assert.IsFalse(
            game.AddedComponents.Any(
                component => component is KitbashSelectionInputComponent));
        Assert.IsFalse(
            game.AddedComponents.Any(
                component => component is KitbashSelectionOverlayComponent));
        Assert.IsFalse(
            game.AddedComponents.Any(
                component => component is KitbashModelGizmoComponent));
    }

    [TestMethod]
    public void ApplicationComposition_DoesNotRegisterGlobalGameComponents()
    {
        var provider = new DependencyInjectionConfig(false).Build(true);
        try
        {
            using var scope = provider.CreateScope();

            Assert.AreEqual(
                0,
                scope.ServiceProvider
                    .GetServices<IGameComponent>()
                    .Count());
        }
        finally
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    private static View3DCoreComponentSet CreateCoreComponentSet()
    {
        return new View3DCoreComponentSet(
            CreateUninitialized<CommandStackRenderer>(),
            Mock.Of<IKeyboardComponent>(),
            Mock.Of<IMouseComponent>(),
            CreateUninitialized<FpsComponent>(),
            CreateUninitialized<ArcBallCamera>(),
            CreateUninitialized<NavigationGizmoComponent>(),
            CreateUninitialized<SceneManager>(),
            CreateUninitialized<RenderEngineComponent>(),
            CreateUninitialized<GridComponent>(),
            new AnimationsContainerComponent(),
            CreateUninitialized<LightControllerComponent>());
    }

    private static T CreateUninitialized<T>() where T : class
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    private sealed class TestGameComponent : IGameComponent
    {
        public void Initialize()
        {
        }
    }

    private sealed class RecordingWpfGame : IWpfGame
    {
        public List<IGameComponent> AddedComponents { get; } = [];
        public int ForceEnsureCreatedCallCount { get; private set; }
        public ContentManager Content { get; set; } = null!;
        public GraphicsDevice GraphicsDevice => null!;

        public T AddComponent<T>(T component)
            where T : IGameComponent
        {
            AddedComponents.Add(component);
            return component;
        }

        public void ForceEnsureCreated()
        {
            ForceEnsureCreatedCallCount++;
        }

        public FrameworkElement GetFocusElement()
        {
            return null!;
        }

        public void RemoveComponent<T>(T component)
            where T : IGameComponent
        {
        }
    }
}
