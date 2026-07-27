using GameWorld.Core.Components.Input;
using GameWorld.Core.WpfWindow.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace GameWorld.Core.Test.Components.Input;

public class KeyboardComponentTests
{
    [Test]
    public void Update_KeyReleasedWithinSameInputContext_ReportsRelease()
    {
        var keyboard = new TestKeyboard
        {
            State = new KeyboardState(Keys.Tab)
        };
        var component = new KeyboardComponent(keyboard);
        component.Update(new GameTime());
        keyboard.State = new KeyboardState();

        component.Update(new GameTime());

        Assert.That(component.IsKeyReleased(Keys.Tab), Is.True);
    }

    [Test]
    public void Update_ApplicationInputContextChanges_DoesNotReportRelease()
    {
        var keyboard = new TestKeyboard
        {
            State = new KeyboardState(Keys.Tab)
        };
        var component = new KeyboardComponent(keyboard);
        component.Update(new GameTime());
        keyboard.State = new KeyboardState();
        keyboard.InputContextVersion++;

        component.Update(new GameTime());

        Assert.That(component.IsKeyReleased(Keys.Tab), Is.False);
    }

    private sealed class TestKeyboard : IWpfKeyboard
    {
        public int InputContextVersion { get; set; }
        public KeyboardState State { get; set; } = new();

        public KeyboardState GetState()
        {
            return State;
        }

        public void Dispose()
        {
        }
    }
}
