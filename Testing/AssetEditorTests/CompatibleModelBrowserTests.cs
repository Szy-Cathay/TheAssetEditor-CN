using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.EmbeddedResources;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.BaseDialogs.StandardDialog.PackFile;
using Shared.Ui.Common;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class CompatibleModelBrowserTests
{
    [TestCase(ThemeType.DarkTheme)]
    [TestCase(ThemeType.LightTheme)]
    [TestCase(ThemeType.HighContrastDark)]
    [TestCase(ThemeType.HighContrastLight)]
    public async Task DefaultFilter_HidesIncompatibleFilesAndClearsHiddenSelection(ThemeType theme)
    {
        PackFileBrowserWindow? dialog = null;
        Task filterTask = Task.CompletedTask;
        TreeNode? matching = null;
        TreeNode? incompatible = null;
        WithTheme(theme, () =>
        {
            dialog = CreateDialog(file => file.Name == "matching.wsmodel");
            var nodes = dialog.ViewModel.Files.Single().GetAllChildFileNodes();
            matching = nodes.Single(node => node.Name == "matching.wsmodel");
            incompatible = nodes.Single(node => node.Name == "other.wsmodel");
            NUnitAssert.That(nodes.All(node => !node.IsVisible), Is.True);
            NUnitAssert.That(((PackFileBrowserView)dialog.FindName("Browser")).IsEnabled, Is.True);
            dialog.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            filterTask = dialog.FilterTask;
        });

        try
        {
            await filterTask;
            WithTheme(theme, () =>
            {
                var toggle = (CheckBox)dialog!.FindName("FileFilterToggle");
                var confirm = (Button)dialog.FindName("ConfirmButton");
                NUnitAssert.That(toggle.IsChecked, Is.True);
                NUnitAssert.That(matching!.IsVisible, Is.True);
                NUnitAssert.That(incompatible!.IsVisible, Is.False);
                dialog.ViewModel.SelectedItem = matching;
                NUnitAssert.That(confirm.IsEnabled, Is.True);

                toggle.IsChecked = false;
                NUnitAssert.That(incompatible.IsVisible, Is.True);
                dialog.ViewModel.SelectedItem = incompatible;
                NUnitAssert.That(confirm.IsEnabled, Is.True);
                toggle.IsChecked = true;
                NUnitAssert.That(dialog.ViewModel.SelectedItem, Is.Null);
                NUnitAssert.That(confirm.IsEnabled, Is.False);
                NUnitAssert.That(((TextBlock)dialog.FindName("FileFilterStatus")).Text, Does.Contain("1"));
                dialog.ViewModel.SelectedItem = matching;
                dialog.ViewModel.Filter.FilterText = "other";
                NUnitAssert.That(dialog.ViewModel.SelectedItem, Is.Null);
                NUnitAssert.That(confirm.IsEnabled, Is.False);
                dialog.ViewModel.Filter.FilterText = string.Empty;

                dialog.Width = 720;
                dialog.Height = 420;
                dialog.ShowActivated = false;
                dialog.ShowInTaskbar = false;
                dialog.Left = -10000;
                dialog.Top = -10000;
                dialog.Show();
                dialog.Measure(new Size(720, 420));
                dialog.Arrange(new Rect(0, 0, 720, 420));
                dialog.UpdateLayout();
                NUnitAssert.That(toggle.ActualWidth, Is.GreaterThan(0));
                var bitmap = new RenderTargetBitmap(720, 420, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(dialog);
            });
        }
        finally
        {
            WithTheme(theme, () =>
            {
                dialog?.Close();
                dialog?.Dispose();
            });
        }
    }

    [Test]
    public async Task NoMatchingFiles_ExplainsHowToDisplayAllFiles()
    {
        PackFileBrowserWindow? dialog = null;
        Task filterTask = Task.CompletedTask;
        WithTheme(ThemeType.DarkTheme, () =>
        {
            dialog = CreateDialog(_ => false);
            dialog.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            filterTask = dialog.FilterTask;
        });
        try
        {
            await filterTask;
            WithTheme(ThemeType.DarkTheme, () =>
            {
                NUnitAssert.That(dialog!.ViewModel.Files.Single().GetAllChildFileNodes().All(node => !node.IsVisible), Is.True);
                NUnitAssert.That(((TextBlock)dialog.FindName("FileFilterStatus")).Text, Does.Contain("取消上方筛选"));
                ((CheckBox)dialog.FindName("FileFilterToggle")).IsChecked = false;
                NUnitAssert.That(dialog.ViewModel.Files.Single().GetAllChildFileNodes().All(node => node.IsVisible), Is.True);
            });
        }
        finally
        {
            WithTheme(ThemeType.DarkTheme, () =>
            {
                dialog?.Close();
                dialog?.Dispose();
            });
        }
    }

    [Test]
    public async Task ClosingDuringFiltering_CancelsRemainingWorkWithoutReopeningTheDialog()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var examined = 0;
        PackFileBrowserWindow? dialog = null;
        Task filterTask = Task.CompletedTask;
        WithTheme(ThemeType.DarkTheme, () =>
        {
            dialog = CreateDialog(_ =>
            {
                Interlocked.Increment(ref examined);
                started.Set();
                release.Wait();
                return true;
            });
            dialog.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            filterTask = dialog.FilterTask;
        });
        try
        {
            NUnitAssert.That(started.Wait(TimeSpan.FromSeconds(5)), Is.True);
            WithTheme(ThemeType.DarkTheme, () => dialog!.Close());
            release.Set();
            await filterTask;
            NUnitAssert.That(examined, Is.EqualTo(1));
        }
        finally
        {
            release.Set();
            WithTheme(ThemeType.DarkTheme, () =>
            {
                dialog?.Close();
                dialog?.Dispose();
            });
        }
    }

    private static PackFileBrowserWindow CreateDialog(Func<PackFile, bool> matches)
    {
        var owner = new PackFileContainer("models.pack");
        foreach (var name in new[] { "matching.wsmodel", "other.wsmodel" })
            owner.FileList[name] = PackFile.CreateFromBytes(name, []);
        var files = new Mock<IPackFileService>();
        files.Setup(service => service.GetAllPackfileContainers()).Returns([owner]);
        var contextMenu = new Mock<IContextMenuBuilder>();
        contextMenu.SetupGet(builder => builder.Type).Returns(ContextMenuType.None);
        contextMenu.Setup(builder => builder.Build(It.IsAny<IReadOnlyList<TreeNode>>())).Returns([]);
        var factory = new PackFileTreeViewFactory(new ApplicationSettingsService(GameTypeEnum.Warhammer3),
            files.Object, Mock.Of<IEventHub>(), new ContextMenuFactory([contextMenu.Object]));
        return new PackFileBrowserWindow(factory, [".wsmodel"], false, false,
            new BrowseDialogFilter("只显示适配当前骨架的模型（bigcat02）", "取消可显示全部", matches), owner.FileList.ToArray());
    }

    private static void WithTheme(ThemeType theme, Action action)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            if (LocalizationManager.Instance == null)
                new LocalizationManager().LoadLanguage();
            try
            {
                ThemesController.SetTheme(theme);
                if (IconLibrary.SaveFileIcon == null)
                    IconLibrary.Load();
                action();
            }
            finally
            {
                ThemesController.SetTheme(previous);
            }
        });
    }
}
