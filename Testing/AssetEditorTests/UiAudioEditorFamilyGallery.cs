using System.Collections.ObjectModel;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AssetEditor.Services.Settings;
using Editors.Audio.AudioEditor.Presentation;
using Editors.Audio.AudioEditor.Presentation.NewAudioProject;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.AudioEditor.Presentation.Settings;
using Editors.Audio.AudioExplorer;
using Editors.Audio.AudioProjectConverter;
using Editors.Audio.AudioProjectMerger;
using Editors.Audio.DialogueEventMerger;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Wwise;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.Common.ValueConverters;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class UiAudioEditorFamilyGallery
{
    private static readonly string[] Variants =
    [
        "audio-explorer-ready",
        "audio-explorer-busy",
        "audio-editor-workspace",
        "audio-editor-busy",
        "audio-settings",
        "new-project",
        "converter",
        "project-merger",
        "dialogue-merger",
    ];

    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [TestCaseSource(nameof(Cases))]
    public void AudioFamily_RendersRequiredThemeAndState(
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

    [Test]
    public void DialogueMerger_DefaultWindowShowsPrimaryAction()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(
            services,
            () =>
            {
                Application.Current.Resources["InvBoolConverter"] =
                    new InverseBooleanConverter();
                Application.Current.Resources["BoolToVisibilityConverter"] =
                    new Shared.Ui.Common.ValueConverters
                        .BoolToVisibilityConverter();
                var window = new DialogueEventMergerWindow
                {
                    DataContext = CreateDialogueMerger(),
                };
                try
                {
                    window.Show();
                    window.UpdateLayout();
                    var primaryAction = FindVisualDescendants<Button>(window)
                        .Single(button => button.IsDefault);
                    var buttonBottom = primaryAction.TranslatePoint(
                        new Point(0, primaryAction.ActualHeight),
                        window);

                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(
                            window.SizeToContent,
                            Is.EqualTo(SizeToContent.Manual));
                        NUnitAssert.That(primaryAction.IsVisible, Is.True);
                        NUnitAssert.That(
                            buttonBottom.Y,
                            Is.LessThan(window.ActualHeight));
                    });
                }
                finally
                {
                    window.Close();
                }
            });
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
            Application.Current.Resources["InvBoolConverter"] =
                new InverseBooleanConverter();
            Application.Current.Resources["BoolToVisibilityConverter"] =
                new Shared.Ui.Common.ValueConverters
                    .BoolToVisibilityConverter();
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

    private static Window CreateWindow(string variant) => variant switch
    {
        "audio-explorer-ready" => Host(
            new AudioExplorerView
            {
                DataContext = CreateAudioExplorer(false),
            },
            1180,
            720),
        "audio-explorer-busy" => Host(
            new AudioExplorerView
            {
                DataContext = CreateAudioExplorer(true),
            },
            1180,
            720),
        "audio-editor-workspace" => Host(
            new AudioEditorView
            {
                DataContext = CreateAudioEditor(false),
            },
            1280,
            760),
        "audio-editor-busy" => Host(
            new AudioEditorView
            {
                DataContext = CreateAudioEditor(true),
            },
            1280,
            760),
        "audio-settings" => Host(
            new SettingsView
            {
                DataContext = CreateAudioSettings(),
            },
            980,
            640),
        "new-project" => new NewAudioProjectWindow
        {
            DataContext = CreateNewProject(),
        },
        "converter" => new AudioProjectConverterWindow
        {
            DataContext = CreateConverter(),
        },
        "project-merger" => new AudioProjectMergerWindow
        {
            DataContext = CreateProjectMerger(),
        },
        "dialogue-merger" => new DialogueEventMergerWindow
        {
            DataContext = CreateDialogueMerger(),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(variant)),
    };

    private static Window Host(
        FrameworkElement content,
        double width,
        double height) => new()
    {
        Content = content,
        Width = width,
        Height = height,
        Background = (Brush)Application.Current.FindResource(
            "AeBrush.Canvas"),
    };

    private static void ConfigureWindow(Window window, string variant)
    {
        window.Title = variant;
        window.WindowStyle = WindowStyle.None;
        window.ResizeMode = ResizeMode.NoResize;
        window.SizeToContent = SizeToContent.Manual;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;

        if (variant == "new-project")
        {
            window.Width = 560;
            window.Height = 330;
        }
        else if (variant == "converter")
        {
            window.Width = 760;
            window.Height = 540;
        }
        else if (variant == "project-merger")
        {
            window.Width = 720;
            window.Height = 350;
        }
        else if (variant == "dialogue-merger")
        {
            window.Width = 760;
            window.Height = 540;
        }
    }

    private static SampleModel CreateAudioExplorer(bool busy)
    {
        var leafA = Node("play_vo_lord_select_01", false);
        var leafB = Node("play_vo_lord_move_02", false);
        var branch = Node("dialogue_events", true, leafA, leafB);
        var languages = new[]
        {
            new SampleModel
            {
                Language = Wh3Language.EnglishUK,
                IsChecked = true,
            },
            new SampleModel
            {
                Language = Wh3Language.Chinese,
                IsChecked = true,
            },
        };
        var filters = new SampleModel
        {
            Filter = "lord",
            Values = new[] { "全部角色", "帝国将军", "震旦领主" },
            SelectedItem = "帝国将军",
        };

        return new SampleModel
        {
            Languages = languages,
            TreeList = new[] { branch, Node("action_events", true) },
            SelectedNode = leafA,
            SelectedNodeText = "play_vo_lord_select_01",
            WwiseObjectLabel = "声音对象 · 选中 1 项",
            ExplorerFilter = new SampleModel { ExplorerList = filters },
            SearchByDialogueEvent = true,
            SearchByActionEvent = false,
            SearchByVOActor = true,
            SearchByHircId = false,
            IsLoading = busy,
            IsExporting = false,
            IsAudioPlaybackVisible = true,
            IsAudioPreviewLoading = false,
            IsPlayAudioButtonEnabled = !busy,
            IsStopAudioButtonEnabled = true,
            IsExportSelectedAudioEnabled = !busy,
            IsExportSelectedBranchEnabled = !busy,
            IsExportCurrentResultsEnabled = !busy,
            LoadProgressDetail = "battle_group_vocalisation_empire_core.bnk",
            LoadProgressIsIndeterminate = false,
            LoadProgressMaximum = 806d,
            LoadProgressValue = busy ? 30d : 806d,
            LoadProgress = busy ? 42d : 100d,
            LoadStatus = busy
                ? "正在读取英语与简体中文声音库……"
                : "已加载 18,426 个音频对象",
            ExportProgress = 64d,
            PlaybackPositionSeconds = 8d,
            TotalPlaybackSeconds = 21d,
            CurrentPlaybackTime = TimeSpan.FromSeconds(8),
            TotalPlaybackTime = TimeSpan.FromSeconds(21),
        };
    }

    private static SampleModel CreateAudioEditor(bool busy)
    {
        var files = new SampleModel
        {
            AudioFilesExplorerLabel = "音频文件",
            FilterQuery = "empire",
            AudioFilesTree = CreateAudioFilesTree(),
            IsSetAudioFilesButtonEnabled = true,
            IsAddAudioFilesButtonEnabled = true,
        };
        var projectExplorer = new SampleModel
        {
            AudioProjectExplorerLabel = "音频工程",
            SearchQuery = "empire",
            AudioProjectTree = CreateAudioProjectTree(),
            DialogueEventTypes = Enum.GetValues<Wh3DialogueEventType>(),
            DialogueEventProfiles =
                Enum.GetValues<Wh3DialogueEventUnitProfile>(),
            IsDialogueEventFilterEnabled = true,
            ShowDialogueEvents = true,
            ShowActionEvents = true,
            ShowEditedItemsOnly = false,
        };
        var projectEditor = CreateTableModel("待编辑条目", "提交 3 项修改");
        projectEditor.IsEditorVisible = true;
        projectEditor.IsEditing = true;
        projectEditor.IsAddRowButtonEnabled = true;
        projectEditor.IsShowModdedStatesCheckBoxEnabled = true;
        projectEditor.IsShowModdedStatesCheckBoxVisible = true;
        var projectViewer = CreateTableModel("工程条目", string.Empty);
        projectViewer.IsViewerVisible = true;
        projectViewer.IsUpdateRowButtonEnabled = true;
        projectViewer.IsRemoveRowButtonEnabled = true;
        projectViewer.IsContextMenuCopyVisible = true;
        projectViewer.IsContextMenuPasteVisible = true;

        return new SampleModel
        {
            AudioFilesExplorerViewModel = files,
            AudioProjectExplorerViewModel = projectExplorer,
            AudioProjectEditorViewModel = projectEditor,
            AudioProjectViewerViewModel = projectViewer,
            SettingsViewModel = CreateAudioSettings(),
            WaveformVisualiserViewModel = new SampleModel
            {
                HasSelectedAudio = true,
                WaveformVisualiserLabel = "波形预览",
                PlaybackStatus = "正在播放 · empire_general_01.wav",
                PlayPauseLabel = "暂停",
                CurrentPlaybackTime = TimeSpan.FromSeconds(12),
                TotalPlaybackTime = TimeSpan.FromSeconds(34),
                WaveformPixelWidth = 600d,
                WaveformPixelHeight = 72d,
            },
            HasSelectedAudio = true,
            IsAudioProjectLoaded = true,
            IsBusy = busy,
            IsLoading = busy,
            IsCompiling = false,
            IsEditorIdle = !busy,
            CanEditAudioProject = true,
            IsSettingsBorderVisible = true,
            EmptyStateText = "打开或新建音频工程以开始编辑",
            CompileStatus = "工程已保存",
            CompileTargets = new[]
            {
                new SampleModel
                {
                    DisplayName = @"所有语言（audio\wwise）",
                    Target = "all",
                    Command = SampleCommand.Instance,
                },
                new SampleModel
                {
                    DisplayName = @"中文（audio\wwise\chinese）",
                    Target = "chinese",
                    Command = SampleCommand.Instance,
                },
            },
            OperationDetail = @"D:\AudioProjects\empire_general_voice.aeaudio",
            OperationProgressIsIndeterminate = false,
            OperationProgressMaximum = 428d,
            OperationProgressValue = 176d,
            OperationStatus = "正在读取音频工程 176 / 428",
        };
    }

    private static SampleModel CreateAudioSettings() => new()
    {
        IsSettingsVisible = true,
        ShowSettingsFromAudioProjectViewer = true,
        WavPackFileName = "empire_general_voice.pack",
        WavPackFilePath = @"D:\Modding\audio\empire_general_voice.pack",
        AudioFiles = new[]
        {
            "empire_general_01.wav",
            "empire_general_02.wav",
            "empire_general_03.wav",
        },
        ContainerTypes = Enum.GetValues<ContainerType>(),
        RandomTypes = Enum.GetValues<RandomType>(),
        PlayModes = Enum.GetValues<PlayMode>(),
        PlaylistEndBehaviours = Enum.GetValues<PlaylistEndBehaviour>(),
        LoopingTypes = Enum.GetValues<LoopingType>(),
        TransitionTypes = Enum.GetValues<TransitionType>(),
        ContainerType = Enum.GetValues<ContainerType>()[0],
        RandomType = Enum.GetValues<RandomType>()[0],
        PlayMode = Enum.GetValues<PlayMode>()[0],
        PlaylistEndBehaviour = Enum.GetValues<PlaylistEndBehaviour>()[0],
        LoopingType = Enum.GetValues<LoopingType>()[0],
        TransitionType = Enum.GetValues<TransitionType>()[0],
        IsContainerTypeVisible = true,
        IsContainerTypeEnabled = true,
        IsRandomTypeVisible = true,
        IsRandomTypeEnabled = true,
        IsPlayModeVisible = true,
        IsPlayModeEnabled = true,
        IsPlaylistEndBehaviourVisible = true,
        IsPlaylistEndBehaviourEnabled = true,
        IsLoopingTypeVisible = true,
        IsLoopingTypeEnabled = true,
        IsNumberOfLoopsVisible = true,
        IsNumberOfLoopsEnabled = true,
        IsAlwaysResetPlaylistVisible = true,
        IsAlwaysResetPlaylistEnabled = true,
        IsRepetitionIntervalVisible = true,
        IsEnableRepetitionIntervalEnabled = true,
        IsTransitionTypeVisible = true,
        IsTransitionTypeEnabled = true,
        IsTransitionDurationVisible = true,
        IsTransitionDurationEnabled = true,
        IsRemoveAudioFilesEnabled = true,
        IsSetRecommendedVOSettingsEnabled = true,
        NumberOfLoops = 2,
        AlwaysResetPlaylist = true,
        EnableRepetitionInterval = true,
        RepetitionInterval = 1.25d,
        TransitionDuration = 0.15d,
    };

    private static SampleModel CreateNewProject() => new()
    {
        AudioProjectFileName = "empire_general_voice",
        AudioProjectDirectory = @"D:\Modding\AudioProjects",
        Languages = Enum.GetValues<Wh3Language>(),
        SelectedLanguage = Wh3Language.Chinese,
        IsCreating = false,
        IsOkButtonEnabled = true,
        ValidationMessage = "名称不能包含 Windows 保留字符",
        CreationStatus = "准备创建工程",
    };

    private static SampleModel CreateConverter() => new()
    {
        AudioProjectName = "empire_general_voice",
        ExistingAudioProjectPath =
            @"audio\audio_projects\cathay_full_voice.aproj",
        SoundbanksInfoXmlPath = @"D:\Wwise\SoundbanksInfo.xml",
        BnksDirectoryPath = @"D:\Wwise\GeneratedSoundBanks",
        WemsDirectoryPath = @"D:\Wwise\.cache\Windows\SFX",
        OutputDirectoryPath = @"D:\Modding\AudioProjects",
        VOActorSubstring = "empire_general",
        IsAppendingToExistingProject = true,
        IsUsingWwiseProject = true,
        IsBusy = false,
        IsOkButtonEnabled = true,
        Status = "已识别 128 个声音对象",
    };

    private static SampleModel CreateProjectMerger() => new()
    {
        MergedAudioProjectName = "empire_general_complete",
        OutputDirectoryPath = @"D:\Modding\AudioProjects",
        BaseAudioProjectPath = @"D:\Audio\empire_general_base.aeaudio",
        MergingAudioProjectPath = @"D:\Audio\empire_general_patch.aeaudio",
        IsOkButtonEnabled = true,
    };

    private static SampleModel CreateDialogueMerger() => new()
    {
        SoundBankSuffix = "empire_general",
        SoundBankSuffixError = "后缀有效，将输出为新的 SoundBank。",
        ModdedSoundBanks = new[]
        {
            new SampleModel
            {
                FilePath = @"D:\Modding\audio\general_select.bnk",
                IsChecked = true,
            },
            new SampleModel
            {
                FilePath = @"D:\Modding\audio\general_move.bnk",
                IsChecked = true,
            },
            new SampleModel
            {
                FilePath = @"D:\Modding\audio\general_battle.bnk",
                IsChecked = false,
            },
        },
        IsOkButtonEnabled = true,
        IsBusy = false,
        LoadStatus = "已扫描 3 个可合并声音库",
    };

    private static SampleModel Node(
        string name,
        bool isExpanded,
        params SampleModel[] children) => new()
    {
        DisplayName = name,
        Name = name,
        FileName = name,
        IsExpanded = isExpanded,
        Children = children,
    };

    private static IReadOnlyList<AudioFilesTreeNode> CreateAudioFilesTree()
    {
        var dialogue = AudioFilesTreeNode.CreateContainerNode(
            "dialogue",
            AudioFilesTreeNodeType.Directory);
        dialogue.IsExpanded = true;
        dialogue.Children.Add(AudioFilesTreeNode.CreateChildNode(
            "empire_general_01.wav",
            AudioFilesTreeNodeType.WavFile,
            dialogue));
        dialogue.Children.Add(AudioFilesTreeNode.CreateChildNode(
            "empire_general_02.wav",
            AudioFilesTreeNodeType.WavFile,
            dialogue));
        return new[] { dialogue };
    }

    private static IReadOnlyList<AudioProjectTreeNode>
        CreateAudioProjectTree()
    {
        var dialogueEvents = AudioProjectTreeNode.CreateNode(
            "dialogue_events",
            AudioProjectTreeNodeType.DialogueEvents);
        dialogueEvents.IsExpanded = true;
        var general = AudioProjectTreeNode.CreateNode(
            "empire_general",
            AudioProjectTreeNodeType.DialogueEvent,
            parent: dialogueEvents);
        general.IsExpanded = true;
        general.Children.Add(AudioProjectTreeNode.CreateNode(
            "select",
            AudioProjectTreeNodeType.DialogueEvent,
            parent: general));
        general.Children.Add(AudioProjectTreeNode.CreateNode(
            "move",
            AudioProjectTreeNodeType.DialogueEvent,
            parent: general));
        dialogueEvents.Children.Add(general);
        return new[] { dialogueEvents };
    }

    private static SampleModel CreateTableModel(
        string label,
        string commitLabel)
    {
        var table = new DataTable();
        table.Columns.Add("名称");
        table.Columns.Add("类型");
        table.Columns.Add("状态");
        table.Rows.Add("empire_general_select", "DialogueEvent", "已修改");
        table.Rows.Add("empire_general_move", "ActionEvent", "正常");
        table.Rows.Add("empire_general_battle", "DialogueEvent", "新增");

        return new SampleModel
        {
            EditorLabel = label,
            ViewerLabel = label,
            CommitButtonLabel = commitLabel,
            CommitButtonToolTip = "提交当前音频条目修改",
            Table = table,
            DataGridColumns = new ObservableCollection<DataGridColumn>
            {
                new DataGridTextColumn
                {
                    Header = "名称",
                    Binding = new System.Windows.Data.Binding("名称"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                },
                new DataGridTextColumn
                {
                    Header = "类型",
                    Binding = new System.Windows.Data.Binding("类型"),
                    Width = 140,
                },
                new DataGridTextColumn
                {
                    Header = "状态",
                    Binding = new System.Windows.Data.Binding("状态"),
                    Width = 100,
                },
            },
        };
    }

    private static void AssertVisualContracts(Window window, string variant)
    {
        var buttons = FindVisualDescendants<Button>(window).ToArray();
        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(window.ActualWidth, Is.GreaterThan(300));
            NUnitAssert.That(window.ActualHeight, Is.GreaterThan(200));
            NUnitAssert.That(buttons, Is.Not.Empty, variant);
            NUnitAssert.That(buttons, Has.All.Matches<Button>(button =>
                button.ActualHeight >= 0 && button.Style is not null));
            NUnitAssert.That(
                FindVisualDescendants<FrameworkElement>(window),
                Has.None.Matches<FrameworkElement>(element =>
                    double.IsNaN(element.ActualWidth) ||
                    double.IsNaN(element.ActualHeight) ||
                    element.ActualWidth < 0 ||
                    element.ActualHeight < 0));
        });
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
            $"audio-{variant}-{theme}.png");
        using var stream = File.Create(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

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

    private sealed class SampleCommand : ICommand
    {
        public static SampleCommand Instance { get; } = new();

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

#pragma warning disable CA1812
    private sealed class SampleModel
    {
        public object? AddRowsToViewerCommand { get; set; } = SampleCommand.Instance;
        public object? AddToAudioFilesCommand { get; set; } = SampleCommand.Instance;
        public object? AlwaysResetPlaylist { get; set; }
        public object? AudioFiles { get; set; }
        public object? AudioFilesExplorerLabel { get; set; }
        public object? AudioFilesExplorerViewModel { get; set; }
        public object? AudioFilesTree { get; set; }
        public object? AudioProjectDirectory { get; set; }
        public object? AudioProjectEditorViewModel { get; set; }
        public object? AudioProjectExplorerLabel { get; set; }
        public object? AudioProjectExplorerViewModel { get; set; }
        public object? AudioProjectFileName { get; set; }
        public object? AudioProjectName { get; set; }
        public object? AudioProjectTree { get; set; }
        public object? AudioProjectViewerViewModel { get; set; }
        public object? BaseAudioProjectPath { get; set; }
        public object? BnksDirectoryPath { get; set; }
        public object? CancelEditCommand { get; set; } = SampleCommand.Instance;
        public object? CancelOrCloseCommand { get; set; } = SampleCommand.Instance;
        public object? CanEditAudioProject { get; set; }
        public object? Children { get; set; }
        public object? ClearTextCommand { get; set; } = SampleCommand.Instance;
        public object? CloseWindowActionCommand { get; set; } = SampleCommand.Instance;
        public object? CollapseOrExpandTreeCommand { get; set; } = SampleCommand.Instance;
        public object? CommitButtonLabel { get; set; }
        public object? CommitButtonToolTip { get; set; }
        public object? Command { get; set; }
        public object? CompileAudioProjectCancelCommand { get; set; } = SampleCommand.Instance;
        public object? CompileAudioProjectCommand { get; set; } = SampleCommand.Instance;
        public object? CompileStatus { get; set; }
        public object? CompileTargets { get; set; }
        public object? ContainerType { get; set; }
        public object? ContainerTypes { get; set; }
        public object? CopyRowsCommand { get; set; } = SampleCommand.Instance;
        public object? CreateAudioProjectCommand { get; set; } = SampleCommand.Instance;
        public object? CreationStatus { get; set; }
        public object? CurrentPlaybackTime { get; set; }
        public object? DataGridColumns { get; set; }
        public object? DialogueEventFilterDisplayText { get; set; }
        public object? DialogueEventProfiles { get; set; }
        public object? DialogueEventTypes { get; set; }
        public object? DisplayName { get; set; }
        public object? EditorLabel { get; set; }
        public object? EmptyStateText { get; set; }
        public object? EnableRepetitionInterval { get; set; }
        public object? ExistingAudioProjectPath { get; set; }
        public object? ExplorerFilter { get; set; }
        public object? ExplorerList { get; set; }
        public object? ExportAudioCancelCommand { get; set; } = SampleCommand.Instance;
        public object? ExportAudioCommand { get; set; } = SampleCommand.Instance;
        public object? ExportCurrentResultsWav { get; set; }
        public object? ExportCurrentResultsWem { get; set; }
        public object? ExportProgress { get; set; }
        public object? ExportSelectedAudioWav { get; set; }
        public object? ExportSelectedAudioWem { get; set; }
        public object? ExportSelectedBranchWav { get; set; }
        public object? ExportSelectedBranchWem { get; set; }
        public object? FileName { get; set; }
        public object? FilePath { get; set; }
        public object? Filter { get; set; }
        public object? FilterQuery { get; set; }
        public object? GenerateMergedDialogueEventSoundBankCommand { get; set; } = SampleCommand.Instance;
        public object? HasSelectedAudio { get; set; }
        public object? IsAddAudioFilesButtonEnabled { get; set; }
        public object? IsAddRowButtonEnabled { get; set; }
        public object? IsAlwaysResetPlaylistEnabled { get; set; }
        public object? IsAlwaysResetPlaylistVisible { get; set; }
        public object? IsAudioPlaybackVisible { get; set; }
        public object? IsAudioPreviewLoading { get; set; }
        public object? IsAudioProjectLoaded { get; set; }
        public object? IsAppendingToExistingProject { get; set; }
        public object? IsBusy { get; set; }
        public object? IsChecked { get; set; }
        public object? IsCompiling { get; set; }
        public object? IsContainerTypeEnabled { get; set; }
        public object? IsContainerTypeVisible { get; set; }
        public object? IsContextMenuCopyVisible { get; set; }
        public object? IsContextMenuPasteVisible { get; set; }
        public object? IsCreating { get; set; }
        public object? IsDialogueEventFilterEnabled { get; set; }
        public object? IsEditing { get; set; }
        public object? IsEditorIdle { get; set; }
        public object? IsEditorVisible { get; set; }
        public object? IsEnableRepetitionIntervalEnabled { get; set; }
        public object? IsExpanded { get; set; }
        public object? IsExportCurrentResultsEnabled { get; set; }
        public object? IsExporting { get; set; }
        public object? IsExportSelectedAudioEnabled { get; set; }
        public object? IsExportSelectedBranchEnabled { get; set; }
        public object? IsLoading { get; set; }
        public object? IsLoopingTypeEnabled { get; set; }
        public object? IsLoopingTypeVisible { get; set; }
        public object? IsNumberOfLoopsEnabled { get; set; }
        public object? IsNumberOfLoopsVisible { get; set; }
        public object? IsOkButtonEnabled { get; set; }
        public object? IsPlaylistEndBehaviourEnabled { get; set; }
        public object? IsPlaylistEndBehaviourVisible { get; set; }
        public object? IsPlayAudioButtonEnabled { get; set; }
        public object? IsPlayModeEnabled { get; set; }
        public object? IsPlayModeVisible { get; set; }
        public object? IsRandomTypeEnabled { get; set; }
        public object? IsRandomTypeVisible { get; set; }
        public object? IsRemoveAudioFilesEnabled { get; set; }
        public object? IsRemoveRowButtonEnabled { get; set; }
        public object? IsRepetitionIntervalVisible { get; set; }
        public object? IsSetAudioFilesButtonEnabled { get; set; }
        public object? IsSetRecommendedVOSettingsEnabled { get; set; }
        public object? IsSettingsBorderVisible { get; set; }
        public object? IsSettingsVisible { get; set; }
        public object? IsShowModdedStatesCheckBoxEnabled { get; set; }
        public object? IsShowModdedStatesCheckBoxVisible { get; set; }
        public object? IsStopAudioButtonEnabled { get; set; }
        public object? IsTransitionDurationEnabled { get; set; }
        public object? IsTransitionDurationVisible { get; set; }
        public object? IsTransitionTypeEnabled { get; set; }
        public object? IsTransitionTypeVisible { get; set; }
        public object? IsUpdateRowButtonEnabled { get; set; }
        public object? IsUsingWwiseProject { get; set; }
        public object? IsViewerVisible { get; set; }
        public object? Language { get; set; }
        public object? Languages { get; set; }
        public object? LoadAudioProjectCancelCommand { get; set; } = SampleCommand.Instance;
        public object? LoadAudioProjectCommand { get; set; } = SampleCommand.Instance;
        public object? LoadAudioRepositoryForSelectedLanguagesCancelCommand { get; set; } = SampleCommand.Instance;
        public object? LoadAudioRepositoryForSelectedLanguagesCommand { get; set; } = SampleCommand.Instance;
        public object? LoadProgress { get; set; }
        public object? LoadProgressDetail { get; set; }
        public object? LoadProgressIsIndeterminate { get; set; }
        public object? LoadProgressMaximum { get; set; }
        public object? LoadProgressValue { get; set; }
        public object? LoadStatus { get; set; }
        public object? LoopingType { get; set; }
        public object? LoopingTypes { get; set; }
        public object? MergeAudioProjectsCommand { get; set; } = SampleCommand.Instance;
        public object? MergedAudioProjectName { get; set; }
        public object? MergingAudioProjectPath { get; set; }
        public object? ModdedSoundBanks { get; set; }
        public object? Name { get; set; }
        public object? NewAudioProjectCommand { get; set; } = SampleCommand.Instance;
        public object? NumberOfLoops { get; set; }
        public object? OpenAudioProjectConverterCommand { get; set; } = SampleCommand.Instance;
        public object? OpenAudioProjectMergerCommand { get; set; } = SampleCommand.Instance;
        public object? OpenDialogueEventMergerCommand { get; set; } = SampleCommand.Instance;
        public object? OperationDetail { get; set; }
        public object? OperationProgressIsIndeterminate { get; set; }
        public object? OperationProgressMaximum { get; set; }
        public object? OperationProgressValue { get; set; }
        public object? OperationStatus { get; set; }
        public object? OutputDirectoryPath { get; set; }
        public object? PasteRowsCommand { get; set; } = SampleCommand.Instance;
        public object? PlaybackPositionSeconds { get; set; }
        public object? PlaybackStatus { get; set; }
        public object? PlaylistEndBehaviour { get; set; }
        public object? PlaylistEndBehaviours { get; set; }
        public object? PlayAudioCommand { get; set; } = SampleCommand.Instance;
        public object? PlayMode { get; set; }
        public object? PlayModes { get; set; }
        public object? PlayPauseCommand { get; set; } = SampleCommand.Instance;
        public object? PlayPauseLabel { get; set; }
        public object? ProcessAudioProjectConversionCommand { get; set; } = SampleCommand.Instance;
        public object? RandomType { get; set; }
        public object? RandomTypes { get; set; }
        public object? RefreshSourceIdsCommand { get; set; } = SampleCommand.Instance;
        public object? RemoveRowCommand { get; set; } = SampleCommand.Instance;
        public object? RemoveSelectedAudioFilesCommand { get; set; } = SampleCommand.Instance;
        public object? RepetitionInterval { get; set; }
        public object? ResetFiltersCommand { get; set; } = SampleCommand.Instance;
        public object? ResetSearchQueryCommand { get; set; } = SampleCommand.Instance;
        public object? ResetSettingsCommand { get; set; } = SampleCommand.Instance;
        public object? SaveAudioProjectCommand { get; set; } = SampleCommand.Instance;
        public object? SearchByActionEvent { get; set; }
        public object? SearchByDialogueEvent { get; set; }
        public object? SearchByHircId { get; set; }
        public object? SearchByVOActor { get; set; }
        public object? SearchQuery { get; set; }
        public object? SelectedDialogueEventProfile { get; set; }
        public object? SelectedDialogueEventType { get; set; }
        public object? SelectedItem { get; set; }
        public object? SelectedLanguage { get; set; }
        public object? SelectedNode { get; set; }
        public object? SelectedNodeText { get; set; }
        public object? SelectAllCommand { get; set; } = SampleCommand.Instance;
        public object? SelectedTreeNodes { get; set; }
        public object? SelectNoneCommand { get; set; } = SampleCommand.Instance;
        public object? SetBaseAudioProjectPathCommand { get; set; } = SampleCommand.Instance;
        public object? SetBnksDirectoryPathCommand { get; set; } = SampleCommand.Instance;
        public object? SetExistingAudioProjectPathCommand { get; set; } = SampleCommand.Instance;
        public object? SetMergingAudioProjectPathCommand { get; set; } = SampleCommand.Instance;
        public object? SetNewFileLocationCommand { get; set; } = SampleCommand.Instance;
        public object? SetOutputDirectoryPathCommand { get; set; } = SampleCommand.Instance;
        public object? SetRecommendedVOSettingsCommand { get; set; } = SampleCommand.Instance;
        public object? SetSoundbanksInfoXmlPathCommand { get; set; } = SampleCommand.Instance;
        public object? SetWemsDirectoryPathCommand { get; set; } = SampleCommand.Instance;
        public object? SettingsViewModel { get; set; }
        public object? SetAudioFilesCommand { get; set; } = SampleCommand.Instance;
        public object? ShowActionEvents { get; set; }
        public object? ShowDialogueEvents { get; set; }
        public object? ShowEditedItemsOnly { get; set; }
        public object? ShowModdedStatesOnly { get; set; }
        public object? ShowSettingsFromAudioProjectViewer { get; set; }
        public object? SoundBankSuffix { get; set; }
        public object? SoundBankSuffixError { get; set; }
        public object? SoundbanksInfoXmlPath { get; set; }
        public object? Status { get; set; }
        public object? StopAudioCommand { get; set; } = SampleCommand.Instance;
        public object? StopCommand { get; set; } = SampleCommand.Instance;
        public object? Table { get; set; }
        public object? TotalPlaybackSeconds { get; set; }
        public object? TotalPlaybackTime { get; set; }
        public object? Target { get; set; }
        public object? TransitionDuration { get; set; }
        public object? TransitionType { get; set; }
        public object? TransitionTypes { get; set; }
        public object? TreeList { get; set; }
        public object? ValidationMessage { get; set; }
        public object? Values { get; set; }
        public object? ViewerLabel { get; set; }
        public object? VOActorSubstring { get; set; }
        public object? WaveformPixelHeight { get; set; }
        public object? WaveformPixelWidth { get; set; }
        public object? WaveformVisualiserLabel { get; set; }
        public object? WaveformVisualiserViewModel { get; set; }
        public object? WavPackFileName { get; set; }
        public object? WavPackFilePath { get; set; }
        public object? WemsDirectoryPath { get; set; }
        public object? WwiseObjectLabel { get; set; }
    }
#pragma warning restore CA1812
}
