using System.Data;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.AnimationMeta.SuperView.Visualisation.Rules;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    public interface IMetaDataBuilder
    {
        bool EnableInteractiveVfxPreview { get => false; set { } }
        EffectPreviewContext EffectPreviewContext { get => new(); set { } }
        void InvalidateEffectPreviewResources() { }
        bool DependsOnEffectPreviewResource(PackFile file) => false;

        MetaDataBuildResult Build(
            ParsedMetadataFile? persistent,
            ParsedMetadataFile? metaData,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            SceneNode root,
            ISkeletonProvider skeleton,
            AnimationPlayer rootPlayer,
            IAnimationBinGenericFormat? fragment);
        MetaDataPreviewBuildResult BuildPreview(
            ParsedMetadataAttribute attribute,
            MetaDataDocumentOwner owner,
            bool isSelected,
            SceneNode root,
            ISkeletonProvider skeleton,
            AnimationPlayer rootPlayer);
    }

    public class MetaDataBuilder : IMetaDataBuilder
    {
        private readonly MetaDataPreviewBuilder _previewBuilder;
        private readonly MetaDataRuleBuilder _ruleBuilder;
        public EffectPreviewContext EffectPreviewContext
        {
            get => _previewBuilder.EffectContext;
            set
            {
                _previewBuilder.EffectContext = value;
            }
        }
        public void InvalidateEffectPreviewResources() => _previewBuilder.EffectResources.Invalidate();
        public bool DependsOnEffectPreviewResource(PackFile file) => _previewBuilder.EffectResources.DependsOn(file);
        public bool EnableInteractiveVfxPreview
        {
            get => _previewBuilder.EnableInteractiveVfxPreview;
            set => _previewBuilder.EnableInteractiveVfxPreview = value;
        }

        public MetaDataBuilder(
            ComplexMeshLoader complexMeshLoader,
            RenderEngineComponent resourceLibrary,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            IPackFileService packFileService,
            AnimationsContainerComponent animationsContainerComponent,
            IVfxPreviewFactory? vfxFactory = null,
            IDeviceResolver? devices = null,
            VfxPreviewDocumentStore? liveDocuments = null,
            SceneRenderParametersStore? renderParameters = null)
        {
            var resourceResolver = new MetaDataResourceResolver(
                packFileService);
            _previewBuilder = new(
                complexMeshLoader,
                resourceLibrary,
                skeletonAnimationLookUpHelper,
                resourceResolver,
                animationsContainerComponent,
                vfxFactory, devices, liveDocuments, renderParameters);
            _ruleBuilder = new(resourceResolver);
        }

        public MetaDataBuildResult Build(
            ParsedMetadataFile? persistent,
            ParsedMetadataFile? metaData,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            SceneNode root,
            ISkeletonProvider skeleton,
            AnimationPlayer rootPlayer,
            IAnimationBinGenericFormat? fragment)
        {
            _previewBuilder.EffectResources.Invalidate();
            var instances = new List<IMetaDataInstance>();
            var rules = new List<GameWorld.Core.Animation.AnimationChange.IAnimationChangeRule>();
            var diagnostics = new List<MetaDataBuildDiagnostic>();

            if (metaData == null ||
                metaData.GetItemsOfType<DisablePersistant_v10>().Count == 0)
            {
                _previewBuilder.AppendInstances(
                    persistent,
                    MetaDataDocumentOwner.Persistent,
                    selectedMetaDataAttribute,
                    root,
                    skeleton,
                    rootPlayer,
                    instances,
                    diagnostics);
                _ruleBuilder.AppendRules(
                    persistent,
                    MetaDataDocumentOwner.Persistent,
                    fragment,
                    skeleton,
                    rules,
                    diagnostics);
            }

            _previewBuilder.AppendInstances(
                metaData,
                MetaDataDocumentOwner.Animation,
                selectedMetaDataAttribute,
                root,
                skeleton,
                rootPlayer,
                instances,
                diagnostics);
            _ruleBuilder.AppendRules(
                metaData,
                MetaDataDocumentOwner.Animation,
                fragment,
                skeleton,
                rules,
                diagnostics);

            return new(instances, rules, diagnostics);
        }

        public MetaDataPreviewBuildResult BuildPreview(
            ParsedMetadataAttribute attribute,
            MetaDataDocumentOwner owner,
            bool isSelected,
            SceneNode root,
            ISkeletonProvider skeleton,
            AnimationPlayer rootPlayer) =>
            _previewBuilder.BuildPreview(
                attribute,
                owner,
                isSelected,
                root,
                skeleton,
                rootPlayer);
    }

    internal sealed class MetaDataPreviewBuilder
    {
        private readonly ILogger _logger = Logging.Create<MetaDataPreviewBuilder>();
        private readonly ComplexMeshLoader _complexMeshLoader;
        private readonly RenderEngineComponent _resourceLibrary;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly MetaDataResourceResolver _resourceResolver;
        private readonly AnimationsContainerComponent _animationsContainerComponent;
        private readonly IVfxPreviewFactory? _vfxFactory;
        private readonly IDeviceResolver? _devices;
        private readonly VfxPreviewDocumentStore? _liveDocuments;
        private readonly SceneRenderParametersStore? _renderParameters;
        internal EffectPreviewContext EffectContext { get; set; } = new();
        internal EffectMetaResourceResolver EffectResources { get; }
        public bool EnableInteractiveVfxPreview { get; set; }

        private static Color s_color = Color.Black;
        private static Color s_selectedColor = Color.Red;

        private static readonly Color s_impactColor = new(226, 180, 95);
        private static readonly Color s_targetColor = new(100, 169, 226);
        private static readonly Color s_fireColor = new(225, 121, 121);
        private static readonly Color s_splashColor = new(220, 141, 85);
        private static readonly Color s_effectColor = new(180, 135, 232);
        private static readonly Color s_crewLocationColor = new(114, 188, 145);

        private sealed record PropPreviewData(
            ParsedMetadataAttribute Source,
            string ModelName,
            string AnimationName,
            float StartTime,
            float EndTime,
            int BoneId,
            Vector3 Position,
            Vector4 Orientation,
            float Scale,
            IThreeKingdomsAnimatedPropWindMeta? Wind = null);

        private sealed record SplashPreviewData(
            ParsedMetadataAttribute Source,
            Vector3 StartPosition,
            Vector3 EndPosition,
            int AoeShape,
            float WidthForCorridor,
            float AngleForCone);

        public MetaDataPreviewBuilder(ComplexMeshLoader complexMeshLoader,
            RenderEngineComponent resourceLibrary,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            MetaDataResourceResolver resourceResolver,
            AnimationsContainerComponent animationsContainerComponent,
            IVfxPreviewFactory? vfxFactory = null,
            IDeviceResolver? devices = null,
            VfxPreviewDocumentStore? liveDocuments = null,
            SceneRenderParametersStore? renderParameters = null)
        {
            _complexMeshLoader = complexMeshLoader;
            _resourceLibrary = resourceLibrary;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _resourceResolver = resourceResolver;
            EffectResources = new(resourceResolver);
            _animationsContainerComponent = animationsContainerComponent;
            _vfxFactory = vfxFactory;
            _devices = devices;
            _liveDocuments = liveDocuments;
            _renderParameters = renderParameters;
        }

        public void AppendInstances(
            ParsedMetadataFile? file,
            MetaDataDocumentOwner owner,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            SceneNode root,
            ISkeletonProvider skeleton,
            AnimationPlayer rootPlayer,
            ICollection<IMetaDataInstance> output,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            ApplyMetaData(
                file,
                owner,
                selectedMetaDataAttribute,
                root,
                skeleton,
                rootPlayer,
                output,
                diagnostics);
        }

        private void ApplyMetaData(
            ParsedMetadataFile? file,
            MetaDataDocumentOwner owner,
            ParsedMetadataAttribute? selectedAttribute,
            SceneNode root,
            ISkeletonProvider skeleton,
            AnimationPlayer rootPlayer,
            ICollection<IMetaDataInstance> output,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            if (file == null)
                return;

            foreach (var animatedProp in file.GetItemsOfType<IAnimatedPropMeta>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    (ParsedMetadataAttribute)animatedProp,
                    root,
                    () => CreateAnimatedProp(
                        animatedProp,
                        root,
                        skeleton,
                        selectedAttribute,
                        rootPlayer,
                        owner,
                        diagnostics));

            foreach (var attribute in file.Attributes
                .OfType<ParsedMetadataAttribute>())
            {
                if (TryGetOrdinaryPropData(attribute, out var propData))
                {
                    TryAddPreview(
                        output,
                        diagnostics,
                        owner,
                        attribute,
                        root,
                        () => CreateOrdinaryProp(
                            propData,
                            root,
                            skeleton,
                            selectedAttribute,
                            rootPlayer,
                            owner,
                            diagnostics));
                }
            }

            foreach (var item in file.GetItemsOfType<ImpactPosition_v2>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "ImpactPos",
                        CombatMetaDataPreviewCategory.Impact,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<ImpactPosition_v10>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "ImpactPos",
                        CombatMetaDataPreviewCategory.Impact,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<TargetPos_0>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "TargetPos",
                        CombatMetaDataPreviewCategory.Target,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<TargetPos_10>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "TargetPos",
                        CombatMetaDataPreviewCategory.Target,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<FirePos_v0>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateFireLocator(
                        item,
                        root,
                        item.Position,
                        skeleton,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<FirePos_v2>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateFireLocator(
                        item,
                        root,
                        item.Position,
                        skeleton,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<FirePos_v10>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateFireLocator(
                        item,
                        root,
                        item.Position,
                        skeleton,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<SplashAttack_v3>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateSplashAttack(
                        CreateSplashPreviewData(item),
                        root,
                        $"SplashAttack_{Math.Round(item.EndTime, 2)}",
                        0.1f,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<SplashAttack_v10>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    item,
                    root,
                    () => CreateSplashAttack(
                        CreateSplashPreviewData(item),
                        root,
                        $"SplashAttack_{Math.Round(item.EndTime, 2)}",
                        0.1f,
                        selectedAttribute,
                        rootPlayer));

            foreach (var item in file.GetItemsOfType<IEffectMeta>())
                TryAddPreview(
                    output,
                    diagnostics,
                    owner,
                    (ParsedMetadataAttribute)item,
                    root,
                    () => CreateEffect(
                        item,
                        root,
                        skeleton,
                        selectedAttribute,
                        owner,
                        diagnostics,
                        rootPlayer));

            foreach (var attribute in file.Attributes
                .OfType<ParsedMetadataAttribute>())
            {
                if (SpatialMetaDataCatalog.TryCreate(
                        attribute,
                        out var spatialBinding) &&
                    spatialBinding.UsesGenericMarker)
                {
                    TryAddPreview(
                        output,
                        diagnostics,
                        owner,
                        attribute,
                        root,
                        () => CreateSpatialMarker(
                            spatialBinding,
                            root,
                            skeleton,
                            selectedAttribute));
                }

                if (IsBoneOnlyMetaData(attribute))
                {
                    TryAddPreview(
                        output,
                        diagnostics,
                        owner,
                        attribute,
                        root,
                        () =>
                        {
                            TryCreateBoneOnlyPreview(
                                attribute,
                                root,
                                skeleton,
                                selectedAttribute,
                                out var bonePreview);
                            if (bonePreview == null)
                            {
                                diagnostics.Add(
                                    MetaDataDiagnosticFactory.Create(
                                        attribute,
                                        owner,
                                        "SuperView.Diagnostics.MissingBone"));
                            }
                            return bonePreview;
                        });
                }
            }
        }

        public MetaDataPreviewBuildResult BuildPreview(
            ParsedMetadataAttribute attribute,
            MetaDataDocumentOwner owner,
            bool isSelected,
            SceneNode root,
            ISkeletonProvider skeleton,
            AnimationPlayer rootPlayer)
        {
            var diagnostics = new List<MetaDataBuildDiagnostic>();
            if (attribute is Transform_v10 or DockEquipment or DockEquipment_v3)
                return new(false, null, diagnostics);

            var existingChildren = root.Children.ToHashSet();
            Func<IMetaDataInstance?>? create;
            if (attribute is IAnimatedPropMeta animatedProp)
            {
                create = () => CreateAnimatedProp(
                    animatedProp,
                    root,
                    skeleton,
                    isSelected ? attribute : null,
                    rootPlayer,
                    owner,
                    diagnostics);
            }
            else if (TryGetOrdinaryPropData(attribute, out var propData))
            {
                create = () => CreateOrdinaryProp(
                    propData,
                    root,
                    skeleton,
                    isSelected ? attribute : null,
                    rootPlayer,
                    owner,
                    diagnostics);
            }
            else
            {
                create = attribute switch
                {
                    ImpactPosition_v2 item => () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "ImpactPos",
                        CombatMetaDataPreviewCategory.Impact,
                        isSelected ? item : null,
                        rootPlayer),
                    ImpactPosition_v10 item => () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "ImpactPos",
                        CombatMetaDataPreviewCategory.Impact,
                        isSelected ? item : null,
                        rootPlayer),
                    TargetPos_0 item => () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "TargetPos",
                        CombatMetaDataPreviewCategory.Target,
                        isSelected ? item : null,
                        rootPlayer),
                    TargetPos_10 item => () => CreateStaticLocator(
                        item,
                        root,
                        item.Position,
                        "TargetPos",
                        CombatMetaDataPreviewCategory.Target,
                        isSelected ? item : null,
                        rootPlayer),
                    FirePos_v0 item => () => CreateFireLocator(
                        item,
                        root,
                        item.Position,
                        skeleton,
                        isSelected ? item : null,
                        rootPlayer),
                    FirePos_v2 item => () => CreateFireLocator(
                        item,
                        root,
                        item.Position,
                        skeleton,
                        isSelected ? item : null,
                        rootPlayer),
                    FirePos_v10 item => () => CreateFireLocator(
                        item,
                        root,
                        item.Position,
                        skeleton,
                        isSelected ? item : null,
                        rootPlayer),
                    SplashAttack_v3 item => () => CreateSplashAttack(
                        CreateSplashPreviewData(item),
                        root,
                        $"SplashAttack_{Math.Round(item.EndTime, 2)}",
                        0.1f,
                        isSelected ? item : null,
                        rootPlayer),
                    SplashAttack_v10 item => () => CreateSplashAttack(
                        CreateSplashPreviewData(item),
                        root,
                        $"SplashAttack_{Math.Round(item.EndTime, 2)}",
                        0.1f,
                        isSelected ? item : null,
                        rootPlayer),
                    IEffectMeta item => () => CreateEffect(
                        item,
                        root,
                        skeleton,
                        isSelected ? attribute : null,
                        owner,
                        diagnostics,
                        rootPlayer),
                    _ => null
                };
            }
            if (create == null &&
                SpatialMetaDataCatalog.TryCreate(
                    attribute,
                    out var spatialBinding) &&
                spatialBinding.UsesGenericMarker)
            {
                create = () => CreateSpatialMarker(
                    spatialBinding,
                    root,
                    skeleton,
                    isSelected ? attribute : null);
            }
            if (create == null && IsBoneOnlyMetaData(attribute))
            {
                create = () =>
                {
                    TryCreateBoneOnlyPreview(
                        attribute,
                        root,
                        skeleton,
                        isSelected ? attribute : null,
                        out var bonePreview);
                    return bonePreview;
                };
            }
            if (create == null)
                return new(false, null, diagnostics);

            try
            {
                var preview = create();
                if (preview == null && IsBoneOnlyMetaData(attribute))
                {
                    diagnostics.Add(MetaDataDiagnosticFactory.Create(
                        attribute,
                        owner,
                        "SuperView.Diagnostics.MissingBone"));
                }
                return new(true, preview, diagnostics);
            }
            catch (Exception e)
            {
                RemoveNewChildren(root, existingChildren);
                _logger.Here().Warning(
                    $"Skipping metadata preview for '{attribute.Name}': {e.Message}");
                diagnostics.Add(MetaDataDiagnosticFactory.Create(
                    attribute,
                    owner,
                    "SuperView.Diagnostics.PreviewUnavailable"));
                return new(true, null, diagnostics);
            }
        }

        private void TryAddPreview(
            ICollection<IMetaDataInstance> output,
            ICollection<MetaDataBuildDiagnostic> diagnostics,
            MetaDataDocumentOwner owner,
            ParsedMetadataAttribute source,
            SceneNode root,
            Func<IMetaDataInstance?> create)
        {
            var existingChildren = root.Children.ToHashSet();
            try
            {
                var instance = create();
                if (instance != null)
                    output.Add(instance);
            }
            catch (Exception e)
            {
                RemoveNewChildren(root, existingChildren);
                _logger.Here().Warning(
                    $"Skipping metadata preview for '{source.Name}': {e.Message}");
                diagnostics.Add(MetaDataDiagnosticFactory.Create(
                    source,
                    owner,
                    "SuperView.Diagnostics.PreviewUnavailable"));
            }
        }

        private static void RemoveNewChildren(
            SceneNode root,
            ISet<ISceneNode> existingChildren)
        {
            foreach (var child in root.Children
                .Where(child => !existingChildren.Contains(child))
                .ToList())
            {
                root.RemoveObject(child);
            }
        }

        private IMetaDataInstance? CreateAnimatedProp(
            IAnimatedPropMeta animatedPropMeta,
            SceneNode root,
            ISkeletonProvider rootSkeleton,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            AnimationPlayer rootPlayer,
            MetaDataDocumentOwner owner,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            var source = (ParsedMetadataAttribute)animatedPropMeta;
            var data = new PropPreviewData(
                source,
                animatedPropMeta.ModelName,
                animatedPropMeta.AnimationName,
                animatedPropMeta.StartTime,
                animatedPropMeta.EndTime,
                animatedPropMeta.BoneId,
                animatedPropMeta.Position,
                animatedPropMeta.Orientation,
                animatedPropMeta.Scale,
                animatedPropMeta as IThreeKingdomsAnimatedPropWindMeta);
            return CreatePropPreview(
                data,
                root,
                rootSkeleton,
                selectedMetaDataAttribute,
                rootPlayer,
                true,
                owner,
                diagnostics);
        }

        private IMetaDataInstance? CreateOrdinaryProp(
            PropPreviewData data,
            SceneNode root,
            ISkeletonProvider rootSkeleton,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            AnimationPlayer rootPlayer,
            MetaDataDocumentOwner owner,
            ICollection<MetaDataBuildDiagnostic> diagnostics) =>
            CreatePropPreview(
                data,
                root,
                rootSkeleton,
                selectedMetaDataAttribute,
                rootPlayer,
                false,
                owner,
                diagnostics);

        private IMetaDataInstance? CreatePropPreview(
            PropPreviewData data,
            SceneNode root,
            ISkeletonProvider rootSkeleton,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            AnimationPlayer rootPlayer,
            bool attachThroughAnimation,
            MetaDataDocumentOwner owner,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            if (!SpatialMetaDataCatalog.TryCreate(
                    data.Source,
                    out var spatialBinding))
            {
                return null;
            }

            var propName = attachThroughAnimation
                ? "Animated_prop"
                : "Prop";
            var color = selectedMetaDataAttribute == data.Source
                ? s_selectedColor
                : s_color;
            var meshPath = _resourceResolver.FindModel(
                data.Source,
                owner,
                data.ModelName,
                diagnostics);
            if (meshPath == null)
            {
                _logger.Here().Warning(
                    $"Skipping prop preview because model '{data.ModelName}' was not found.");
                return CreateSpatialMarker(
                    spatialBinding,
                    root,
                    rootSkeleton,
                    selectedMetaDataAttribute);
            }

            AnimationPlayer? propPlayer = null;
            SceneNode? loadedNode = null;
            SkeletonNode? propSkeletonNode = null;
            Func<Matrix>? nativePropAttachment = null;
            try
            {
                var animationPath = string.IsNullOrWhiteSpace(data.AnimationName)
                    ? null
                    : _resourceResolver.FindAnimation(
                        data.Source,
                        owner,
                        data.AnimationName,
                        diagnostics);
                propPlayer = _animationsContainerComponent.RegisterAnimationPlayer(
                    new AnimationPlayer(),
                    propName + Guid.NewGuid());
                loadedNode = _complexMeshLoader.Load(
                    meshPath,
                    new GroupNode(propName),
                    propPlayer,
                    true,
                    true);

                if (animationPath != null)
                {
                    var skeletonName = SceneNodeHelper.GetSkeletonName(loadedNode);
                    var skeletonFile = _skeletonAnimationLookUpHelper
                        .GetSkeletonFileFromName(skeletonName);
                    if (skeletonFile == null)
                    {
                        throw new Exception(
                            $"Unable to find skeleton '{skeletonName}' for prop.");
                    }

                    var skeleton = new GameSkeleton(skeletonFile, propPlayer);
                    var animFile = AnimationFile.Create(animationPath);
                    var clip = new AnimationClip(animFile, skeleton);
                    propPlayer.SetAnimation(clip, skeleton);
                    if (data.Wind is { AttachMethod: 2 } wind &&
                        (!string.IsNullOrWhiteSpace(wind.MetaData) ||
                         !string.IsNullOrWhiteSpace(wind.PathToAnotherAnim)))
                    {
                        AnimationClip? alternate = null;
                        if (!string.IsNullOrWhiteSpace(wind.PathToAnotherAnim))
                        {
                            var alternatePath = _resourceResolver.FindAnimation(
                                data.Source, owner, wind.PathToAnotherAnim, diagnostics);
                            if (alternatePath != null)
                                alternate = ReferenceEquals(alternatePath, animationPath) ? clip :
                                    new AnimationClip(AnimationFile.Create(alternatePath), skeleton);
                        }

                        var windBone = string.IsNullOrEmpty(wind.MetaData)
                            ? -1 : skeleton.GetBoneIndexByName(wind.MetaData);
                        if (windBone >= 0 && animFile.Bones?.Any(bone => bone.Name == wind.MetaData) != true)
                            windBone = -1;
                        if (!string.IsNullOrEmpty(wind.MetaData) && windBone < 0)
                        {
                            diagnostics.Add(new(data.Source, owner, MetaDataDiagnosticSeverity.Warning,
                                "SuperView.Diagnostics.MissingBone", BoneName: wind.MetaData));
                        }

                        Matrix GetWindAttachment()
                        {
                            return ThreeKingdomsPropWindRule.CreateAttachmentTransform(skeleton,
                                spatialBinding.Position, spatialBinding.Orientation ?? Quaternion.Identity,
                                rootSkeleton.Skeleton.GetAnimatedWorldTranform(data.BoneId));
                        }

                        if (data.BoneId >= 0 && data.BoneId < rootSkeleton.Skeleton.BoneCount)
                        {
                            propPlayer.AnimationRules.Add(new ThreeKingdomsPropWindRule(
                                skeleton, clip, alternate, windBone, wind.OverrideProp == 1,
                                () => _renderParameters?.WindVector ?? Vector2.Zero,
                                () => GetWindAttachment() *
                                    (root.SceneManager?.GetWorldPosition(root) ?? Matrix.Identity),
                                () => rootPlayer.PlaybackTime,
                                owner == MetaDataDocumentOwner.Persistent, data.StartTime));
                            nativePropAttachment = GetWindAttachment;
                            loadedNode.ModelMatrix = nativePropAttachment();
                        }
                        else
                        {
                            diagnostics.Add(new(data.Source, owner, MetaDataDiagnosticSeverity.Warning,
                                "SuperView.Diagnostics.MissingBone", BoneName: data.BoneId.ToString()));
                        }
                    }
                    else if (data.Wind is { } unsupportedWind &&
                        (!string.IsNullOrWhiteSpace(unsupportedWind.MetaData) ||
                         !string.IsNullOrWhiteSpace(unsupportedWind.PathToAnotherAnim)))
                    {
                        diagnostics.Add(new(data.Source, owner, MetaDataDiagnosticSeverity.Warning,
                            "SuperView.Diagnostics.PropWindAttachmentUnsupported"));
                    }
                    propSkeletonNode = new SkeletonNode(skeleton)
                    {
                        NodeColour = color,
                        ScaleMult = data.Scale
                    };
                    loadedNode.AddObject(propSkeletonNode);
                }

                loadedNode.ForeachNodeRecursive(node =>
                {
                    if (node is ISelectable selectableNode)
                        selectableNode.IsSelectable = false;

                    if (node is SceneNode sceneNode)
                        sceneNode.ScaleMult = data.Scale;
                });
                loadedNode.ScaleMult = data.Scale;

                if (attachThroughAnimation && nativePropAttachment == null)
                {
                    propPlayer.AnimationRules.Add(new CopyRootTransform(
                        rootSkeleton,
                        data.BoneId,
                        () => spatialBinding.Position,
                        () => spatialBinding.Orientation ??
                            Quaternion.Identity));
                }

                propPlayer.IsEnabled = true;
                if (rootPlayer.IsPlaying)
                    propPlayer.Play();
                else
                    propPlayer.Pause();
                propPlayer.Refresh();
                root.AddObject(loadedNode);

                if (attachThroughAnimation)
                {
                    var animatedProp = new AnimatedPropInstance(
                        loadedNode,
                        propPlayer,
                        rootSkeleton,
                        data.BoneId,
                        () => spatialBinding.Position,
                        () => spatialBinding.Orientation ??
                            Quaternion.Identity,
                        data.StartTime,
                        data.EndTime,
                        data.Source,
                        ReferenceEquals(
                            selectedMetaDataAttribute,
                            data.Source),
                        selected =>
                        {
                            SetPreviewOutline(loadedNode, selected);
                            if (propSkeletonNode != null)
                            {
                                propSkeletonNode.NodeColour = selected
                                    ? s_selectedColor
                                    : s_color;
                            }
                        },
                        nativePropAttachment);
                    animatedProp.Update(
                        (float)rootPlayer.CurrentTime.TotalSeconds);
                    return animatedProp;
                }

                return new PropInstance(
                    loadedNode,
                    propPlayer,
                    rootSkeleton,
                    data.BoneId,
                    () => spatialBinding.Position,
                    () => spatialBinding.Orientation ??
                        Quaternion.Identity,
                    data.StartTime,
                    data.EndTime,
                    data.Source,
                    ReferenceEquals(
                        selectedMetaDataAttribute,
                        data.Source),
                    selected =>
                    {
                        SetPreviewOutline(loadedNode, selected);
                        if (propSkeletonNode != null)
                        {
                            propSkeletonNode.NodeColour = selected
                                ? s_selectedColor
                                : s_color;
                        }
                    });
            }
            catch (Exception e)
            {
                if (loadedNode?.Parent != null)
                    loadedNode.Parent.RemoveObject(loadedNode);
                if (propPlayer != null)
                    _animationsContainerComponent.Remove(propPlayer);

                _logger.Here().Warning(
                    $"Skipping prop preview: {e.Message}");
                diagnostics.Add(MetaDataDiagnosticFactory.Create(
                    data.Source,
                    owner,
                    "SuperView.Diagnostics.PreviewUnavailable"));
                return CreateSpatialMarker(
                    spatialBinding,
                    root,
                    rootSkeleton,
                    selectedMetaDataAttribute);
            }
        }

        private static void SetPreviewOutline(
            SceneNode root,
            bool enabled) =>
            root.ForeachNodeRecursive(node =>
            {
                if (node is Rmv2MeshNode meshNode)
                    meshNode.SetPreviewOutline(enabled);
            });

        private static bool TryGetOrdinaryPropData(
            ParsedMetadataAttribute attribute,
            out PropPreviewData data)
        {
            data = null!;
            if (GetMetaDataName(attribute) != "PROP")
                return false;

            data = attribute switch
            {
                Prop_v15 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    prop.Scale),
                Prop_v14 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    prop.Scale),
                Prop_v13_3K prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    prop.Scale),
                Prop_v12_3K prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    prop.Scale),
                Prop_v13 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    prop.Scale),
                Prop_v12 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    1),
                Prop_v11 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    1),
                Prop_v10 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    1),
                Prop_v4 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    1),
                Prop_v3 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    1),
                Prop_v2 prop => new(
                    prop,
                    prop.ModelName,
                    prop.AnimationName,
                    prop.StartTime,
                    prop.EndTime,
                    prop.BoneId,
                    prop.Position,
                    prop.Orientation,
                    1),
                _ => null!
            };
            return data != null;
        }

        private IMetaDataInstance CreateStaticLocator(
            ParsedMetadataAttribute metaData,
            SceneNode root,
            Vector3 position,
            string displayName,
            CombatMetaDataPreviewCategory category,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            AnimationPlayer player,
            float scale = 0.3f,
            Func<Matrix>? referenceLocalTransform = null,
            int? highlightedBoneIndex = null)
        {
            var isSelected = selectedMetaDataAttribute == metaData;
            var node = new SimpleDrawableNode(displayName);
            var instance = new CombatMetaDataInstance(
                metaData,
                category,
                () => GetSinglePointPosition(metaData, position),
                node,
                isSelected,
                selected => PopulateStaticLocatorNode(
                    node,
                    metaData,
                    category,
                    scale,
                    selected),
                player,
                GetActiveTimeRange(metaData),
                referenceLocalTransform,
                highlightedBoneIndex);
            root.AddObject(node);
            return instance;
        }

        private IMetaDataInstance CreateFireLocator(
            ParsedMetadataAttribute metaData,
            SceneNode root,
            Vector3 position,
            ISkeletonProvider skeleton,
            ParsedMetadataAttribute? selectedMetaDataAttribute,
            AnimationPlayer player)
        {
            var gameSkeleton = skeleton?.Skeleton;
            var animRootIndex = gameSkeleton?.GetBoneIndexByName(
                "animroot") ?? -1;
            Func<Matrix> referenceLocalTransform = () => Matrix.Identity;
            if (gameSkeleton != null && animRootIndex >= 0)
            {
                referenceLocalTransform = () =>
                    gameSkeleton.GetAnimatedWorldTranform(animRootIndex);
            }
            return CreateStaticLocator(
                metaData,
                root,
                position,
                "FirePos",
                CombatMetaDataPreviewCategory.Fire,
                selectedMetaDataAttribute,
                player,
                referenceLocalTransform: referenceLocalTransform,
                highlightedBoneIndex: animRootIndex >= 0
                    ? animRootIndex
                    : null);
        }

        private void PopulateStaticLocatorNode(
            SimpleDrawableNode node,
            ParsedMetadataAttribute metaData,
            CombatMetaDataPreviewCategory category,
            float scale,
            bool isSelected)
        {
            var color = GetCombatPreviewColor(category, isSelected);
            var markerScale = scale;
            var edgeWidth = isSelected ? 1.6f : 0.65f;
            node.ClearItems();
            node.AddItem(new WorldTextRenderItem(
                _resourceLibrary,
                metaData.Name,
                Vector3.Zero,
                color));
            if (category == CombatMetaDataPreviewCategory.Impact)
            {
                node.AddItem(PreviewShapeGeometry.CreateCircleMarker(
                    Vector3.Zero,
                    markerScale,
                    PreviewShapeGeometry.WithPremultipliedAlpha(
                        s_impactColor,
                        48),
                    color,
                    edgeWidth));
            }
            else if (category == CombatMetaDataPreviewCategory.Target)
            {
                node.AddItem(PreviewShapeGeometry.CreateBoxMarker(
                    Vector3.Zero,
                    markerScale,
                    PreviewShapeGeometry.WithPremultipliedAlpha(
                        s_targetColor,
                        48),
                    color,
                    edgeWidth));
            }
            else
            {
                node.AddItem(PreviewShapeGeometry.CreateLocatorMarker(
                    Vector3.Zero,
                    markerScale,
                    PreviewShapeGeometry.WithPremultipliedAlpha(
                        s_fireColor,
                        48),
                    color,
                    edgeWidth));
            }

        }

        private static Vector3 GetSinglePointPosition(
            ParsedMetadataAttribute source,
            Vector3 fallback) =>
            source switch
            {
                ImpactPosition_v2 value => value.Position,
                ImpactPosition_v10 value => value.Position,
                TargetPos_0 value => value.Position,
                TargetPos_10 value => value.Position,
                FirePos_v0 value => value.Position,
                FirePos_v2 value => value.Position,
                FirePos_v10 value => value.Position,
                _ => fallback
            };

        private static SplashPreviewData CreateSplashPreviewData(
            SplashAttack_v3 splashAttack) =>
            new(
                splashAttack,
                splashAttack.StartPosition,
                splashAttack.EndPosition,
                splashAttack.AoeShape,
                splashAttack.WidthForCorridor,
                splashAttack.AngleForCone);

        private static SplashPreviewData CreateSplashPreviewData(
            SplashAttack_v10 splashAttack) =>
            new(
                splashAttack,
                splashAttack.StartPosition,
                splashAttack.EndPosition,
                splashAttack.AoeShape,
                splashAttack.WidthForCorridor,
                splashAttack.AngleForCone);

        private IMetaDataInstance CreateSplashAttack(SplashPreviewData splashAttack, SceneNode root, string displayName, float scale, ParsedMetadataAttribute? selectedAttribute, AnimationPlayer player)
        {
            var distance = Vector3.Distance(splashAttack.StartPosition, splashAttack.EndPosition);
            if (MathUtil.CompareEqualFloats(distance))
                throw new ConstraintException($"{displayName}: the distance between StartPosition {splashAttack.StartPosition} and EndPosition {splashAttack.EndPosition} is close to 0");

            var isSelected = selectedAttribute == splashAttack.Source;
            var node = new SimpleDrawableNode(displayName);
            var currentSplashAttack = splashAttack;
            void RefreshVisual()
            {
                var updated = CreateSplashPreviewData(
                    currentSplashAttack.Source);
                if (updated == currentSplashAttack)
                    return;

                currentSplashAttack = updated;
                try
                {
                    PopulateSplashAttackNode(
                        node,
                        currentSplashAttack,
                        displayName,
                        scale,
                        isSelected);
                }
                catch (ConstraintException)
                {
                    node.ClearItems();
                }
            }
            var instance = new CombatMetaDataInstance(
                splashAttack.Source,
                CombatMetaDataPreviewCategory.Splash,
                () => (currentSplashAttack.EndPosition +
                    currentSplashAttack.StartPosition) / 2,
                node,
                isSelected,
                selected =>
                {
                    isSelected = selected;
                    PopulateSplashAttackNode(
                        node,
                        currentSplashAttack,
                        displayName,
                        scale,
                        selected);
                },
                player,
                GetActiveTimeRange(splashAttack.Source),
                nodeLocalTransformProvider: () => Matrix.Identity,
                refreshVisual: RefreshVisual,
                localHitTargetsProvider: () =>
                    CreateSplashHitTargets(currentSplashAttack));
            root.AddObject(node);
            return instance;
        }

        private static SplashPreviewData CreateSplashPreviewData(
            ParsedMetadataAttribute source) =>
            source switch
            {
                SplashAttack_v3 splashAttack =>
                    CreateSplashPreviewData(splashAttack),
                SplashAttack_v10 splashAttack =>
                    CreateSplashPreviewData(splashAttack),
                _ => throw new InvalidOperationException(
                    $"Unsupported splash attack type {source.GetType().Name}")
            };

        private static IReadOnlyList<MetaDataMarkerHitTarget>
            CreateSplashHitTargets(SplashPreviewData splashAttack)
        {
            var hitRadius = splashAttack.AoeShape == 1 &&
                splashAttack.WidthForCorridor > 0
                    ? splashAttack.WidthForCorridor / 2
                    : 0.3f;
            return
            [
                new MetaDataMarkerHitTarget(
                    splashAttack.StartPosition,
                    MetaDataMarkerPoint.SplashStart,
                    hitRadius),
                new MetaDataMarkerHitTarget(
                    splashAttack.EndPosition,
                    MetaDataMarkerPoint.SplashEnd,
                    hitRadius),
            ];
        }

        private static MetaDataTimeRange? GetActiveTimeRange(
            ParsedMetadataAttribute attribute) =>
            MetaDataTimeRange.TryCreate(attribute, out var timeRange)
                ? timeRange
                : null;

        private void PopulateSplashAttackNode(
            SimpleDrawableNode node,
            SplashPreviewData splashAttack,
            string displayName,
            float scale,
            bool isSelected)
        {
            var edgeColor = GetCombatPreviewColor(
                CombatMetaDataPreviewCategory.Splash,
                isSelected);
            var edgeWidth = isSelected ? 1.6f : 0.65f;
            var fillColor = PreviewShapeGeometry.WithPremultipliedAlpha(
                s_splashColor,
                48);
            node.ClearItems();
            var textPos = (splashAttack.EndPosition + splashAttack.StartPosition) / 2;

            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "StartPos", splashAttack.StartPosition, edgeColor));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "EndPos", splashAttack.EndPosition, edgeColor));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, displayName, textPos, edgeColor));
            PreviewShape? filledArea = null;
            if (splashAttack.AoeShape == 0)
            {
                if (MathUtil.CompareEqualFloats(
                    splashAttack.AngleForCone / 2,
                    tolerance: 0.1f))
                {
                    throw new ConstraintException(
                        $"{displayName}: the half-angle {splashAttack.AngleForCone / 2} of the cone is close to 0");
                }

                filledArea = PreviewShapeGeometry.CreateSplashCone(
                    splashAttack.StartPosition,
                    splashAttack.EndPosition,
                    splashAttack.AngleForCone,
                    fillColor,
                    edgeColor,
                    edgeWidth);
            }
            else if (splashAttack.AoeShape == 1)
            {
                if (MathUtil.CompareEqualFloats(
                    splashAttack.WidthForCorridor,
                    tolerance: 0.001f))
                {
                    throw new ConstraintException(
                        $"{displayName}: the WidthForCorridor {splashAttack.WidthForCorridor} of the corridor is close to 0");
                }

                filledArea = PreviewShapeGeometry.CreateSplashCorridor(
                    splashAttack.StartPosition,
                    splashAttack.EndPosition,
                    splashAttack.WidthForCorridor / 2,
                    fillColor,
                    edgeColor,
                    edgeWidth);
            }

            var endpointRadius = splashAttack.AoeShape == 1
                ? splashAttack.WidthForCorridor / 2
                : scale / 2;
            var marker = CreateSplashAttackMarker(
                splashAttack.StartPosition,
                splashAttack.EndPosition,
                endpointRadius,
                edgeColor,
                edgeWidth,
                PreviewShapeGeometry.SmoothSegments);
            node.AddItem(filledArea == null
                ? marker
                : new PreviewShape(filledArea.Triangles, marker.Edges));
        }

        internal static PreviewShape CreateSplashAttackMarker(
            Vector3 start,
            Vector3 end,
            float endpointRadius,
            Color color,
            float edgeWidth,
            int segments = 24)
        {
            var direction = Vector3.Normalize(end - start);
            var side = Vector3.Cross(direction, Vector3.Up);
            if (side.LengthSquared() < 0.000001f)
                side = Vector3.Cross(direction, Vector3.Forward);
            side.Normalize();
            var up = Vector3.Normalize(Vector3.Cross(side, direction));
            var edges = new List<EdgeData>(segments * 2 + 3);

            AddRing(start);
            AddRing(end);
            AddEdge(start, end);

            var distance = Vector3.Distance(start, end);
            var arrowLength = MathF.Min(distance * 0.18f, 0.35f);
            var arrowHalfWidth = arrowLength * 0.45f;
            var arrowBase = end - direction * arrowLength;
            AddEdge(end, arrowBase + side * arrowHalfWidth);
            AddEdge(end, arrowBase - side * arrowHalfWidth);

            return new PreviewShape([], edges.ToArray());

            void AddRing(Vector3 center)
            {
                for (var index = 0; index < segments; index++)
                {
                    var angle = MathHelper.TwoPi * index / segments;
                    var nextAngle = MathHelper.TwoPi *
                        ((index + 1) % segments) / segments;
                    AddEdge(
                        center + side * (MathF.Cos(angle) * endpointRadius) +
                            up * (MathF.Sin(angle) * endpointRadius),
                        center + side * (MathF.Cos(nextAngle) * endpointRadius) +
                            up * (MathF.Sin(nextAngle) * endpointRadius));
                }
            }

            void AddEdge(Vector3 edgeStart, Vector3 edgeEnd)
            {
                edges.Add(new EdgeData
                {
                    P0 = edgeStart,
                    P1 = edgeEnd,
                    C0 = color.ToVector3(),
                    C1 = color.ToVector3(),
                    Width = edgeWidth,
                });
            }
        }

        private static Color GetCombatPreviewColor(
            CombatMetaDataPreviewCategory category,
            bool isSelected)
        {
            if (isSelected)
                return Color.White;

            return category switch
            {
                CombatMetaDataPreviewCategory.Impact => s_impactColor,
                CombatMetaDataPreviewCategory.Target => s_targetColor,
                CombatMetaDataPreviewCategory.Fire => s_fireColor,
                CombatMetaDataPreviewCategory.Splash => s_splashColor,
                _ => s_color
            };
        }

        private IMetaDataInstance CreateEffect(
            IEffectMeta effect,
            SceneNode root,
            ISkeletonProvider skeleton,
            ParsedMetadataAttribute? selectedAttribute,
            MetaDataDocumentOwner owner,
            ICollection<MetaDataBuildDiagnostic> diagnostics,
            AnimationPlayer rootPlayer)
        {
            var source = (ParsedMetadataAttribute)effect;
            AddEffectSettingsDiagnostics(effect, source, owner, EffectContext, diagnostics);
            var id = source is DecodedMetaEntryBase modern ? modern.Id : 0;
            var distanceCulled = effect switch
            { Effect_v7 v => v.DistanceCulled, Effect_v10 v => v.DistanceCulled, Effect_v11 v => v.DistanceCulled, Effect_v12 v => v.DistanceCulled, _ => false };
            var allowed = EffectIdFilter.IsAllowed(id, EffectContext.CurrentEffectId,
                EffectContext.ApplyIdFilter, EffectContext.EffectGroupMask)
                && !EffectDistanceCullGate.ShouldCull(distanceCulled, EffectContext.SuppressDistanceCulledEffects,
                    EffectContext.DistanceCullingOverrideCount);
            var resource = allowed ? EffectResources.Resolve(effect, source, owner, EffectContext, diagnostics) : null;
            var node = new SimpleDrawableNode("Effect:" + effect.VfxName);
            var instance = new DrawableMetaInstance(
                GetActiveTimeRange(source),
                node,
                source,
                ReferenceEquals(selectedAttribute, source),
                selected => PopulateEffectNode(
                    node,
                    effect.VfxName,
                    selected),
                () => effect.Position,
                () => new Quaternion(effect.Orientation));
            root.AddObject(node);
            var start = GetActiveTimeRange(source)?.StartTime ?? 0;
            var authoredTracking = IsEffectTracking(effect);
            AnimationPlayer AttachmentPlayer() => skeleton?.Skeleton?.AnimationPlayer ?? rootPlayer;
            AnimationClip? AttachmentClip() => skeleton?.Skeleton?.AnimationPlayer?.AnimationClip ?? rootPlayer.AnimationClip;
            bool HasBoneReferences() => skeleton?.Skeleton is { BoneCount: > 0 };
            bool CanTrack() => authoredTracking && AttachmentClip() != null && HasBoneReferences();
            var tracking = CanTrack();
            var attachmentSamples = new Dictionary<float, Matrix>();
            AnimationClip? sampledClip = null;
            GameSkeleton? sampledSkeleton = null;
            bool sampledEnabled = false;
            GameWorld.Core.Animation.AnimationChange.IAnimationChangeRule[] sampledRules = [];
            Matrix? birthReference = null;
            Matrix? sampledActorWorld = null;
            var worldAttachmentSamples = new Dictionary<float, Matrix>();
            Matrix? frozenWorldAttachment = null;
            Matrix? frozenWorldPose = null;
            void ValidateAttachmentCache()
            {
                var currentSkeleton = skeleton?.Skeleton;
                var player = AttachmentPlayer();
                var clip = AttachmentClip();
                var enabled = player?.IsEnabled == true;
                var canTrack = CanTrack();
                if (!ReferenceEquals(clip, sampledClip) || !ReferenceEquals(currentSkeleton, sampledSkeleton)
                    || enabled != sampledEnabled || canTrack != tracking
                    || player != null && !player.AnimationRules.SequenceEqual(sampledRules))
                {
                    tracking = canTrack;
                    sampledClip = clip; sampledSkeleton = currentSkeleton; sampledEnabled = enabled;
                    sampledRules = player?.AnimationRules.ToArray() ?? [];
                    attachmentSamples.Clear();
                    birthReference = null;
                    frozenWorldAttachment = null;
                    frozenWorldPose = null;
                    worldAttachmentSamples = new();
                    sampledActorWorld = null;
                }
            }
            Matrix BirthReference()
            {
                ValidateAttachmentCache();
                return birthReference ??= effect.NodeIndex == -1 || !HasBoneReferences()
                    ? UnboundReference() : SampleEffectReference(effect, skeleton, start);
            }
            Matrix UnboundReference()
            {
                var currentSkeleton = skeleton?.Skeleton;
                return !tracking && EffectRootMotionCompensation.TryCreate(currentSkeleton, AttachmentClip(),
                    start, EffectContext.ApplyRootMotionCompensation, out var correction)
                    ? correction : Matrix.Identity;
            }
            Matrix BoneReferenceAt(float seconds)
            {
                ValidateAttachmentCache();
                if (!tracking) seconds = 0;
                if (attachmentSamples.TryGetValue(seconds, out var matrix)) return matrix;
                matrix = tracking ? SampleEffectReference(effect, skeleton, seconds + start) : BirthReference();
                if (attachmentSamples.Count >= 4096) attachmentSamples.Clear();
                attachmentSamples[seconds] = matrix;
                return matrix;
            }
            Matrix CurrentAnimatedBoneReference()
            {
                if (effect.NodeIndex == -1 || !HasBoneReferences()) return UnboundReference();
                var currentSkeleton = skeleton?.Skeleton;
                if (currentSkeleton == null || effect.NodeIndex < 0 || effect.NodeIndex >= currentSkeleton.BoneCount)
                    return Matrix.Identity;
                return currentSkeleton.AnimationPlayer?.IsEnabled == true
                    ? currentSkeleton.GetAnimatedWorldTranform(effect.NodeIndex)
                    : currentSkeleton.GetWorldTransform(effect.NodeIndex);
            }
            Matrix CurrentBoneReference()
            {
                ValidateAttachmentCache();
                return tracking ? CurrentAnimatedBoneReference() : BirthReference();
            }
            var validBone = skeleton?.Skeleton != null && effect.NodeIndex >= 0 && effect.NodeIndex < skeleton.Skeleton.BoneCount;
            instance.FollowTransform(CurrentBoneReference, validBone ? effect.NodeIndex : null);
            if (effect.NodeIndex != -1 && !validBone)
            {
                diagnostics.Add(MetaDataDiagnosticFactory.Create(
                    source,
                    owner,
                    "SuperView.Diagnostics.MissingBone",
                    boneName: effect.NodeIndex.ToString()));
            }
            if (resource != null && _vfxFactory != null && _devices != null && _liveDocuments != null)
            {
                var documentSource = new VfxPreviewDocumentSource(_resourceResolver.Files, resource, _liveDocuments);
                // The SuperView grid is a world-Y0 preview receiver, independent of animated bones.
                var particles = new VfxPreviewSceneNode(_vfxFactory, _devices, documentSource)
                {
                    FlatGroundHeight = 0,
                    EnableInteractiveReload = EnableInteractiveVfxPreview,
                };
                var scale = GetEffectScale(effect);
                particles.ModelMatrix = Matrix.CreateScale(scale);
                particles.IsVisible = scale != 0;
                instance.UseDisplayWorldTransform(() => tracking ? null : frozenWorldPose);
                var sourceName = source.Name;
                var stableFields = source.GetType().GetProperties()
                    .Where(property => property.Name is not ("Position" or "Orientation" or "Scale")
                        && property.GetCustomAttributes(typeof(MetaDataTagAttribute), true).Length != 0)
                    .Select(property => (Property: property, Value: property.GetValue(source))).ToArray();
                particles.LocalAttachmentAtTime = seconds =>
                {
                    var local = Matrix.CreateScale(GetEffectScale(effect))
                        * Matrix.CreateFromQuaternion(new Quaternion(effect.Orientation)) * Matrix.CreateTranslation(effect.Position);
                    return local * BoneReferenceAt(seconds);
                };
                try { node.AddObject(particles); }
                catch { particles.Dispose(); throw; }
                try
                {
                    var snapshot = documentSource.Read();
                    IReadOnlyList<VfxPreviewDiagnostic> previewDiagnostics = snapshot == null
                        ? [new VfxPreviewDiagnostic(documentSource.Path, "", "", "Vfx.Preview.LiveDocumentInvalid")]
                        : EnableInteractiveVfxPreview ? [] : VfxPreviewInspector.Inspect(snapshot).Diagnostics;
                    foreach (var diagnostic in previewDiagnostics.DistinctBy(x => x.ReasonKey))
                        diagnostics.Add(MetaDataDiagnosticFactory.Create(source, owner, diagnostic.ReasonKey, documentSource.Path));
                }
                catch
                {
                    diagnostics.Add(MetaDataDiagnosticFactory.Create(source, owner, "Vfx.Preview.InvalidData", documentSource.Path));
                }
                rootPlayer.ContinuePlaybackTimeAfterAnimationEnd = true;
                return new EffectMetaInstance(instance, particles, GetActiveTimeRange(source), owner == MetaDataDocumentOwner.Persistent, rootPlayer,
                    refreshTransform: () =>
                    {
                        // Any other serialized field change must take the normal rebuild/diagnostic path.
                        if (source.Name != sourceName || stableFields.Any(field => !Equals(field.Value, field.Property.GetValue(source)))) return false;
                        var currentScale = GetEffectScale(effect);
                        particles.ModelMatrix = Matrix.CreateScale(currentScale);
                        particles.IsVisible = currentScale != 0;
                        attachmentSamples.Clear();
                        worldAttachmentSamples = new();
                        sampledActorWorld = null;
                        frozenWorldAttachment = null;
                        frozenWorldPose = null;
                        return true;
                    }, prepareAttachment: (currentTime, resetAttachment) =>
                    {
                        if (root.SceneManager == null) return;
                        if (resetAttachment)
                        {
                            frozenWorldAttachment = null;
                            frozenWorldPose = null;
                            birthReference = null;
                            attachmentSamples.Clear();
                            worldAttachmentSamples = new();
                            sampledActorWorld = null;
                            instance.Update(currentTime);
                        }
                        // Keep this frame's actor root immutable for retired playback histories.
                        ValidateAttachmentCache();
                        var actorWorld = root.SceneManager.GetWorldPosition(root);
                        var orientation = new Quaternion(effect.Orientation);
                        var position = effect.Position;
                        var authoredScale = GetEffectScale(effect);
                        var scalesWithParent = effect is Effect_v12 { ScalesWithParent: true };
                        if (!tracking && !frozenWorldAttachment.HasValue && particles.CurrentPlaybackEnabled
                            && particles.AttachmentSampleTimeSeconds >= -.000001f)
                        {
                            // Running triggers use their first eligible frame; explicit seeks reconstruct Start.
                            if (particles.IsPlaybackAdvancing)
                            {
                                birthReference = CurrentAnimatedBoneReference();
                                attachmentSamples.Clear();
                                instance.Update(currentTime);
                            }
                            frozenWorldAttachment = EffectAttachmentTransform.Create(orientation, position, authoredScale,
                                scalesWithParent, BirthReference(), actorWorld);
                            frozenWorldPose = EffectAttachmentTransform.CreatePose(orientation, position, BirthReference(), actorWorld);
                        }
                        if (!tracking && frozenWorldAttachment is { } frozen)
                        {
                            particles.CurrentAttachment = () => frozen;
                            particles.WorldAttachmentAtTime = _ => frozen;
                            return;
                        }
                        if (sampledActorWorld != actorWorld)
                        {
                            sampledActorWorld = actorWorld;
                            worldAttachmentSamples = new();
                        }
                        var samples = worldAttachmentSamples;
                        var currentAttachment = EffectAttachmentTransform.Create(orientation, position, authoredScale,
                            scalesWithParent, CurrentBoneReference(), actorWorld);
                        particles.CurrentAttachment = () => currentAttachment;
                        particles.WorldAttachmentAtTime = seconds =>
                        {
                            if (!tracking) seconds = 0;
                            if (samples.TryGetValue(seconds, out var matrix)) return matrix;
                            matrix = EffectAttachmentTransform.Create(orientation, position, authoredScale,
                                scalesWithParent, BoneReferenceAt(seconds), actorWorld);
                            if (samples.Count >= 4096) samples.Clear();
                            samples[seconds] = matrix;
                            return matrix;
                        };
                    });
            }
            return instance;
        }

        private static Matrix SampleEffectReference(IEffectMeta effect, ISkeletonProvider? provider, float sceneTime)
        {
            var skeleton = provider?.Skeleton;
            if (skeleton == null || effect.NodeIndex < 0 || effect.NodeIndex >= skeleton.BoneCount) return Matrix.Identity;
            var player = skeleton.AnimationPlayer;
            if (player == null || !player.IsEnabled) return skeleton.GetWorldTransform(effect.NodeIndex);
            if (player.AnimationClip == null || player.Duration.TotalSeconds <= 0)
                return skeleton.GetAnimatedWorldTranform(effect.NodeIndex);
            var frame = AnimationSampler.Sample((float)(Math.Max(0, sceneTime) / player.Duration.TotalSeconds),
                skeleton, player.AnimationClip, player.AnimationRules);
            return frame.GetSkeletonAnimatedWorld(skeleton, effect.NodeIndex);
        }

        private static float GetEffectScale(IEffectMeta effect) => effect switch
        {
            Effect_v3 value => value.Scale,
            Effect_v6 value => value.Scale,
            Effect_v7 value => value.Scale,
            Effect_v10 value => value.Scale,
            Effect_v11 value => value.Scale,
            Effect_v12 value => value.Scale,
            _ => 1,
        };

        private static bool IsEffectTracking(IEffectMeta effect) => effect switch
        {
            Effect_v1 value => value.BoolUnknown == "true",
            Effect_v2 value => value.BoolUnknown == "true",
            _ => effect.Tracking,
        };

        private static void AddEffectSettingsDiagnostics(IEffectMeta effect, ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner, EffectPreviewContext context, ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            var type = effect switch { Effect_v11 value => value.EffectType, Effect_v12 value => value.EffectType, _ => 0 };
            if ((!EffectMetaResourceResolver.IsTerrainMutable(effect) || context.BypassTerrainSelection) && (type is < 0 or > 2))
                diagnostics.Add(MetaDataDiagnosticFactory.Create(source, owner, "SuperView.Diagnostics.EffectTypeUnsupported"));
        }

        private IMetaDataInstance CreateSpatialMarker(
            SpatialMetaDataBinding binding,
            SceneNode root,
            ISkeletonProvider skeleton,
            ParsedMetadataAttribute? selectedAttribute)
        {
            var node = new SimpleDrawableNode(binding.Source.Name);
            var instance = new DrawableMetaInstance(
                GetActiveTimeRange(binding.Source),
                node,
                binding.Source,
                ReferenceEquals(selectedAttribute, binding.Source),
                selected => PopulateSpatialMarkerNode(
                    node,
                    binding,
                    selected),
                () => binding.Position,
                () => binding.Orientation ?? Quaternion.Identity);
            root.AddObject(node);

            var boneIndex = binding.BoneIndex ?? -1;
            if (binding.AttachToBone &&
                boneIndex >= 0 &&
                boneIndex < skeleton.Skeleton.BoneCount)
            {
                instance.FollowBone(skeleton, boneIndex);
            }

            return instance;
        }

        private bool TryCreateBoneOnlyPreview(
            ParsedMetadataAttribute source,
            SceneNode root,
            ISkeletonProvider skeleton,
            ParsedMetadataAttribute? selectedAttribute,
            out IMetaDataInstance? preview)
        {
            preview = null;
            if (!TryGetBoneOnlyTarget(source, skeleton, out var boneIndex))
                return IsBoneOnlyMetaData(source);

            var node = new SimpleDrawableNode(source.Name);
            var instance = new DrawableMetaInstance(
                GetActiveTimeRange(source),
                node,
                source,
                ReferenceEquals(source, selectedAttribute),
                _ => PopulateBoneOnlyMarkerNode(node),
                () => Vector3.Zero,
                () => Quaternion.Identity,
                isHitTestEnabled: false);
            root.AddObject(node);
            instance.FollowBone(skeleton, boneIndex);
            preview = instance;
            return true;
        }

        private static bool TryGetBoneOnlyTarget(
            ParsedMetadataAttribute source,
            ISkeletonProvider skeleton,
            out int boneIndex)
        {
            boneIndex = -1;
            if (skeleton?.Skeleton == null)
                return false;

            var metaDataName = GetMetaDataName(source);
            if (metaDataName == "SPLICE")
            {
                var property = source.GetType().GetProperty(
                    "GenericBoneIndex");
                if (property?.PropertyType == typeof(int))
                    boneIndex = (int)property.GetValue(source)!;
            }
            else if (metaDataName.StartsWith(
                "DOCK_EQPT_",
                StringComparison.Ordinal))
            {
                var property = source.GetType().GetProperty(
                    "SkeletonNameAlternatives");
                if (property?.GetValue(source) is string[] names)
                {
                    foreach (var name in names.Where(name =>
                        !string.IsNullOrWhiteSpace(name)))
                    {
                        boneIndex = skeleton.Skeleton.GetBoneIndexByName(name);
                        if (boneIndex >= 0)
                            break;
                    }
                }
            }

            return boneIndex >= 0 &&
                boneIndex < skeleton.Skeleton.BoneCount;
        }

        private static bool IsBoneOnlyMetaData(
            ParsedMetadataAttribute source)
        {
            var metaDataName = GetMetaDataName(source);
            return metaDataName == "SPLICE" ||
                metaDataName.StartsWith(
                "DOCK_EQPT_",
                StringComparison.Ordinal);
        }

        private static string GetMetaDataName(
            ParsedMetadataAttribute source)
        {
            if (!string.IsNullOrWhiteSpace(source.Name))
                return source.Name;

            return source.GetType()
                .GetCustomAttributes(typeof(MetaDataAttribute), false)
                .OfType<MetaDataAttribute>()
                .FirstOrDefault()?.Name ?? "";
        }

        private static void PopulateBoneOnlyMarkerNode(
            SimpleDrawableNode node)
        {
            node.ClearItems();
        }

        private void PopulateSpatialMarkerNode(
            SimpleDrawableNode node,
            SpatialMetaDataBinding binding,
            bool isSelected)
        {
            var baseColor = binding.Kind switch
            {
                SpatialMetaDataKind.Blood => Color.Crimson,
                SpatialMetaDataKind.CameraShake => Color.Gold,
                SpatialMetaDataKind.CrewLocation => s_crewLocationColor,
                SpatialMetaDataKind.SoundTrigger => Color.LimeGreen,
                SpatialMetaDataKind.SoundBuilding =>
                    new Color(76, 175, 125),
                SpatialMetaDataKind.Transform => Color.Orange,
                _ => Color.Gray
            };
            var color = isSelected ? Color.White : baseColor;
            var fill = PreviewShapeGeometry.WithPremultipliedAlpha(
                baseColor,
                48);
            var edgeWidth = isSelected ? 1.6f : 0.65f;
            const float scale = 0.3f;
            var origin = Vector3.Zero;

            node.ClearItems();
            node.AddItem(new WorldTextRenderItem(
                _resourceLibrary,
                binding.Source.Name,
                origin,
                color));

            if (binding.Kind is
                SpatialMetaDataKind.SoundTrigger or
                SpatialMetaDataKind.SoundBuilding or
                SpatialMetaDataKind.CameraShake)
            {
                node.AddItem(PreviewShapeGeometry.CreateCircleMarker(
                    origin,
                    scale,
                    fill,
                    color,
                    edgeWidth));
                node.AddItem(LineHelper.AddCircle(
                    origin,
                    scale * 0.55f,
                    color));
            }
            else if (binding.Kind == SpatialMetaDataKind.CrewLocation)
            {
                node.AddItem(PreviewShapeGeometry.CreateBoxMarker(
                    origin,
                    scale * 0.9f,
                    fill,
                    color,
                    edgeWidth));
                node.AddItem(LineHelper.AddLine(
                    origin,
                    Vector3.Forward * scale,
                    color));
            }
            else if (binding.Kind == SpatialMetaDataKind.Blood)
            {
                node.AddItem(PreviewShapeGeometry.CreateCircleMarker(
                    origin,
                    scale * 0.45f,
                    fill,
                    color,
                    edgeWidth));
                node.AddItem(LineHelper.AddLine(
                    origin,
                    Vector3.Down * scale,
                    color));
            }
            else
            {
                node.AddItem(PreviewShapeGeometry.CreateLocatorMarker(
                    origin,
                    scale,
                    fill,
                    color,
                    edgeWidth));
            }

            if (binding.CanRotate)
            {
                node.AddItem(LineHelper.AddLine(
                    origin,
                    Vector3.UnitX * scale,
                    Color.Red));
                node.AddItem(LineHelper.AddLine(
                    origin,
                    Vector3.UnitY * scale,
                    Color.Green));
                node.AddItem(LineHelper.AddLine(
                    origin,
                    Vector3.UnitZ * scale,
                    Color.Blue));
            }
        }

        private void PopulateEffectNode(
            SimpleDrawableNode node,
            string effectName,
            bool isSelected)
        {
            var locatorScale = 0.3f;
            var textOffset = locatorScale * 1.5f + 0.01f;

            var position = Vector3.Zero;
            var localX = Vector3.UnitX;
            var localY = Vector3.UnitY;
            var localZ = Vector3.UnitZ;

            node.ClearItems();
            var edgeColor = isSelected ? Color.White : s_effectColor;
            node.AddItem(PreviewShapeGeometry.CreateBoxMarker(
                position,
                locatorScale * 0.3f,
                PreviewShapeGeometry.WithPremultipliedAlpha(
                    s_effectColor,
                    48),
                edgeColor,
                isSelected ? 1.6f : 0.65f));
            node.AddItem(LineHelper.AddLine(position, position + localX * locatorScale, Color.Red));
            node.AddItem(LineHelper.AddLine(position, position + localY * locatorScale, Color.Green));
            node.AddItem(LineHelper.AddLine(position, position + localZ * locatorScale, Color.Blue));

            node.AddItem(new WorldTextRenderItem(
                _resourceLibrary,
                effectName,
                position,
                edgeColor));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "X", position + localX * textOffset, Color.Red));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "Y", position + localY * textOffset, Color.Green));
            node.AddItem(new WorldTextRenderItem(_resourceLibrary, "Z", position + localZ * textOffset, Color.Blue));
        }
    }
}
