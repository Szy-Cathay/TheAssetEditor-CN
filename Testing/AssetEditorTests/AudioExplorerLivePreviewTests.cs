using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Editors.Audio.AudioEditor.Presentation.WaveformVisualiser;
using Editors.Audio.AudioExplorer;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Utilities;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.HircExploration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise.Hirc;
using Shared.Ui.Common.OperationProgress;
using Shared.Ui.Common.ValueConverters;
using Assert = NUnit.Framework.Assert;
using TestContext = NUnit.Framework.TestContext;

namespace AssetEditorTests;

[NonParallelizable]
[Category("DesktopPreview")]
public class AudioExplorerLivePreviewTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    public void RealGameAudio_LoadsBrowsesCancelsAndCloses(ThemeType theme)
    {
        var gameData = Environment.GetEnvironmentVariable("AE_AUDIO_GAME_DATA");
        var output = Environment.GetEnvironmentVariable("AE_UI_QA_OUTPUT");
        if (Environment.GetEnvironmentVariable("AE_UI_LIVE_PREVIEW") != "1" || string.IsNullOrEmpty(gameData))
            Assert.Ignore("Requires a reserved desktop and an installed game supplied through AE_AUDIO_GAME_DATA.");
        Assert.That(output, Is.Not.Null.And.Not.Empty);
        Directory.CreateDirectory(output!);
        var registrations = new ServiceCollection();
        new Shared.Core.DependencyInjectionContainer().Register(registrations);
        var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
        var localization = new LocalizationManager();
        localization.LoadLanguage();
        registrations.AddSingleton(settings).AddSingleton(localization);
        using var services = registrations.BuildServiceProvider();
        var packFiles = services.GetRequiredService<IPackFileService>();
        var loader = new PackFileContainerLoader(settings);
        foreach (var name in new[] { "audio_base_bnk.pack", "audio_en_bnk.pack", "audio_cn_bnk.pack", "audio_base.pack", "audio_en.pack", "audio_cn.pack" })
        {
            var container = loader.Load(Path.Combine(gameData!, name));
            Assert.That(container, Is.Not.Null, name);
            container!.IsCaPackFile = true;
            packFiles.AddContainer(container);
            TestContext.WriteLine($"Pack: {name}, {container.FileList.Count} files.");
        }
        using var repository = new AudioRepository(settings, new BnkLoader(packFiles), new DatLoader(packFiles, settings));
        using var soundPlayer = new SoundPlayer(packFiles, repository, new VgStreamWrapper(), new SoundEngine(packFiles));

        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            Application.Current.Resources["InvBoolConverter"] = new InverseBooleanConverter();
            Application.Current.Resources["BoolToVisibilityConverter"] = new BoolToVisibilityConverter();
            var previousTheme = ThemesController.CurrentTheme;
            var previousMain = Application.Current.MainWindow;
            ThemesController.SetTheme(theme);
            var model = new AudioExplorerViewModel(repository, soundPlayer, new AudioExportDialogService(), new WaveformRendererService(packFiles));
            var view = new AudioExplorerView { DataContext = model };
            var owner = new Window
            {
                Content = new Border(), Title = "音频浏览器 · 真实游戏资源验收", Width = 1000, Height = 680,
                Left = 30, Top = 30, ShowInTaskbar = false, Topmost = true,
            };
            owner.SetResourceReference(Control.BackgroundProperty, "AeBrush.Canvas");
            var clock = Stopwatch.StartNew();
            var frames = new List<(long Time, BitmapSource Image)>();
            var trace = new List<string> { "milliseconds,operation,loading,completed,total,opacity,scale,status" };
            var operation = "initial";
            var lastFrame = -1000L;
            var lastTick = 0L;
            var longestGap = 0L;
            var sawPopup = false;
            var sawIntermediateOpacity = false;
            var sawCount = false;
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(25) };
            timer.Tick += (_, _) =>
            {
                var now = clock.ElapsedMilliseconds;
                longestGap = Math.Max(longestGap, now - lastTick);
                lastTick = now;
                var popup = owner.OwnedWindows.OfType<OperationProgressWindow>().FirstOrDefault();
                var surface = popup?.Template.FindName("WindowSurface", popup) as FrameworkElement;
                var opacity = popup?.Opacity ?? 0;
                var scale = (surface?.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
                sawPopup |= popup?.IsVisible == true;
                sawIntermediateOpacity |= opacity > 0 && opacity < 1;
                sawCount |= model.LoadProgressMaximum > 0 && model.LoadProgressValue > 0;
                trace.Add(FormattableString.Invariant($"{now},{operation},{model.IsLoading},{model.LoadProgressValue},{model.LoadProgressMaximum},{opacity:F3},{scale:F3},{model.LoadStatus.Replace(',', '，')}"));
                if (frames.Count < 250 && (now - lastFrame >= 300 || opacity > 0 && opacity < 1))
                {
                    frames.Add((now, OperationProgressWindowGallery.Capture(owner)));
                    lastFrame = now;
                }
            };
            try
            {
                Application.Current.MainWindow = owner;
                var ownerRendered = false;
                owner.ContentRendered += (_, _) => ownerRendered = true;
                owner.Show();
                PumpUntil(() => ownerRendered);
                clock.Restart();
                timer.Start();
                owner.Content = view;
                PumpUntil(() => model.LoadAudioRepositoryForSelectedLanguagesCommand.ExecutionTask?.IsCompleted == true);
                Assert.That(model.ExplorerFilter.ExplorerList.Values, Is.Not.Empty, model.LoadStatus);
                Assert.That(repository.StatesByStateGroup.ContainsKey("VO_Actor"), Is.True, "Actual DAT metadata must be loaded.");
                Assert.That(sawPopup, Is.True, "Real first load must expose its long operation.");
                Assert.That(sawCount, Is.True, "Real banks must report a measured total.");
                PumpUntil(() => owner.OwnedWindows.Count == 0);
                SavePng(owner, Path.Combine(output!, $"audio-{theme}-ready.png"));
                TestContext.WriteLine($"Initial: {clock.ElapsedMilliseconds} ms, {repository.HircsById.Count} HIRC IDs, {model.ExplorerFilter.ExplorerList.Values.Count} events; {model.LoadStatus}");

                operation = "event";
                model.ExplorerFilter.ExplorerList.SelectedItem = model.ExplorerFilter.ExplorerList.Values.First();
                PumpUntil(() => !model.IsLoading);
                Assert.That(model.TreeList, Is.Not.Empty, model.LoadStatus);

                operation = "hirc-list";
                var listStart = clock.ElapsedMilliseconds;
                model.SearchByHircId = true;
                PumpUntil(() => !model.IsLoading);
                Assert.That(model.ExplorerFilter.ExplorerList.Values.Count, Is.GreaterThanOrEqualTo(repository.HircsById.Count));
                TestContext.WriteLine($"Full object list: {model.ExplorerFilter.ExplorerList.Values.Count} items, {clock.ElapsedMilliseconds - listStart} ms.");

                operation = "actor";
                model.SearchByVOActor = true;
                PumpUntil(() => !model.IsLoading);
                var actor = model.ExplorerFilter.ExplorerList.Values.FirstOrDefault(value => value.DisplayName.Contains("cathay", StringComparison.OrdinalIgnoreCase))
                    ?? model.ExplorerFilter.ExplorerList.Values.First(value => value.DisplayName != "Any");
                model.ExplorerFilter.ExplorerList.SelectedItem = actor;
                PumpUntil(() => !model.IsLoading);
                Assert.That(model.TreeList, Is.Not.Empty, $"{actor.DisplayName}: {model.LoadStatus}");
                TestContext.WriteLine($"Actor: {actor.DisplayName}; {model.LoadStatus}");
                var playable = Descendants(model.TreeList).First(node => node.Hirc is ICAkSound);
                operation = "waveform";
                model.SelectedNode = playable;
                PumpUntil(() => !model.IsAudioPreviewLoading && owner.OwnedWindows.Count == 0);
                Assert.That(model.AudioWaveformBaseImageSource, Is.Not.Null, model.LoadStatus);
                SavePng(owner, Path.Combine(output!, $"audio-{theme}-waveform.png"));
                TestContext.WriteLine($"Decoded actual source: {playable.DisplayName}; {model.TotalPlaybackTime}.");

                operation = "cached-refresh";
                var refresh = model.LoadAudioRepositoryForSelectedLanguagesCommand.ExecuteAsync(null);
                PumpUntil(() => refresh.IsCompleted && owner.OwnedWindows.Count == 0);
                Assert.That(refresh.IsCompletedSuccessfully, Is.True);

                operation = "cancel-language";
                model.Languages.Single(language => language.Language == Wh3Language.Chinese).IsChecked = true;
                var reload = model.LoadAudioRepositoryForSelectedLanguagesCommand.ExecuteAsync(null);
                PumpUntil(() => model.LoadProgressMaximum > 0 || reload.IsCompleted);
                Assert.That(reload.IsCompleted, Is.False, "Cancel must reach actual unfinished work.");
                model.CancelLoadingCommand.Execute(null);
                PumpUntil(() => reload.IsCompleted && owner.OwnedWindows.Count == 0);
                Assert.That(model.LoadStatus, Is.EqualTo(LocalizationManager.Instance.Get("AudioExplorer.LoadCancelled")));

                operation = "owner-close";
                var closingLoad = model.LoadAudioRepositoryForSelectedLanguagesCommand.ExecuteAsync(null);
                PumpUntil(() => model.IsLoading);
                model.Close();
                owner.Close();
                timer.Stop();
                PumpUntil(() => closingLoad.IsCompleted);
                Assert.That(model.IsLoadProgressWindowActive, Is.False);
                Assert.That(Application.Current.Windows.OfType<OperationProgressWindow>(), Is.Empty);
                if (SystemParameters.ClientAreaAnimation && SystemParameters.MinimizeAnimation)
                    Assert.That(sawIntermediateOpacity, Is.True, "Animation must be visible in actual rendered frames.");
                TestContext.WriteLine($"Heartbeat maximum gap: {longestGap} ms; desktop frames: {frames.Count}; actual elapsed: {clock.ElapsedMilliseconds} ms; DPI: {VisualTreeHelper.GetDpi(view).PixelsPerInchX}.");
            }
            finally
            {
                timer.Stop();
                File.WriteAllLines(Path.Combine(output!, $"audio-{theme}-trace.csv"), trace);
                if (frames.Count > 0)
                    OperationProgressWindowGallery.SaveFrames(frames, Path.Combine(output!, $"audio-{theme}-real"));
                model.Close();
                owner.Close();
                Application.Current.MainWindow = previousMain;
                ThemesController.SetTheme(previousTheme);
            }
        });
    }

    private static IEnumerable<HircTreeNode> Descendants(IEnumerable<HircTreeNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Descendants(root.Children))
                yield return child;
        }
    }

    private static void PumpUntil(Func<bool> completed)
    {
        var clock = Stopwatch.StartNew();
        while (!completed() && clock.Elapsed < TimeSpan.FromMinutes(3))
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
            timer.Tick += (_, _) => frame.Continue = false;
            timer.Start();
            Dispatcher.PushFrame(frame);
            timer.Stop();
        }
        Assert.That(completed(), Is.True, "Actual audio workflow did not complete within three minutes.");
    }

    private static void SavePng(Window owner, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(OperationProgressWindowGallery.Capture(owner)));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
