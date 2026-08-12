using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services.Settings;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.DataTemplates;
using Shared.Ui.Common.ValueConverters;
using Shared.Ui.Editors.BoneMapping;
using AnimationBatchExportView = CommonControls.Editors.AnimationBatchExporter.AnimationBatchExportView;
using AnimationPackView = CommonControls.Editors.AnimationPack.AnimationPackView;
using AnimSetTableEditorView = CommonControls.Editors.AnimationPack.AnimSetTableEditorView;
using BoneMappingMetaDataView = Editors.Shared.Core.Editors.BoneMapping.View.BoneMappingMetaDataView;
using CampaignAnimationView = AnimationEditor.CampaignAnimationCreator.EditorView;
using EditorHostView = AnimationEditor.Common.BaseControl.EditorHostView;
using KeyframeEditorView = AnimationEditor.AnimationKeyframeEditor.EditorView;
using MetadataAttributeView = Editors.AnimationMeta.Presentation.View.MetaDataAttributeView;
using MetadataEntryView = Editors.AnimationMeta.Presentation.View.MetaDataEntryView;
using MetadataMainView = Editors.AnimationMeta.Presentation.View.MainEditorView;
using MetadataNewEntryWindow = Editors.AnimationMeta.Presentation.View.NewMetaDataEntryWindow;
using MetadataSuperView = Editors.AnimationMeta.SuperView.EditorView;
using MountAnimationSettingsView = AnimationEditor.MountAnimationCreator.Views.AnimationSettingsView;
using MountAnimationView = AnimationEditor.MountAnimationCreator.EditorView;
using MountBatchOptionsWindow = AnimationEditor.MountAnimationCreator.BatchProcessOptionsWindow;
using MountLinkView = AnimationEditor.MountAnimationCreator.MountLinkSubView;
using MountSavePreviewView = AnimationEditor.MountAnimationCreator.Views.SaveAndPreviewView;
using MountVisualisationView = AnimationEditor.MountAnimationCreator.VisualisationHelperSubView;
using RetargetAnimationSettingsView = AnimationEditor.AnimationTransferTool.AnimationSettingsView;
using RetargetBoneMappingWindow = Editors.AnimatioReTarget.Editor.BoneHandling.Presentation.BoneMappingWindow;
using RetargetBoneMappingReviewView = Editors.AnimatioReTarget.Editor.BoneHandling.Presentation.BoneMappingReviewView;
using RetargetBoneSettingsView = Editors.AnimatioReTarget.Editor.BoneHandling.Presentation.BoneSettingsView;
using RetargetEditorView = Editors.AnimatioReTarget.Editor.EditorView;
using RetargetSaveWindow = Editors.AnimatioReTarget.Editor.Saving.SaveWindow;
using RetargetSelectedBoneView = Editors.AnimatioReTarget.Editor.BoneHandling.Presentation.SelectedBoneView;
using RetargetSettingsView = Editors.AnimatioReTarget.Editor.Settings.SettingsView;
using RiderAttachmentView = AnimationEditor.MountAnimationCreator.RiderAttachmentSubView;
using SceneObjectView = AnimationEditor.Common.ReferenceModel.SceneObjectView;
using SharedAnimationPlayerView = AnimationEditor.Common.AnimationPlayer.AnimationPlayerView;
using SharedBoneMappingView = CommonControls.Editors.BoneMapping.View.BoneMappingView;
using SharedBoneMappingWindow = CommonControls.Editors.BoneMapping.View.BoneMappingWindow;
using TextEditorView = CommonControls.Editors.TextEditor.TextEditorView;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiAnimationMetadataFamilyGallery
{
    private static readonly string[] Variants =
    [
        "animation-keyframe",
        "campaign-animation",
        "mount-animation",
        "mount-animation-settings",
        "mount-batch-options",
        "mount-link",
        "rider-attachment",
        "mount-save-preview",
        "mount-visualisation",
        "animation-batch-export",
        "animation-pack",
        "animset-table",
        "retarget-animation-settings",
        "retarget-bone-mapping-window",
        "retarget-bone-settings",
        "retarget-selected-bone",
        "retarget-editor",
        "retarget-editor-review-empty",
        "retarget-editor-preview-playing",
        "retarget-editor-preview-ready",
        "retarget-editor-confirmed",
        "retarget-save-window",
        "retarget-settings",
        "metadata-main",
        "metadata-attribute",
        "metadata-entry",
        "metadata-new-entry-window",
        "metadata-super-editor",
        "metadata-super-editor-loaded",
        "metadata-super-editor-loaded-narrow",
        "shared-animation-player",
        "shared-editor-host",
        "shared-scene-object",
        "shared-bone-metadata",
        "shared-bone-mapping",
        "shared-bone-mapping-window",
        "shared-text-editor",
    ];

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCaseSource(nameof(Cases))]
    public void AnimationMetadataFamily_RendersRequiredThemeAndState(
        ThemeType theme,
        string variant)
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () => Render(theme, variant));
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (var theme in new[]
                 {
                     ThemeType.DarkTheme,
                     ThemeType.LightTheme,
                     ThemeType.HighContrastDark,
                     ThemeType.HighContrastLight,
                 })
        {
            foreach (var variant in Variants)
                yield return new TestCaseData(theme, variant);
        }
    }

    private static void Render(ThemeType theme, string variant)
    {
        var previousTheme = ThemesController.CurrentTheme;
        try
        {
            ThemesController.SetTheme(theme);
            RegisterApplicationResources();
            var window = CreateWindow(variant);
            ConfigureWindow(window, variant);
            try
            {
                window.Show();
                window.UpdateLayout();
                AssertVisualContracts(window, variant);
                Capture(window, theme, variant);
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            ThemesController.SetTheme(previousTheme);
        }
    }

    private static void RegisterApplicationResources()
    {
        Application.Current.Resources["BoolToCollapsedConverter"] =
            new BoolToVisibilityConverter
            {
                TrueValue = Visibility.Visible,
                FalseValue = Visibility.Collapsed,
            };
        Application.Current.Resources["BoolToHiddenConverter"] =
            new BoolToVisibilityConverter
            {
                TrueValue = Visibility.Visible,
                FalseValue = Visibility.Hidden,
            };
        Application.Current.Resources["InvBoolToHiddenConverter"] =
            new BoolToVisibilityConverter
            {
                TrueValue = Visibility.Hidden,
                FalseValue = Visibility.Visible,
            };
        Application.Current.Resources["InvBoolConverter"] =
            new InverseBooleanConverter();
        Application.Current.Resources["ViewTemplateDataSelector"] =
            new ViewTemplateDataSelector();
    }

    private static Window CreateWindow(string variant) => variant switch
    {
        "animation-keyframe" => Host(
            new KeyframeEditorView { DataContext = CreateGeneralModel() },
            760,
            780),
        "campaign-animation" => Host(
            CreateCampaignAnimationWorkspace(),
            920,
            620),
        "mount-animation" => Host(
            new MountAnimationView { DataContext = CreateGeneralModel() },
            820,
            780),
        "mount-animation-settings" => Host(
            new MountAnimationSettingsView
            {
                DataContext = CreateGeneralModel(),
            },
            720,
            520),
        "mount-batch-options" => new MountBatchOptionsWindow
        {
            DataContext = CreateGeneralModel(),
        },
        "mount-link" => Host(
            new MountLinkView { DataContext = CreateGeneralModel() },
            800,
            600),
        "rider-attachment" => Host(
            new RiderAttachmentView { DataContext = CreateGeneralModel() },
            760,
            360),
        "mount-save-preview" => Host(
            new MountSavePreviewView { DataContext = CreateGeneralModel() },
            820,
            520),
        "mount-visualisation" => Host(
            new MountVisualisationView
            {
                DataContext = CreateGeneralModel(),
            },
            720,
            360),
        "animation-batch-export" => Host(
            new AnimationBatchExportView
            {
                DataContext = CreateBatchExportModel(),
            },
            760,
            520),
        "animation-pack" => Host(
            new AnimationPackView { DataContext = CreateAnimationPackModel() },
            1180,
            720),
        "animset-table" => Host(
            new AnimSetTableEditorView { DataContext = CreateAnimSetModel() },
            1180,
            720),
        "retarget-animation-settings" => Host(
            new RetargetAnimationSettingsView
            {
                DataContext = CreateGeneralModel(),
            },
            820,
            520),
        "retarget-bone-mapping-window" =>
            CreateRetargetBoneMappingWindow(),
        "retarget-bone-settings" => Host(
            new RetargetBoneSettingsView { DataContext = CreateBoneModel() },
            900,
            680),
        "retarget-selected-bone" => Host(
            new RetargetSelectedBoneView { DataContext = CreateBoneModel() },
            720,
            560),
        "retarget-editor" => Host(
            new RetargetEditorView { DataContext = CreateBoneModel() },
            900,
            760),
        "retarget-editor-review-empty" => Host(
            new RetargetEditorView { DataContext = CreateBoneModel(RetargetApprovalGalleryState.PreviewRequired) },
            900,
            760),
        "retarget-editor-preview-playing" => Host(
            new RetargetEditorView { DataContext = CreateBoneModel(RetargetApprovalGalleryState.Previewing) },
            900,
            760),
        "retarget-editor-preview-ready" => Host(
            new RetargetEditorView { DataContext = CreateBoneModel(RetargetApprovalGalleryState.ConfirmationRequired) },
            900,
            760),
        "retarget-editor-confirmed" => Host(
            new RetargetEditorView { DataContext = CreateBoneModel(RetargetApprovalGalleryState.Confirmed) },
            900,
            760),
        "retarget-save-window" =>
            new RetargetSaveWindow(
                new Editors.AnimatioReTarget.Editor.Saving.SaveSettings()),
        "retarget-settings" => Host(
            new RetargetSettingsView { DataContext = CreateBoneModel() },
            760,
            560),
        "metadata-main" => Host(
            new MetadataMainView { DataContext = CreateMetadataModel() },
            1000,
            680),
        "metadata-attribute" => Host(
            new MetadataAttributeView
            {
                DataContext = CreateMetadataModel(),
            },
            720,
            620),
        "metadata-entry" => Host(
            new MetadataEntryView { DataContext = CreateMetadataModel() },
            620,
            640),
        "metadata-new-entry-window" => new MetadataNewEntryWindow
        {
            DataContext = new GalleryModel
            {
                Items = new[]
                {
                    "animation_speed",
                    "animation_sound",
                    "weapon_bone",
                    "root_motion",
                },
                SelectedItem = "animation_speed",
            },
        },
        "metadata-super-editor" => Host(
            new MetadataSuperView
            {
                DataContext = new MetadataSuperViewGalleryModel(
                    hasPersistentMetaFile: false,
                    hasAnimationMetaFile: false,
                    selectedTabControllerIndex: 0),
            },
            1080,
            720),
        "metadata-super-editor-loaded" => Host(
            new MetadataSuperView
            {
                DataContext = new MetadataSuperViewGalleryModel(
                    hasPersistentMetaFile: false,
                    hasAnimationMetaFile: true,
                    selectedTabControllerIndex: 1),
            },
            1080,
            720),
        "metadata-super-editor-loaded-narrow" => Host(
            new MetadataSuperView
            {
                DataContext = new MetadataSuperViewGalleryModel(
                    hasPersistentMetaFile: false,
                    hasAnimationMetaFile: true,
                    selectedTabControllerIndex: 1),
            },
            480,
            720),
        "shared-animation-player" => Host(
            new SharedAnimationPlayerView
            {
                DataContext = CreateAnimationPlayerModel(),
            },
            980,
            320),
        "shared-editor-host" => Host(
            new EditorHostView { DataContext = CreateEditorHostModel() },
            1180,
            760),
        "shared-scene-object" => Host(
            new SceneObjectView { DataContext = CreateSceneObjectModel() },
            700,
            620),
        "shared-bone-metadata" => Host(
            new BoneMappingMetaDataView { DataContext = CreateBoneModel() },
            780,
            560),
        "shared-bone-mapping" => Host(
            new SharedBoneMappingView { DataContext = CreateBoneModel() },
            1120,
            680),
        "shared-bone-mapping-window" => new SharedBoneMappingWindow
        {
            DataContext = CreateBoneModel(),
        },
        "shared-text-editor" => Host(
            new TextEditorView { DataContext = CreateTextEditorModel() },
            1000,
            700),
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static Window CreateRetargetBoneMappingWindow()
    {
        var window = new RetargetBoneMappingWindow(new BoneMappingViewModel());
        window.DataContext = CreateBoneModel();
        return window;
    }

    private static Window Host(FrameworkElement content, double width, double height)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            Background = FindBrush("AeBrush.Canvas"),
            Foreground = FindBrush("AeBrush.TextPrimary"),
            FontFamily = FindFontFamily(),
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -32000,
            Top = -32000,
            ShowInTaskbar = false,
        };
        window.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(
                "pack://application:,,,/Shared.Ui;component/Common/Styles/EditorWorkspaceStyles.xaml"),
        });
        return window;
    }

    private static GalleryModel CreateGeneralModel()
    {
        var bones = new[]
        {
            new GalleryModel
            {
                BoneName = "root",
                Name = new GalleryModel { Value = "root" },
                BoneIndex = 0,
                HasMapping = true,
                IsVisible = new GalleryModel { Value = true },
                Children = new[]
                {
                    new GalleryModel
                    {
                        BoneName = "bip_spine_01",
                        Name = new GalleryModel { Value = "bip_spine_01" },
                        BoneIndex = 12,
                        HasMapping = true,
                        IsVisible = new GalleryModel { Value = true },
                        Children = Array.Empty<object>(),
                    },
                },
            },
        };
        var filter = new GalleryModel
        {
            Filter = string.Empty,
            Values = bones,
            SelectedItem = bones[0],
            FilterValid = true,
        };
        return new GalleryModel
        {
            AnimationSettings = new GalleryModel
            {
                Scale = new GalleryModel { TextValue = "1.000" },
                SpeedMult = new GalleryModel { TextValue = "1.000" },
                ApplyRelativeScale = new GalleryModel { Value = true },
                FreezeUnmapped = new GalleryModel { Value = false },
            },
            ModelBoneList = filter,
            ModelBoneListForIKEndBone = filter,
            SelectedRiderBone = filter,
            SelectedLegAnimation = filter,
            SelectedLegBone = filter,
            SelectedMountBone = filter,
            SelectedVertexesText = new GalleryModel { Value = "842, 843" },
            ActiveOutputFragment = new GalleryModel
            {
                Filter = "empire_general.fragment",
                Values = new[]
                {
                    new GalleryModel { FileName = "empire_general.fragment" },
                },
            },
            ActiveFragmentSlot = new GalleryModel
            {
                Filter = "stand_idle",
                Values = Array.Empty<object>(),
            },
            CanPreview = new GalleryModel { Value = true },
            CanSave = new GalleryModel { Value = true },
            CanBatchProcess = new GalleryModel { Value = true },
            CanAddToFragment = new GalleryModel { Value = true },
            DisplayGeneratedMesh = new GalleryModel { Value = true },
            DisplayGeneratedSkeleton = new GalleryModel { Value = true },
            AnimationOutputFormats = new uint[] { 5, 6, 7 },
            SelectedAnimationOutputFormat = new GalleryModel { Value = 7 },
            SavePrefixText = new GalleryModel { Value = "empire_general_" },
            EnsureUniqeFileName = new GalleryModel { Value = true },
            Translation = new System.Numerics.Vector3(0, 0.9f, 0),
            Rotation = new System.Numerics.Vector3(0, 0, 0),
            FitAnimation = true,
            LoopCounter = 2,
            KeepRiderRotation = true,
            IsRootNodeAnimation = false,
            CreateAnimPack = true,
            CreateFragment = true,
            CreateAnimations = true,
            AnimPackName = "empire_general.animpack",
            FragmentName = "empire_general.fragment",
            SavePrefix = "empire_general_",
        };
    }

    private static FrameworkElement CreateCampaignAnimationWorkspace()
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });
        layout.RowDefinitions.Add(new RowDefinition
        {
            Height = GridLength.Auto,
        });

        var sceneObject = new SceneObjectView
        {
            DataContext = CreateSceneObjectModel(),
        };
        var campaignEditor = new CampaignAnimationView
        {
            DataContext = CreateGeneralModel(),
        };
        Grid.SetRow(campaignEditor, 1);
        layout.Children.Add(sceneObject);
        layout.Children.Add(campaignEditor);
        return layout;
    }

    private static GalleryModel CreateBatchExportModel() => new()
    {
        PackfileList = new[]
        {
            new GalleryModel
            {
                Process = new GalleryModel { Value = true },
                Name = new GalleryModel { Value = "帝国将军.pack" },
            },
            new GalleryModel
            {
                Process = new GalleryModel { Value = false },
                Name = new GalleryModel { Value = "动画补丁.pack" },
            },
        },
        PossibleOutputFormats = new[] { "ANIM v7", "ANIM v6" },
        SelectedOutputFormat = new GalleryModel { Value = "ANIM v7" },
    };

    private static GalleryModel CreateAnimationPackModel()
    {
        var item = new GalleryModel
        {
            DisplayName = "empire_general.animset",
            IsUnknownFile = false,
            IsChanged = new GalleryModel { Value = true },
        };
        return new GalleryModel
        {
            AnimationPackItems = new GalleryModel
            {
                Filter = "empire",
                FilterValid = true,
                Values = new[]
                {
                    item,
                    new GalleryModel
                    {
                        DisplayName = "empire_cavalry.animset",
                        IsUnknownFile = false,
                        IsChanged = new GalleryModel { Value = false },
                    },
                },
                SelectedItem = item,
            },
            SelectedItemViewModel = new GalleryModel
            {
                Text = "<anim_set name=\"empire_general\" />",
            },
        };
    }

    private static GalleryModel CreateAnimSetModel() => new()
    {
        IsWh3 = true,
        Name = "empire_general",
        SkeletonName = "humanoid01",
        MountBin = "battle_entities/empire_mount.bin",
        LocomotionGraph = "humanoid_locomotion",
        Rows = new[]
        {
            new GalleryModel
            {
                SlotName = "stand_idle",
                AnimationFile = "animations/humanoid/stand_idle.anim",
                MetaFile = "animations/humanoid/stand_idle.meta",
                SoundFile = "",
            },
            new GalleryModel
            {
                SlotName = "walk",
                AnimationFile = "animations/humanoid/walk.anim",
                MetaFile = "animations/humanoid/walk.meta",
                SoundFile = "footsteps/armour_heavy",
            },
        },
        SlotNames = new[] { "stand_idle", "walk", "run" },
        AnimFiles = new[] { "stand_idle.anim", "walk.anim", "run.anim" },
        MetaFiles = new[] { "stand_idle.meta", "walk.meta" },
        SoundFiles = new[] { "footsteps/armour_heavy" },
    };

    private enum RetargetApprovalGalleryState
    {
        NeedsReview,
        PreviewRequired,
        Previewing,
        ConfirmationRequired,
        Confirmed,
    }

    private static GalleryModel CreateBoneModel(
        RetargetApprovalGalleryState approvalState = RetargetApprovalGalleryState.NeedsReview)
    {
        var reviewEmpty = approvalState != RetargetApprovalGalleryState.NeedsReview;
        var previewing = approvalState == RetargetApprovalGalleryState.Previewing;
        var previewed = approvalState is
            RetargetApprovalGalleryState.ConfirmationRequired or RetargetApprovalGalleryState.Confirmed;
        var confirmed = approvalState == RetargetApprovalGalleryState.Confirmed;
        var child = new GalleryModel
        {
            BoneName = "bip_spine_01",
            Name = new GalleryModel { Value = "bip_spine_01" },
            BoneIndex = new GalleryModel { Value = 12 },
            MappedBoneName = new GalleryModel { Value = "bip_spine_01" },
            MappedBoneIndex = new GalleryModel { Value = 12 },
            HasMapping = true,
            IsUsedByCurrentModel = new GalleryModel { Value = true },
            IsVisible = new GalleryModel { Value = true },
            Children = Array.Empty<object>(),
        };
        var root = new GalleryModel
        {
            BoneName = "root",
            Name = new GalleryModel { Value = "root" },
            BoneIndex = new GalleryModel { Value = 0 },
            MappedBoneName = new GalleryModel { Value = "root" },
            MappedBoneIndex = new GalleryModel { Value = 0 },
            HasMapping = true,
            IsUsedByCurrentModel = new GalleryModel { Value = true },
            IsVisible = new GalleryModel { Value = true },
            Children = new[] { child },
        };
        var bones = new GalleryModel
        {
            Filter = string.Empty,
            Values = new[] { root },
            SelectedItem = child,
        };
        return new GalleryModel
        {
            BoneManager = new GalleryModel
            {
                Bones = new[] { root },
                SelectedBone = child,
                LastAutoMappingSummary = new GalleryModel
                {
                    ConfirmedCount = reviewEmpty ? 2 : 1,
                    ReviewRequiredCount = reviewEmpty ? 0 : 1,
                    UnmatchedCount = reviewEmpty ? 0 : 1,
                    IntentionalUnmappedCount = reviewEmpty ? 1 : 0,
                },
                ReviewItems = reviewEmpty
                    ? Array.Empty<object>()
                    : new[]
                    {
                        new GalleryModel
                        {
                            TargetBoneName = "bip_spine_02",
                            StatusText = "待复核",
                            ReasonText = "找到 1 个名称相近的候选，请确认正确骨骼",
                            CanMarkIntentionalUnmapped = false,
                            Candidates = new[]
                            {
                                new GalleryModel
                                {
                                    DisplayText = "确认候选：source_spine_02",
                                },
                            },
                        },
                        new GalleryModel
                        {
                            TargetBoneName = "cape_back_0",
                            StatusText = "未匹配",
                            ReasonText = "自动匹配没有找到可靠候选，请从完整源骨骼树中手动选择",
                            CanMarkIntentionalUnmapped = true,
                            Candidates = Array.Empty<object>(),
                        },
                    },
                IsMappingStructurallyReady = reviewEmpty,
                IsPreviewingCurrentMapping = previewing,
                HasPreviewedCurrentMapping = previewed || confirmed,
                IsMappingConfirmed = confirmed,
                CanBatchRetarget = confirmed,
                BatchRetargetGateText = confirmed
                    ? "映射方案已确认，可以执行批量重定向"
                    : previewed
                        ? "映射预览已完整播放；请检查实际动作后明确确认此映射方案"
                        : previewing
                            ? "映射预览正在播放；完整播放一遍后才能确认"
                        : reviewEmpty
                            ? "骨骼结构检查已通过；请先生成并播放当前映射预览"
                            : "仍有 1 个核心动作骨骼和 1 个其他骨骼未解决，批量重定向不可用",
                ConfirmCandidateCommand = GalleryCommand.Instance,
                ConfirmMappingCommand = approvalState == RetargetApprovalGalleryState.ConfirmationRequired
                    ? GalleryCommand.Instance
                    : GalleryCommand.Disabled,
                ShowManualBoneMappingCommand = GalleryCommand.Instance,
                MarkIntentionalUnmappedCommand = GalleryCommand.Instance,
                ShowBoneMappingWindowCommand = GalleryCommand.Instance,
            },
            MeshBones = bones,
            ParentModelBones = bones,
            MeshSkeletonName = new GalleryModel { Value = "humanoid01" },
            ParentSkeletonName = new GalleryModel { Value = "humanoid01" },
            OnlyShowUsedBones = new GalleryModel { Value = true },
            ShowTransformSection = new GalleryModel { Value = true },
            RootScale = "1.000",
            SkeletonDisplayOffset = new System.Numerics.Vector3(0, 0, 0),
            PosOffset = new System.Numerics.Vector3(0, 0, 0),
            RotOffset = new System.Numerics.Vector3(0, 0, 0),
            ScaleOffset = new System.Numerics.Vector3(1, 1, 1),
            Rendering = new GalleryModel
            {
                ShowGeneratedMesh = true,
                ShowGeneratedSkeleton = true,
                VisualOffset = "1.5",
            },
            Settings = new GalleryModel
            {
                SkeletonScale = "1.000",
                AnimationSpeedMult = "1.000",
                ZeroUnmappedBones = false,
                ApplyRelativeScale = true,
            },
            SaveManager = new GalleryModel
            {
                ShowSaveSettingsCommand = GalleryCommand.Instance,
            },
            UpdateAnimationCommand = GalleryCommand.Instance,
        };
    }

    private static GalleryModel CreateMetadataModel()
    {
        var tag = new Editors.AnimationMeta.Presentation.MetaDataEntry(
            new Shared.GameFormats.AnimationMeta.Definitions.SplashAttack_v10
            {
                Name = "SPLASH_ATTACK",
                Version = 10,
                StartTime = 0.35f,
                EndTime = 0.8f,
                StartPosition = new Microsoft.Xna.Framework.Vector3(0, 0, 2),
                EndPosition = new Microsoft.Xna.Framework.Vector3(0, 0, 0.5f),
            },
            "触发溅射攻击并标记影响范围。",
            Moq.Mock.Of<Shared.Core.Events.IEventHub>(),
            true)
        {
            IsSelected = true,
        };
        return new GalleryModel
        {
            MetaDataFileVersion = 2,
            Tags = new[] { tag },
            SelectedTag = tag,
            NewActionCommand = GalleryCommand.Instance,
            DeleteActionCommand = GalleryCommand.Instance,
            MoveUpActionCommand = GalleryCommand.Instance,
            MoveDownActionCommand = GalleryCommand.Instance,
            CopyActionCommand = GalleryCommand.Instance,
            PasteActionCommand = GalleryCommand.Instance,
            SaveActionCommand = GalleryCommand.Instance,
        };
    }

    private static GalleryModel CreateAnimationPlayerModel() => new()
    {
        IsEnabled = new GalleryModel { Value = true },
        PlayerControlsVisibility = new GalleryModel { Value = Visibility.Visible },
        SelectedAnimation = new GalleryModel
        {
            Filter = "stand_idle",
            Values = new[] { "stand_idle", "walk", "run" },
            SelectedItem = "stand_idle",
        },
        SelectedAnimationCurrentFrame = new GalleryModel { Value = 18 },
        SelectedAnimationFrameCount = new GalleryModel { Value = 60 },
        SelectedAnimationFps = new GalleryModel { Value = 30 },
        SelectedAnimationCurrentTime = new GalleryModel { Value = 0.6 },
        SelectedAnimationMaxTime = new GalleryModel { Value = 2.0 },
        LoopAnimation = new GalleryModel { Value = true },
        PlayerItems = new[]
        {
            new GalleryModel
            {
                SlotName = new GalleryModel { Value = "主动画" },
                MaxFrames = new GalleryModel { Value = 60 },
                AnimationName = new GalleryModel { Value = "stand_idle" },
            },
        },
    };

    private static GalleryModel CreateEditorHostModel() => new()
    {
        LeftColumnWidth = new GridLength(3, GridUnitType.Star),
        RightColumnWidth = new GridLength(2, GridUnitType.Star),
        GameWorld = new Border
        {
            Margin = new Thickness(8),
            Background = FindBrush("AeBrush.Surface1"),
            BorderBrush = FindBrush("AeBrush.Border"),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "3D 动画预览",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = FindBrush("AeBrush.TextMuted"),
            },
        },
        Player = CreateAnimationPlayerModel(),
        SceneObjects = new[] { CreateSceneObjectModel() },
        FocusCameraCommand = GalleryCommand.Instance,
        ResetCameraCommand = GalleryCommand.Instance,
    };

    private static GalleryModel CreateSceneObjectModel() => new()
    {
        IsExpanded = true,
        IsEnabled = true,
        IsControlVisible = true,
        IsVisible = true,
        HeaderName = "参考模型",
        SubHeaderName = " · empire_general_body",
        Data = new GalleryModel
        {
            ShowMesh = new GalleryModel { Value = true },
            ShowSkeleton = new GalleryModel { Value = true },
            ShowWeapon = new GalleryModel { Value = false },
        },
        SkeletonInformation = new GalleryModel
        {
            SelectedBone = new GalleryModel { BoneName = "bip_spine_01" },
            Bones = Array.Empty<object>(),
        },
    };

    private static GalleryModel CreateTextEditorModel() => new()
    {
        Text = "<animation_meta>\n  <entry name=\"locomotion\" speed=\"1.0\" />\n  <entry name=\"weapon_action\" />\n</animation_meta>",
        SaveCommand = GalleryCommand.Instance,
    };

    private static void ConfigureWindow(Window window, string variant)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -32000;
        window.Top = -32000;
        window.ShowInTaskbar = false;
        window.Title = $"AE UI · {variant}";

        switch (variant)
        {
            case "mount-batch-options":
                window.MinWidth = 620;
                break;
            case "retarget-bone-mapping-window":
                window.Width = 1000;
                window.Height = 680;
                break;
            case "retarget-save-window":
                window.Width = 820;
                window.Height = 480;
                break;
            case "metadata-new-entry-window":
                window.Width = 460;
                window.Height = 520;
                break;
            case "shared-bone-mapping-window":
                window.Width = 1180;
                window.Height = 720;
                break;
        }
    }

    private static void AssertVisualContracts(Window window, string variant)
    {
        var buttons = FindVisualDescendants<Button>(window).ToArray();
        var comboBoxes = FindVisualDescendants<ComboBox>(window).ToArray();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(window.ActualWidth, Is.GreaterThan(300));
            NUnitAssert.That(window.ActualHeight, Is.GreaterThan(80));
            NUnitAssert.That(buttons, Has.All.Matches<Button>(button =>
                button.ActualHeight >= 0 && button.Style is not null));
            NUnitAssert.That(comboBoxes, Has.All.Matches<ComboBox>(comboBox =>
                comboBox.ActualHeight >= 0 && comboBox.Style is not null));
            NUnitAssert.That(
                FindVisualDescendants<FrameworkElement>(window),
                Has.None.Matches<FrameworkElement>(element =>
                    double.IsNaN(element.ActualWidth) ||
                    double.IsNaN(element.ActualHeight) ||
                    element.ActualWidth < 0 ||
                    element.ActualHeight < 0));
        });

        if (variant == "shared-animation-player")
        {
            NUnitAssert.That(
                FindVisualDescendants<System.Windows.Shapes.Path>(window)
                    .Count(),
                Is.GreaterThanOrEqualTo(5));
        }

        if (variant.StartsWith("retarget-editor", StringComparison.Ordinal))
        {
            var review = FindVisualDescendants<RetargetBoneMappingReviewView>(window).Single();
            NUnitAssert.That(review.IsVisible, Is.True);
            var emptyState = FindVisualDescendants<TextBlock>(review)
                .Any(textBlock => textBlock.IsVisible &&
                    Equals(textBlock.Text, "疑难骨骼已全部处理"));

            var confirmButton = FindVisualDescendants<Button>(review).Single(button =>
                Equals(button.Content, "确认此映射方案"));

            if (variant == "retarget-editor-review-empty")
            {
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(emptyState, Is.True);
                    NUnitAssert.That(confirmButton.IsEnabled, Is.False);
                    NUnitAssert.That(FindVisualDescendants<TextBlock>(review).Any(textBlock =>
                        textBlock.IsVisible &&
                        textBlock.Text?.Contains("先生成并播放", StringComparison.Ordinal) == true), Is.True);
                });
            }
            else if (variant == "retarget-editor-preview-ready")
            {
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(emptyState, Is.True);
                        NUnitAssert.That(confirmButton.IsEnabled, Is.True);
                        NUnitAssert.That(FindVisualDescendants<TextBlock>(review).Any(textBlock =>
                            textBlock.IsVisible &&
                            textBlock.Text?.Contains("预览已完整播放", StringComparison.Ordinal) == true), Is.True);
                });
            }
            else if (variant == "retarget-editor-preview-playing")
            {
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(emptyState, Is.True);
                    NUnitAssert.That(confirmButton.IsEnabled, Is.False);
                    NUnitAssert.That(FindVisualDescendants<TextBlock>(review).Any(textBlock =>
                        textBlock.IsVisible &&
                        textBlock.Text?.Contains("预览正在播放", StringComparison.Ordinal) == true), Is.True);
                });
            }
            else if (variant == "retarget-editor-confirmed")
            {
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(emptyState, Is.True);
                    NUnitAssert.That(confirmButton.IsEnabled, Is.False);
                    NUnitAssert.That(FindVisualDescendants<TextBlock>(review).Any(textBlock =>
                        textBlock.IsVisible &&
                        textBlock.Text?.Contains("映射方案已确认", StringComparison.Ordinal) == true), Is.True);
                });
            }
            else
            {
                var reviewButtons = FindVisualDescendants<Button>(review)
                    .Where(button => button.IsVisible)
                    .ToArray();
                var blockedGateVisible = FindVisualDescendants<TextBlock>(review)
                    .Any(textBlock => textBlock.IsVisible &&
                        textBlock.Text?.Contains("批量重定向不可用", StringComparison.Ordinal) == true);
                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(emptyState, Is.False);
                    NUnitAssert.That(blockedGateVisible, Is.True);
                    NUnitAssert.That(confirmButton.IsEnabled, Is.False);
                    NUnitAssert.That(reviewButtons, Has.Some.Matches<Button>(button =>
                        Equals(button.Content, "确认候选：source_spine_02")));
                    NUnitAssert.That(reviewButtons, Has.Some.Matches<Button>(button =>
                        Equals(button.Content, "搜索完整骨骼树")));
                    NUnitAssert.That(reviewButtons, Has.Some.Matches<Button>(button =>
                        Equals(button.Content, "标记有意不映射")));
                    NUnitAssert.That(reviewButtons, Has.All.Matches<Button>(button =>
                        button.Command != null &&
                        button.Focusable &&
                        button.IsTabStop &&
                        string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)) == false));
                });
            }
        }

        if (variant.StartsWith(
            "metadata-super-editor-loaded",
            StringComparison.Ordinal))
        {
            var editor = FindVisualDescendants<MetadataMainView>(window)
                .Single();
            NUnitAssert.That(editor.Visibility, Is.EqualTo(Visibility.Visible));
            NUnitAssert.That(editor.ActualHeight, Is.GreaterThan(100));
        }

        if (variant.StartsWith("metadata-super-editor", StringComparison.Ordinal))
        {
            var isLoadedMetadataEditor = variant.StartsWith(
                "metadata-super-editor-loaded",
                StringComparison.Ordinal);
            var previewToggles = FindVisualDescendants<CheckBox>(window)
                .Where(checkBox => checkBox.Content is string text &&
                    checkBox.IsVisible &&
                    (text == "撞击点" || text == "目标点" ||
                     text == "发射点" || text == "溅射范围"))
                .ToArray();
            var displayModeSelectors =
                FindVisualDescendants<RadioButton>(window)
                    .Where(radioButton =>
                        radioButton.IsVisible &&
                        (Equals(radioButton.Content, "生效期间") ||
                         Equals(radioButton.Content, "全程")))
                    .ToArray();
            var focusButtons = buttons.Where(button =>
                button.IsVisible &&
                Equals(button.Content, "定位所选 META")).ToArray();
            NUnitAssert.Multiple(() =>
            {
                NUnitAssert.That(
                    previewToggles,
                    Has.Length.EqualTo(isLoadedMetadataEditor ? 1 : 0));
                NUnitAssert.That(
                    previewToggles,
                    Has.All.Matches<CheckBox>(toggle =>
                        toggle.IsChecked == true));
                if (isLoadedMetadataEditor)
                {
                    NUnitAssert.That(
                        displayModeSelectors,
                        Has.Length.EqualTo(2));
                    NUnitAssert.That(
                        displayModeSelectors.Single(radioButton =>
                            Equals(radioButton.Content, "生效期间")).IsChecked,
                        Is.True);
                    NUnitAssert.That(
                        displayModeSelectors.Single(radioButton =>
                            Equals(radioButton.Content, "全程")).IsChecked,
                        Is.False);
                }
                NUnitAssert.That(
                    focusButtons,
                    Has.Length.EqualTo(isLoadedMetadataEditor ? 1 : 0));
                NUnitAssert.That(
                    focusButtons,
                    Has.All.Matches<Button>(button => button.IsEnabled));
            });

            if (isLoadedMetadataEditor)
            {
                var splashPointSelectors =
                    FindVisualDescendants<RadioButton>(window)
                        .Where(radioButton =>
                            radioButton.IsVisible &&
                            (Equals(radioButton.Content, "起点") ||
                             Equals(radioButton.Content, "终点")))
                        .ToArray();
                var undoButton = buttons.Single(button =>
                    button.IsVisible && Equals(button.Content, "撤销"));
                var redoButton = buttons.Single(button =>
                    button.IsVisible && Equals(button.Content, "重做"));
                var edit3dSwitch =
                    FindVisualDescendants<ToggleButton>(window)
                        .Single(toggle =>
                            toggle.IsVisible &&
                            ReferenceEquals(
                                toggle.Style,
                                Application.Current.FindResource(
                                    "AeInput.Switch")));

                NUnitAssert.Multiple(() =>
                {
                    NUnitAssert.That(
                        splashPointSelectors,
                        Has.Length.EqualTo(2));
                    NUnitAssert.That(
                        splashPointSelectors.Single(radioButton =>
                            Equals(radioButton.Content, "起点")).IsChecked,
                        Is.True);
                    NUnitAssert.That(edit3dSwitch.IsChecked, Is.False);
                    NUnitAssert.That(
                        splashPointSelectors,
                        Has.All.Matches<RadioButton>(selector =>
                            selector.IsEnabled == false));
                    NUnitAssert.That(undoButton.IsEnabled, Is.False);
                    NUnitAssert.That(redoButton.IsEnabled, Is.False);
                });
            }
        }
    }

    private static void Capture(
        Window window,
        ThemeType theme,
        string variant)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(
                window.ActualWidth * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(
                window.ActualHeight * dpi.DpiScaleY)),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var outputDirectory = Environment.GetEnvironmentVariable(
            "AE_UI_QA_OUTPUT");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            return;

        Directory.CreateDirectory(outputDirectory);
        var path = Path.Combine(
            outputDirectory,
            $"animation-metadata-{variant}-{theme}.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static Brush FindBrush(string key) =>
        (Brush)Application.Current.FindResource(key);

    private static FontFamily FindFontFamily() =>
        (FontFamily)Application.Current.FindResource("AppFontFamily");

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private sealed class GalleryCommand : ICommand
    {
        public static GalleryCommand Instance { get; } = new(true);
        public static GalleryCommand Disabled { get; } = new(false);

        private readonly bool _canExecute;

        private GalleryCommand(bool canExecute)
        {
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute;

        public void Execute(object? parameter)
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class MetadataSuperViewGalleryModel
    {
        public int SelectedTabControllerIndex { get; set; }
        public GalleryModel PersistentMetaEditor { get; } =
            CreateMetadataModel();
        public GalleryModel MetaEditor { get; } = CreateMetadataModel();
        public bool HasPersistentMetaFile { get; }
        public bool HasAnimationMetaFile { get; }
        public bool CanCreatePersistentMetaFile =>
            HasPersistentMetaFile == false;
        public bool CanCreateAnimationMetaFile =>
            HasAnimationMetaFile == false;
        public bool IsPersistentMetaReferenceMissing => false;
        public bool IsAnimationMetaReferenceMissing => false;
        public bool ShowImpactPositions { get; set; } = true;
        public bool ShowTargetPositions { get; set; } = true;
        public bool ShowFirePositions { get; set; } = true;
        public bool ShowSplashAttacks { get; set; } = true;
        public bool ShowCombatMetaDataDuringActiveTime { get; set; } = true;
        public bool ShowCombatMetaDataForEntireAnimation { get; set; }
        public bool CanFocusSelectedMetaData => HasAnimationMetaFile;
        public bool HasSelectedSceneMarkerSettings => HasAnimationMetaFile;
        public bool CanEditSelectedCombatMetaData => HasAnimationMetaFile;
        public bool CanEditSelectedMetaData3D =>
            CanEditSelectedCombatMetaData;
        public bool IsCombatMetaData3dEditingEnabled { get; set; }
        public bool IsImpactMetaDataSelected => false;
        public bool IsTargetMetaDataSelected => false;
        public bool IsFireMetaDataSelected => false;
        public bool IsSplashMetaDataSelected => HasAnimationMetaFile;
        public bool CanUndoCombatMetaData =>
            HasAnimationMetaFile && IsCombatMetaData3dEditingEnabled;
        public bool CanRedoCombatMetaData => false;
        public bool EditSplashStart { get; set; } = true;
        public bool EditSplashEnd { get; set; }
        public bool HasSelectedMetaDataTimeRange => HasAnimationMetaFile;
        public float SelectedMetaDataStartTimeSeconds => 0.35f;
        public float SelectedMetaDataEndTimeSeconds => 0.8f;
        public bool SelectedMetaDataUsesZeroRangeConvention => false;
        public string SelectedMetaDataZeroRangeHint =>
            "0/0：格式没有额外开关，预览按整段持续显示";
        public string SelectedMetaDataStartToolTip =>
            "暂停动画并跳到所选 META 的开始时间";
        public string SelectedMetaDataTimeState => "当前正在生效";
        public bool IsEffectMetaDataSelected => false;
        public string PersistentMetaFilePath =>
            @"animations\battle\example\persistent.meta";
        public string AnimationMetaFilePath =>
            @"animations\battle\example\attack.anm.meta";
        public ICommand FocusSelectedMetaDataActionCommand =>
            GalleryCommand.Instance;
        public ICommand JumpToSelectedMetaDataStartActionCommand =>
            GalleryCommand.Instance;
        public ICommand JumpToSelectedMetaDataEndActionCommand =>
            GalleryCommand.Instance;
        public ICommand UndoCombatMetaDataActionCommand =>
            GalleryCommand.Instance;
        public ICommand RedoCombatMetaDataActionCommand =>
            GalleryCommand.Instance;

        public MetadataSuperViewGalleryModel(
            bool hasPersistentMetaFile,
            bool hasAnimationMetaFile,
            int selectedTabControllerIndex)
        {
            HasPersistentMetaFile = hasPersistentMetaFile;
            HasAnimationMetaFile = hasAnimationMetaFile;
            SelectedTabControllerIndex = selectedTabControllerIndex;
        }

        public void CreatePersistentMetaFile()
        {
        }

        public void CreateAnimationMetaFile()
        {
        }

        public void UndoCombatMetaDataAction()
        {
        }

        public void RedoCombatMetaDataAction()
        {
        }

        public void FocusSelectedMetaDataAction()
        {
        }

        public void JumpToSelectedMetaDataStartAction()
        {
        }

        public void JumpToSelectedMetaDataEndAction()
        {
        }
    }

#pragma warning disable CA1812
    private sealed class GalleryModel
    {
        public object? ActiveFragmentSlot { get; set; }
        public object? ActiveOutputFragment { get; set; }
        public object? AnimationFile { get; set; }
        public object? AnimationName { get; set; }
        public object? AnimationOutputFormats { get; set; }
        public object? AnimationPackItems { get; set; }
        public object? AnimationSettings { get; set; }
        public object? AnimationSpeedMult { get; set; }
        public object? AnimFiles { get; set; }
        public object? AnimPackName { get; set; }
        public object? ApplyRelativeScale { get; set; }
        public object? BoneIndex { get; set; }
        public object? BoneManager { get; set; }
        public object? BoneName { get; set; }
        public object? Bones { get; set; }
        public object? CanAddToFragment { get; set; }
        public object? CanBatchProcess { get; set; }
        public object? CanBatchRetarget { get; set; }
        public object? CanMarkIntentionalUnmapped { get; set; }
        public object? CanPreview { get; set; }
        public object? CanSave { get; set; }
        public object? Children { get; set; }
        public object? Candidates { get; set; }
        public object? ConfirmCandidateCommand { get; set; }
        public object? ConfirmMappingCommand { get; set; }
        public object? ConfirmedCount { get; set; }
        public object? CopyActionCommand { get; set; }
        public object? CreateAnimations { get; set; }
        public object? CreateAnimPack { get; set; }
        public object? CreateFragment { get; set; }
        public object? Data { get; set; }
        public object? DeleteActionCommand { get; set; }
        public object? Description { get; set; }
        public object? DisplayGeneratedMesh { get; set; }
        public object? DisplayGeneratedSkeleton { get; set; }
        public object? DisplayName { get; set; }
        public object? DisplayText { get; set; }
        public object? EnsureUniqeFileName { get; set; }
        public object? FieldName { get; set; }
        public object? FileName { get; set; }
        public object? Filter { get; set; }
        public object? FilterValid { get; set; }
        public object? FitAnimation { get; set; }
        public object? FocusCameraCommand { get; set; }
        public object? FragmentName { get; set; }
        public object? FreezeUnmapped { get; set; }
        public object? GameWorld { get; set; }
        public object? HasMapping { get; set; }
        public object? HeaderName { get; set; }
        public object? IsChanged { get; set; }
        public object? IsControlVisible { get; set; }
        public object? IsDecodedCorrectly { get; set; }
        public object? IsEnabled { get; set; }
        public object? IsExpanded { get; set; }
        public object? IsMappingConfirmed { get; set; }
        public object? IsMappingStructurallyReady { get; set; }
        public object? IsPreviewingCurrentMapping { get; set; }
        public object? IsReadOnly { get; set; }
        public object? IsRootNodeAnimation { get; set; }
        public object? IsSelected { get; set; }
        public object? IsUnknownFile { get; set; }
        public object? IsUsedByCurrentModel { get; set; }
        public object? IsValid { get; set; }
        public object? IsVisible { get; set; }
        public object? IsWh3 { get; set; }
        public object? IntentionalUnmappedCount { get; set; }
        public object? Items { get; set; }
        public object? KeepRiderRotation { get; set; }
        public object? LeftColumnWidth { get; set; }
        public object? LocomotionGraph { get; set; }
        public object? LoopAnimation { get; set; }
        public object? LoopCounter { get; set; }
        public object? LastAutoMappingSummary { get; set; }
        public object? HasPreviewedCurrentMapping { get; set; }
        public object? MappedBoneIndex { get; set; }
        public object? MappedBoneName { get; set; }
        public object? MaxFrames { get; set; }
        public object? MeshBones { get; set; }
        public object? MeshSkeletonName { get; set; }
        public object? MetaDataFileVersion { get; set; }
        public object? MetaEditor { get; set; }
        public object? MetaFile { get; set; }
        public object? MetaFiles { get; set; }
        public object? ModelBoneList { get; set; }
        public object? ModelBoneListForIKEndBone { get; set; }
        public object? MountBin { get; set; }
        public object? MoveDownActionCommand { get; set; }
        public object? MoveUpActionCommand { get; set; }
        public object? Name { get; set; }
        public object? NewActionCommand { get; set; }
        public object? OnlyShowUsedBones { get; set; }
        public object? PackfileList { get; set; }
        public object? ParentModelBones { get; set; }
        public object? ParentSkeletonName { get; set; }
        public object? ReasonText { get; set; }
        public object? ReviewItems { get; set; }
        public object? ReviewRequiredCount { get; set; }
        public object? PasteActionCommand { get; set; }
        public object? PersistentMetaEditor { get; set; }
        public object? PersistentMetaFilePath { get; set; }
        public object? HasPersistentMetaFile { get; set; }
        public object? HasAnimationMetaFile { get; set; }
        public object? CanCreatePersistentMetaFile { get; set; }
        public object? CanCreateAnimationMetaFile { get; set; }
        public object? IsPersistentMetaReferenceMissing { get; set; }
        public object? IsAnimationMetaReferenceMissing { get; set; }
        public object? AnimationMetaFilePath { get; set; }
        public object? Player { get; set; }
        public object? PlayerControlsVisibility { get; set; }
        public object? PlayerItems { get; set; }
        public object? PosOffset { get; set; }
        public object? PossibleOutputFormats { get; set; }
        public object? Process { get; set; }
        public object? Rendering { get; set; }
        public object? ResetCameraCommand { get; set; }
        public object? RightColumnWidth { get; set; }
        public object? RootScale { get; set; }
        public object? Rotation { get; set; }
        public object? RotOffset { get; set; }
        public object? Rows { get; set; }
        public object? SaveActionCommand { get; set; }
        public object? SaveCommand { get; set; }
        public object? SaveManager { get; set; }
        public object? SavePrefix { get; set; }
        public object? SavePrefixText { get; set; }
        public object? Scale { get; set; }
        public object? ScaleOffset { get; set; }
        public object? SceneObjects { get; set; }
        public object? SelectedAnimation { get; set; }
        public object? SelectedAnimationCurrentFrame { get; set; }
        public object? SelectedAnimationCurrentTime { get; set; }
        public object? SelectedAnimationFps { get; set; }
        public object? SelectedAnimationFrameCount { get; set; }
        public object? SelectedAnimationMaxTime { get; set; }
        public object? SelectedAnimationOutputFormat { get; set; }
        public object? SelectedBone { get; set; }
        public object? SelectedItem { get; set; }
        public object? SelectedItemViewModel { get; set; }
        public object? SelectedLegAnimation { get; set; }
        public object? SelectedLegBone { get; set; }
        public object? SelectedMountBone { get; set; }
        public object? SelectedOutputFormat { get; set; }
        public object? SelectedRiderBone { get; set; }
        public object? SelectedTabControllerIndex { get; set; }
        public object? SelectedTag { get; set; }
        public object? SelectedVertexesText { get; set; }
        public object? Settings { get; set; }
        public object? ShowBoneMappingWindowCommand { get; set; }
        public object? ShowManualBoneMappingCommand { get; set; }
        public object? ShowGeneratedMesh { get; set; }
        public object? ShowGeneratedSkeleton { get; set; }
        public object? ShowMesh { get; set; }
        public object? ShowSaveSettingsCommand { get; set; }
        public object? ShowSkeleton { get; set; }
        public object? ShowTransformSection { get; set; }
        public object? ShowWeapon { get; set; }
        public object? SkeletonDisplayOffset { get; set; }
        public object? SkeletonInformation { get; set; }
        public object? SkeletonName { get; set; }
        public object? SkeletonScale { get; set; }
        public object? SlotName { get; set; }
        public object? SlotNames { get; set; }
        public object? SoundFile { get; set; }
        public object? SoundFiles { get; set; }
        public object? StatusText { get; set; }
        public object? SpeedMult { get; set; }
        public object? SubHeaderName { get; set; }
        public object? Tags { get; set; }
        public object? Text { get; set; }
        public object? TextValue { get; set; }
        public object? Translation { get; set; }
        public object? UpdateAnimationCommand { get; set; }
        public object? UnmatchedCount { get; set; }
        public object? BatchRetargetGateText { get; set; }
        public object? MarkIntentionalUnmappedCommand { get; set; }
        public object? TargetBoneName { get; set; }
        public object? Value { get; set; }
        public object? ValueAsString { get; set; }
        public object? Values { get; set; }
        public object? Variables { get; set; }
        public object? VisualOffset { get; set; }
        public object? ZeroUnmappedBones { get; set; }
    }
#pragma warning restore CA1812
}
