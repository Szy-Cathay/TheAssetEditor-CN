using System.Windows.Input;
using GameWorld.Core.WpfWindow;

namespace GameWorld.Core.Test.WpfWindow;

public class WpfGameInputTests
{
    [TestCase(Key.Tab, ModifierKeys.None, true)]
    [TestCase(Key.Tab, ModifierKeys.Shift, true)]
    [TestCase(Key.Tab, ModifierKeys.Alt, false)]
    [TestCase(Key.A, ModifierKeys.None, false)]
    public void ShouldPreserveViewportFocus_OnlyHandlesNonAltTab(
        Key key,
        ModifierKeys modifiers,
        bool expected)
    {
        Assert.That(
            WpfGame.ShouldPreserveViewportFocus(key, modifiers),
            Is.EqualTo(expected));
    }
}
