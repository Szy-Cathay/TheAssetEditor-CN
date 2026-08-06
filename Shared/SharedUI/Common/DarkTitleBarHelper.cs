using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Shared.Core.Settings;

namespace CommonControls
{
    /// <summary>
    /// Enables the optional dark title bar via DWM when the Windows build
    /// supports it.
    /// Automatically follows the current theme - dark themes get dark title bar, light themes get light title bar.
    /// Call DarkTitleBarHelper.Enable(this) in a Window constructor.
    /// </summary>
    public static class DarkTitleBarHelper
    {
        private static readonly ConditionalWeakTable<Window, Registration> Registrations = new();

        internal delegate int DwmSetWindowAttributeDelegate(
            IntPtr hwnd,
            int attribute,
            ref int attributeValue,
            int attributeSize);

        [DllImport(
            "dwmapi.dll",
            EntryPoint = "DwmSetWindowAttribute",
            PreserveSig = true)]
        private static extern int DwmSetWindowAttributeNative(
            IntPtr hwnd,
            int attribute,
            ref int attributeValue,
            int attributeSize);

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
            TrySetImmersiveDarkMode(
                handle,
                value,
                DwmSetWindowAttributeNative);
        }

        internal static bool TrySetImmersiveDarkMode(
            IntPtr handle,
            int value,
            DwmSetWindowAttributeDelegate setter)
        {
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                return setter(
                    handle,
                    DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref value,
                    sizeof(int)) >= 0;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
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
