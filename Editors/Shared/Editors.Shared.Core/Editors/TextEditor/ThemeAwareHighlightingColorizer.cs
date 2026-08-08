using System;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace CommonControls.Editors.TextEditor;

internal sealed class ThemeAwareHighlightingColorizer(
    IHighlightingDefinition definition) : HighlightingColorizer(definition)
{
    protected override void ApplyColorToElement(
        VisualLineElement element,
        HighlightingColor color)
    {
        if (color.Foreground == null)
        {
            base.ApplyColorToElement(element, color);
            return;
        }

        var themedColor = color.Clone();
        themedColor.Foreground = new ThemeResourceHighlightingBrush(
            GetForegroundResourceKey(color.Name));
        base.ApplyColorToElement(element, themedColor);
    }

    internal static string GetForegroundResourceKey(string? colorName)
    {
        var name = colorName ?? string.Empty;
        if (ContainsAny(name,
                "Broken", "Removed", "Error", "Invalid"))
        {
            return "AeBrush.Danger";
        }

        if (ContainsAny(name,
                "Comment", "DocComment", "Unchanged", "LineBreak"))
        {
            return "AeBrush.TextMuted";
        }

        if (ContainsAny(name,
                "String", "Char", "Regex", "CData", "Added",
                "FileName", "Image", "AttributeValue"))
        {
            return "AeBrush.Success";
        }

        if (ContainsAny(name,
                "Number", "Digit", "Literal", "Constant", "TrueFalse",
                "Bool", "Null", "Value", "Entity", "Date"))
        {
            return "AeBrush.Warning";
        }

        if (ContainsAny(name,
                "Punctuation", "Slash", "Assignment", "Operator",
                "Brace", "Colon"))
        {
            return "AeBrush.TextSecondary";
        }

        return "AeBrush.Accent";
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        foreach (var term in terms)
        {
            if (value.Contains(term, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private sealed class ThemeResourceHighlightingBrush(string resourceKey) :
        HighlightingBrush
    {
        public override Brush GetBrush(ITextRunConstructionContext context) =>
            Application.Current?.TryFindResource(resourceKey) as Brush ??
            SystemColors.ControlTextBrush;
    }
}
