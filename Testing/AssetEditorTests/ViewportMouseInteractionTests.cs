using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GameWorld.Core.Components.Input;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.Services;
using Point = System.Windows.Point;

namespace AssetEditorTests;

[TestClass]
public partial class ViewportMouseInteractionTests
{
    [DataTestMethod]
    [DataRow(2, 0)]
    [DataRow(-2, 0)]
    [DataRow(0, 2)]
    [DataRow(0, -2)]
    [DataRow(2, 2)]
    public void NativeRelativeMiddleDrag_StaysConfinedAndContinuesSmoothlyAfterCrossing(int dx, int dy)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            ActivateNativeInputViewport(viewport, mouse);
            mouse.SetCursorPosition(dx > 0 ? 297 : dx < 0 ? 3 : 150,
                dy > 0 ? 197 : dy < 0 ? 3 : 100);
            mouse.BeginContinuousDrag();
            PumpInput();
            mouse.Update(new GameTime());
            var start = mouse.Position();
            var physicalStart = NativePosition(viewport);
            var wraps = 0;
            // Exercise the native relative-input queue of this test HWND, with MMB held.
            NativeMouseInputEvent(0, 0, 0x0020);
            try
            {
                for (var index = 0; index < 64; index++)
                {
                    var physicalBefore = NativePosition(viewport);
                    var before = mouse.Position();
                    NativeMouseInputEvent(dx, dy, 0x0001 | 0x2000);
                    var undispatched = NativePosition(viewport);
                    Assert.IsTrue(undispatched.X >= 0 && undispatched.X < viewport.ActualWidth &&
                        undispatched.Y >= 0 && undispatched.Y < viewport.ActualHeight,
                        $"Step {index}: cursor {undispatched} escaped; focused={viewport.IsKeyboardFocused}, captured={viewport.IsMouseCaptured}.");
                    PumpInput(10);
                    mouse.Update(new GameTime());
                    var physical = NativePosition(viewport);
                    if (Vector2.Distance(physicalBefore, physical) > 100) wraps++;
                    var delta = mouse.Position() - before;
                    Assert.IsTrue(Math.Abs(delta.X) < 32 && Math.Abs(delta.Y) < 32,
                        $"Tiny relative motion produced a viewport-sized jump: {delta}");
                    Assert.IsTrue(dx == 0 || delta.X * dx >= -0.05f,
                        $"Step {index}: input ({dx}, {dy}), delta {delta}, physical {physicalBefore} -> {physical}.");
                    Assert.IsTrue(dy == 0 || delta.Y * dy >= -0.05f,
                        $"Step {index}: input ({dx}, {dy}), delta {delta}, physical {physicalBefore} -> {physical}.");
                }
                Assert.IsTrue(wraps > 0,
                    $"Relative motion must cross at least one edge. Physical {physicalStart} -> {NativePosition(viewport)}, continuous {start} -> {mouse.Position()}, captured={viewport.IsMouseCaptured}.");
                Assert.IsTrue(Vector2.Distance(start, mouse.Position()) > 5, "Dragging must continue after wrapping.");
            }
            finally
            {
                NativeMouseInputEvent(0, 0, 0x0040);
                PumpInput();
            }
        });
    }

    [TestMethod]
    public void CapturedDrag_SystemCursorCannotLeaveViewportEvenBeforeInputDispatch()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.BeginContinuousDrag();
            foreach (var position in new[] { new Vector2(-500, 100), new Vector2(900, 100),
                new Vector2(150, -500), new Vector2(150, 900), new Vector2(900, 900) })
            {
                mouse.SetCursorPosition((int)position.X, (int)position.Y);
                var physical = NativePosition(viewport);
                Assert.IsTrue(physical.X >= 0 && physical.X < viewport.ActualWidth &&
                    physical.Y >= 0 && physical.Y < viewport.ActualHeight,
                    $"An active drag escaped the 3D viewport before message dispatch: {physical}");
                PumpInput();
            }
        });
    }

    [TestMethod]
    public void StaleEdgeReportsAfterAcknowledgement_CannotAddAnotherViewportWidth()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.BeginContinuousDrag();
            mouse.SetCursorPosition(300, 100);
            PumpInput();
            mouse.Update(new GameTime());
            var before = mouse.Position();
            var physical = NativePosition(viewport);
            Assert.IsTrue(physical.X < 4);
            for (var index = 0; index < 20; index++)
            {
                NativeMoveMessage(viewport, new Vector2(300, 100));
                PumpInput();
                mouse.Update(new GameTime());
                Assert.AreEqual(before, mouse.Position(), "Replayed pre-warp coordinates must not move or scale the model.");
                Assert.AreEqual(physical, NativePosition(viewport), "A stale edge report must not reposition the OS cursor.");
            }
        });
    }

    [DataTestMethod]
    [DataRow(290)]
    [DataRow(150)]
    [DataRow(80)]
    public void StaleInteriorReportsAfterAcknowledgement_CannotMoveAStationaryCursor(int x)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.BeginContinuousDrag();
            mouse.SetCursorPosition(300, 100);
            PumpInput();
            mouse.Update(new GameTime());
            var before = mouse.Position();
            for (var index = 0; index < 5; index++)
            {
                NativeMoveMessage(viewport, new Vector2(x, 100));
                mouse.Update(new GameTime());
                Assert.AreEqual(before, mouse.Position(), "No physical movement must mean no change to translation or scale.");
                PumpInput();
            }
        });
    }

    [TestMethod]
    public void StaleInteriorReportBeforeAcknowledgement_CannotPublishAnIntermediateJump()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.BeginContinuousDrag();
            PumpInput();
            mouse.Update(new GameTime());
            mouse.SetCursorPosition(300, 100);
            var edge = NativePosition(viewport);
            NativeMoveMessage(viewport, edge);
            NativeMoveMessage(viewport, new Vector2(290, 100));
            mouse.Update(new GameTime());
            Assert.AreEqual(edge.X, mouse.Position().X, 0.05f, "Only the actual movement to the edge may be consumed.");
            PumpInput();
            mouse.Update(new GameTime());
            Assert.AreEqual(edge.X, mouse.Position().X, 0.05f);
        });
    }

    [TestMethod]
    public void RenderPolling_DoesNotWarpOrInventMotionBetweenNativeMessages()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(150, 100);
            mouse.BeginContinuousDrag();
            PumpInput();
            mouse.Update(new GameTime());
            var before = mouse.Position();
            mouse.SetCursorPosition(300, 100);
            var physical = NativePosition(viewport);
            for (var index = 0; index < 20; index++)
            {
                mouse.Update(new GameTime());
                Assert.AreEqual(physical, NativePosition(viewport), "Rendering must not relocate the OS cursor.");
                Assert.AreEqual(before, mouse.Position(), "Only native input messages may advance a drag.");
            }
        });
    }

    [TestMethod]
    public void RenderDpiContext_StationaryCursorCannotOscillateOrAccumulateMotion()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(150, 100);
            mouse.BeginContinuousDrag();
            mouse.SetCursorPosition(300, 100);
            PumpInput();
            mouse.Update(new GameTime());
            var before = mouse.Position();
            var physical = NativePosition(viewport);
            for (var index = 0; index < 20; index++)
            {
                var previous = SetThreadDpiAwarenessContext(new nint(-1));
                try { mouse.Update(new GameTime()); }
                finally { SetThreadDpiAwarenessContext(previous); }
                Assert.AreEqual(before, mouse.Position(), "A render callback's DPI context must not change drag input.");
                Assert.AreEqual(physical, NativePosition(viewport), "No physical input must mean no cursor warps.");
            }
        });
    }

    [DataTestMethod]
    [DataRow(-1)]
    [DataRow(-2)]
    [DataRow(-4)]
    public void NativeNotificationsFromAnotherDpiContext_DoNotMoveAStationaryCursor(int context)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.BeginContinuousDrag();
            mouse.SetCursorPosition(300, 100);
            PumpInput();
            mouse.Update(new GameTime());
            var before = mouse.Position();
            var physical = NativePosition(viewport);
            for (var index = 0; index < 10; index++)
            {
                var previous = SetThreadDpiAwarenessContext(new nint(context));
                try { NativeMoveMessage(viewport, new Vector2(150, 100)); }
                finally { SetThreadDpiAwarenessContext(previous); }
                mouse.Update(new GameTime());
                Assert.AreEqual(before, mouse.Position());
                Assert.AreEqual(physical, NativePosition(viewport));
            }
        });
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void BeginAndEndFromRenderDpiContext_UseTheViewportCoordinateSpace(bool wrap)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            GetClipCursor(out var original);
            var physical = NativePosition(viewport);
            var previous = SetThreadDpiAwarenessContext(new nint(-1));
            try { mouse.BeginContinuousDrag(wrap); }
            finally { SetThreadDpiAwarenessContext(previous); }
            mouse.Update(new GameTime());
            Assert.AreEqual(physical, mouse.Position());
            Assert.AreEqual(physical, NativePosition(viewport));
            GetClipCursor(out var during);
            Assert.AreNotEqual(original, during);
            previous = SetThreadDpiAwarenessContext(new nint(-1));
            try { mouse.EndContinuousDrag(); }
            finally { SetThreadDpiAwarenessContext(previous); }
            GetClipCursor(out var restored);
            Assert.AreEqual(original, restored);
            Assert.IsFalse(viewport.IsMouseCaptured);
        });
    }

    [DataTestMethod]
    [DataRow(1.0, 298, 100, 5, 0)]
    [DataRow(1.25, 150, 198, 0, 5)]
    [DataRow(1.5, 298, 198, 5, 5)]
    public void ReversingSmallMovesAcrossEdges_PreservesEveryDisplacement(
        double scale, int x, int y, int dx, int dy)
    {
        WithViewport(scale, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(x, y);
            mouse.BeginContinuousDrag();
            PumpInput();
            mouse.Update(new GameTime());
            for (var step = 0; step < 12; step++)
            {
                var before = mouse.Position();
                var physical = NativePosition(viewport);
                var direction = step % 2 == 0 ? 1 : -1;
                mouse.SetCursorPosition((int)Math.Round(physical.X) + direction * dx,
                    (int)Math.Round(physical.Y) + direction * dy);
                var expectedDelta = NativePosition(viewport) - physical;
                PumpInput();
                mouse.Update(new GameTime());
                Assert.AreEqual(expectedDelta.X, mouse.Position().X - before.X, 0.05f);
                Assert.AreEqual(expectedDelta.Y, mouse.Position().Y - before.Y, 0.05f);
            }
        });
    }

    [DataTestMethod]
    [DataRow(1.0, 300, 100)]
    [DataRow(1.0, 0, 100)]
    [DataRow(1.25, 0, 100)]
    [DataRow(1.5, 150, 200)]
    [DataRow(1.0, 150, 0)]
    [DataRow(1.5, 300, 200)]
    public void NativeMessagePump_BoundaryWarpAndSmallMotionStayContinuous(double scale, int x, int y)
    {
        WithViewport(scale, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(150, 100);
            mouse.BeginContinuousDrag();
            mouse.SetCursorPosition(x, y);
            PumpInput();
            mouse.Update(new GameTime());
            var physical = NativePosition(viewport);
            if (x == 300) Assert.IsTrue(physical.X < 4, $"Native cursor did not wrap: {physical}");
            if (x == 0) Assert.IsTrue(physical.X >= 296, $"Native cursor did not wrap: {physical}");
            if (y == 200) Assert.IsTrue(physical.Y < 4, $"Native cursor did not wrap: {physical}");
            if (y == 0) Assert.IsTrue(physical.Y >= 196, $"Native cursor did not wrap: {physical}");
            var before = mouse.Position();
            for (var index = 0; index < 5; index++)
            {
                PumpInput();
                mouse.Update(new GameTime());
                Assert.AreEqual(before, mouse.Position(), "Stationary cursor must not accumulate another viewport width.");
            }
            mouse.SetCursorPosition((int)Math.Round(physical.X) + (x == 0 ? -3 : 3),
                (int)Math.Round(physical.Y) + (y == 0 ? -3 : 3));
            PumpInput();
            mouse.Update(new GameTime());
            var actualDelta = NativePosition(viewport) - physical;
            Assert.AreEqual(actualDelta.X, mouse.Position().X - before.X, 0.05f);
            Assert.AreEqual(actualDelta.Y, mouse.Position().Y - before.Y, 0.05f);
        });
    }

    [TestMethod]
    public void ExternalClipPreventsWarp_CancelsOnceInsteadOfLoopingAtTheEdge()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(150, 100);
            mouse.BeginContinuousDrag();
            PumpInput();
            GetClipCursor(out var viewportClip);
            var edge = viewport.PointToScreen(new Point(300, 100));
            var blockedClip = viewportClip with { Left = (int)edge.X - 1, Right = (int)edge.X };
            var interruptions = 0;
            mouse.CaptureInterrupted += () => interruptions++;
            try
            {
                Assert.IsTrue(ClipCursor(ref blockedClip));
                mouse.SetCursorPosition(300, 100);
                PumpInput();
                Assert.AreEqual(1, interruptions);
                Assert.IsNull(mouse.CapturedCursorPosition);
                Assert.IsFalse(viewport.IsMouseCaptured);
                GetClipCursor(out var currentClip);
                Assert.AreEqual(blockedClip, currentClip, "Do not overwrite another native owner's restriction.");
            }
            finally
            {
                ClipCursor(ref viewportClip);
            }
            mouse.SetCursorPosition(150, 100);
            mouse.BeginContinuousDrag();
            mouse.SetCursorPosition(300, 100);
            PumpInput();
            mouse.Update(new GameTime());
            Assert.IsTrue(NativePosition(viewport).X < 4);
            Assert.AreEqual((int)mouse.Position().X, mouse.State().X);
        });
    }

    [DataTestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    public void FastBoundaryMove_WrapsBeforeAFrameAndKeepsContinuousDisplacement(double scale)
    {
        for (var repetition = 0; repetition < 12; repetition++)
            WithViewport(scale, (viewport, mouse, _) =>
            {
                ActivateNativeInputViewport(viewport, mouse);
                mouse.SetCursorPosition(150, 100);
                mouse.Update(new GameTime());
                mouse.BeginContinuousDrag();
                Assert.IsTrue(viewport.IsMouseCaptured);
                mouse.SetCursorPosition(300, 100);
                PumpInput();
                mouse.Update(new GameTime());
                var first = mouse.Position();
                Assert.AreEqual(300f, first.X, 1.1f);
                Assert.IsTrue(mouse.CapturedCursorPosition!.Value.X < 4);
                Assert.IsTrue(mouse.DeltaPosition().X < -147);
                // The warp's queued MouseMove must not count as physical movement.
                for (var index = 0; index < 20; index++)
                {
                    MoveEvent(viewport);
                    mouse.Update(new GameTime());
                    Assert.AreEqual(first.X, mouse.Position().X, 0.01f);
                    Assert.AreEqual(Vector2.Zero, mouse.DeltaPosition());
                }
                var expected = first.X + 102 - mouse.CapturedCursorPosition.Value.X;
                mouse.SetCursorPosition(102, 100);
                PumpInput();
                mouse.Update(new GameTime());
                Assert.AreEqual(expected, mouse.Position().X, 1.1f);
            });
    }

    [DataTestMethod]
    [DataRow(-500, 100)]
    [DataRow(900, 100)]
    [DataRow(150, -500)]
    [DataRow(150, 900)]
    [DataRow(900, 900)]
    public void FastMoveBeyondViewport_KeepsCursorAndDisplacementWithinTheCapturedInput(int x, int y)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(150, 100);
            mouse.BeginContinuousDrag();
            mouse.SetCursorPosition(x, y);
            var input = NativePosition(viewport);
            PumpInput();
            mouse.Update(new GameTime());
            var position = NativePosition(viewport);
            Assert.IsTrue(position.X >= 0 && position.X < viewport.ActualWidth);
            Assert.IsTrue(position.Y >= 0 && position.Y < viewport.ActualHeight);
            Assert.AreEqual(input.X, mouse.Position().X, 0.05f);
            Assert.AreEqual(input.Y, mouse.Position().Y, 0.05f);
            Assert.IsNotNull(mouse.CapturedCursorPosition);
        });
    }

    [TestMethod]
    public void EndAndRestartCapture_RestoresClipAndCanCaptureAgain()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            GetClipCursor(out var original);
            for (var index = 0; index < 3; index++)
            {
                mouse.BeginContinuousDrag();
                GetClipCursor(out var restricted);
                Assert.AreNotEqual(original, restricted, "Every drag must confine the OS cursor to the 3D viewport.");
                mouse.EndContinuousDrag();
                GetClipCursor(out var restored);
                Assert.AreEqual(original, restored);
                Assert.IsFalse(viewport.IsMouseCaptured);
            }
        });
    }

    [TestMethod]
    public void PressMoveReleaseBetweenFrames_PreservesBothEdgesAndDragOrigin()
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(50, 60);
            mouse.BeginContinuousDrag();
            viewport.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = Mouse.MouseDownEvent
            });
            mouse.SetCursorPosition(200, 160);
            PumpInput();
            viewport.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = Mouse.MouseUpEvent
            });
            mouse.Update(new GameTime());
            Assert.IsTrue(mouse.IsMouseButtonPressed(GameWorld.Core.Components.Input.MouseButton.Left));
            Assert.IsTrue(mouse.IsMouseButtonReleased(GameWorld.Core.Components.Input.MouseButton.Left));
            Assert.AreEqual(new Vector2(50, 60), mouse.GetPressPosition(GameWorld.Core.Components.Input.MouseButton.Left));
            Assert.AreEqual(new Vector2(200, 160), mouse.Position());
            mouse.Update(new GameTime());
            Assert.IsFalse(mouse.IsMouseButtonPressed(GameWorld.Core.Components.Input.MouseButton.Left));
            Assert.IsFalse(mouse.IsMouseButtonReleased(GameWorld.Core.Components.Input.MouseButton.Left));
        });
    }

    [TestMethod]
    public void FocusLeavesViewport_ReleasesCaptureAndNotifiesGestureOnce()
    {
        WithViewport(1, (viewport, mouse, input) =>
        {
            GetClipCursor(out var original);
            var interruptions = 0;
            mouse.CaptureInterrupted += () => interruptions++;
            mouse.BeginContinuousDrag();
            Keyboard.Focus(input);
            GetClipCursor(out var restored);
            Assert.AreEqual(original, restored);
            Assert.IsFalse(viewport.IsMouseCaptured);
            Assert.IsNull(mouse.CapturedCursorPosition);
            Assert.AreEqual(1, interruptions);
        });
    }

    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void ViewportResizeOrDisposal_ReleasesNativeRestriction(bool wrap)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            GetClipCursor(out var original);
            var interruptions = 0;
            mouse.CaptureInterrupted += () => interruptions++;
            mouse.BeginContinuousDrag(wrap);
            viewport.Width = 280;
            viewport.UpdateLayout();
            GetClipCursor(out var restored);
            Assert.AreEqual(original, restored);
            Assert.AreEqual(1, interruptions);
            mouse.BeginContinuousDrag(wrap);
            mouse.Dispose();
            GetClipCursor(out restored);
            Assert.AreEqual(original, restored);
        });
    }

    private static void WithViewport(double scale, Action<Border, MouseComponent, TextBox> action)
    {
        WpfTestApplicationHost.Invoke(_ =>
        {
            var viewport = new Border { Width = 300, Height = 200, Focusable = true, Background = Brushes.Black,
                LayoutTransform = new ScaleTransform(scale, scale) };
            var input = new TextBox();
            var panel = new StackPanel();
            panel.Children.Add(viewport);
            panel.Children.Add(input);
            var window = new Window { Title = "Kitbash mouse regression", Content = panel,
                SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false };
            var game = new Mock<IWpfGame>();
            game.Setup(value => value.GetFocusElement()).Returns(viewport);
            using var mouse = new MouseComponent(game.Object);
            try
            {
                window.Show();
                window.Activate();
                window.UpdateLayout();
                Keyboard.Focus(viewport);
                Assert.IsTrue(viewport.IsKeyboardFocused);
                mouse.SetCursorPosition(150, 100);
                PumpInput();
                action(viewport, mouse, input);
            }
            finally
            {
                mouse.EndContinuousDrag();
                window.Close();
            }
        });
    }

    private static void MoveEvent(UIElement viewport) => viewport.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
    {
        RoutedEvent = Mouse.MouseMoveEvent
    });

    [DataTestMethod]
    [DataRow(300, 100)]
    [DataRow(0, 100)]
    [DataRow(150, 200)]
    [DataRow(150, 0)]
    [DataRow(300, 200)]
    public void QueuedNativeEdgeReports_CountEachWarpOnceUntilAcknowledged(int x, int y)
    {
        WithViewport(1, (viewport, mouse, _) =>
        {
            mouse.SetCursorPosition(150, 100);
            mouse.BeginContinuousDrag();
            PumpInput();
            mouse.Update(new GameTime());
            mouse.SetCursorPosition(x, y);
            var input = NativePosition(viewport);
            // Deliver queued pre-warp coordinates while the actual OS cursor is already opposite.
            // Only the physical movement to the edge may change the continuous position.
            for (var index = 0; index < 100; index++)
            {
                NativeMoveMessage(viewport, input);
                mouse.Update(new GameTime());
                Assert.AreEqual(input.X, mouse.Position().X, 0.05f);
                Assert.AreEqual(input.Y, mouse.Position().Y, 0.05f);
            }
            PumpInput();
            mouse.Update(new GameTime());
            Assert.AreEqual(input.X, mouse.Position().X, 0.05f);
            Assert.AreEqual(input.Y, mouse.Position().Y, 0.05f);
        });
    }

    private static void NativeMoveMessage(FrameworkElement viewport, Vector2 position)
    {
        var screen = viewport.PointToScreen(new Point(position.X, position.Y));
        var client = new NativePoint { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        var handle = new WindowInteropHelper(Window.GetWindow(viewport)).Handle;
        Assert.IsTrue(ScreenToClient(handle, ref client));
        var coordinates = (client.X & 0xffff) | ((client.Y & 0xffff) << 16);
        SendMessage(handle, 0x0200, 0, new nint(coordinates));
    }

    private static Vector2 NativePosition(Visual viewport)
    {
        Assert.IsTrue(GetCursorPos(out var screen));
        var point = viewport.PointFromScreen(new Point(screen.X, screen.Y));
        return new Vector2((float)point.X, (float)point.Y);
    }

    private static void ActivateNativeInputViewport(FrameworkElement viewport, MouseComponent mouse)
    {
        var window = Window.GetWindow(viewport);
        var handle = new WindowInteropHelper(window).Handle;
        // WPF IsActive can be true while Windows still sends native input to another process.
        window.Topmost = true;
        window.Activate();
        var ready = System.Diagnostics.Stopwatch.StartNew();
        NativePoint cursor;
        do
        {
            mouse.SetCursorPosition(150, 100);
            PumpInput(20);
            Assert.IsTrue(GetCursorPos(out cursor));
        }
        while (WindowFromPoint(cursor) != handle && ready.ElapsedMilliseconds < 3000);
        Assert.AreEqual(handle, WindowFromPoint(cursor), "Only inject activation into the visible test HWND.");
        NativeMouseInputEvent(0, 0, 0x0002);
        NativeMouseInputEvent(0, 0, 0x0004);
        PumpInput();
        Keyboard.Focus(viewport);
        mouse.Update(new GameTime());
        Assert.AreEqual(handle, GetForegroundWindow(), "Native input requires the test HWND to own foreground focus.");
    }

    private static void PumpInput(int milliseconds = 100)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timer.Stop(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private record struct NativeRectangle(int Left, int Top, int Right, int Bottom);
    [DllImport("user32.dll")]
    private static extern bool GetClipCursor(out NativeRectangle rectangle);
    [DllImport("user32.dll")]
    private static extern bool ClipCursor(ref NativeRectangle rectangle);
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hwnd, int message, nint wParam, nint lParam);

    private static void NativeMouseInputEvent(int dx, int dy, uint flags)
    {
        var input = new NativeInput { Mouse = new NativeMouseInput { X = dx, Y = dy, Flags = flags } };
        Assert.AreEqual(1u, SendInput(1, new[] { input }, Marshal.SizeOf<NativeInput>()));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { public uint Type; public NativeMouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInformation;
    }
    [DllImport("user32.dll")]
    private static extern uint SendInput(uint count, NativeInput[] input, int size);
}
