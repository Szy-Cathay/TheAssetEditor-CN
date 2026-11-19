using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit;
using MdXaml;
using MdXaml.Plugins;

namespace AssetEditor.ModAssistant;

// Keep model text inert: no HTML plugins, automatic images, local links or commands.
public sealed class AssistantMarkdownView : FlowDocumentScrollViewer
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(AssistantMarkdownView),
        new PropertyMetadata("", (owner, _) => ((AssistantMarkdownView)owner).QueueRender()));
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    private readonly DispatcherTimer _timer;
    private readonly Markdown _markdown = new()
    {
        Plugins = new MdXamlPlugins(SyntaxManager.Standard.Clone()),
        // These tags identify Markdown headings/tables; they do not enable HTML.
        DisabledTag = false,
        DisabledContextMenu = true,
        HyperlinkCommand = new RelayCommand<string>(OpenLink),
    };

    public AssistantMarkdownView()
    {
        IsToolBarVisible = false;
        IsSelectionEnabled = true;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        SetResourceReference(ForegroundProperty, "AeBrush.TextPrimary");
        SetResourceReference(FontFamilyProperty, "AppFontFamily");
        SetResourceReference(FocusVisualStyleProperty, "AeFocus.Keyboard");
        Background = System.Windows.Media.Brushes.Transparent;
        BorderThickness = new Thickness(0);
        FontSize = 14;
        Padding = new Thickness(0);
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => RenderText(), Dispatcher);
        _timer.Stop();
        Loaded += (_, _) => RenderText();
        Unloaded += (_, _) => _timer.Stop();
    }

    private void QueueRender()
    {
        if (!IsLoaded) RenderText();
        else if (!_timer.IsEnabled) _timer.Start();
    }

    public void RenderText()
    {
        _timer.Stop();
        try
        {
            // MdXaml's image loader reads URIs during parsing. Image-bearing replies
            // remain selectable literal text; explicit user attachments have their own preview.
            Document = Text.Contains("![", StringComparison.Ordinal)
                ? new FlowDocument(new Paragraph(new Run(Text))) : _markdown.Transform(Text);
        }
        catch (Exception) { Document = new FlowDocument(new Paragraph(new Run(Text))); }
        Document.PagePadding = new Thickness(0);
        Document.FontSize = 14;
        Document.LineHeight = 21;
        Document.TextAlignment = TextAlignment.Left;
        Document.SetResourceReference(FlowDocument.FontFamilyProperty, "AppFontFamily");
        Document.SetResourceReference(FlowDocument.ForegroundProperty, "AeBrush.TextPrimary");
        FormatBlocks(Document.Blocks);
    }

    private static void FormatBlocks(BlockCollection blocks, bool compact = false)
    {
        foreach (var block in blocks)
        {
            block.Margin = new Thickness(0, 0, 0, compact ? 4 : 12);
            block.TextAlignment = TextAlignment.Left;
            if (block is BlockUIContainer { Tag: "CodeBlock", Child: TextEditor code })
            {
                // The parser's editor uses fixed light-theme syntax colours by default.
                code.SyntaxHighlighting = null;
                code.SetResourceReference(Control.ForegroundProperty, "AeBrush.TextPrimary");
                code.SetResourceReference(Control.BackgroundProperty, "AeBrush.Surface1");
                code.FontSize = 14;
                code.Padding = new Thickness(12);
                code.WordWrap = true;
                code.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
                code.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            }
            if (block is Paragraph paragraph)
            {
                if (paragraph.Tag is string tag && tag.StartsWith("Heading", StringComparison.Ordinal))
                {
                    paragraph.FontSize = tag is "Heading1" or "Heading2" ? 20 : 16;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.LineHeight = paragraph.FontSize * 1.5;
                    paragraph.Margin = new Thickness(0, 24, 0, 12);
                    paragraph.KeepWithNext = true;
                }
                else if (!compact && paragraph.Inlines.Count == 1 && paragraph.Inlines.FirstInline is Span { Tag: "Bold" } &&
                    new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Length <= 40)
                {
                    paragraph.FontSize = 16;
                    paragraph.Margin = new Thickness(0, 16, 0, 8);
                    paragraph.KeepWithNext = true;
                }
                if (paragraph.Tag as string == "CodeBlock")
                {
                    paragraph.Padding = new Thickness(8);
                    paragraph.SetResourceReference(TextElement.BackgroundProperty, "AeBrush.Surface1");
                }
            }
            else if (block is Section section) FormatBlocks(section.Blocks, compact);
            else if (block is List list)
            {
                list.Padding = new Thickness(24, 0, 0, 0);
                list.MarkerStyle = list.MarkerStyle is TextMarkerStyle.Circle or TextMarkerStyle.Square ? TextMarkerStyle.Disc : list.MarkerStyle;
                foreach (var item in list.ListItems)
                {
                    item.Margin = new Thickness(0, 0, 0, 4);
                    FormatBlocks(item.Blocks, true);
                }
            }
            else if (block is Table table)
            {
                table.CellSpacing = 0;
                if (table.Columns.Count == 2)
                {
                    table.Columns[0].Width = new GridLength(1, GridUnitType.Star);
                    table.Columns[1].Width = new GridLength(3, GridUnitType.Star);
                }
                foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                        foreach (var cell in row.Cells)
                        {
                            cell.Padding = new Thickness(12, 8, 12, 8);
                            cell.BorderThickness = new Thickness(0, 0, 0, 1);
                            cell.SetResourceReference(TableCell.BorderBrushProperty, "AeBrush.Border");
                            if (group.Tag as string == "TableHeader")
                            {
                                cell.FontWeight = FontWeights.SemiBold;
                                cell.SetResourceReference(TextElement.BackgroundProperty, "AeBrush.Surface1");
                            }
                            FormatBlocks(cell.Blocks, true);
                        }
            }
        }
    }

    public static bool IsWebLink(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";
    private static void OpenLink(string? value)
    {
        if (!IsWebLink(value)) return;
        try { Process.Start(new ProcessStartInfo(value!) { UseShellExecute = true }); }
        catch (Exception) { /* The external browser may be unavailable. */ }
    }
}
