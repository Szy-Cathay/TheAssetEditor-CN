using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AssetEditor.ModAssistant;
using AssetEditor.Services.Settings;
using Moq;
using Shared.Core.ModAssistant;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace AssetEditorTests;

[TestClass, DoNotParallelize]
public class ModAssistantRenderTests
{
    internal static AssistantViewModel CreateViewModel(IAssistantModelClient? client = null)
    {
        new LocalizationManager().LoadLanguage();
        var workspace = new Mock<IAssistantWorkspace>();
        workspace.Setup(w => w.GetContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => JsonNode.Parse("""
            {"project":{"name":"武器制作工程"},"bound_editor":"model-1","editors":[{"id":"model-1","name":"cathay_blade.rigid_model_v2","dirty":true}]}
            """)!);
        var vm = new AssistantViewModel(client ?? Mock.Of<IAssistantModelClient>(), workspace.Object, new AssistantSettingsViewModel(Mock.Of<IAssistantSecretStore>()));
        vm.RefreshContextCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return vm;
    }

    [TestMethod]
    public void Assistant_AgentWorkspaceRendersAllStatesInFourThemesWithoutDesktopInput()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var previous = ThemesController.CurrentTheme;
            try
            {
                foreach (var theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight })
                    foreach (var width in new[] { 400, 1200 })
                    {
                        ThemesController.SetTheme(theme);
                        using var vm = CreateViewModel();
                        var view = new AssistantView { DataContext = vm };
                        Layout(view, width);
                        Assert.AreEqual(width < 960, vm.IsCompact);
                        Assert.AreEqual(width >= 960, vm.HistoryOpen);
                        Assert.IsInstanceOfType(AutomationProperties.GetLabeledBy(view.Prompt), typeof(Label));
                        Assert.IsTrue(view.Prompt.AcceptsReturn);
                        Capture(view, theme, width, "welcome");
                        vm.CurrentSession.Title = "调整武器部件并检查动画";
                        vm.Messages.Add(new AssistantMessage { IsUser = true, Role = "用户", Text = "把选中的刀刃缩小到原来的 80%，然后播放当前动画检查。" });
                        var answer = new AssistantMessage { Role = vm.DisplayName, TargetSummary = vm.TargetSummary + " · " + vm.SaveSummary, Usage = "输入 328 · 输出 96" };
                        answer.AppendReasoning("先检查当前对象、缩放值和动画状态，再调用对应操作。这里只是渲染用的模拟响应。");
                        answer.AppendText("已找到选中的刀刃部件，正在调整缩放并检查预览。\n");
                        answer.AddOperation(new AssistantOperationCard(JsonNode.Parse("""
                        {"target":"mesh-1","operation":"kitbash.transform","effect":"Edit","description":"缩放选中的刀刃部件","status":"completed","before":{"scale":1},"after":{"scale":0.8,"dirty":true,"editor":{"name":"cathay_blade.rigid_model_v2"}}}
                        """)!, (_, _) => Task.CompletedTask));
                        answer.AppendText("**刀刃已缩小到原来的 80%。**\n\n- 当前动画已开始播放。\n- 修改保留在编辑器中，**尚未保存**。\n\n可以继续调整，或让我保存当前模型。");
                        answer.Sources.Add(new AssistantSource("DeepSeek 官方接口文档", "https://api-docs.deepseek.com/zh-cn/"));
                        vm.Messages.Add(answer);
                        vm.Status = "已完成";
                        Layout(view, width);
                        Capture(view, theme, width, "conversation");
                        foreach (var item in AnimPackWorkflowTests.Descendants<Expander>(view).Where(e => e.DataContext is AssistantOperationCard)) item.IsExpanded = true;
                        Layout(view, width);
                        Capture(view, theme, width, "details");
                        vm.IsBusy = true; vm.Status = "正在检查动画预览";
                        Layout(view, width);
                        Assert.AreEqual(Visibility.Visible, view.StopButton.Visibility);
                        Assert.AreEqual(Visibility.Collapsed, view.SendButton.Visibility);
                        Assert.IsTrue(AnimPackWorkflowTests.Descendants<ListBoxItem>(view.SessionList).All(i => !i.IsEnabled));
                        Assert.AreEqual("", vm.Announcement);
                        PumpFor(TimeSpan.FromMilliseconds(600));
                        Assert.AreEqual(Visibility.Visible, view.TaskProgress.Visibility);
                        Capture(view, theme, width, "running");
                        vm.IsBusy = false;
                        answer.Outcome = "已取消。缩放修改已经完成；保存步骤未执行，模型仍未保存。";
                    vm.Status = "已取消";
                        PumpFor(TimeSpan.FromMilliseconds(350));
                        Capture(view, theme, width, "cancelled");
                        var preview = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgr32, null,
                            new byte[] { 180, 100, 40, 0, 180, 100, 40, 0, 180, 100, 40, 0, 180, 100, 40, 0 }, 8);
                        preview.Freeze(); vm.Attachment = preview; vm.AttachmentName = "测试截图.png";
                        Capture(view, theme, width, "attachment");
                        vm.RemoveAttachmentCommand.Execute(null);
                        foreach (var panel in new[] { "context", "settings" })
                        {
                            vm.ShowPanelCommand.Execute(panel); Layout(view, width);
                            Assert.AreEqual(Visibility.Collapsed, view.ConversationPage.Visibility);
                            Capture(view, theme, width, panel);
                        }
                        vm.ShowPanelCommand.Execute("conversation");
                        vm.HistoryOpen = true; Layout(view, width);
                        Assert.AreEqual(width >= 960, vm.ShowWorkspace);
                        Capture(view, theme, width, "history");
                        view.DataContext = null;
                    }
            }
            finally { ThemesController.SetTheme(previous); }
        });
    }

    [TestMethod]
    public void Assistant_ReadingHistoryDoesNotJumpWhenAnotherMessageArrives()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var vm = CreateViewModel();
            for (var i = 0; i < 16; i++) vm.Messages.Add(new AssistantMessage { Text = $"第 {i + 1} 条响应\n\n这是一段用于检查长对话滚动的文字。" });
            var view = new AssistantView { DataContext = vm };
            Layout(view, 400);
            view.ConversationScroll.ScrollToEnd(); Layout(view, 400);
            Assert.IsTrue(view.ConversationScroll.VerticalOffset > 0);
            view.ConversationScroll.ScrollToTop(); Layout(view, 400);
            Assert.AreEqual(0, view.ConversationScroll.VerticalOffset);
            vm.Messages.Add(new AssistantMessage { Text = "新的流式响应到达，但用户仍在阅读旧消息。" });
            Layout(view, 400);
            Assert.AreEqual(0, view.ConversationScroll.VerticalOffset);
            Assert.AreEqual(Visibility.Visible, view.JumpToLatest.Visibility);
            view.JumpToLatest.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Layout(view, 400);
            Assert.AreEqual(view.ConversationScroll.ScrollableHeight, view.ConversationScroll.VerticalOffset);
            Assert.AreEqual(Visibility.Collapsed, view.JumpToLatest.Visibility);
            view.DataContext = null;
        });
    }

    [TestMethod]
    public void Assistant_MarkdownHasSelectableFormattingAndCannotLoadModelSuppliedImagesOrLocalLinks()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var view = new AssistantMarkdownView { Text = "## 修改结果\n\n**已完成**\n\n- 模型尚未保存\n- 预览已启动\n\n`scale = 0.8`\n\n[来源](https://api-docs.deepseek.com/)" };
            view.RenderText();
            Assert.IsTrue(view.Document.Blocks.OfType<List>().Any());
            Assert.IsTrue(new TextRange(view.Document.ContentStart, view.Document.ContentEnd).Text.Contains("修改结果"));
            Assert.IsTrue(view.IsSelectionEnabled);
            view.Text = "![external](http://127.0.0.1:1/private.png)\n![local](file:///C:/private.png)\n<img src='https://example.com/private'/>";
            view.RenderText();
            Assert.AreEqual(1, view.Document.Blocks.Count);
            var paragraph = (Paragraph)view.Document.Blocks.FirstBlock;
            Assert.IsTrue(paragraph.Inlines.All(i => i is Run));
            Assert.IsFalse(AssistantMarkdownView.IsWebLink("file:///C:/private"));
            Assert.IsFalse(AssistantMarkdownView.IsWebLink("javascript:alert(1)"));
            Assert.IsFalse(AssistantMarkdownView.IsWebLink("cmd.exe"));
            Assert.IsTrue(AssistantMarkdownView.IsWebLink("https://api-docs.deepseek.com/"));
            Assert.IsFalse(AssistantView.IsSendGesture(Key.Enter, ModifierKeys.None));
            Assert.IsFalse(AssistantView.IsSendGesture(Key.ImeProcessed, ModifierKeys.Control));
            Assert.IsTrue(AssistantView.IsSendGesture(Key.Enter, ModifierKeys.Control));
        });
    }

    private static void Layout(AssistantView view, int width)
    {
        view.Measure(new Size(width, 760)); view.Arrange(new Rect(0, 0, width, 760)); view.UpdateLayout();
        foreach (var markdown in AnimPackWorkflowTests.Descendants<AssistantMarkdownView>(view)) markdown.RenderText();
        view.UpdateLayout();
        Assert.IsFalse(AnimPackWorkflowTests.Descendants<TextBlock>(view).Any(t => t.Text.StartsWith("Assistant.")), "Missing Chinese resource");
        if (view.DataContext is AssistantViewModel { Panel: "conversation", ShowWorkspace: true })
        {
            var origin = view.Composer.TranslatePoint(new Point(), view);
            Assert.IsTrue(origin.X >= 0 && origin.X + view.Composer.ActualWidth <= width + 1);
            Assert.IsTrue(origin.Y >= 0 && origin.Y + view.Composer.ActualHeight <= 760);
            Assert.IsTrue(view.Prompt.ActualWidth > 200);
            Assert.IsTrue(view.SendButton.ActualWidth >= 24 || view.StopButton.ActualWidth >= 24);
        }
    }

    private static void PumpFor(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(duration, DispatcherPriority.Background, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
    }

    private static void Capture(AssistantView view, ThemeType theme, int width, string state)
    {
        Layout(view, width);
        foreach (var scale in new[] { 1.0, 1.5 })
        {
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(760 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var output = Environment.GetEnvironmentVariable("ASSETEDITOR_ASSISTANT_RENDER_OUTPUT");
            if (string.IsNullOrWhiteSpace(output)) continue;
            Directory.CreateDirectory(output);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, $"agent-{theme}-{width}-{state}-{scale * 100:0}.png")); encoder.Save(stream);
        }
    }
}
