using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Shared.Core.Settings;

namespace CommonControls
{
    /// <summary>
    /// Enables dark mode title bar on Windows 11+ via DWM API.
    /// Automatically follows the current theme - dark themes get dark title bar, light themes get light title bar.
    /// Call DarkTitleBarHelper.Enable(this) in a Window constructor.
    /// </summary>
    public static class DarkTitleBarHelper
    {
        private static readonly ConditionalWeakTable<Window, Registration> Registrations = new();

        [DllImport("dwmapi.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void Enable(Window window)
        {
            Registrations.GetValue(window, static value => new Registration(value));
        }

        private static void Apply(Window window)
        {
            var isDark = !ThemesController.CurrentTheme.ToString().Contains("Light");
            var value = isDark ? 1 : 0;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
                DwmSetWindowAttribute(handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }

        private sealed class Registration
        {
            private readonly Window _window;

            public Registration(Window window)
            {
                _window = window;
                _window.SourceInitialized += OnSourceInitialized;
                _window.Closed += OnClosed;
                ThemesController.ThemeChanged += OnThemeChanged;

                if (new WindowInteropHelper(_window).Handle != IntPtr.Zero)
                    Apply(_window);
            }

            private void OnSourceInitialized(object? sender, EventArgs e) =>
                Apply(_window);

            private void OnThemeChanged(ThemeType theme)
            {
                if (_window.Dispatcher.CheckAccess())
                {
                    Apply(_window);
                    return;
                }

                _window.Dispatcher.BeginInvoke(() => Apply(_window));
            }

            private void OnClosed(object? sender, EventArgs e)
            {
                _window.SourceInitialized -= OnSourceInitialized;
                _window.Closed -= OnClosed;
                ThemesController.ThemeChanged -= OnThemeChanged;
                Registrations.Remove(_window);
            }
        }
    }
}
