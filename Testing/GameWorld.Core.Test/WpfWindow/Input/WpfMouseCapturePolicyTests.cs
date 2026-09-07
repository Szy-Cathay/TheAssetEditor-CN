using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GameWorld.Core.WpfWindow.Input;

namespace GameWorld.Core.Test.WpfWindow.Input;

public class WpfMouseCapturePolicyTests
{
    [TestCase(2, 0, 300, 2)]
    [TestCase(298, 0, 300, 298)]
    [TestCase(299, 0, 300, 3)]
    [TestCase(0, 0, 300, 296)]
    [TestCase(610, 0, 300, 18)]
    [TestCase(-310, 0, 300, 282)]
    [TestCase(-294, 0, 300, 2)]
    [TestCase(594, 0, 300, 298)]
    [TestCase(-1801, -2200, -1800, -2197)]
    public void WrapCoordinate_PreservesOvershootAndLeavesBothInnerEdgesStable(
        int position, int minimum, int maximum, int expected)
    {
        var wrapped = WpfMouse.WrapCoordinate(position, minimum, maximum);
        Assert.That(wrapped, Is.EqualTo(expected));
        Assert.That(WpfMouse.WrapCoordinate(wrapped, minimum, maximum), Is.EqualTo(wrapped));
    }

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

    [TestCase(Visibility.Visible)]
    [TestCase(Visibility.Collapsed)]
    [Apartment(ApartmentState.STA)]
    public void HitTestFocusElement_NonInteractiveOverlay_AllowsViewportHit(
        Visibility overlayVisibility)
    {
        var viewport = new Border
        {
            Width = 100,
            Height = 100,
            Background = Brushes.Black
        };
        var overlay = new Border
        {
            Width = 100,
            Height = 100,
            Background = Brushes.Red,
            IsHitTestVisible = false,
            Visibility = overlayVisibility
        };
        var parent = new Grid
        {
            Width = 100,
            Height = 100
        };
        parent.Children.Add(viewport);
        parent.Children.Add(overlay);
        var window = new Window
        {
            Width = 120,
            Height = 120,
            Left = -10_000,
            Top = -10_000,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = parent
        };

        try
        {
            window.Show();
            window.UpdateLayout();

            if (overlayVisibility == Visibility.Visible)
            {
                var rawVisualHit =
                    VisualTreeHelper.HitTest(parent, new Point(50, 50))
                        ?.VisualHit;
                Assert.That(
                    rawVisualHit,
                    Is.Not.SameAs(viewport),
                    "Visual-layer hit testing must reproduce the overlay interception.");
            }

            var isViewportHit = WpfMouse.HitTestFocusElement(
                parent,
                new Point(50, 50),
                viewport);

            Assert.That(isViewportHit, Is.True);
        }
        finally
        {
            window.Close();
        }
    }
}
