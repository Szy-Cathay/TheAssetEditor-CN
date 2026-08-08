using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using Editors.KitbasherEditor.ChildEditors.PhotoStudio;
using Editors.KitbasherEditor.ChildEditors.PinTool;
using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using Editors.KitbasherEditor.ChildEditors.ReRiggingTool;
using Editors.KitbasherEditor.ChildEditors.VertexDebugger;
using Editors.KitbasherEditor.Components;
using Editors.KitbasherEditor.Commands;
using Editors.KitbasherEditor.Core;
using Editors.KitbasherEditor.Core.MenuBarViews;
using Editors.KitbasherEditor.EventHandlers;
using Editors.KitbasherEditor.Services;
using Editors.KitbasherEditor.UiCommands;
using Editors.KitbasherEditor.ViewModels;
using Editors.KitbasherEditor.ViewModels.PinTool;
using Editors.KitbasherEditor.ViewModels.SaveDialog;
using Editors.KitbasherEditor.ViewModels.SceneExplorer;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.MeshSubViews;
using Editors.KitbasherEditor.ViewModels.SceneExplorer.Nodes.Rmv2;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using KitbasherEditor.ViewModels.MenuBarViews;
using KitbasherEditor.ViewModels.SaveDialog;
using KitbasherEditor.ViewModels.SceneExplorerNodeViews;
using KitbasherEditor.Views;
using Microsoft.Extensions.DependencyInjection;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Commands.Edge;
using GameWorld.Core.Commands.Face;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Services;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Serialization;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Geometry;
using GameWorld.Core.Services.SceneSaving.Geometry.Strategies;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Services.SceneSaving.Lod.Strategies;
using GameWorld.Core.Services.SceneSaving.Material;
using GameWorld.Core.Services.SceneSaving.Material.Strategies;
using Shared.Core.DependencyInjection;
using Shared.Core.DevConfig;
using Shared.Core.ToolCreation;
using Shared.Ui.Common.MenuSystem;

