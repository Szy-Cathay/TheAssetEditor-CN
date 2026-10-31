#nullable enable
using System;
using System.Windows;
using GameWorld.Core.WpfWindow.Internals;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct3D11;

namespace GameWorld.Core.WpfWindow;

// The private images retain their WPF interop bitmaps while complete scene frames alternate.
internal sealed class D3D11FramePresenter : IDisposable
{
    private sealed class Buffer : IDisposable
    {
        internal readonly RenderTarget2D Target;
        internal readonly D3D11Image Image;
        internal readonly D3D11Image.SharedSurface Surface;
        internal readonly Query Completion;
        internal long Sequence;
        internal bool WriteLocked;
        private readonly DependencyPropertyChangedEventHandler _availabilityChanged;
        private bool _needsRebind, _detached;

        internal Buffer(GraphicsDevice graphics, Device device, int width, int height,
            DependencyPropertyChangedEventHandler availabilityChanged)
        {
            _availabilityChanged = availabilityChanged;
            Target = new RenderTarget2D(graphics, width, height, false, SurfaceFormat.Bgr32,
                DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.DiscardContents, true);
            Image = new D3D11Image();
            D3D11Image.SharedSurface? surface = null;
            try
            {
                Surface = surface = Image.CreateSharedSurface(Target);
                Image.Lock();
                try { Image.SetRegisteredBackBuffer(Surface); }
                finally { Image.Unlock(); }
                Completion = new Query(device, new QueryDescription { Type = QueryType.Event });
            }
            catch { Image.Dispose(); surface?.Dispose(); Target.Dispose(); throw; }
            Image.IsFrontBufferAvailableChanged += OnAvailabilityChanged;
        }

        private void OnAvailabilityChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            if (!(bool)args.NewValue) _needsRebind = true;
            _availabilityChanged(sender, args);
        }

        internal void EnsureBinding()
        {
            if (!_needsRebind) return;
            Image.SetRegisteredBackBuffer(Surface);
            _needsRebind = false;
        }

        internal bool TryBeginWrite()
        {
            if (_detached || !Image.IsFrontBufferAvailable) return false;
            if (WriteLocked) throw new InvalidOperationException("The GPU still owns this presentation buffer.");
            if (!Image.TryLockForPresentation()) return false;
            WriteLocked = true;
            try { EnsureBinding(); }
            catch { EndWrite(); throw; }
            return true;
        }

        internal void AbortWrite()
        {
            // A failed submission may already have queued GPU commands. Never unlock a bound image.
            try { Image.SetRegisteredBackBuffer(null); _needsRebind = true; }
            finally { EndWrite(); }
        }

        internal void EndWrite()
        {
            if (!WriteLocked) return;
            WriteLocked = false;
            Image.Unlock();
        }

        internal bool TryDetach()
        {
            if (_detached) return true;
            if (WriteLocked)
            {
                Image.SetRegisteredBackBuffer(null);
                EndWrite();
            }
            else if (!Image.TryDetachBackBuffer()) return false;
            _detached = true;
            return true;
        }

