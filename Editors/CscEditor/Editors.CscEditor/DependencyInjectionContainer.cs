using Editors.CscEditor.Services;
using Editors.CscEditor.ViewModels;
using Editors.CscEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.ToolCreation;

namespace Editors.CscEditor
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            // Views
            serviceCollection.AddTransient<CscEditorView>();

            // ViewModels
            serviceCollection.AddScoped<CscEditorViewModel>();
            serviceCollection.AddScoped<IEditorInterface, CscEditorViewModel>();

            // Services
            serviceCollection.AddScoped<CscPlaybackContext>();
            serviceCollection.AddScoped<CscSceneGraphBuilder>();

            // Game components (picked up by IComponentInserter)
            RegisterGameComponent<CscAnimationComponent>(serviceCollection);
            RegisterGameComponent<CscGizmoComponent>(serviceCollection);
        }

        public override void RegisterTools(IEditorDatabase editorDatabase)
        {
            EditorInfoBuilder
                .Create<CscEditorViewModel, CscEditorView>(EditorEnums.Csc_Editor)
                .AddExtention(".csc", EditorPriorites.Default)
                .Build(editorDatabase);
        }
    }
}