namespace Editors.KitbasherEditor
{
    public class DependencyInjectionContainer : DependencyContainer
    {
        public override void Register(IServiceCollection serviceCollection)
        {
            // Creators
            serviceCollection.AddScoped<KitbashSceneCreator>();
            serviceCollection.AddScoped<KitbashSelectionOverlayComponent>();
            serviceCollection.AddScoped<KitbashSelectionInputComponent>();
            serviceCollection.AddScoped<KitbashModelGizmoComponent>();
            serviceCollection.AddScoped<ViewOnlySelectedService>();
            serviceCollection.AddScoped<FaceEditor>();
            serviceCollection.AddScoped<ObjectEditor>();
            serviceCollection.AddScoped<GeometrySaveSettings>();
            serviceCollection.AddScoped<MeshBuilderService>();
            serviceCollection.AddTransient<WsModelGeneratorService>();
            serviceCollection.AddTransient<MaterialToWsMaterialFactory>();
            serviceCollection.AddScoped<SaveService>();
            serviceCollection.AddScoped<NodeToRmvSaveHelper>();
            serviceCollection.AddScoped<GeometryStrategyProvider>();
            serviceCollection.AddScoped<IGeometryStrategy, NoMeshStrategy>();
            serviceCollection.AddScoped<IGeometryStrategy, Rmw6Strategy>();
            serviceCollection.AddScoped<IGeometryStrategy, Rmw7Strategy>();
            serviceCollection.AddScoped<IGeometryStrategy, Rmw8Strategy>();
            serviceCollection.AddScoped<LodStrategyProvider>();
            serviceCollection.AddScoped<
                ILodGenerationStrategy,
                AssetEditorLodGeneration>();
            serviceCollection.AddScoped<
                ILodGenerationStrategy,
                Lod0ForAllLodGeneration>();
            serviceCollection.AddScoped<
                ILodGenerationStrategy,
                NoLodGeneration>();
            serviceCollection.AddScoped<MaterialStrategyProvider>();
            serviceCollection.AddScoped<
                IMaterialStrategy,
                Warhammer3WsModelStrategy>();
            serviceCollection.AddScoped<
                IMaterialStrategy,
                Warhammer2WsModelStrategy>();
            serviceCollection.AddScoped<
                IMaterialStrategy,
                PharaohWsModelStrategy>();
            serviceCollection.AddScoped<
                IMaterialStrategy,
                NoWsModelStrategy>();

            serviceCollection.AddTransient<ConvertFacesToVertexSelectionCommand>();
            serviceCollection.AddTransient<FaceSelectionCommand>();
            serviceCollection.AddTransient<EdgeSelectionCommand>();
            serviceCollection.AddTransient<DuplicateFacesCommand>();
            serviceCollection.AddTransient<VertexSelectionCommand>();
            serviceCollection.AddTransient<DeleteFaceCommand>();
            serviceCollection.AddTransient<DeleteObjectsCommand>();
            serviceCollection.AddTransient<MakeNodeEditableCommand>();
            serviceCollection.AddTransient<SortSceneNodesCommand>();
            serviceCollection.AddTransient<
                GameWorld.Core.Commands.Object.ReduceMeshCommand>();
            serviceCollection.AddTransient<TransformVertexCommand>();
            serviceCollection.AddTransient<CombineMeshCommand>();
            serviceCollection.AddTransient<DivideObjectIntoSubmeshesCommand>();
            serviceCollection.AddTransient<
                GameWorld.Core.Commands.Object.DuplicateObjectCommand>();
            serviceCollection.AddTransient<CreateStaticMeshFromAnimationCommand>();
            serviceCollection.AddTransient<AddObjectsToGroupCommand>();
            serviceCollection.AddTransient<UnGroupObjectsCommand>();
            serviceCollection.AddTransient<GroupObjectsCommand>();
            serviceCollection.AddTransient<GrowMeshCommand>();

            // View models 
            serviceCollection.AddScoped<KitbasherView>();
            serviceCollection.AddScoped<KitbasherViewModel>();
            serviceCollection.AddScoped<IEditorInterface, KitbasherViewModel>();
            serviceCollection.AddScoped<SceneExplorerViewModel>();
            serviceCollection.AddTransient<SceneExplorerContextMenuHandler>();
            serviceCollection.AddScoped<AnimationControllerViewModel>();

            // View models - scene node editors
            serviceCollection.AddScoped<SceneNodeEditorViewModel>();
            serviceCollection.AddScoped<ISceneNodeEditorFactory, SceneNodeEditorFactory>();
            serviceCollection.AddScoped<SceneNodePropertyEditor>();

            // Commands
            serviceCollection.AddTransient<AssignMaterialFromOtherMeshCommand>();
            serviceCollection.AddTransient<ConstructPrimitiveCommand>();
            serviceCollection.AddTransient<PrimitiveConstructor>();

            // Mesh fitter
            RegisterWindow<MeshFitterWindow>(serviceCollection);
            serviceCollection.AddTransient<MeshFitterViewModel>();

            // Re-Rigging
            serviceCollection.AddTransient<ReRiggingViewModel>();
            RegisterWindow<ReRiggingWindow>(serviceCollection);

            // Vertex debugger
            serviceCollection.AddScoped<VertexDebuggerViewModel>();
            RegisterWindow<VertexDebuggerWindow>(serviceCollection);

            // Pin tool
            serviceCollection.AddTransient<PinToolViewModel>();
            RegisterWindow<PinToolWindow>(serviceCollection);
            serviceCollection.AddTransient<PinMeshToVertexCommand>();
            serviceCollection.AddTransient<SkinWrapRiggingCommand>();

            // Photo Studio
            serviceCollection.AddScoped<PhotoStudioViewModel>();
            RegisterWindow<PhotoStudioWindow>(serviceCollection);

            // Save dialog
            serviceCollection.AddTransient<SaveDialogViewModel>();
            RegisterWindow<SaveDialogWindow>(serviceCollection);

            // Menubar 
            serviceCollection.AddScoped<TransformToolViewModel>();
            serviceCollection.AddScoped<MenuBarViewModel>();
            serviceCollection.AddScoped<MenuItemVisibilityRuleEngine>();

            // Misc
            serviceCollection.AddScoped<WindowKeyboard>();
            serviceCollection.AddScoped<KitbashViewDropHandler>();
            serviceCollection.AddScoped<KitbasherRootScene>();

            // Event handlers
            serviceCollection.AddScoped<SkeletonChangedHandler>();

            // Commands
            RegisterAllAsOriginalType<ITransientKitbasherUiCommand>(serviceCollection, ServiceLifetime.Transient);
            RegisterAllAsOriginalType<IScopedKitbasherUiCommand>(serviceCollection, ServiceLifetime.Scoped);
            serviceCollection.AddTransient<CopyTexturesToPackCommand>();
            serviceCollection.AddTransient<ImportReferenceMeshCommand>();

            RegisterAllAsInterface<IDeveloperConfiguration>(serviceCollection, ServiceLifetime.Transient);

            // Commands
            serviceCollection.AddTransient<RemapBoneIndexesCommand>();
        }

        public override void RegisterTools(IEditorDatabase factory)
        {
            EditorInfoBuilder
                .Create<KitbasherViewModel, KitbasherView>(EditorEnums.Kitbash_Editor)
                .AddExtention(".rigid_model_v2", EditorPriorites.High)
                .AddExtention(".variantmeshdefinition", EditorPriorites.Default)
                .AddExtention(".wsmodel", EditorPriorites.High)
                .Build(factory);
        }
    }
}
