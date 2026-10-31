using GameWorld.Core.WpfWindow.Internals;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.Direct3D11;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = Microsoft.Xna.Framework.Color;

namespace GameWorld.Core.WpfWindow
{
    /// <summary>
    /// Host a Direct3D 11 scene.
    /// Low level abstraction, should not be used (use <see cref="WpfGame"/> instead).
    /// </summary>
    public abstract class D3D11Host : Image, IDisposable
    {
        private static readonly object GraphicsDeviceLock = new object();

        private bool _isRendering;

        // The Direct3D 11 device (shared by all D3D11Host elements)
        // TODO: find out why the original implementation had this as static & shared
        private static GraphicsDevice _staticGraphicsDevice;
        private GraphicsDevice _graphicsDevice;
        private static bool? _isInDesignMode;
        private static int _referenceCount;

        private D3D11Image _d3D11Image;
        private D3D11Image _legacyImage = null!;
        private RenderTarget2D? _legacyRenderTarget;
        private bool _disposed;
        private TimeSpan _lastRenderingTime;
        private bool _suspendWhenHidden, _renderingSuspended;
        private D3D11HostFrameDiagnostics? _frameDiagnostics;
        private bool _loaded, GraphicsDeviceInitialized;

        /// <summary>
        /// Shared between WPF and monogame.
        /// </summary>
        private RenderTarget2D _sharedRenderTarget;
        /// <summary>
        /// Actual rendertarget that monogame will draw into.
        /// Once a draw call is finished it then copies its content to <see cref="_sharedRenderTarget"/>.
        /// This prevents flickering of the screen when WPF decides to draw the rendertarget to screen while monogame is midway populating it.
        /// </summary>
        private RenderTarget2D _cachedRenderTarget;
        private bool _resetBackBuffer, _dpiChanged;
        private bool _isActive;
        private SpriteBatch _spriteBatch;
        private DeviceContext _presentationContext;
        private Query _presentationQuery;
        private D3D11FramePresenter? _framePresenter;
        private readonly List<D3D11FramePresenter> _retiredPresenters = new();
        private bool _useAsynchronousPresentation;
        private long _renderSequence;
        private bool _preparedStartupBackBuffer;
        private PresentationParameters? _pendingPresentationParameters;
        private double _dpiScalingFactor = 1;
        private static bool _useASingleSharedGraphicsDevice = true;
        private List<IDisposable> _toBeDisposedNextFrame = new List<IDisposable>();

        /// <summary>
        /// Initializes a new instance of the <see cref="D3D11Host"/> class.
        /// </summary>
        protected D3D11Host()
        {
            // defaulting to fill as that's what's needed in most cases
            Stretch = Stretch.Fill;

            Loaded += OnLoaded;
            IsVisibleChanged += OnIsVisibleChanged;
        }

        /// <summary>
        /// <para>
        /// Must be set before the first instance is created.
        /// </para>
        /// <para>
        /// Defaults to false, (prior 2.0 the behaviour was as if this was set to true).
        /// If set to false each WpfGame instance gets its own graphicsdevice.
        /// If set to true, A single graphics device is shared across all instances of WpfGame.
        /// </para>
        /// <para>
        /// Currently there is bug in monogame when setting this to false AND using MSAA (Disposing rendertargets will crash, so your only option is to not dispose them, causing memoryleaks).
        /// Meanwhile if this is true, you can Dispose rendertargets just fine.
        /// </para>
        /// </summary>
        public static bool UseASingleSharedGraphicsDevice
        {
            get { return _useASingleSharedGraphicsDevice; }
            set
            {
                if (_referenceCount > 0)
                {
                    throw new InvalidOperationException($"{nameof(UseASingleSharedGraphicsDevice)} must be set before the first instance is created and cannot be changed during runtime.");
                }
                _useASingleSharedGraphicsDevice = value;
            }
        }

        /// <summary>
        /// Determines whether the game runs in fixed timestep or not.
        /// The current implementation always calls Update and Draw after each other continuously.
        /// Since the rendering is based on the WPF render thread the exact times at which it will be called cannot be guaranteed.
        /// Therefore this value is always false.
        /// </summary>
        public bool IsFixedTimeStep => false;

