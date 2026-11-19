using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json.Nodes;
using AssetEditor.Services.Settings;
using Shared.Core.Settings;
using AssetEditor.ModAssistant;

namespace AssetEditorTests;

[TestClass, DoNotParallelize]
public class ModAssistantReadingTests
{
    internal const string LongReply = """
        我可以直接调用编辑器完成查询、修改和导出，并核实结果。修改默认保留在编辑器中，只有你要求保存时才写入文件。

        ## 可以帮你做什么

        - **找资源**：搜索模型、动画、贴图和音频，定位并打开文件。
        - **编辑模型与动画**：调整部件、替换材质，定位动画帧并检查预览。
        - **修改特效与音频**：使用当前编辑器支持的参数，并保留没有要求修改的内容。
        - **保存和导出**：保存指定文件，再按工程配置生成 Pack。

        ## 当前可用范围

        | 功能 | 支持范围 |
        | --- | --- |
        | 模型格式 | 可以导入 glTF / GLB，导出 glTF。当前没有 FBX 入口。 |
        | 报告 | 可以检查文件列表、模型、材质和动画，结果包含找不到的引用。 |
        | 当前模型 | 尚未打开模型。先打开你要编辑的模型，我就能检查其中的部件和材质。 |

        ### 一个完整操作示例

        1. 选中需要修改的刀刃部件。
        2. 告诉我“缩小到原来的 80%，然后播放动画检查”。
        3. 我会读取当前数据、执行修改，并说明预览和保存状态。

        **需要注意**：预览成功不代表已经保存，也不代表游戏内效果已经验收。

        ## 查询结果

        - 已找到目标模型，模型中共有三个部件。
        - 两个材质引用有效，一个贴图引用缺失。
        - 尚未修改模型，也没有向磁盘写入文件。

        ### 缺失贴图

        选中材质引用了一个当前工程和参考包里都没有找到的贴图。你可以提供替代贴图，或者打开包含原贴图的参考包，再让我检查一次。

        ```xml
        <texture path="variantmeshes/wh_variantmodels/weapons/example_texture.dds" />
        ```

        ## 接下来

        打开要处理的资源，直接告诉我想改什么。
        """;

