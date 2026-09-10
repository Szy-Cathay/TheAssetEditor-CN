using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CommonControls;

// there is a bug in visual studio code generation. Having generated classes with
// Overlapping names (Shared.Ui and Editors.Shared) causes compile errors as the code
// generator struggls to resolve the naming
namespace WindowHandling
{
    public  class AssetEditorWindow : Window, IDisposable
    {
        public bool AlwaysOnTop { get; set; } = false;
        bool _isDisposed = false;

        public AssetEditorWindow()
        {
            SetOwnerToActiveWindow(this);
            Deactivated += AssetEdWindow_Deactivated;
            SetResourceReference(StyleProperty, "CustomWindowStyle");
            SetResourceReference(BackgroundProperty, "AeBrush.Canvas");
            SetResourceReference(ForegroundProperty, "AeBrush.TextPrimary");
            SetResourceReference(FontFamilyProperty, "AppFontFamily");
            SetResourceReference(FontWeightProperty, "AppFontWeight");
            DarkTitleBarHelper.Enable(this);
        }

        public static void SetOwnerToActiveWindow(Window window)
        {
            var application = Application.Current;
            var owner = application?.Windows.OfType<Window>()
                .FirstOrDefault(candidate => candidate != window && candidate.IsActive) ??
                window.Owner ?? application?.MainWindow;
            if (owner is { IsLoaded: true } && owner != window)
                window.Owner = owner;
        }

        private void AssetEdWindow_Deactivated(object? sender, EventArgs e)
        {
            if (AlwaysOnTop)
            {
                var window = (Window)sender;
                window.Topmost = true;
            }
        }

        public void Dispose()
        {
            if (_isDisposed == false)
                Deactivated -= AssetEdWindow_Deactivated;
            _isDisposed = true;
        }
    }
}
