using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Point = System.Windows.Point;

namespace GameWorld.Core.WpfWindow.Input
{
    public partial class WpfMouse
    {
        private bool _continuousDrag;
        private bool _wrapCursor;
        private bool _samplingCursor;
        private Vector2 _continuousPosition;
        private Vector2 _physicalPosition;
        private NativeRectangle _previousClip;
        private NativeRectangle _activeClip;
        private bool _hasCursorClip;
        private Window _captureWindow;
        private HwndSource _captureSource;
        private nint _captureDpiContext;

        public event Action CaptureInterrupted;
        public Vector2? CapturedCursorPosition => _continuousDrag ? _physicalPosition : null;
        internal Vector2 Position => _continuousDrag ? _continuousPosition : _physicalPosition;

        public void BeginContinuousDrag(bool wrap)
        {
            if (_continuousDrag || !_focusElement.IsVisible || !_focusElement.IsKeyboardFocused)
                return;
            _captureWindow = Window.GetWindow(_focusElement);
            _captureSource = PresentationSource.FromVisual(_focusElement) as HwndSource;
            if (_captureWindow?.IsActive != true || _captureSource == null || _captureSource.IsDisposed)
                return;
            _captureDpiContext = GetWindowDpiAwarenessContext(_captureSource.Handle);
            var previousDpi = SetThreadDpiAwarenessContext(_captureDpiContext);
            _activeClip = default;
            _hasCursorClip = false;
            _continuousPosition = Position;
            _wrapCursor = wrap;
            _samplingCursor = false;
            _continuousDrag = true;
            try
            {
                if (!_focusElement.CaptureMouse())
                {
                    EndContinuousDrag();
                    return;
                }
                _captureWindow.Deactivated += OnWindowDeactivated;
                _focusElement.LayoutUpdated += OnCaptureLayoutUpdated;
                _captureSource.AddHook(OnCapturedWindowMessage);
                if (!RefreshCaptureBounds())
                {
                    InterruptCapture();
                    return;
                }
                if (GetCursorPos(out var screen))
                {
                    var position = _focusElement.PointFromScreen(new Point(screen.X, screen.Y));
                    _physicalPosition = new Vector2((float)position.X, (float)position.Y);
                    _continuousPosition = _physicalPosition;
                    // Queue native input even when a gesture starts on an edge.
                    SetCursorPos(screen.X, screen.Y);
                }
            }
            catch
            {
                EndContinuousDrag();
                throw;
            }
            finally
            {
                SetThreadDpiAwarenessContext(previousDpi);
            }
        }

        public void EndContinuousDrag()
        {
            if (!_continuousDrag)
                return;
            _continuousDrag = false;
            _samplingCursor = false;
            _focusElement.LayoutUpdated -= OnCaptureLayoutUpdated;
            if (_captureWindow != null)
                _captureWindow.Deactivated -= OnWindowDeactivated;
            _captureWindow = null;
            var previousDpi = SetThreadDpiAwarenessContext(_captureDpiContext);
            try
            {
                if (_captureSource?.IsDisposed == false)
                {
                    _captureSource.RemoveHook(OnCapturedWindowMessage);
                    if (PresentationSource.FromVisual(_focusElement) == _captureSource && GetCursorPos(out var screen))
                    {
                        var position = _focusElement.PointFromScreen(new Point(screen.X, screen.Y));
                        _physicalPosition = new Vector2((float)position.X, (float)position.Y);
                    }
                }
                // Restore even if the HWND was destroyed, without overwriting another owner's clip.
                if (_hasCursorClip && GetClipCursor(out var current) && current.Equals(_activeClip))
                    ClipCursor(ref _previousClip);
            }
            finally { SetThreadDpiAwarenessContext(previousDpi); }
            _captureSource = null;
            _hasCursorClip = false;
            _mouseState = new MouseState((int)_physicalPosition.X, (int)_physicalPosition.Y,
                _mouseState.ScrollWheelValue, ButtonState.Released, ButtonState.Released,
                ButtonState.Released, ButtonState.Released, ButtonState.Released);
            if (_focusElement.IsMouseCaptured)
                _focusElement.ReleaseMouseCapture();
        }

