using System.Text.RegularExpressions;
using NUnit.Framework;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

public class UiAudioEditorFamilyTests
{
    private static readonly string[] AudioXamlPaths =
    [
        "AudioEditor/Presentation/AudioEditorView.xaml",
        "AudioEditor/Presentation/AudioFilesExplorer/AudioFilesExplorerView.xaml",
        "AudioEditor/Presentation/AudioProjectEditor/AudioProjectEditorView.xaml",
        "AudioEditor/Presentation/AudioProjectExplorer/AudioProjectExplorerView.xaml",
        "AudioEditor/Presentation/AudioProjectViewer/AudioProjectViewerView.xaml",
        "AudioEditor/Presentation/NewAudioProject/NewAudioProjectWindow.xaml",
        "AudioEditor/Presentation/Settings/SettingsView.xaml",
        "AudioEditor/Presentation/WaveformVisualiser/WaveformVisualiserView.xaml",
        "AudioExplorer/AudioExplorerView.xaml",
        "AudioProjectConverter/AudioProjectConverterWindow.xaml",
        "AudioProjectMerger/AudioProjectMergerWindow.xaml",
        "DialogueEventMerger/DialogueEventMergerWindow.xaml",
    ];

    private static readonly Regex LegacyThemeResource = new(
        @"\{DynamicResource\s+(?:ABrush\.|App\.Border|Button\.|TextBox\.|TreeView\.|TreeViewItem\.|Window\.)",
        RegexOptions.CultureInvariant);

    private static readonly Regex HardcodedThemeColor = new(
        @"(?:Background|Foreground|BorderBrush)\s*=\s*""#",
        RegexOptions.CultureInvariant);

    [Test]
    public void AudioFamily_UsesSemanticThemeAndTypographyResources()
    {
        var sources = ReadAudioSources();

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(sources.Count, Is.EqualTo(12));
            NUnitAssert.That(sources, Has.All.Contains("AeBrush."));
            NUnitAssert.That(sources, Has.All.Contains("AppFontFamily"));
            NUnitAssert.That(sources, Has.All.Contains("AppFontWeight"));
            NUnitAssert.That(
                sources,
                Has.None.Matches<string>(source =>
                    LegacyThemeResource.IsMatch(source)));
            NUnitAssert.That(
                sources,
                Has.None.Matches<string>(source =>
                    HardcodedThemeColor.IsMatch(source)));
        });
    }

    [Test]
    public void AudioFamily_UsesSharedInteractiveControlFamilies()
    {
        var sources = ReadAudioSources();
        var combined = string.Join(
            Environment.NewLine,
            sources.Append(ReadAudioStyleSource()));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(combined, Does.Contain("AeButton.Primary"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Secondary"));
            NUnitAssert.That(combined, Does.Contain("AeButton.Quiet"));
            NUnitAssert.That(combined, Does.Contain("AeInput.TextBox"));
            NUnitAssert.That(combined, Does.Contain("AeInput.ComboBox"));
            NUnitAssert.That(combined, Does.Contain("AeInput.CheckBox"));
            NUnitAssert.That(combined, Does.Contain("AeTree.View"));
            NUnitAssert.That(combined, Does.Contain("AeList.View"));
            NUnitAssert.That(combined, Does.Contain("AeTable.Grid"));
            NUnitAssert.That(combined, Does.Contain("AeProgress.Bar"));
        });
    }

    [Test]
    public void AudioFamily_PreservesBusyProgressAndCollectionContracts()
    {
        var sources = ReadAudioSources();
        var combined = string.Join(Environment.NewLine, sources);

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(combined, Does.Contain("IsBusy"));
            NUnitAssert.That(combined, Does.Contain("IsLoading"));
            NUnitAssert.That(combined, Does.Contain("IsExporting"));
            NUnitAssert.That(combined, Does.Contain("IsCreating"));
            NUnitAssert.That(combined, Does.Contain("VirtualizingPanel"));
            NUnitAssert.That(combined, Does.Contain("SelectionMode=\"Extended\""));
            NUnitAssert.That(combined, Does.Contain("ContextMenu"));
        });
    }

    [Test]
    public void AudioProjectConverter_LoadingUsesRealBusyProgressBindings()
    {
        var source = ReadAudioSources().Single(source =>
            source.Contains("AudioProjectConverter.Title"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                source,
                Does.Contain("IsOperationActive=\"{Binding IsBusy}\""));
            NUnitAssert.That(
                source,
                Does.Contain("CurrentDetailText=\"{Binding ProgressDetail}\""));
            NUnitAssert.That(
                source,
                Does.Contain("ProgressMaximum=\"{Binding ProgressMaximum}\""));
            NUnitAssert.That(
                source,
                Does.Contain("ProgressValue=\"{Binding ProgressValue}\""));
            NUnitAssert.That(
                source,
                Does.Contain("StatusText=\"{Binding Status}\""));
        });
    }

    [Test]
    public void AudioEditor_CompileMenuProvidesExplicitOutputTargets()
    {
        var source = ReadAudioSources().Single(source =>
            source.Contains("AudioEditor.Menu.File.CompileAudioProject"));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                source,
                Does.Contain("ItemsSource=\"{Binding CompileTargets}\""));
            NUnitAssert.That(
                source,
                Does.Contain("Value=\"{Binding Command}\""));
            NUnitAssert.That(
                source,
                Does.Contain("Value=\"{Binding Target}\""));
        });
    }

    private static IReadOnlyList<string> ReadAudioSources()
    {
        var root = FindSolutionRoot();
        return AudioXamlPaths
            .Select(path => File.ReadAllText(Path.Combine(
                root,
                "Editors",
                "Audio",
                path.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();
    }

    private static string ReadAudioStyleSource() => File.ReadAllText(
        Path.Combine(
            FindSolutionRoot(),
            "Editors",
            "Audio",
            "AudioUiStyles.xaml"));

    private static string FindSolutionRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the solution root.");
    }
}
