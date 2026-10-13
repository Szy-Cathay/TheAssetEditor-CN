using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;
using AssetEditor.Services;
using AssetEditor.Views;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class EditorTabDirtyStateTests
{
    internal static readonly EditorEnums[] Tools =
    [
        EditorEnums.AnimationBatchExporter_Editor, EditorEnums.AnimationRetarget_Editor,
        EditorEnums.AudioExplorer_Editor, EditorEnums.CampaginAnimation_Editor,
        EditorEnums.MountTool_Editor, EditorEnums.VisualSkeletonEditor,
    ];

    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public void ActualTools_InitializeWithoutDirtyDots_AndDocumentTabsStillTrackChanges(ThemeType theme)
    {
        WithServices(services =>
        {
            var previous = ThemesController.CurrentTheme;
            ThemesController.SetTheme(theme);
            var source = new MainWindow(services);
            var tabs = new CachedTabControl
            {
                Style = (Style)Application.Current.FindResource("AeShell.EditorTabs"),
                ItemTemplate = source.EditorsTabControl.ItemTemplate,
            };
            var window = new Window { Content = tabs, Width = 1700, Height = 230,
                Left = -10000, Top = -10000, ShowActivated = false, ShowInTaskbar = false };
            var scopes = new List<IServiceScope>();
            var editors = new List<IEditorInterface>();
            try
            {
                foreach (var tool in Tools)
                {
                    var scope = services.CreateScope();
                    scopes.Add(scope);
                    var registration = services.GetRequiredService<IEditorDatabase>().GetEditorInfos().Single(item => item.EditorEnum == tool);
                    var editor = (IEditorInterface)scope.ServiceProvider.GetRequiredService(registration.ViewModel);
                    editors.Add(editor);
                    tabs.Items.Add(editor);
                    Assert.That(editor is ISaveableEditor saveable && saveable.HasUnsavedChanges, Is.False, tool.ToString());
                }
                window.Show();
                Pump();
                foreach (var editor in editors)
                {
                    tabs.SelectedItem = editor;
                    Pump();
                    Assert.That(Dot(tabs, editor).Visibility, Is.EqualTo(Visibility.Collapsed), editor.DisplayName);
                }
                UiRefinementTests.SaveSnapshot(tabs, $"clean-tools-{theme}.png");

                var document = new DocumentState();
                tabs.Items.Add(document);
                Pump();
                Assert.That(Dot(tabs, document).Visibility, Is.EqualTo(Visibility.Collapsed));
                document.HasUnsavedChanges = true;
                Pump();
                Assert.That(Dot(tabs, document).Visibility, Is.EqualTo(Visibility.Visible));
                Assert.That(document.Save(), Is.False);
                Pump();
                Assert.That(Dot(tabs, document).Visibility, Is.EqualTo(Visibility.Visible));
                document.SaveSucceeds = true;
                Assert.That(document.Save(), Is.True);
                Pump();
                Assert.That(Dot(tabs, document).Visibility, Is.EqualTo(Visibility.Collapsed));
            }
            finally
            {
                window.Close();
                source.Close();
                foreach (var editor in editors)
                    editor.Close();
                foreach (var scope in scopes)
                    scope.Dispose();
                ThemesController.SetTheme(previous);
            }
        });
    }

    internal static void WithServices(Action<IServiceProvider> action,
        Action<IServiceCollection>? configure = null)
    {
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3) { AllowSettingsUpdate = false };
        settings.CurrentSettings.StartMaximised = false;
        var services = new DependencyInjectionConfig(loadResources: false).Build(false, registrations =>
        {
            registrations.AddSingleton(settings);
            registrations.AddScoped<IWpfGame>(_ =>
            {
                var game = new Mock<IWpfGame>();
                game.Setup(item => item.AddComponent(It.IsAny<SceneObject>())).Returns<SceneObject>(item => item);
                return game.Object;
            });
            registrations.AddScoped(_ => Mock.Of<IComponentInserter>());
            registrations.AddScoped(_ => Mock.Of<IKeyboardComponent>());
            registrations.AddScoped(_ => Mock.Of<IMouseComponent>());
            registrations.AddScoped(provider =>
            {
                var arguments = typeof(RenderEngineComponent).GetConstructors().Single().GetParameters()
                    .Select(parameter => provider.GetRequiredService(parameter.ParameterType)).ToArray();
                var rendering = new Mock<RenderEngineComponent>(arguments);
                var events = provider.GetRequiredService<IEventHub>();
                // State-only fixtures never allocate the GPU resources disposed by the concrete renderer.
                rendering.As<IDisposable>().Setup(item => item.Dispose())
                    .Callback(() => events.UnRegister(rendering.Object));
                return rendering.Object;
            });
            registrations.AddScoped(_ => Mock.Of<IStandardDialogs>());
            configure?.Invoke(registrations);
        });
        try
        {
            WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
            {
                services.GetRequiredService<LocalizationManager>().LoadLanguage();
                action(services);
            });
        }
        finally { (services as IDisposable)?.Dispose(); }
    }

    private static Ellipse Dot(CachedTabControl tabs, object editor)
    {
        var tab = (TabItem)tabs.ItemContainerGenerator.ContainerFromItem(editor);
        return UiRefinementTests.Descendants<Ellipse>(tab).Single();
    }
    private static void Pump() => Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private sealed class DocumentState : CommunityToolkit.Mvvm.ComponentModel.ObservableObject, IEditorInterface, ISaveableEditor
    {
        private bool _dirty;
        public string DisplayName { get; set; } = "文档状态回归";
        public bool HasUnsavedChanges { get => _dirty; set => SetProperty(ref _dirty, value); }
        public bool SaveSucceeds { get; set; }
        public bool Save()
        {
            if (SaveSucceeds)
                HasUnsavedChanges = false;
            return SaveSucceeds;
        }
        public void Close() { }
    }
}