        /// <summary>
        /// Gets or sets the target time between two updates.
        /// Set to 1 tick to sync with screen refresh rate (no artificial limit).
        /// WPF CompositionTarget.Rendering will drive rendering at the display's refresh rate.
        /// </summary>
        public TimeSpan TargetElapsedTime { get; set; } = TimeSpan.FromTicks(1); // Sync with screen refresh rate

        // Explicit VFX startup preparation; no UI-thread wait and no elapsed-time catch-up.
        public System.Threading.Tasks.Task? RenderingPreparation { get; set; }

        // Explicit opt-in for VFX scenes. Other editors retain synchronous presentation.
        public bool UseAsynchronousPresentation
        {
            get => _useAsynchronousPresentation;
            set
            {
                if (_useAsynchronousPresentation == value) return;
                _useAsynchronousPresentation = value;
                _resetBackBuffer = true;
            }
        }

        // Opt in only for scenes whose hidden playback must remain paused.
        public bool SuspendWhenHidden
        {
            get => _suspendWhenHidden;
            set
            {
                if (_suspendWhenHidden == value) return;
                _suspendWhenHidden = value;
                _renderingSuspended = value && !IsVisible;
#if DEBUG
                if (value)
                    _frameDiagnostics ??= D3D11HostFrameDiagnostics.TryCreate(_suspendWhenHidden, this);
                else
                {
                    _frameDiagnostics?.Dispose();
                    _frameDiagnostics = null;
                }
#endif
            }
        }

        /// <summary>
        /// Gets a value indicating whether the controls runs in the context of a designer (e.g.
        /// Visual Studio Designer or Expression Blend).
        /// </summary>
        /// <value>
        /// <see langword="true" /> if controls run in design mode; otherwise, 
        /// <see langword="false" />.
        /// </value>
        public static bool IsInDesignMode
        {
            get
            {
                if (!_isInDesignMode.HasValue)
                    _isInDesignMode =
                        (bool)
                        DependencyPropertyDescriptor.FromProperty(DesignerProperties.IsInDesignModeProperty, typeof(FrameworkElement))
                            .Metadata.DefaultValue;

                return _isInDesignMode.Value;
            }
        }

        /// <summary>
        /// Gets whether the current control is active.
        /// This property will be true, when the control is active (parent window has focus).
        /// Note that if the game is inside a tab, then this property will only be true if the window has focus and the tab is active.
        /// If either the window loses focus or the tab is switched, this property will be false.
        /// </summary>
        public bool IsActive
        {
            get { return _isActive; }
            private set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                if (!_disposed)
                {
                    if (IsActive)
                        Activated?.Invoke(this, EventArgs.Empty);
                    else
                        Deactivated?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>
        /// Gets the scaling factor that is applied to the attached gamecontrol.
        /// For legacy compatibility this always defaults to a factor of 1.
        /// If your monitor is scaled at 200%, then this will cause the game to render at only half the size.
        /// In order to render at full native resolution, set this value to the correct <see cref="SystemDpiScalingFactor"/>.
        /// </summary>
        public double DpiScalingFactor
        {
            get => _dpiScalingFactor;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(DpiScalingFactor), "value must be positive");
                if (_dpiScalingFactor == value)
                    return;

                _dpiScalingFactor = value;
                _dpiChanged = true;
            }
        }

        /// <summary>
        /// Gets the graphics device.
        /// </summary>
        /// <value>The graphics device.</value>
        public GraphicsDevice GraphicsDevice => UseASingleSharedGraphicsDevice ? _staticGraphicsDevice : _graphicsDevice;

        /// <summary>
        /// Default services collection.
        /// </summary>
        public GameServiceContainer Services { get; } = new GameServiceContainer();

        /// <summary>
        /// Event is invoked when the game is activated (window receives focus).
        /// Note for game instances inside a tab control this will fire only when the tab is activated (or the tab was active and the window is focused).
        /// </summary>
        public event EventHandler<EventArgs> Activated;

        /// <summary>
        /// Event is invoked when the game is deactivated (window loses focus).
        /// Note for game instances inside a tab control this will fire only when the tab is deactivated (or the tab was active and the window loses focus).
        /// </summary>
        public event EventHandler<EventArgs> Deactivated;

