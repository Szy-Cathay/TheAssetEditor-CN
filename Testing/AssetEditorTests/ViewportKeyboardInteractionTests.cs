using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameWorld.Core.Components.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;
using Shared.Core.Services;
using Keyboard = System.Windows.Input.Keyboard;

namespace AssetEditorTests;

[TestClass]
public class ViewportKeyboardInteractionTests
{
    [TestMethod]
    public void TwoRTapsBetweenFramesRemainTwoPresses()
    {
        WithViewport((viewport, keyboard, _) =>
        {
            for (var i = 0; i < 2; i++)
            {
                RaiseKey(viewport, Key.R, Keyboard.PreviewKeyDownEvent);
                RaiseKey(viewport, Key.R, Keyboard.PreviewKeyUpEvent);
            }
            keyboard.Update(new GameTime());
            Assert.AreEqual(2, keyboard.GetKeyPressCount(Keys.R));
            keyboard.Update(new GameTime());
            Assert.AreEqual(0, keyboard.GetKeyPressCount(Keys.R));
        });
    }

    [TestMethod]
    public void NumericTextPreservesEventOrderAndIsConsumedOnce()
    {
        WithViewport((viewport, keyboard, input) =>
        {
            foreach (var text in new[] { "=12", "3*", "(4+5)" })
                viewport.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                    new TextComposition(InputManager.Current, viewport, text))
                { RoutedEvent = TextCompositionManager.PreviewTextInputEvent, Handled = true });
            keyboard.Update(new GameTime());
            Assert.AreEqual("=123*(4+5)", keyboard.TextInput);
            keyboard.Update(new GameTime());
            Assert.AreEqual("", keyboard.TextInput);
            viewport.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                new TextComposition(InputManager.Current, viewport, "999"))
            { RoutedEvent = TextCompositionManager.PreviewTextInputEvent });
            Keyboard.Focus(input);
            keyboard.Update(new GameTime());
            Assert.AreEqual("", keyboard.TextInput);
        });
    }

    [TestMethod]
    public void ViewportShortcuts_DoNotStartChineseInputComposition()
    {
        WithViewport((viewport, _, input) =>
        {
            Assert.IsFalse(InputMethod.GetIsInputMethodEnabled(viewport));
            Assert.IsTrue(InputMethod.GetIsInputMethodEnabled(input));
        });
    }

    [DataTestMethod]
    [DataRow(Key.Tab, Keys.Tab)]
    [DataRow(Key.Return, Keys.Enter)]
    [DataRow(Key.G, Keys.G)]
    public void ShortTapBetweenFrames_IsReportedOnceEvenWhenWpfHandlesTheEvent(Key key, Keys gameKey)
    {
        WithViewport((viewport, keyboard, _) =>
        {
            RaiseKey(viewport, key, Keyboard.PreviewKeyDownEvent);
            RaiseKey(viewport, key, Keyboard.PreviewKeyUpEvent);
            keyboard.Update(new GameTime());
            Assert.IsTrue(keyboard.IsKeyPressed(gameKey));
            Assert.IsNotNull(keyboard.GetKeyPressPosition(gameKey));
            Assert.IsTrue(keyboard.IsKeyReleased(gameKey));
            keyboard.Update(new GameTime());
            Assert.IsFalse(keyboard.IsKeyPressed(gameKey));
            Assert.IsFalse(keyboard.IsKeyReleased(gameKey));
        });
    }

    [TestMethod]
    public void PressBeforeRelease_StartsImmediatelyAndDoesNotRepeatWhileHeld()
    {
        WithViewport((viewport, keyboard, _) =>
        {
            RaiseKey(viewport, Key.G, Keyboard.PreviewKeyDownEvent);
            keyboard.Update(new GameTime());
            Assert.IsTrue(keyboard.IsKeyPressed(Keys.G));
            Assert.IsFalse(keyboard.IsKeyReleased(Keys.G));
            RaiseKey(viewport, Key.G, Keyboard.PreviewKeyDownEvent);
            keyboard.Update(new GameTime());
            Assert.IsFalse(keyboard.IsKeyPressed(Keys.G));
            RaiseKey(viewport, Key.G, Keyboard.PreviewKeyUpEvent);
            keyboard.Update(new GameTime());
            Assert.IsTrue(keyboard.IsKeyReleased(Keys.G));
        });
    }

    [TestMethod]
    public void FocusMovesToTextInput_DiscardsPendingViewportShortcut()
    {
        WithViewport((viewport, keyboard, input) =>
        {
            RaiseKey(viewport, Key.Tab, Keyboard.PreviewKeyDownEvent);
            RaiseKey(viewport, Key.Tab, Keyboard.PreviewKeyUpEvent);
            Keyboard.Focus(input);
            keyboard.Update(new GameTime());
            Assert.IsFalse(keyboard.IsKeyReleased(Keys.Tab));
            Keyboard.Focus(viewport);
            keyboard.Update(new GameTime());
            Assert.IsFalse(keyboard.IsKeyReleased(Keys.Tab));
        });
    }

    [TestMethod]
    public void ModifierChordCompletedBetweenFrames_PreservesTheModifier()
    {
        WithViewport((viewport, keyboard, _) =>
        {
            RaiseKey(viewport, Key.LeftCtrl, Keyboard.PreviewKeyDownEvent);
            RaiseKey(viewport, Key.I, Keyboard.PreviewKeyDownEvent);
            RaiseKey(viewport, Key.I, Keyboard.PreviewKeyUpEvent);
            RaiseKey(viewport, Key.LeftCtrl, Keyboard.PreviewKeyUpEvent);
            keyboard.Update(new GameTime());
            Assert.IsTrue(keyboard.IsKeyComboReleased(Keys.I, Keys.LeftControl));
        });
    }

    private static void WithViewport(Action<Border, KeyboardComponent, TextBox> action)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var viewport = new Border { Focusable = true, Width = 50, Height = 50 };
            var input = new TextBox();
            var content = new StackPanel();
            content.Children.Add(viewport);
            content.Children.Add(input);
            var window = new Window { Content = content, Width = 100, Height = 100, ShowInTaskbar = false };
            var game = new Mock<IWpfGame>();
            game.Setup(value => value.GetFocusElement()).Returns(viewport);
            using var keyboard = new KeyboardComponent(game.Object);
            try
            {
                window.Show();
                Keyboard.Focus(viewport);
                Assert.IsTrue(viewport.IsKeyboardFocused);
                keyboard.Update(new GameTime());
                action(viewport, keyboard, input);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void RaiseKey(UIElement target, Key key, RoutedEvent routedEvent)
    {
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target)!, 0, key)
        {
            RoutedEvent = routedEvent,
            Handled = true
        });
    }
}
