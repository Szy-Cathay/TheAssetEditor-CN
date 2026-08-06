namespace Shared.Core.Services;

public enum UiMessageBoxButtonSet
{
    Ok,
    OkCancel,
    YesNo,
    YesNoCancel,
}

public enum UiMessageBoxIcon
{
    None,
    Error,
    Warning,
    Question,
    Information,
}

public enum UiMessageBoxResult
{
    None,
    Ok,
    Cancel,
    Yes,
    No,
}

public static class UiMessageBoxBridge
{
    private static Func<
        string,
        string,
        UiMessageBoxButtonSet,
        UiMessageBoxIcon,
        UiMessageBoxResult>? _show;

    public static void Configure(
        Func<
            string,
            string,
            UiMessageBoxButtonSet,
            UiMessageBoxIcon,
            UiMessageBoxResult> show)
    {
        _show = show;
    }

    public static UiMessageBoxResult Show(string message) =>
        Show(message, string.Empty);

    public static UiMessageBoxResult Show(string message, string title) =>
        Show(message, title, UiMessageBoxButtonSet.Ok);

    public static UiMessageBoxResult Show(
        string message,
        string title,
        UiMessageBoxButtonSet buttons) =>
        Show(message, title, buttons, UiMessageBoxIcon.None);

    public static UiMessageBoxResult Show(
        string message,
        string title,
        UiMessageBoxButtonSet buttons,
        UiMessageBoxIcon image)
    {
        return _show?.Invoke(message, title, buttons, image) ??
               GetFallbackResult(buttons);
    }

    private static UiMessageBoxResult GetFallbackResult(
        UiMessageBoxButtonSet buttons) => buttons switch
        {
            UiMessageBoxButtonSet.Ok => UiMessageBoxResult.Ok,
            UiMessageBoxButtonSet.YesNo => UiMessageBoxResult.No,
            _ => UiMessageBoxResult.Cancel,
        };
}