        public void Dispose()
        {
            _frameDiagnostics?.Dispose();
            _frameDiagnostics = null;
            // each of those has its own check for disposed
            StopRendering();
            UnitializeImageSource();
            DisposeRenderTargetsFromPreviousFrames();
            if (GraphicsDeviceInitialized)
            {
                UninitializeGraphicsDevice();
                GraphicsDeviceInitialized = false;
            }

            if (_disposed)
                return;

            _disposed = true;
            Activated = null;
            Deactivated = null;
            if (_spriteBatch != null)
            {
                _spriteBatch.Dispose();
                _spriteBatch = null;
            }
            Dispose(true);
        }

        protected abstract void Dispose(bool disposing);

        protected virtual void Initialize()
        {
            _disposed = false;

            var graphicsDeviceManager = (IGraphicsDeviceManager)Services.GetService(typeof(IGraphicsDeviceManager));
            if (graphicsDeviceManager == null)
                throw new NotSupportedException($"Services must contain a {nameof(IGraphicsDeviceManager)} instance");

            graphicsDeviceManager.CreateDevice();
        }

        /// <summary>
        /// Raises the <see cref="FrameworkElement.SizeChanged" /> event, using the specified 
        /// information as part of the eventual event data.
        /// </summary>
        /// <param name="sizeInfo">Details of the old and new size involved in the change.</param>
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            _resetBackBuffer = true;
            base.OnRenderSizeChanged(sizeInfo);
        }

        protected virtual void Render(GameTime time)
        {
        }

        protected virtual bool SupportsFrameUpdatesWithoutDrawing => false;
        protected virtual void UpdateFrameWithoutDrawing(GameTime time) { }

        private void InitializeGraphicsDevice()
        {
            lock (GraphicsDeviceLock)
            {
                _referenceCount++;
                if (_referenceCount == 1 || !UseASingleSharedGraphicsDevice)
                {
                    // Create Direct3D 11 device.
                    var presentationParameters = new PresentationParameters
                    {
                        // Do not associate graphics device with window.
                        DeviceWindowHandle = nint.Zero,
                        DepthStencilFormat = DepthFormat.Depth24,
                    };
                    var gd = CreateSharedGraphicsDevice(presentationParameters);
                    if (UseASingleSharedGraphicsDevice)
                        _staticGraphicsDevice = gd;
                    else
                        _graphicsDevice = gd;
                }
            }
        }

