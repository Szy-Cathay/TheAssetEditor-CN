using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Bone.Clipboard;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Navigation;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using GameWorld.Core.WpfWindow;
using GameWorld.Core.WpfWindow.FactionColourSettings;
using Microsoft.Extensions.DependencyInjection;
using Shared.Core.DependencyInjection;
using Shared.Core.Services;

namespace GameWorld.Core
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            // Graphics scene
            serviceCollection.AddScoped<IGeometryGraphicsContextFactory, GeometryGraphicsContextFactory>();
            serviceCollection.AddScoped<IWpfGame, WpfGame>();
            serviceCollection.AddScoped<IScopedResourceLibrary, ScopedResourceLibrary>();
            
            serviceCollection.AddSingleton<ResourceLibrary>();

            // Settings
            serviceCollection.AddScoped<SceneRenderParametersStore>();
            serviceCollection.AddSingleton<FactionColourSettingsService>();
            serviceCollection.AddTransient<
                IFactionColourSettingsDialogService,
                FactionColourSettingsDialogService>();

            // Services
            serviceCollection.AddSingleton<ISkeletonAnimationLookUpHelper, SkeletonAnimationLookUpHelper>();
            serviceCollection.AddScoped<FocusSelectableObjectService>();
            serviceCollection.AddScoped<ComplexMeshLoader>();
            serviceCollection.AddScoped<Rmv2ModelNodeLoader>();

            // Shader
            serviceCollection.AddScoped<CapabilityMaterialFactory>(); 

            // Resolvers - sort of hacks 
            serviceCollection.AddScoped<IDeviceResolver, DeviceResolver>();

            // Components
            RegisterComponents(serviceCollection);

            // Commands
            RegisterCommands(serviceCollection);
        }

        void RegisterComponents(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<IComponentInserter, ComponentInserter>();
            serviceCollection.AddScoped<View3DCoreComponentSet>();
            serviceCollection.AddScoped<CommandStackRenderer>();
            serviceCollection.AddScoped<IKeyboardComponent, KeyboardComponent>();
            serviceCollection.AddScoped<IMouseComponent, MouseComponent>();

            serviceCollection.AddScoped<FpsComponent>();
            serviceCollection.AddScoped<ArcBallCamera>();
            serviceCollection.AddScoped<NavigationGizmoComponent>();
            serviceCollection.AddScoped<SceneManager>();
            serviceCollection.AddScoped<SelectionManager>();
            serviceCollection.AddScoped<ReferenceObjectSelectionComponent>();
            serviceCollection.AddScoped<ReferenceObjectSelectionOutlineComponent>();
            serviceCollection.AddScoped<BoneSelectionHighlightComponent>();
            serviceCollection.AddScoped<RenderEngineComponent>();
            serviceCollection.AddScoped<GridComponent>();
            serviceCollection.AddScoped<AnimationsContainerComponent>();
            serviceCollection.AddScoped<LightControllerComponent>();

            //serviceCollection.AddScoped<ISceneLightParameters>(x => x.GetRequiredService<LightControllerComponent>());
        }

        void RegisterCommands(IServiceCollection serviceCollection)
        {
            serviceCollection.AddScoped<CommandExecutor>();
            serviceCollection.AddScoped<CommandFactory>();

            serviceCollection.AddTransient<ObjectSelectionCommand>();
            serviceCollection.AddTransient<CreateAnimatedMeshPoseCommand>();
            serviceCollection.AddTransient<ObjectSelectionModeCommand>();

            serviceCollection.AddTransient<BoneSelectionCommand>();
            serviceCollection.AddTransient<TransformBoneCommand>();
            serviceCollection.AddTransient<ResetTransformBoneCommand>();
            serviceCollection.AddTransient<PasteWholeTransformBoneCommand>();
            serviceCollection.AddTransient<PasteIntoSelectedBonesTransformBoneCommand>();
            serviceCollection.AddTransient<PasteIntoSelectedBonesInRangeTransformFromClipboardBoneCommand>();
            serviceCollection.AddTransient<PasteIntoSelectedBonesTransformFromClipboardBoneCommand>();
            serviceCollection.AddTransient<PasteWholeInRangeTransformFromClipboardBoneCommand>();
            serviceCollection.AddTransient<PasteWholeTransformFromClipboardBoneCommand>();
            serviceCollection.AddTransient<DuplicateFrameBoneCommand>();
            serviceCollection.AddTransient<DeleteFrameBoneCommand>();
            serviceCollection.AddTransient<InterpolateFramesBoneCommand>();
            serviceCollection.AddTransient<InterpolateFramesSelectedBonesBoneCommand>();
        }


    }
}
