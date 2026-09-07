using GameWorld.Core.WpfWindow;
using Microsoft.Xna.Framework.Input;
using Vector2 = Microsoft.Xna.Framework.Vector2;
using Shared.Core.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Keyboard = System.Windows.Input.Keyboard;

namespace GameWorld.Core.WpfWindow.Input
{
    internal interface IWpfKeyboard : IDisposable
    {
        int InputContextVersion { get; }
        KeyboardState ReleasedKeys { get; }
        KeyboardState PressedKeys { get; }
        string TextInput { get; }
        Vector2? GetKeyPressPosition(Keys key);
        int GetKeyPressCount(Keys key);
        KeyboardState GetState();
    }

    /// <summary>
    /// Helper class that accesses a native API to get the current keystate.
    /// Required for any WPF hosted control.
    /// </summary>
    public class WpfKeyboard : IDisposable, IWpfKeyboard
    {
        private readonly FrameworkElement _focusElement;
        private readonly Application? _application;
        private readonly object _previousInputMethodEnabled;
        private readonly Dictionary<Keys, Vector2> _eventKeysDown = new();
        private readonly HashSet<Keys> _pendingReleases = new();
        private readonly HashSet<Keys> _pendingPresses = new();
        private Dictionary<Keys, int> _pendingPressCounts = new();
        private Dictionary<Keys, int> _pressCounts = new();
        private Dictionary<Keys, Vector2> _pendingPressPositions = new();
        private Dictionary<Keys, Vector2> _pressPositions = new();
        private string _pendingTextInput = "";
        public string TextInput { get; private set; } = "";

        public int InputContextVersion { get; private set; }
        public KeyboardState ReleasedKeys { get; private set; }
        public KeyboardState PressedKeys { get; private set; }
        public Vector2? GetKeyPressPosition(Keys key) => _pressPositions.TryGetValue(key, out var position) ? position : null;
        public int GetKeyPressCount(Keys key) => _pressCounts.GetValueOrDefault(key);

