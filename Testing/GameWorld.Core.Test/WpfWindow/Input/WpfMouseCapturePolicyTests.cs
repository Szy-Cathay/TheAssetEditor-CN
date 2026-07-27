using System.Windows.Input;
using GameWorld.Core.WpfWindow.Input;

namespace GameWorld.Core.Test.WpfWindow.Input;

public class WpfMouseCapturePolicyTests
{
    [Test]
    public void ShouldReleaseMouseCapture_CompletedClickInsideViewport_ReturnsTrue()
    {
        var shouldRelease = WpfMouse.ShouldReleaseMouseCapture(
            true,
            MouseButtonState.Released,
            MouseButtonState.Released);

        Assert.That(shouldRelease, Is.True);
    }

    [Test]
    public void ShouldReleaseMouseCapture_MiddleDragOutsideViewport_ReturnsFalse()
    {
        var shouldRelease = WpfMouse.ShouldReleaseMouseCapture(
            true,
            MouseButtonState.Released,
            MouseButtonState.Pressed);

        Assert.That(shouldRelease, Is.False);
    }
}
