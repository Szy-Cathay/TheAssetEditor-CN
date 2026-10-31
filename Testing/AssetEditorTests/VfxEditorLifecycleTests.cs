using System.Reflection;
using System.Windows.Threading;
using Editors.VfxEditor;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Rendering;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Vfx;

namespace AssetEditorTests;

[TestClass]
[DoNotParallelize]
public class VfxEditorLifecycleTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    [TestMethod]
    public void ScopeDisposalClosesEditorBeforeDependenciesAndLaterCloseIsHarmless() =>
        WpfTestApplicationHost.Invoke(application =>
        {
            using var context = new Context();
            var editor = context.Editor;
            editor.PreviewQuality = "1";
            Assert.IsTrue(Timer(editor).IsEnabled);
            Assert.IsTrue(context.Store.TryGet(context.File, out _));
            context.Scope.Dispose();
            try
            {
                Assert.IsTrue(context.TimerStoppedBeforeRuntimeDisposal,
                    "DI must stop the editor timer before releasing its scene dependencies.");
                Assert.IsTrue(context.SnapshotRemovedBeforeRuntimeDisposal);
                Assert.IsTrue(context.EditorUnsubscribedBeforeRuntimeDisposal);
                Assert.IsTrue(context.PlaybackUnsubscribedBeforeRuntimeDisposal);
                CollectionAssert.AreEqual(new[] { "runtime", "game" }, context.DisposalOrder);
                Assert.IsFalse(Timer(editor).IsEnabled);
                Assert.IsFalse(context.Store.TryGet(context.File, out _));
                editor.Close();
                context.Scene.Stop();
                context.Scene.Dispose();
                context.Component.Dispose();
                context.Runtime.Verify(value => value.Dispose(), Times.Once);
                Assert.IsFalse(Timer(editor).IsEnabled);
            }
            finally { editor.Close(); }
        });

    [TestMethod]
    public void ClosedEditorIgnoresQueuedRefreshAndCannotRestartItsTimer() =>
        WpfTestApplicationHost.Invoke(application =>
        {
            using var context = new Context();
            var editor = context.Editor;
            context.Scope.Dispose();
            editor.Close();
            try { Refresh(editor); }
            catch (TargetInvocationException exception)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException!).Throw();
            }
            editor.PreviewQuality = "2";
            Assert.IsFalse(Timer(editor).IsEnabled,
                "A late binding or event must not restart the closed editor's refresh timer.");
            Assert.IsFalse(context.Store.TryGet(context.File, out _),
                "A queued callback must not republish a closed editor snapshot.");
            Assert.IsFalse(Timer(editor).IsEnabled);
            context.Runtime.Verify(value => value.Reload(It.IsAny<VfxPreviewSnapshot>()), Times.Once);
            context.Runtime.Verify(value => value.Dispose(), Times.Once);
        });

    private static DispatcherTimer Timer(VfxEditorViewModel editor) => (DispatcherTimer)typeof(VfxEditorViewModel)
        .GetField("_scenePreviewTimer", Private)!.GetValue(editor)!;

    private static void Refresh(VfxEditorViewModel editor) => typeof(VfxEditorViewModel)
        .GetMethod("RefreshScenePreview", Private)!.Invoke(editor, [null, EventArgs.Empty]);

    private static bool HasSubscriber(object source, string eventName, object subscriber) =>
        (typeof(VfxPreviewComponent).GetField(eventName, Private)!.GetValue(source) as Delegate)?
            .GetInvocationList().Any(callback => ReferenceEquals(callback.Target, subscriber)) == true;

    private sealed class Context : IDisposable
    {
        private readonly ServiceProvider _provider;
        public IServiceScope Scope { get; }
        public VfxEditorViewModel Editor { get; }
        public VfxScenePreview Scene { get; }
        public VfxPreviewComponent Component { get; }
        public VfxPreviewDocumentStore Store { get; } = new();
        public Mock<IVfxPreview> Runtime { get; } = new();
        public PackFile File { get; } = PackFile.CreateFromBytes("lifecycle.xml", VfxDocument.CreateComposition().Write());
        public List<string> DisposalOrder { get; } = [];
        public bool TimerStoppedBeforeRuntimeDisposal { get; private set; }
        public bool SnapshotRemovedBeforeRuntimeDisposal { get; private set; }
        public bool EditorUnsubscribedBeforeRuntimeDisposal { get; private set; }
        public bool PlaybackUnsubscribedBeforeRuntimeDisposal { get; private set; }

        public Context()
        {
            var files = new Mock<IPackFileService>();
            var container = new PackFileContainer("lifecycle");
            container.FileList.Add("vfx\\lifecycle.xml", File);
            files.Setup(value => value.GetAllPackfileContainers()).Returns([container]);
            files.Setup(value => value.GetPackFileContainer(File)).Returns(container);
            files.Setup(value => value.GetEditablePack()).Returns(container);
            files.Setup(value => value.GetFullPath(File, It.IsAny<PackFileContainer?>())).Returns("vfx\\lifecycle.xml");
            var localization = new LocalizationManager();
            localization.LoadLanguage();
            Runtime.SetupGet(value => value.Diagnostics).Returns(Array.Empty<VfxPreviewDiagnostic>());
            Runtime.SetupGet(value => value.CycleDuration).Returns(8);
            Runtime.SetupGet(value => value.EmitterCount).Returns(1);
            Runtime.SetupGet(value => value.TotalEmitterCount).Returns(1);
            Runtime.Setup(value => value.BeginInteractiveReload(It.IsAny<VfxPreviewSnapshot>()))
                .Callback<VfxPreviewSnapshot>(snapshot => Runtime.Object.Reload(snapshot)).Returns(true);
            Runtime.Setup(value => value.ContinueInteractiveReload(It.IsAny<VfxInteractiveReloadFrame>())).Returns(true);
            Runtime.Setup(value => value.Dispose()).Callback(() =>
            {
                var editor = Editor ?? throw new InvalidOperationException("The editor must be resolved before runtime disposal.");
                var component = Component ?? throw new InvalidOperationException("The component must be resolved before runtime disposal.");
                DisposalOrder.Add("runtime");
                TimerStoppedBeforeRuntimeDisposal = !Timer(editor).IsEnabled;
                SnapshotRemovedBeforeRuntimeDisposal = !Store.TryGet(File, out _);
                EditorUnsubscribedBeforeRuntimeDisposal = !HasSubscriber(component, nameof(Component.Reloaded), editor);
                PlaybackUnsubscribedBeforeRuntimeDisposal = !HasSubscriber(component, nameof(Component.PlaybackChanged), editor.Playback);
            });
            var factory = new Mock<IVfxPreviewFactory>();
            factory.Setup(value => value.Create(It.IsAny<GraphicsDevice>())).Returns(Runtime.Object);
            var game = new Mock<IWpfGame>();
            game.As<IDisposable>().Setup(value => value.Dispose()).Callback(() => DisposalOrder.Add("game"));
            var mouse = new Mock<IMouseComponent>();
            mouse.SetupProperty(value => value.MouseOwner);
            var services = new ServiceCollection();
            services.AddScoped<IWpfGame>(_ => game.Object);
            services.AddScoped(_ => new VfxPreviewComponent(factory.Object, Mock.Of<IDeviceResolver>(), null!));
            services.AddScoped(provider => new VfxEmitterInteractionComponent(mouse.Object, Mock.Of<IKeyboardComponent>(), null!,
                Mock.Of<IDeviceResolver>(), null!, provider.GetRequiredService<VfxPreviewComponent>()));
            services.AddScoped(provider => new VfxScenePreview(provider.GetRequiredService<IWpfGame>(), Mock.Of<IComponentInserter>(),
                new View3DCoreComponentSet(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!),
                provider.GetRequiredService<VfxPreviewComponent>(), provider.GetRequiredService<VfxEmitterInteractionComponent>()));
            services.AddScoped(provider => new VfxEditorViewModel(files.Object, Mock.Of<IFileSaveService>(),
                Mock.Of<IStandardDialogs>(), Mock.Of<IEditorCreator>(), localization,
                provider.GetRequiredService<VfxScenePreview>(), Store));
            _provider = services.BuildServiceProvider();
            Scope = _provider.CreateScope();
            Editor = Scope.ServiceProvider.GetRequiredService<VfxEditorViewModel>();
            Scene = Scope.ServiceProvider.GetRequiredService<VfxScenePreview>();
            Component = Scope.ServiceProvider.GetRequiredService<VfxPreviewComponent>();
            Editor.LoadFile(File);
            Refresh(Editor);
            Component.Update(new GameTime(TimeSpan.Zero, TimeSpan.Zero));
        }

        public void Dispose()
        {
            Editor.Close();
            Scope.Dispose();
            _provider.Dispose();
        }
    }
}