        /// <summary>
        /// Creates a new instance of the keyboard helper.
        /// </summary>
        /// <param name="focusElement">The element that will be used as the focus point. Provide your implementation of <see cref="WpfGame"/> here.</param>
        public WpfKeyboard(IWpfGame focusElement)
        {
            if (focusElement == null)
                throw new ArgumentNullException(nameof(focusElement));

            _focusElement = focusElement.GetFocusElement();
            _previousInputMethodEnabled = _focusElement.ReadLocalValue(InputMethod.IsInputMethodEnabledProperty);
            InputMethod.SetIsInputMethodEnabled(_focusElement, false);
            _focusElement.AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown), true);
            _focusElement.AddHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(KeyUp), true);
            _focusElement.AddHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(OnTextInput), true);
            _focusElement.LostKeyboardFocus += FocusChanged;

            _application = Application.Current;
            if (_application != null)
            {
                _application.Activated += Application_ActivationChanged;
                _application.Deactivated += Application_ActivationChanged;
            }
        }

        /// <summary>
        /// Gets the active keyboardstate.
        /// </summary>
        /// <returns></returns>
        public KeyboardState GetState()
        {
            if (_focusElement.IsMouseDirectlyOver && Keyboard.FocusedElement != _focusElement)
            {
                if (System.Windows.Input.Mouse.Captured == _focusElement)
                    _focusElement.Focus();
            }
            ReleasedKeys = new KeyboardState(_pendingReleases.ToArray());
            PressedKeys = new KeyboardState(_pendingPresses.ToArray());
            TextInput = _pendingTextInput;
            _pendingTextInput = "";
            _pendingReleases.Clear();
            _pendingPresses.Clear();
            (_pressCounts, _pendingPressCounts) = (_pendingPressCounts, _pressCounts);
            _pendingPressCounts.Clear();
            (_pressPositions, _pendingPressPositions) = (_pendingPressPositions, _pressPositions);
            _pendingPressPositions.Clear();
            return new KeyboardState(GetKeys(_focusElement));
        }

        private void Application_ActivationChanged(object sender, EventArgs e)
        {
            InvalidateInput();
        }

        private void FocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        {
            InvalidateInput();
        }

        private void InvalidateInput()
        {
            InputContextVersion++;
            _eventKeysDown.Clear();
            _pressCounts.Clear();
            _pendingPressCounts.Clear();
            _pendingReleases.Clear();
            _pendingPresses.Clear();
            _pendingPressPositions.Clear();
            _pressPositions.Clear();
            ReleasedKeys = default;
            PressedKeys = default;
            _pendingTextInput = "";
            TextInput = "";
        }

        private void OnTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_focusElement.IsKeyboardFocused && _pendingTextInput.Length + e.Text.Length <= 4096)
                _pendingTextInput += e.Text;
        }

        private void KeyDown(object sender, KeyEventArgs e)
        {
            if (!_focusElement.IsKeyboardFocused)
                return;
            var key = ToGameKey(e);
            var mouse = System.Windows.Input.Mouse.GetPosition(_focusElement);
            var position = new Vector2((float)mouse.X, (float)mouse.Y);
            if (_eventKeysDown.TryAdd(key, position))
            {
                _pendingPresses.Add(key);
                _pendingPressCounts[key] = _pendingPressCounts.GetValueOrDefault(key) + 1;
                _pendingPressPositions[key] = position;
            }
            if (key == Keys.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                try
                {
                    var text = Clipboard.GetText();
                    if (_pendingTextInput.Length + text.Length <= 4096)
                        _pendingTextInput += text;
                }
                catch (ExternalException) { }
            }
        }

        private void KeyUp(object sender, KeyEventArgs e)
        {
            var key = ToGameKey(e);
            if (_focusElement.IsKeyboardFocused && _eventKeysDown.Remove(key, out var position))
            {
                _pendingReleases.Add(key);
                _pendingPressPositions[key] = position;
            }
        }

        private static Keys ToGameKey(KeyEventArgs e) =>
            (Keys)KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);

        private static Keys[] GetKeys(IInputElement focusElement)
        {
            // the buffer must be exactly 256 bytes long as per API definition
            var keyStates = new byte[256];

            if (!NativeGetKeyboardState(keyStates))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var pressedKeys = new List<Keys>();
            if (focusElement.IsKeyboardFocused)
            {
                // skip the first 8 entries as they are actually mouse events and not keyboard keys
                const int skipMouseKeys = 8;
                for (var i = skipMouseKeys; i < keyStates.Length; i++)
                {
                    var key = keyStates[i];

                    //Logical 'and' so we can drop the low-order bit for toggled keys, else that key will appear with the value 1!
                    if ((key & 0x80) != 0)
                    {

                        //This is just for a short demo, you may want this to return
                        //multiple keys!
                        if (key != 0)
                            pressedKeys.Add((Keys)i);
                    }
                }
            }
            return pressedKeys.ToArray();
        }

        [DllImport("user32.dll", EntryPoint = "GetKeyboardState", SetLastError = true)]
        private static extern bool NativeGetKeyboardState([Out] byte[] keyStates);

        public void Dispose()
        {
            if (_previousInputMethodEnabled == DependencyProperty.UnsetValue)
                _focusElement.ClearValue(InputMethod.IsInputMethodEnabledProperty);
            else
                _focusElement.SetValue(InputMethod.IsInputMethodEnabledProperty, _previousInputMethodEnabled);
            _focusElement.RemoveHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(KeyDown));
            _focusElement.RemoveHandler(Keyboard.PreviewKeyUpEvent, new KeyEventHandler(KeyUp));
            _focusElement.RemoveHandler(TextCompositionManager.PreviewTextInputEvent, new TextCompositionEventHandler(OnTextInput));
            _focusElement.LostKeyboardFocus -= FocusChanged;
            if (_application != null)
            {
                _application.Activated -= Application_ActivationChanged;
                _application.Deactivated -= Application_ActivationChanged;
            }
        }
    }
}