        public void Dispose()
        {
            Image.IsFrontBufferAvailableChanged -= OnAvailabilityChanged;
            if (WriteLocked)
            {
                // Detaching first prevents WPF from reading an unfinished GPU write during close.
                Image.SetRegisteredBackBuffer(null);
                EndWrite();
            }
            Image.Dispose();
            Completion.Dispose();
            Surface.Dispose();
            Target.Dispose();
        }
    }

    private readonly Buffer[] _buffers;
    private readonly DeviceContext _context;
    private readonly int _maximumPendingFrames;
    private int _displayed = -1, _pending = -1, _following = -1, _reserved = -1;
    private bool _disposed;
    internal long LastSubmittedSequence { get; private set; }
    internal long PresentedSequence { get; private set; }
    internal long GpuNotReadyCount { get; private set; }
    internal long FrontBufferUnavailableCount { get; private set; }
    internal long ImageLockUnavailableCount { get; private set; }
    internal long SubmissionBackpressureCount { get; private set; }
    internal long SupersededSequenceCount { get; private set; }
    internal long UpdateWithoutDrawingCount { get; private set; }
    internal string LastDeferralReason { get; private set; } = "None";
    internal bool HasPending => _pending >= 0;
    internal int PendingFrameCount => (_pending >= 0 ? 1 : 0) + (_following >= 0 ? 1 : 0);
    internal RenderTarget2D DisplayedTarget => _buffers[_displayed < 0 ? 0 : _displayed].Target;
    internal D3D11Image DisplayedImage => _buffers[_displayed < 0 ? 0 : _displayed].Image;

    internal D3D11FramePresenter(GraphicsDevice graphics, int width, int height,
        DependencyPropertyChangedEventHandler availabilityChanged, int maximumPendingFrames = 1)
    {
        if (maximumPendingFrames is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(maximumPendingFrames));
        _maximumPendingFrames = maximumPendingFrames;
        var device = (Device)graphics.Handle;
        _context = device.ImmediateContext;
        _buffers = new Buffer[maximumPendingFrames + 1];
        try
        {
            for (var index = 0; index < _buffers.Length; index++)
                _buffers[index] = new Buffer(graphics, device, width, height, availabilityChanged);
        }
        catch { foreach (var buffer in _buffers) buffer?.Dispose(); throw; }
    }

    internal bool OwnsImage(object? image)
    {
        foreach (var buffer in _buffers) if (ReferenceEquals(image, buffer.Image)) return true;
        return false;
    }

    internal bool IsComplete => (_pending < 0 || _context.IsDataAvailable(_buffers[_pending].Completion, AsynchronousFlags.DoNotFlush))
        && (_following < 0 || _context.IsDataAvailable(_buffers[_following].Completion, AsynchronousFlags.DoNotFlush));

    internal void RecordUpdateWithoutDrawing() => UpdateWithoutDrawingCount++;

    internal bool TryPresent(D3D11HostFrameDiagnostics? diagnostics, ref D3D11FrameTiming sample)
    {
        if (_pending < 0) return false;
        diagnostics?.StartPhase(ref sample, D3D11FramePhase.QueryWait);
        sample.QueryPolls++;
        var complete = _context.IsDataAvailable(_buffers[_pending].Completion, AsynchronousFlags.DoNotFlush);
        diagnostics?.EndPhase(ref sample, D3D11FramePhase.QueryWait);
        var buffer = _buffers[_pending];
        if (!complete)
        {
            GpuNotReadyCount++; LastDeferralReason = "GpuQueryNotReady";
            sample.PresentationDeferred++; return false;
        }
        if (!buffer.Image.IsFrontBufferAvailable)
        {
            FrontBufferUnavailableCount++; LastDeferralReason = "FrontBufferUnavailable";
            sample.PresentationDeferred++; return false;
        }
        sample.PresentAttempts++;
        try
        {
            buffer.EnsureBinding();
            buffer.Image.MarkDirty();
            _displayed = _pending;
            _pending = _following;
            _following = -1;
            PresentedSequence = buffer.Sequence;
            sample.PresentCalls++;
            sample.PresentedSequence = PresentedSequence;
            LastDeferralReason = "None";
            return true;
        }
        catch
        {
            _pending = _following;
            _following = -1;
            buffer.AbortWrite();
            throw;
        }
        finally
        {
            diagnostics?.StartPhase(ref sample, D3D11FramePhase.Unlock);
            buffer.EndWrite();
            diagnostics?.EndPhase(ref sample, D3D11FramePhase.Unlock);
        }
    }

    internal bool TrySubmit(GraphicsDevice graphics, SpriteBatch sprites, RenderTarget2D frame, long sequence,
        D3D11HostFrameDiagnostics? diagnostics, ref D3D11FrameTiming sample)
    {
        if (PendingFrameCount >= _maximumPendingFrames)
        {
            SubmissionBackpressureCount++; LastDeferralReason = "PendingSubmission";
            return false;
        }
        if (sequence <= LastSubmittedSequence) { CancelReservedFrame(); return false; }
        if (!TryReserveFrame(diagnostics, ref sample)) return false;
        var next = _reserved;
        var buffer = _buffers[next];
        try
        {
            diagnostics?.StartPhase(ref sample, D3D11FramePhase.Blit);
            try
            {
                graphics.SetRenderTarget(buffer.Target);
                sprites.Begin();
                sprites.Draw(frame, graphics.Viewport.Bounds, Color.White);
                sprites.End();
            }
            finally { graphics.SetRenderTarget(frame); }
            _context.End(buffer.Completion);
            diagnostics?.EndPhase(ref sample, D3D11FramePhase.Blit);
            diagnostics?.StartPhase(ref sample, D3D11FramePhase.Flush);
            graphics.Flush();
            diagnostics?.EndPhase(ref sample, D3D11FramePhase.Flush);
            if (_pending < 0) _pending = next;
            else _following = next;
            _reserved = -1;
            buffer.Sequence = sequence;
            SupersededSequenceCount += Math.Max(0, sequence - LastSubmittedSequence - 1);
            LastSubmittedSequence = sequence;
            LastDeferralReason = "None";
            sample.PresentationSubmitted++;
            sample.SubmittedSequence = sequence;
            return true;
        }
        catch { _reserved = -1; buffer.AbortWrite(); throw; }
    }

    internal bool TryReserveFrame(D3D11HostFrameDiagnostics? diagnostics, ref D3D11FrameTiming sample)
    {
        if (PendingFrameCount >= _maximumPendingFrames)
        {
            SubmissionBackpressureCount++; LastDeferralReason = "PendingSubmission";
            return false;
        }
        if (_reserved >= 0) return true;
        for (var next = 0; next < _buffers.Length; next++)
        {
            if (next == _displayed || next == _pending || next == _following) continue;
            var buffer = _buffers[next];
            diagnostics?.StartPhase(ref sample, D3D11FramePhase.Lock);
            var acquired = buffer.TryBeginWrite();
            diagnostics?.EndPhase(ref sample, D3D11FramePhase.Lock);
            if (acquired) { _reserved = next; return true; }
            if (!buffer.Image.IsFrontBufferAvailable)
            { FrontBufferUnavailableCount++; LastDeferralReason = "FrontBufferUnavailable"; }
            else { ImageLockUnavailableCount++; LastDeferralReason = "ImageLockUnavailable"; }
        }
        sample.PresentationDeferred++; return false;
    }

    internal void CancelReservedFrame()
    {
        if (_reserved < 0) return;
        _buffers[_reserved].EndWrite();
        _reserved = -1;
    }

    internal bool TryDisposeIfComplete()
    {
        if (!IsComplete) return false;
        foreach (var buffer in _buffers)
            if (!buffer.TryDetach()) return false;
        Dispose();
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var buffer in _buffers) buffer.Dispose();
        // The immediate context belongs to the shared GraphicsDevice.
    }
}