        private static GraphicsDevice CreateSharedGraphicsDevice(PresentationParameters presentationParameters)
        {
            // TODO: could allow user to chose which adapter to use
            return new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef, presentationParameters);
        }

        /// <summary>
        /// Helper that allows applying new presentation parameters.
        /// </summary>
        /// <param name="pp"></param>
        public void RecreateGraphicsDevice(PresentationParameters pp)
        {
            lock (GraphicsDeviceLock)
            {
                if (GraphicsDevice == null)
                    throw new NotSupportedException("Can only recreate graphicsdevice when one already exists. Initalize one first!");

                if (UseAsynchronousPresentation || _framePresenter != null)
                {
                    _pendingPresentationParameters = pp;
                    _resetBackBuffer = true;
                }
                else CreateGraphicsDeviceDependentResources(pp);
            }
        }

        protected virtual void OnGraphicDeviceDisposed()
        {
        }

        private void UninitializeGraphicsDevice()
        {
            // We pretende this is a singelton...
            //return;
            //lock (GraphicsDeviceLock)
            //{
            //    _referenceCount--;
            //    if (_referenceCount == 0 || !UseASingleSharedGraphicsDevice)
            //    {
            //        // dirty workaround. when just calling .Dispose monogame crashes because MonoGame internal _swapChain is null and
            //        // they just blindly trust it isn't null and call .Dipose on it
            //
            //        // as a workaround we will now call the bare minimum setup methods to create a (temporary) swapchain in the last second and then call Dispose
            //        // that way Dispose doesn't crash
            //
            //        // literally anything but 0 will do, our WPF window is already closed at this point (returns 0) so just use a random pointer
            //        // technically a swapChain will be attached to this device handle for a few ms before we dispose it again
            //        // let's hope attaching it to "1" doesn't break anything
            //        GraphicsDevice.PresentationParameters.DeviceWindowHandle = new IntPtr(1);
            //
            //        // call below will now trigger some events on the GraphicsDevice and we have no way of suppressing them
            //        // but we need to call reset to actually create a swapChain, otherwise Dispose will crash
            //        GraphicsDevice.Reset();
            //
            //        // finally we can safely call dispose without receiving NullReferenceException
            //        GraphicsDevice.Dispose();
            //        if (UseASingleSharedGraphicsDevice)
            //            _staticGraphicsDevice = null;
            //        else
            //            _graphicsDevice = null;
            //
            //        OnGraphicDeviceDisposed();
            //    }
            //}
        }

        private bool CreateBackBuffer()
        {
            // Keep a single live GPU generation while a submitted frame is unfinished.
            if (_framePresenter != null && !_framePresenter.IsComplete) return false;
            if (!UseAsynchronousPresentation) _legacyImage.SetBackBuffer(null);
            if (_framePresenter != null)
            {
                _retiredPresenters.Add(_framePresenter);
                _framePresenter = null;
            }
            if (!UseAsynchronousPresentation && _legacyRenderTarget != null)
            {
                // can't directly dispose rendertargets now since Monogame needs it for one more frame
                // (when a rendertarget is swapped, monogame still uses the old one for one more frame)
                // so queue dispose for later
                _toBeDisposedNextFrame.Add(_legacyRenderTarget);
                _legacyRenderTarget = null;
            }

            var sampleCount = 0;
            if (_cachedRenderTarget != null)
            {
                // if there was a previous rendertarget, reuse its sample count
                sampleCount = _cachedRenderTarget.MultiSampleCount;
                // can't directly dispose rendertargets now since Monogame needs it for one more frame
                // (when a rendertarget is swapped, monogame still uses the old one for one more frame)
                // so queue dispose for later
                _toBeDisposedNextFrame.Add(_cachedRenderTarget);
            }

            var width = (int)(Math.Max(ActualWidth, 1) * DpiScalingFactor);
            var height = (int)(Math.Max(ActualHeight, 1) * DpiScalingFactor);

            var parameters = new PresentationParameters
            {
                BackBufferWidth = ActualWidth > 0 ? width : _pendingPresentationParameters?.BackBufferWidth ?? width,
                BackBufferHeight = ActualHeight > 0 ? height : _pendingPresentationParameters?.BackBufferHeight ?? height,
                MultiSampleCount = _pendingPresentationParameters?.MultiSampleCount ?? sampleCount
            };
            CreateGraphicsDeviceDependentResources(parameters);
            _pendingPresentationParameters = null;
            return true;
        }

        private void CreateGraphicsDeviceDependentResources(PresentationParameters pp)
        {
            var width = pp.BackBufferWidth;
            var height = pp.BackBufferHeight;
            var ms = pp.MultiSampleCount;

            if (UseAsynchronousPresentation)
            {
                _framePresenter = new D3D11FramePresenter(GraphicsDevice, width, height, OnIsFrontBufferAvailableChanged,
                    SupportsFrameUpdatesWithoutDrawing ? 2 : 1);
                if (_sharedRenderTarget == null || _legacyRenderTarget == null && ReferenceEquals(Source, _legacyImage))
                    _sharedRenderTarget = _framePresenter.DisplayedTarget;
            }
            else
            {
                _sharedRenderTarget = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Bgr32,
                    DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.DiscardContents, true);
                _sharedRenderTarget.Name = "sharedRenderTarget";
                _legacyRenderTarget = _sharedRenderTarget;
                _d3D11Image = _legacyImage;
                _legacyImage.SetBackBuffer(_sharedRenderTarget);
                Source = _legacyImage;
            }

            // internal rendertarget; all user draws render into this before we draw it to the actual back buffer
            // that way flickering of screen will be prevented when under heavy system load (such as when using rendertargets on intel graphics: https://gitlab.com/MarcStan/MonoGame.Framework.WpfInterop/issues/12)
            // -> always preserve its contents so worst case user gets to see the old screen again
            _cachedRenderTarget = new RenderTarget2D(GraphicsDevice, width, height, false, SurfaceFormat.Bgr32,
                DepthFormat.Depth24Stencil8, ms, RenderTargetUsage.PreserveContents, false);
            _cachedRenderTarget.Name = "cachedRenderTarget";
        }

        private void InitializeImageSource()
        {
            _d3D11Image = new D3D11Image();
            _legacyImage = _d3D11Image;
            _d3D11Image.IsFrontBufferAvailableChanged += OnIsFrontBufferAvailableChanged;
            CreateBackBuffer();
            Source = _d3D11Image;
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            var device = (SharpDX.Direct3D11.Device)GraphicsDevice.Handle;
            _presentationContext = device.ImmediateContext;
            _presentationQuery = new Query(device, new QueryDescription { Type = QueryType.Event });
        }

        private void OnIsFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
        {
            if (!ReferenceEquals(sender, Source)) return;
            if (((D3D11Image)sender).IsFrontBufferAvailable)
            {
                StartRendering();
                _resetBackBuffer = true;
            }
            else
            {
                if (SuspendWhenHidden) _renderingSuspended = true;
                StopRendering();
            }
        }

        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs eventArgs)
        {
            if (SuspendWhenHidden && !(bool)eventArgs.NewValue) _renderingSuspended = true;
        }

        public void ForceEnsureCreated()
        {
            OnLoaded(null, null);
        }

        private void OnLoaded(object sender, RoutedEventArgs eventArgs)
        {
            if (IsInDesignMode || _loaded)
                return;

            _loaded = true;

            // get the current window and register Activate/Deactivate
            //var window = LogicalTreeHelperEx.FindParent<Window>(this);
            //if (window == null)
            //    throw new NotSupportedException("The game control does not have a parent window, this is currently not supported");

            //window.Activated += OnWindowActivated;
            //window.Deactivated += OnWindowDeactivated;
            //window.Closed += OnWindowClosed;
            var tabControl = LogicalTreeHelperEx.FindParent<TabControl>(this);
            // if there is any parent that is a tabitem, we must be inside a TabControl

            var windowIsInTab = tabControl != null;
            if (windowIsInTab)
            {
                // also hook up to tab events
                tabControl.SelectionChanged += TabChanged;
            }
            // check whether current window is indeed the active one (usually it will be)
            // but there are some edge cases (programmatically show a tab with this game control after a timer) -> window doesn't have to be active
            //var active = Application.Current.Windows.OfType<Window>().SingleOrDefault(x => x.IsActive);
            //if (active == window && IsVisible)
            {
                IsActive = true;
            }

            if (!GraphicsDeviceInitialized)
            {
                InitializeGraphicsDevice();
                GraphicsDeviceInitialized = true;
            }
            InitializeImageSource();

            // workaround for exceptions in Onloaded being swallowed by default on x64
            // https://stackoverflow.com/questions/4807122/wpf-showdialog-swallowing-exceptions-during-window-load
            try
            {
                // initialize runs in userland
                // user can fuck up anything an have an exception thrown
                // if it throws, we don't want rendering to start
                Initialize();
                StartRendering();
            }
            catch (Exception ex)
            {
                if (Environment.Is64BitOperatingSystem || Environment.Is64BitProcess)
                {
                    // catch and rethrow because WPF just swallows it silently on x64..
                    var deCancerifyWpf = new BackgroundWorker();
                    deCancerifyWpf.DoWork += (e, arg) => { arg.Result = arg.Argument; };
                    deCancerifyWpf.RunWorkerCompleted += (e, arg) =>
                    {
                        // who needs a proper stacktrace anyway
                        // at least we get to see the exception..
                        throw new Exception("Initialize failed, see inner exception for details.", (Exception)arg.Result);
                    };
                    deCancerifyWpf.RunWorkerAsync(ex);
                }
            }
        }

        private void TabChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl)
            {
                // are we being added or removed?
                var gameParent = LogicalTreeHelperEx.FindParent<TabItem>(this);
                if (e.AddedItems.Contains(gameParent))
                {
                    // added
                    IsActive = true;
                }
                else if (e.RemovedItems.Contains(gameParent))
                {
                    // removed
                    IsActive = false;
                }
            }
        }

        private void OnWindowClosed(object sender, EventArgs e)
        {
            Dispose();
        }

        private void OnWindowActivated(object sender, EventArgs e)
        {
            var tabControl = LogicalTreeHelperEx.FindParent<TabControl>(this);
            // if there is any parent that is a tabitem, we must be inside a TabControl
            // 
            var windowIsInTab = tabControl != null;
            if (windowIsInTab)
            {
                // inside a tab, check whether the tab is active
                var tabItem = LogicalTreeHelperEx.FindParent<TabItem>(this);
                // only set to true for the currently active tab
                IsActive = tabControl.SelectedItem == tabItem;
            }
            else
            {
                // regular control on the window
                IsActive = true;
            }
        }

        private void OnWindowDeactivated(object sender, EventArgs e)
        {
            IsActive = false;
        }

        private void OnRendering(object sender, EventArgs eventArgs)
        {
            var diagnostics = _frameDiagnostics;
            if (diagnostics != null && !diagnostics.IsCollecting)
            {
                _frameDiagnostics = null;
                diagnostics = null;
            }
            var sample = default(D3D11FrameTiming);
            diagnostics?.BeginCallback(ref sample);
            if (diagnostics != null)
            {
                sample.CompositionTicks = (eventArgs as RenderingEventArgs)?.RenderingTime.Ticks ?? 0;
                sample.PreviousCompositionTicks = _lastRenderingTime.Ticks;
                sample.IsRendering = _isRendering; sample.IsVisible = IsVisible; sample.WasSuspended = _renderingSuspended;
                sample.PendingResize = _resetBackBuffer; sample.PendingDpi = _dpiChanged;
                sample.ActualWidth = ActualWidth; sample.ActualHeight = ActualHeight; sample.DpiScale = DpiScalingFactor;
                sample.WidthBefore = _cachedRenderTarget?.Width ?? 0; sample.HeightBefore = _cachedRenderTarget?.Height ?? 0;
            }
            try
            {
                if (!_isRendering)
                    return;

                var renderingEventArgs = (RenderingEventArgs)eventArgs;
                if (RenderingPreparation is { } preparation)
                {
                    if (!preparation.IsCompleted)
                    {
                        PrepareStartupBackBuffer(diagnostics, ref sample);
                        _lastRenderingTime = renderingEventArgs.RenderingTime;
                        _renderingSuspended = true;
                        return;
                    }
                    RenderingPreparation = null;
                    // A failed optional preparation falls back to ordinary runtime compilation.
                    _ = preparation.Exception;
                }
                if (!PrepareRenderingTime(renderingEventArgs.RenderingTime, IsVisible, out var frameTime))
                {
                    sample.HiddenSkipped = true;
                    return;
                }

                if (_toBeDisposedNextFrame.Count > 0)
                {
                    diagnostics?.StartPhase(ref sample, D3D11FramePhase.DisposeTargets);
                    DisposeRenderTargetsFromPreviousFrames();
                    diagnostics?.EndPhase(ref sample, D3D11FramePhase.DisposeTargets);
                }
                DisposeCompletedPresenters();
                ReleaseUnusedLegacyTarget();

                // Recreate back buffer if necessary.
                if (_resetBackBuffer || _dpiChanged)
                {
                    diagnostics?.StartPhase(ref sample, D3D11FramePhase.Rebuild);
                    if (!CreateBackBuffer())
                    {
                        diagnostics?.EndPhase(ref sample, D3D11FramePhase.Rebuild);
                        sample.PresentationDeferred++;
                        TryUpdateWithoutPresentation(frameTime, renderingEventArgs.RenderingTime, ref sample);
                        return;
                    }
                    _dpiChanged = false;
                    diagnostics?.EndPhase(ref sample, D3D11FramePhase.Rebuild);
                    sample.ResourceRebuilds++;
                }

                if (_framePresenter != null)
                {
                    sample.AsynchronousPresentation = true;
                    TryCompleteAsynchronousPresentation(diagnostics, ref sample);
                    if (frameTime != null && SupportsFrameUpdatesWithoutDrawing
                        && !_framePresenter.TryReserveFrame(diagnostics, ref sample)
                        && TryUpdateWithoutPresentation(frameTime, renderingEventArgs.RenderingTime, ref sample))
                        return;
                }

                if (frameTime != null)
                {
                    diagnostics?.StartPhase(ref sample, D3D11FramePhase.Render);
                    GraphicsDevice.SetRenderTarget(_cachedRenderTarget);
                    Render(frameTime);
                    _renderSequence++;
                    diagnostics?.EndPhase(ref sample, D3D11FramePhase.Render);
                    sample.ElapsedGameTimeTicks = frameTime.ElapsedGameTime.Ticks;
                    _lastRenderingTime = renderingEventArgs.RenderingTime;
                    _renderingSuspended = false;
                }

                if (_framePresenter != null)
                {
                    _framePresenter.TrySubmit(GraphicsDevice, _spriteBatch, _cachedRenderTarget, _renderSequence,
                        diagnostics, ref sample);
                    sample.SubmittedSequence = _framePresenter.LastSubmittedSequence;
                    sample.PresentedSequence = _framePresenter.PresentedSequence;
                    _resetBackBuffer = false;
                    return;
                }

                sample.PresentAttempts++;
                diagnostics?.StartPhase(ref sample, D3D11FramePhase.Lock);
                _d3D11Image.Lock();
                diagnostics?.EndPhase(ref sample, D3D11FramePhase.Lock);
                try
                {
                    diagnostics?.StartPhase(ref sample, D3D11FramePhase.Blit);
                    // poor man's swap chain implementation
                    // now draw from cache to backbuffer
                    GraphicsDevice.SetRenderTarget(_sharedRenderTarget);
                    _spriteBatch.Begin();
                    _spriteBatch.Draw(_cachedRenderTarget, GraphicsDevice.Viewport.Bounds, Color.White);
                    _spriteBatch.End();
                    GraphicsDevice.SetRenderTarget(_cachedRenderTarget);
                    // Wait for the blit without copying a GPU texture back to the CPU.
                    _presentationContext.End(_presentationQuery);
                    diagnostics?.EndPhase(ref sample, D3D11FramePhase.Blit);
                    diagnostics?.StartPhase(ref sample, D3D11FramePhase.Flush);
                    GraphicsDevice.Flush();
                    diagnostics?.EndPhase(ref sample, D3D11FramePhase.Flush);
                    diagnostics?.StartPhase(ref sample, D3D11FramePhase.QueryWait);
                    while (!_presentationContext.IsDataAvailable(_presentationQuery, AsynchronousFlags.DoNotFlush))
                    {
                        if (diagnostics != null) sample.QueryPolls++;
                        Thread.Yield();
                    }
                    diagnostics?.EndPhase(ref sample, D3D11FramePhase.QueryWait);
                    _d3D11Image.MarkDirty();
                    sample.PresentCalls++;
                }
                finally
                {
                    diagnostics?.StartPhase(ref sample, D3D11FramePhase.Unlock);
                    _d3D11Image.Unlock();
                    diagnostics?.EndPhase(ref sample, D3D11FramePhase.Unlock);
                }
                _resetBackBuffer = false;
            }
            catch { _framePresenter?.CancelReservedFrame(); sample.Faulted = true; throw; }
            finally
            {
                if (diagnostics != null)
                {
                    sample.Width = _cachedRenderTarget?.Width ?? 0; sample.Height = _cachedRenderTarget?.Height ?? 0;
                    diagnostics.EndCallback(ref sample);
                }
            }
        }

        private void PrepareStartupBackBuffer(D3D11HostFrameDiagnostics? diagnostics, ref D3D11FrameTiming sample)
        {
            if (_preparedStartupBackBuffer || !GraphicsDeviceInitialized || !UseAsynchronousPresentation || _renderSequence != 0
                || SuspendWhenHidden && !IsVisible
                || !_resetBackBuffer && !_dpiChanged
                || !(ActualWidth > 0 && ActualHeight > 0
                    || _pendingPresentationParameters is { BackBufferWidth: > 0, BackBufferHeight: > 0 })) return;
            diagnostics?.StartPhase(ref sample, D3D11FramePhase.Rebuild);
            try
            {
                if (!CreateBackBuffer()) return;
                _resetBackBuffer = false;
                _dpiChanged = false;
                _preparedStartupBackBuffer = true;
                sample.ResourceRebuilds++;
            }
            finally { diagnostics?.EndPhase(ref sample, D3D11FramePhase.Rebuild); }
        }

        private bool TryUpdateWithoutPresentation(GameTime? frameTime, TimeSpan now, ref D3D11FrameTiming sample)
        {
            if (frameTime == null || !SupportsFrameUpdatesWithoutDrawing) return false;
            UpdateFrameWithoutDrawing(frameTime);
            _lastRenderingTime = now;
            _renderingSuspended = false;
            sample.ElapsedGameTimeTicks = frameTime.ElapsedGameTime.Ticks;
            if (_framePresenter != null)
            {
                _framePresenter.RecordUpdateWithoutDrawing();
                sample.SubmittedSequence = _framePresenter.LastSubmittedSequence;
                sample.PresentedSequence = _framePresenter.PresentedSequence;
            }
            return true;
        }

        private bool TryCompleteAsynchronousPresentation(D3D11HostFrameDiagnostics? diagnostics, ref D3D11FrameTiming sample)
        {
            if (_framePresenter == null || !_framePresenter.TryPresent(diagnostics, ref sample)) return false;
            _sharedRenderTarget = _framePresenter.DisplayedTarget;
            _d3D11Image = _framePresenter.DisplayedImage;
            Source = _d3D11Image;
            return true;
        }

        private void DisposeCompletedPresenters()
        {
            for (var index = _retiredPresenters.Count - 1; index >= 0; index--)
            {
                var presenter = _retiredPresenters[index];
                if (presenter.OwnsImage(Source) || !presenter.TryDisposeIfComplete()) continue;
                _retiredPresenters.RemoveAt(index);
            }
        }

        private void ReleaseUnusedLegacyTarget()
        {
            if (!UseAsynchronousPresentation || _legacyRenderTarget == null || ReferenceEquals(Source, _legacyImage)
                || !_legacyImage.TryDetachBackBuffer()) return;
            _toBeDisposedNextFrame.Add(_legacyRenderTarget);
            _legacyRenderTarget = null;
        }

        private bool PrepareRenderingTime(TimeSpan now, bool isVisible, out GameTime? frameTime)
        {
            frameTime = null;
            if (SuspendWhenHidden && !isVisible)
            {
                _lastRenderingTime = now;
                _renderingSuspended = true;
                return false;
            }
            if (_renderingSuspended)
                frameTime = new GameTime(now, TimeSpan.Zero);
            else if (_lastRenderingTime != now)
            {
                var delta = now - _lastRenderingTime;
                if (delta >= TargetElapsedTime) frameTime = new GameTime(now, delta);
            }
            else if (_resetBackBuffer)
                frameTime = new GameTime(now, TimeSpan.Zero);
            return true;
        }

        private void DisposeRenderTargetsFromPreviousFrames()
        {
            for (var i = 0; i < _toBeDisposedNextFrame.Count; i++)
            {
                _toBeDisposedNextFrame[i]?.Dispose();
                _toBeDisposedNextFrame.RemoveAt(i--);
            }
        }

        protected void StartRendering()
        {
            if (_disposed)
                return;

            if (_isRendering)
                return;

            CompositionTarget.Rendering += OnRendering;
            _isRendering = true;
        }

        protected void StopRendering()
        {
            if (!_isRendering)
                return;

            CompositionTarget.Rendering -= OnRendering;
            _isRendering = false;
        }

        private void UnitializeImageSource()
        {
            Source = null;
            _presentationQuery?.Dispose();
            _presentationQuery = null;
            // The immediate context is owned by the shared graphics device.
            _presentationContext = null;

            if (_legacyImage != null)
            {
                _legacyImage.IsFrontBufferAvailableChanged -= OnIsFrontBufferAvailableChanged;
                _legacyImage.Dispose();
                _legacyImage = null!;
            }
            _d3D11Image = null;
            _legacyRenderTarget?.Dispose();
            _legacyRenderTarget = null;
            _sharedRenderTarget = null;
            _framePresenter?.Dispose();
            _framePresenter = null;
            foreach (var presenter in _retiredPresenters) presenter.Dispose();
            _retiredPresenters.Clear();
            if (_cachedRenderTarget != null)
            {
                if (_cachedRenderTarget.MultiSampleCount > 0 && UseASingleSharedGraphicsDevice)
                {
                    // TODO: this is a memoryleak, the code is intentional because Dispose actually crashes Monogame when using a shared graphicsdevice and disposing MSAA enabled rendertargets
                    // at the very latest this will happen on window close, for SPA this is fine as the process will shut down
                    // but if your editor has multiple windows (or tabs) that can be created/closed multiple times this will slowly increase memory usage..
                }
                else
                {
                    _cachedRenderTarget.Dispose();
                }
                _cachedRenderTarget = null;
            }
        }
    }
}
