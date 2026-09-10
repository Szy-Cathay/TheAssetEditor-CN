using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Editors.ImportExport.Exporting.Exporters;
using Editors.ImportExport.Exporting.Presentation;
using Editors.ImportExport.Importing;
using Editors.ImportExport.Importing.Presentation;
using Editors.ImportExport.Misc;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;
using Shared.Ui.BaseDialogs.StandardDialog;
using Shared.Ui.BaseDialogs.StandardDialog.PackFile;
using Shared.Ui.Common.OperationProgress;
using WindowHandling;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class DialogWorkflowRegressionTests
{
    [TestCase(MessageBoxButton.OK, MessageBoxResult.OK)]
    [TestCase(MessageBoxButton.OKCancel, MessageBoxResult.Cancel)]
    [TestCase(MessageBoxButton.YesNo, MessageBoxResult.No)]
    [TestCase(MessageBoxButton.YesNoCancel, MessageBoxResult.Cancel)]
    public void Confirmation_TitleBarCloseReturnsSafeResult(
        MessageBoxButton buttons,
        MessageBoxResult expected)
    {
        WithWindows(() =>
        {
            Later(() => CloseTitleBar(Application.Current.Windows
                .OfType<MessageDialogWindow>().Single()));
            var result = UnifiedMessageBox.Show("关闭确认测试", "确认", buttons);
            NUnitAssert.That(result, Is.EqualTo(expected));
        });
    }

    [TestCase("YesButton", true)]
    [TestCase("NoButton", false)]
    [TestCase("btnClose", false)]
    public void OverwriteConfirmation_OnlyYesAcceptsSave(string buttonName, bool expected)
    {
        WithWindows(() =>
        {
            var files = new Mock<IPackFileService>();
            files.Setup(service => service.GetAllPackfileContainers()).Returns([]);
            files.Setup(service => service.GetFullPath(
                    It.IsAny<PackFile>(), It.IsAny<PackFileContainer>()))
                .Returns("test\\existing.anim");
            var contextMenu = new Mock<IContextMenuBuilder>();
            contextMenu.SetupGet(builder => builder.Type).Returns(ContextMenuType.Simple);
            var factory = new PackFileTreeViewFactory(
                new ApplicationSettingsService(GameTypeEnum.Warhammer3),
                files.Object,
                Mock.Of<IEventHub>(),
                new ContextMenuFactory([contextMenu.Object]));
            using var save = new SavePackFileWindow(files.Object, factory)
            {
                CurrentFileName = "existing.anim",
                SelectedFile = new PackFile("existing.anim", null!),
            };
            Later(() =>
            {
                Later(() =>
                {
                    var confirmation = Application.Current.Windows
                        .OfType<MessageDialogWindow>().Single();
                    if (buttonName == "btnClose")
                        CloseTitleBar(confirmation);
                    else
                        Click((Button)confirmation.FindName(buttonName));
                });
                Click(FindChildren<Button>(save).Single(button => button.IsDefault));
                if (save.IsVisible)
                    save.Close();
            });

            NUnitAssert.That(save.ShowDialog() == true, Is.EqualTo(expected));
            NUnitAssert.That(save.FilePath,
                expected ? Is.EqualTo("test\\existing.anim") : Is.Null.Or.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StandardDialog_UsesActiveChildAsOwner(bool textInput)
    {
        WithWindows(() =>
        {
            using var child = new AssetEditorWindow
            {
                Title = "子操作窗口", Width = 400, Height = 250,
                Left = -10000, Top = -10000, ShowInTaskbar = false,
            };
            var dialogs = new StandardDialogs(null!, null!, null!, null!, null!, null!);
            Window? actualOwner = null;
            Later(() =>
            {
                child.Activate();
                Later(() =>
                {
                    var dialog = Application.Current.Windows.OfType<Window>()
                        .Single(window => window != child && window != Application.Current.MainWindow);
                    actualOwner = dialog.Owner;
                    dialog.Close();
                });
                if (textInput)
                    dialogs.ShowTextInputDialog("输入测试");
                else
                    dialogs.ShowDialogBox("子窗口发起的提示", "提示");
                child.Close();
            });
            child.ShowDialog();
            NUnitAssert.That(actualOwner, Is.SameAs(child));
        });
    }

    [TestCase(true, false, false)]
    [TestCase(true, true, false)]
    [TestCase(false, false, false)]
    [TestCase(false, true, false)]
    [TestCase(true, false, true)]
    public void ImportExport_KeepsWindowUntilWorkAndProgressFinish(
        bool import, bool fail, bool cancel)
    {
        var inputPath = Path.GetTempFileName();
        try
        {
            WithWindows(() =>
            {
                using var release = new ManualResetEventSlim();
                var dialogs = new Mock<IStandardDialogs>();
                var notifications = 0;
                var progressAtNotification = -1;
                var activeAtNotification = true;
                Func<bool> isActive;
                Window window;
                if (import)
                {
                    var importer = new Mock<IImporterViewModel>();
                    importer.SetupGet(item => item.SupportsCancellation).Returns(true);
                    importer.Setup(item => item.CanImportFile(It.IsAny<PackFile>()))
                        .Returns(ImportSupportEnum.HighPriority);
                    importer.Setup(item => item.Execute(
                            It.IsAny<PackFile>(), It.IsAny<string>(),
                            It.IsAny<PackFileContainer>(), It.IsAny<GameTypeEnum>(),
                            It.IsAny<IProgress<OperationProgressUpdate>>(),
                            It.IsAny<CancellationToken>()))
                        .Returns((PackFile source, string path, PackFileContainer container,
                            GameTypeEnum game, IProgress<OperationProgressUpdate> progress,
                            CancellationToken token) =>
                        {
                            WaitForRelease(release, token, fail);
                            return ImportResult.Success(["test\\result.anim"]);
                        });
                    var viewModel = new ImporterCoreViewModel([importer.Object],
                        new ApplicationSettingsService(GameTypeEnum.Warhammer3));
                    viewModel.Initialize(new PackFileContainer("test"), "test", inputPath);
                    isActive = () => viewModel.IsOperationActive;
                    window = new ImportWindow(viewModel, dialogs.Object);
                }
                else
                {
                    var exporter = new Mock<IExporterViewModel>();
                    exporter.Setup(item => item.CanExportFile(It.IsAny<PackFile>()))
                        .Returns(ExportSupportEnum.HighPriority);
                    exporter.Setup(item => item.Execute(It.IsAny<PackFile>(), It.IsAny<string>()))
                        .Returns(() =>
                        {
                            WaitForRelease(release, CancellationToken.None, fail);
                            return true;
                        });
                    var viewModel = new ExporterCoreViewModel([exporter.Object]);
                    viewModel.Initialize(new PackFile("source.anim", null!));
                    viewModel.SystemPath = inputPath;
                    isActive = () => viewModel.IsOperationActive;
                    window = new ExportWindow(viewModel, dialogs.Object);
                }

                void RecordNotification()
                {
                    notifications++;
                    progressAtNotification = ProgressCount(window);
                    activeAtNotification = isActive();
                }
                dialogs.Setup(item => item.ShowExceptionWindow(It.IsAny<Exception>(), It.IsAny<string>()))
                    .Callback(RecordNotification);
                dialogs.Setup(item => item.ShowDialogBox(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<UiMessageBoxIcon>()))
                    .Callback(RecordNotification);
                window.Left = -10000;
                window.Top = -10000;
                window.ShowInTaskbar = false;
                window.Show();
                var start = (Button)window.FindName(import ? "ImportButton" : "ExportButton");
                try
                {
                    Click(start);
                    PumpUntil(() => ProgressCount(window) == 1);
                    CloseTitleBar(window);
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(window.IsVisible, Is.True, "Running work must keep its owner visible.");
                        NUnitAssert.That(isActive(), Is.True);
                        NUnitAssert.That(FindChildren<Button>(window)
                            .Single(button => button.IsCancel).IsEnabled, Is.False);
                    });

                    if (cancel)
                    {
                        var progressHost = ((Grid)window.Content).Children
                            .OfType<OperationProgressWindowHost>().Single();
                        progressHost.CancelCommand!.Execute(null);
                    }
                    else
                        release.Set();
                    PumpUntil(() => !isActive() && start.IsEnabled);
                    NUnitAssert.Multiple(() =>
                    {
                        NUnitAssert.That(ProgressCount(window), Is.Zero);
                        NUnitAssert.That(window.IsVisible, Is.EqualTo(fail || cancel));
                        NUnitAssert.That(notifications, Is.EqualTo(cancel ? 0 : import || fail ? 1 : 0));
                        if (notifications > 0)
                        {
                            NUnitAssert.That(progressAtNotification, Is.Zero);
                            NUnitAssert.That(activeAtNotification, Is.False);
                        }
                    });
                }
                finally
                {
                    release.Set();
                    PumpUntil(() => !isActive() && start.IsEnabled);
                    window.Close();
                }
            });
        }
        finally
        {
            File.Delete(inputPath);
        }
    }

    private static void WaitForRelease(ManualResetEventSlim release, CancellationToken token, bool fail)
    {
        if (!release.Wait(TimeSpan.FromSeconds(10), token))
            throw new TimeoutException("The test did not release the operation.");
        token.ThrowIfCancellationRequested();
        if (fail)
            throw new InvalidOperationException("测试异常");
    }

    private static void WithWindows(Action action)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var application = Application.Current;
            var previousMain = application.MainWindow;
            var previousSelector = application.Resources["ViewTemplateDataSelector"];
            application.Resources["ViewTemplateDataSelector"] = new DataTemplateSelector();
            using var root = new AssetEditorWindow
            {
                Width = 600, Height = 400, Left = -10000, Top = -10000,
                ShowInTaskbar = false,
            };
            application.MainWindow = root;
            root.Show();
            try
            {
                action();
            }
            finally
            {
                foreach (var window in application.Windows.OfType<Window>().Where(item => item != previousMain).ToArray())
                    window.Close();
                application.MainWindow = previousMain;
                if (previousSelector == null)
                    application.Resources.Remove("ViewTemplateDataSelector");
                else
                    application.Resources["ViewTemplateDataSelector"] = previousSelector;
            }
        });
    }

    private static void Later(Action action) =>
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action);

    private static void CloseTitleBar(Window window)
    {
        window.UpdateLayout();
        Click((Button)window.Template.FindName("btnClose", window));
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static int ProgressCount(Window owner) => Application.Current.Windows
        .OfType<OperationProgressWindow>().Count(window => window.Owner == owner && window.IsVisible);

    private static void PumpUntil(Func<bool> completed)
    {
        var timeout = Stopwatch.StartNew();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(10),
        };
        timer.Tick += (_, _) => frame.Continue = !completed() && timeout.Elapsed < TimeSpan.FromSeconds(10);
        timer.Start();
        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timer.Stop();
        }
        NUnitAssert.That(completed(), Is.True, "The window workflow timed out.");
    }

    private static IEnumerable<T> FindChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindChildren<T>(child))
                yield return descendant;
        }
    }
}
