using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommonControls.BaseDialogs.ToolSelector;
using NUnit.Framework;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.ToolSelector;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class ToolSelectorWindowTests
{
    [Test]
    public void CreateAndShow_DoubleClickingEditorReturnsThatEditor()
    {
        var result = EditorEnums.None;

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => result = RunDialog(window =>
                RaiseDoubleClick(window, EditorEnums.Kitbash_Editor)));

        NUnitAssert.That(result, Is.EqualTo(EditorEnums.Kitbash_Editor));
    }

    [Test]
    public void CreateAndShow_DoubleClickingEmptySpaceDoesNotConfirm()
    {
        var result = EditorEnums.None;
        var remainedOpen = false;

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => result = RunDialog(window =>
            {
                RaiseDoubleClick(window.PossibleTools);
                remainedOpen = window.IsVisible;
            }));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(remainedOpen, Is.True);
            NUnitAssert.That(result, Is.EqualTo(EditorEnums.None));
        });
    }

    [Test]
    public void CreateAndShow_DoubleClickingNoneDoesNotConfirm()
    {
        var result = EditorEnums.XML_Editor;
        var remainedOpen = false;

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => result = RunDialog(window =>
            {
                RaiseDoubleClick(window, EditorEnums.None);
                remainedOpen = window.IsVisible;
            }));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(remainedOpen, Is.True);
            NUnitAssert.That(result, Is.EqualTo(EditorEnums.None));
        });
    }

    [Test]
    public void CreateAndShow_SingleClickSelectsWithoutConfirming()
    {
        var result = EditorEnums.XML_Editor;
        var selectedEditor = EditorEnums.None;
        var remainedOpen = false;

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => result = RunDialog(window =>
            {
                RaiseSingleClick(window, EditorEnums.Kitbash_Editor);
                selectedEditor = (EditorEnums)
                    window.PossibleTools.SelectedItem;
                remainedOpen = window.IsVisible;
            }));

        NUnitAssert.Multiple(() =>
        {
            NUnitAssert.That(
                selectedEditor,
                Is.EqualTo(EditorEnums.Kitbash_Editor));
            NUnitAssert.That(remainedOpen, Is.True);
            NUnitAssert.That(result, Is.EqualTo(EditorEnums.None));
        });
    }

    [Test]
    public void CreateAndShow_OpenButtonReturnsSelectedEditor()
    {
        var result = EditorEnums.None;

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => result = RunDialog(window =>
            {
                window.PossibleTools.SelectedItem =
                    EditorEnums.Kitbash_Editor;
                InvokeOpenButton(window);
            }));

        NUnitAssert.That(result, Is.EqualTo(EditorEnums.Kitbash_Editor));
    }

    [Test]
    public void CreateAndShow_ClosingWindowReturnsNone()
    {
        var result = EditorEnums.XML_Editor;

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => result = RunDialog(_ => { }));

        NUnitAssert.That(result, Is.EqualTo(EditorEnums.None));
    }

    [Test]
    public void CreateAndShow_OpenButtonWithNoneReturnsNone()
    {
        var result = EditorEnums.XML_Editor;

        WpfTestApplicationHost.InvokeWithThemeResources(
            WpfTestApplicationHost.EmptyServices,
            () => result = RunDialog(window =>
            {
                window.PossibleTools.SelectedItem = EditorEnums.None;
                InvokeOpenButton(window);
            }));

        NUnitAssert.That(result, Is.EqualTo(EditorEnums.None));
    }

    private static EditorEnums RunDialog(
        Action<ToolSelectorWindow> interaction)
    {
        var application = Application.Current;
        var previousMainWindow = application.MainWindow;
        var owner = new Window
        {
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Left = -10000,
            Top = -10000,
        };

        try
        {
            owner.Show();
            application.MainWindow = owner;
            application.Dispatcher.BeginInvoke(
                DispatcherPriority.ApplicationIdle,
                () =>
                {
                    var window = application.Windows
                        .OfType<ToolSelectorWindow>()
                        .Single(candidate =>
                            candidate.IsVisible &&
                            candidate.Owner == owner);
                    window.UpdateLayout();
                    interaction(window);
                    if (window.IsVisible)
                        window.Close();
                });

            return new ToolSelectorUiProvider().CreateAndShow(
                [
                    EditorEnums.XML_VariantMesh_Editor,
                    EditorEnums.Kitbash_Editor,
                ]);
        }
        finally
        {
            application.MainWindow = previousMainWindow;
            owner.Close();
        }
    }

    private static void RaiseDoubleClick(
        ToolSelectorWindow window,
        EditorEnums editor)
    {
        var item = GetItem(window, editor);
        RaiseDoubleClick(item);
    }

    private static void RaiseDoubleClick(UIElement element) =>
        RaiseMouseButtonEvent(element, Control.MouseDoubleClickEvent);

    private static void RaiseSingleClick(
        ToolSelectorWindow window,
        EditorEnums editor)
    {
        var item = GetItem(window, editor);
        RaiseMouseButtonEvent(item, UIElement.MouseLeftButtonDownEvent);
        RaiseMouseButtonEvent(item, UIElement.MouseLeftButtonUpEvent);
        window.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private static void RaiseMouseButtonEvent(
        UIElement element,
        RoutedEvent routedEvent) =>
        element.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = routedEvent,
            Source = element,
        });

    private static void InvokeOpenButton(ToolSelectorWindow window)
    {
        var button = FindVisualChildren<Button>(window).Single();
        var peer = new ButtonAutomationPeer(button);
        var provider = (IInvokeProvider)peer.GetPattern(
            PatternInterface.Invoke);
        provider.Invoke();
        window.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.ApplicationIdle);
    }

    private static ListViewItem GetItem(
        ToolSelectorWindow window,
        EditorEnums editor) =>
        (ListViewItem)window.PossibleTools.ItemContainerGenerator
            .ContainerFromItem(editor);

    private static IEnumerable<T> FindVisualChildren<T>(
        DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(parent);
             index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;

            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
