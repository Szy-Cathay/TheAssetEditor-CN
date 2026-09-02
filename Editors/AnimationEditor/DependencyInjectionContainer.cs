using AnimationEditor.CampaignAnimationCreator;
using AnimationEditor.CampaignAnimationCreator.Commands;
using AnimationEditor.Common.BaseControl;
using AnimationEditor.MountAnimationCreator;
using Editors.AnimationVisualEditors.AnimationWorkbench;
using Editors.AnimationVisualEditors.ContextMenu;
using Editors.Shared.Core.Common.BaseControl;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace Editors.AnimationVisualEditors
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<CampaignAnimationCreatorViewModel>();
            serviceCollection.AddTransient<ConvertCampaignAnimationCommand>();
            serviceCollection.AddTransient<SaveCampaignAnimationCommand>();

            serviceCollection.AddScoped<EditorHost<MountAnimationCreatorViewModel>>();
            serviceCollection.AddScoped<MountAnimationCreatorViewModel>();
            serviceCollection.AddScoped<MountVertexSelectionComponent>();

            serviceCollection.AddScoped<TrustedAnimationPreviewViewModel>();
            serviceCollection.AddScoped<
                ITrustedAnimationPreviewViewport,
                TrustedAnimationPreviewViewport>();
            serviceCollection.AddScoped<
                ITrustedAnimationModelDiscovery,
                TrustedAnimationModelDiscovery>();
            serviceCollection.AddScoped<
                ITrustedAnimationDiscovery,
                TrustedAnimationDiscovery>();
            serviceCollection.AddTransient<
                IOpenAnimationWorkbenchCommand,
                OpenAnimationWorkbenchCommand>();

            RegisterAllAsInterface<IDeveloperConfiguration>(serviceCollection, ServiceLifetime.Transient);
        }

        public override void RegisterTools(IEditorDatabase database)
        {
            EditorInfoBuilder
                .Create<EditorHost<MountAnimationCreatorViewModel>, EditorHostView>(EditorEnums.MountTool_Editor)
                .AddToToolbar("DisplayName.MountTool", false)
                .Build(database);

            EditorInfoBuilder
              .Create<CampaignAnimationCreatorViewModel, EditorHostView>(EditorEnums.CampaginAnimation_Editor)
              .AddToToolbar("DisplayName.CampaignAnimationTool", true)
              .Build(database);

            EditorInfoBuilder
                .Create<
                    TrustedAnimationPreviewViewModel,
                    TrustedAnimationPreviewView>(
                    EditorEnums.AnimationKeyFrame_Editor)
                .AddToToolbar("DisplayName.AnimationWorkbench", true)
                .ForGames(GameTypeEnum.Warhammer3)
                .Build(database);
        }
    }
}
