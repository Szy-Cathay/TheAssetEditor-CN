using System.Threading;
using System.Windows;
using System.Windows.Controls;
using CommonControls.Editors.TextEditor;

namespace AssetEditorTests;

public class TextEditorViewLifecycleTests
{
    [NUnit.Framework.Test]
    [NUnit.Framework.Apartment(ApartmentState.STA)]
    public void FoldingTimer_FollowsLoadedAndUnloadedLifecycle()
    {
        var application = Application.Current ?? new Application();
        const string resourceKey = "ComboBoxTemplate";
        var hadExistingTemplate = application.Resources.Contains(resourceKey);
        var existingTemplate = hadExistingTemplate
            ? application.Resources[resourceKey]
            : null;
        application.Resources[resourceKey] = new ControlTemplate(typeof(ComboBox));

        TextEditorView? view = null;
        try
        {
            view = new TextEditorView();
            NUnit.Framework.Assert.That(view.IsFoldingTimerEnabled, NUnit.Framework.Is.False);

            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            NUnit.Framework.Assert.That(view.IsFoldingTimerEnabled, NUnit.Framework.Is.True);

            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            NUnit.Framework.Assert.That(view.IsFoldingTimerEnabled, NUnit.Framework.Is.True);

            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            NUnit.Framework.Assert.That(view.IsFoldingTimerEnabled, NUnit.Framework.Is.False);

            view.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            NUnit.Framework.Assert.That(view.IsFoldingTimerEnabled, NUnit.Framework.Is.True);
        }
        finally
        {
            view?.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));

            if (hadExistingTemplate)
                application.Resources[resourceKey] = existingTemplate;
            else
                application.Resources.Remove(resourceKey);
        }
    }
}