        private bool RefreshCaptureBounds()
        {
            if (PresentationSource.FromVisual(_focusElement) is not HwndSource source || source.IsDisposed ||
                source != _captureSource || _focusElement.ActualWidth < 8 || _focusElement.ActualHeight < 8)
                return false;
            var topLeft = _focusElement.PointToScreen(new Point());
            var bottomRight = _focusElement.PointToScreen(new Point(_focusElement.ActualWidth, _focusElement.ActualHeight));
            var bounds = new NativeRectangle
            {
                Left = Math.Max((int)Math.Ceiling(topLeft.X), GetSystemMetrics(76)),
                Top = Math.Max((int)Math.Ceiling(topLeft.Y), GetSystemMetrics(77)),
                Right = Math.Min((int)Math.Floor(bottomRight.X), GetSystemMetrics(76) + GetSystemMetrics(78)),
                Bottom = Math.Min((int)Math.Floor(bottomRight.Y), GetSystemMetrics(77) + GetSystemMetrics(79))
            };
            // Native screen bounds and PointToScreen are physical pixels, including mixed DPI monitors.
            if (bounds.Right - bounds.Left < 8 || bounds.Bottom - bounds.Top < 8)
                return false;
            if (bounds.Equals(_activeClip))
                return true;
            // A changed viewport also changes the projection used by the active gesture.
            if (_activeClip.Right != _activeClip.Left)
                return false;
            // Confinement must hold before queued input is dispatched, including fast motion.
            if (!GetClipCursor(out _previousClip) || !ClipCursor(ref bounds))
                return false;
            _activeClip = bounds;
            _hasCursorClip = true;
            return true;
        }

        private nint OnCapturedWindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            const int mouseMove = 0x0200;
            if (!_continuousDrag || message != mouseMove)
                return 0;
            var previousDpi = SetThreadDpiAwarenessContext(_captureDpiContext);
            try { handled = !UpdateContinuousPosition(); }
            finally { SetThreadDpiAwarenessContext(previousDpi); }
            return 0;
        }

        private bool UpdateContinuousPosition()
        {
            if (_samplingCursor)
                return false;
            _samplingCursor = true;
            try
            {
                if (!GetCursorPos(out var screen) || !GetClipCursor(out var clip) || !clip.Equals(_activeClip))
                {
                    InterruptCapture();
                    return false;
                }
                // Native messages are notifications, not trusted position samples after a warp.
                // Consume each observed physical displacement once, in the HWND's DPI context.
                var input = _focusElement.PointFromScreen(new Point(screen.X, screen.Y));
                var physical = new Vector2((float)input.X, (float)input.Y);
                _continuousPosition += physical - _physicalPosition;
                _physicalPosition = physical;
                if (!_wrapCursor)
                    return true;
                var wrappedX = WrapCoordinate(screen.X, _activeClip.Left, _activeClip.Right);
                var wrappedY = WrapCoordinate(screen.Y, _activeClip.Top, _activeClip.Bottom);
                if (wrappedX == screen.X && wrappedY == screen.Y)
                    return true;
                if (!SetCursorPos(wrappedX, wrappedY) || !GetCursorPos(out var landed))
                {
                    InterruptCapture();
                    return false;
                }
                // Rebase immediately to the observed destination without moving the model.
                // Old edge/interior messages and warp echoes then produce zero displacement.
                var rebased = _focusElement.PointFromScreen(new Point(landed.X, landed.Y));
                _physicalPosition = new Vector2((float)rebased.X, (float)rebased.Y);
                return false;
            }
            finally { _samplingCursor = false; }
        }

        internal static int WrapCoordinate(int value, int minimum, int maximum)
        {
            const int margin = 2;
            var length = maximum - minimum - margin * 2;
            if (length <= 0 || (value >= minimum + margin && value <= maximum - margin))
                return value;
            if (value < minimum + margin)
                return minimum + margin + (int)(((long)value - minimum - margin) % length + length) % length;
            return maximum - margin - (int)(((long)maximum - margin - value) % length + length) % length;
        }

        private void OnCaptureLayoutUpdated(object sender, EventArgs e)
        {
            if (!_continuousDrag || _captureSource?.IsDisposed != false)
                return;
            var previousDpi = SetThreadDpiAwarenessContext(_captureDpiContext);
            try
            {
                if (!RefreshCaptureBounds())
                    InterruptCapture();
            }
            finally { SetThreadDpiAwarenessContext(previousDpi); }
        }

        private void OnCaptureLost(object sender, MouseEventArgs e)
        {
            if (_continuousDrag && !_focusElement.IsMouseCaptured)
                InterruptCapture();
        }

        private void OnInputFocusLost(object sender, KeyboardFocusChangedEventArgs e) => InterruptCapture();
        private void OnInputUnloaded(object sender, RoutedEventArgs e) => InterruptCapture();
        private void OnWindowDeactivated(object sender, EventArgs e) => InterruptCapture();

        private void InterruptCapture()
        {
            if (!_continuousDrag)
                return;
            EndContinuousDrag();
            ClearButtonTransitions();
            ResetCursor();
            CaptureInterrupted?.Invoke();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool GetClipCursor(out NativeRectangle rectangle);
        [DllImport("user32.dll")]
        private static extern bool ClipCursor(ref NativeRectangle rectangle);
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
        [DllImport("user32.dll")]
        private static extern nint GetWindowDpiAwarenessContext(nint hwnd);
        [DllImport("user32.dll")]
        private static extern nint SetThreadDpiAwarenessContext(nint context);
    }
}