    [TestMethod]
    public void Markdown_PreservesHeadingHierarchyAndReadableTableAlignment()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var markdown = new AssistantMarkdownView { Text = LongReply };
            markdown.RenderText();
            var heading = markdown.Document.Blocks.OfType<Paragraph>().FirstOrDefault(p => p.Tag as string == "Heading2");
            Assert.IsNotNull(heading, "Markdown heading metadata must survive parsing.");
            Assert.IsTrue(heading.FontSize > markdown.Document.FontSize, "Headings must be visibly larger than body text.");
            Assert.IsTrue(heading.Margin.Top >= 16, "Separate sections instead of making one dense wall of text.");
            Assert.IsTrue(markdown.Document.LineHeight >= markdown.Document.FontSize * 1.5);
            var table = markdown.Document.Blocks.OfType<Table>().Single();
            foreach (var group in table.RowGroups)
                foreach (var row in group.Rows)
                    foreach (var cell in row.Cells)
                        foreach (var paragraph in cell.Blocks.OfType<Paragraph>())
                            Assert.AreEqual(TextAlignment.Left, paragraph.TextAlignment, "Never justify table cell words.");
            Assert.IsTrue(table.RowGroups.First().Rows.First().Cells.First().FontWeight == FontWeights.SemiBold);
            var codeBlock = markdown.Document.Blocks.OfType<BlockUIContainer>().Single(b => b.Tag as string == "CodeBlock");
            var code = (ICSharpCode.AvalonEdit.TextEditor)codeBlock.Child;
            Assert.AreEqual(Application.Current.FindResource("AeBrush.Surface1"), code.Background);
            Assert.AreEqual(Application.Current.FindResource("AeBrush.TextPrimary"), code.Foreground);
            Assert.IsNull(code.SyntaxHighlighting, "Fixed light-theme syntax colours must not bypass the application's theme.");
            Assert.IsTrue(code.WordWrap, "Long resource paths must remain readable in a narrow workspace.");
        });
    }

    [TestMethod]
    public void WheelOverMarkdownMovesTheConversationInBothDirections()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var vm = ModAssistantRenderTests.CreateViewModel();
            var answer = new AssistantMessage(); answer.AppendText(LongReply + "\n\n" + LongReply); vm.Messages.Add(answer);
            var view = new AssistantView { DataContext = vm };
            Layout(view);
            var markdown = AnimPackWorkflowTests.Descendants<AssistantMarkdownView>(view).First(m => m.Text.Length > 0);
            view.ConversationScroll.ScrollToVerticalOffset(120); Layout(view);
            var before = view.ConversationScroll.VerticalOffset;
            Wheel(markdown, -120); Layout(view);
            Assert.IsTrue(view.ConversationScroll.VerticalOffset > before, "Wheel over the actual message must scroll down.");
            before = view.ConversationScroll.VerticalOffset;
            Wheel(markdown, 120); Layout(view);
            Assert.IsTrue(view.ConversationScroll.VerticalOffset < before, "Wheel must also scroll up.");
            view.DataContext = null;
        });
    }

    internal static void Wheel(UIElement element, int delta)
    {
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
        element.RaiseEvent(wheel);
        if (!wheel.Handled) { wheel.RoutedEvent = Mouse.MouseWheelEvent; element.RaiseEvent(wheel); }
    }

    [TestMethod]
    public void WheelOverBoundedDetailsScrollsInsideThenReturnsToConversationAtTheEdge()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var vm = ModAssistantRenderTests.CreateViewModel();
            var answer = new AssistantMessage();
            answer.AppendText(LongReply);
            var operation = new AssistantOperationCard(new JsonObject { ["status"] = "completed", ["description"] = "测试诊断", ["state"] = new JsonObject { ["lines"] = string.Join('\n', Enumerable.Range(0, 150).Select(i => "诊断行 " + i)) } }, (_, _) => Task.CompletedTask);
            answer.AddOperation(operation); answer.AppendText(LongReply); vm.Messages.Add(answer);
            var view = new AssistantView { DataContext = vm }; Layout(view);
            // The inner details template is created only after the outer operation opens.
            for (var level = 0; level < 2; level++)
                foreach (var expander in AnimPackWorkflowTests.Descendants<Expander>(view).Where(e => e.DataContext == operation && !e.IsExpanded).ToArray())
                {
                    expander.IsExpanded = true;
                    Layout(view);
                }
            var details = AnimPackWorkflowTests.Descendants<TextBox>(view).First(t => t.Text == operation.Detail);
            var inner = AnimPackWorkflowTests.Descendants<ScrollViewer>(details).First();
            Assert.IsTrue(inner.ScrollableHeight > 0);
            inner.ScrollToVerticalOffset(100); view.ConversationScroll.ScrollToVerticalOffset(200); Layout(view);
            var outerBefore = view.ConversationScroll.VerticalOffset;
            var innerBefore = inner.VerticalOffset;
            Wheel(inner, -120); Layout(view);
            Assert.IsTrue(inner.VerticalOffset > innerBefore);
            Assert.AreEqual(outerBefore, view.ConversationScroll.VerticalOffset);
            inner.ScrollToBottom(); Layout(view);
            Wheel(inner, -120); Layout(view);
            Assert.IsTrue(view.ConversationScroll.VerticalOffset > outerBefore, "At a detail boundary, the conversation must take over.");
            view.DataContext = null;
        });
    }

    [TestMethod]
    public void LongRepliesRenderWithHeadingsListsAndTablesInEveryTheme()
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
                        using var vm = ModAssistantRenderTests.CreateViewModel();
                        vm.CurrentSession.Title = "你能帮我做什么？";
                        vm.Messages.Add(new AssistantMessage { IsUser = true, Text = "你能帮我做什么？" });
                        var answer = new AssistantMessage(); answer.AppendText(LongReply); vm.Messages.Add(answer);
                        var view = new AssistantView { DataContext = vm };
                        view.Measure(new Size(width, 820)); view.Arrange(new Rect(0, 0, width, 820)); view.UpdateLayout();
                        view.ConversationScroll.ScrollToTop(); view.UpdateLayout();
                        Capture(view, theme, width, "long-top");
                        view.ConversationScroll.ScrollToVerticalOffset(500); view.UpdateLayout();
                        Capture(view, theme, width, "long-table");
                        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_ASSISTANT_RENDER_OUTPUT");
                        if (output != null && File.Exists(Path.Combine(output, "live-readability.txt")))
                        {
                            vm.Messages.Clear();
                            vm.CurrentSession.Title = "你能干什么？";
                            vm.TargetSummary = "未打开工程";
                            vm.Messages.Add(new AssistantMessage { IsUser = true, Text = "你能干什么？" });
                            var live = new AssistantMessage(); live.AppendText(File.ReadAllText(Path.Combine(output, "live-readability.txt"))); vm.Messages.Add(live);
                            view.UpdateLayout(); view.ConversationScroll.ScrollToTop(); view.UpdateLayout();
                            Capture(view, theme, width, "live");
                        }
                        view.DataContext = null;
                    }
            }
            finally { ThemesController.SetTheme(previous); }
        });
    }

    private static void Capture(AssistantView view, ThemeType theme, int width, string name)
    {
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_ASSISTANT_RENDER_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        foreach (var scale in new[] { 1d, 1.5d })
        {
            var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(820 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(output, $"reading-{theme}-{width}-{name}-{scale * 100:0}.png")); encoder.Save(stream);
        }
    }

    internal static void Layout(AssistantView view)
    {
        view.Measure(new Size(800, 760)); view.Arrange(new Rect(0, 0, 800, 760)); view.UpdateLayout();
    }
}
