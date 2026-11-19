using AssetEditor.Services;
using AssetEditor.Services.Settings;
using AssetEditor.ModAssistant;
using Shared.Core.ModAssistant;
using System.Net.Http;
using AssetEditor.UiCommands;
using AssetEditor.ViewModels;
using AssetEditor.Views;
using AssetEditor.Views.FolderProjectHistory;
using AssetEditor.Views.Settings;
using AssetEditor.Views.Updater;
using Editors.Ipc;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Shared.Core.ErrorHandling.Exceptions;
using Shared.Core.Events.Global;
using Shared.Core.ToolCreation;

namespace AssetEditor
{
    internal class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<IAssistantSecretStore, AssistantSecretStore>();
            serviceCollection.AddSingleton(_ => new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan });
            serviceCollection.AddScoped<IAssistantModelClient, DeepSeekClient>();
            serviceCollection.AddScoped<AssistantWorkspace>();
            serviceCollection.AddScoped<IAssistantWorkspace>(services => services.GetRequiredService<AssistantWorkspace>());
            serviceCollection.AddSingleton<AssistantSelectionContext>();
            serviceCollection.AddScoped<AssistantViewModel>();
            serviceCollection.AddTransient<AssistantView>();
            serviceCollection.AddTransient<AssistantSettingsViewModel>();
            serviceCollection.AddScoped<MainWindow>();
            serviceCollection.AddScoped<MainViewModel>();
            serviceCollection.AddSingleton<IEditorCreator>(x => x.GetRequiredService<IEditorManager>());
            serviceCollection.AddSingleton<IEditorManager, EditorManager>();
            serviceCollection.AddSingleton<
                IFolderProjectUnsavedChangesService,
                FolderProjectUnsavedChangesService>();
            serviceCollection.AddSingleton<
                IFolderProjectUnsavedChangesPrompt,
                FolderProjectUnsavedChangesPrompt>();
            serviceCollection.AddTransient<
                IExternalPackOpenChoiceDialog,
                ExternalPackOpenChoiceDialog>();
            serviceCollection.AddTransient<
                IExternalPackOpenWorkflow,
                ExternalPackOpenWorkflow>();

            serviceCollection.AddTransient<OpenGamePackCommand>();
            serviceCollection.AddTransient<OpenReferencePackCommand>();
            serviceCollection.AddTransient<CreateFolderProjectCommand>();
            serviceCollection.AddTransient<OpenFolderProjectCommand>();
            serviceCollection.AddTransient<OpenFolderProjectHistoryCommand>();
            serviceCollection.AddTransient<ImportPackAsFolderProjectCommand>();
            serviceCollection.AddTransient<OpenSettingsDialogCommand>();
            serviceCollection.AddTransient<OpenUpdaterWindowCommand>();
            serviceCollection.AddTransient<OpenWebpageCommand>();
            serviceCollection.AddTransient<PrintScopesCommand>();
            serviceCollection.AddTransient<OpenEditorCommand>();
            serviceCollection.AddTransient<TogglePackFileExplorerCommand>();

            serviceCollection.AddTransient<SettingsWindow>();
            serviceCollection.AddTransient<SettingsViewModel>();
            serviceCollection.AddTransient<ApplicationSettingsApplier>();
            serviceCollection.AddTransient<UpdaterWindow>();
            serviceCollection.AddTransient<UpdaterViewModel>();
            serviceCollection.AddTransient<
                FolderProjectHistoryWindow>();
            serviceCollection.AddScoped<FolderProjectHistoryViewModel>();
            serviceCollection.AddScoped<
                FolderProjectHistoryWorkspaceViewModel>();
            serviceCollection.AddScoped<MenuBarViewModel>();

            serviceCollection.AddScoped<MainWindow>();

            serviceCollection.AddSingleton<RecentFilesTracker>();
            serviceCollection.AddSingleton<
                IFolderProjectGitOperationCoordinator,
                FolderProjectGitOperationCoordinator>();
            serviceCollection.AddScoped<
                IFolderProjectOpenService,
                FolderProjectOpenService>();
            serviceCollection.AddScoped<
                IFolderProjectHistoryWindowService,
                FolderProjectHistoryWindowService>();
            serviceCollection.AddScoped<
                IFolderProjectCloseGuard,
                FolderProjectCloseGuard>();

            serviceCollection.AddScoped<IExceptionInformationProvider, CurrentEditorExceptionInfoProvider>();

            RegisterAllAsInterface<IDeveloperConfiguration>(serviceCollection, ServiceLifetime.Transient);
        }

        public override void RegisterTools(IEditorDatabase database) =>
            EditorInfoBuilder.Create<AssistantViewModel, AssistantView>(EditorEnums.ModAssistant)
                .AddToToolbar("Assistant.Title").Build(database);
    }
}
