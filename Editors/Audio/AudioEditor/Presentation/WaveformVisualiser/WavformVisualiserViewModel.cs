using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.Audio.AudioEditor.Events.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Events.WaveformVisualiser;
using Editors.Audio.Shared.Wwise;
using NAudio.Wave;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.Ui.Common;

namespace Editors.Audio.AudioEditor.Presentation.WaveformVisualiser
{
    public partial class WaveformVisualiserViewModel : ObservableObject, IDisposable
    {
        private readonly IEventHub _eventHub;
        private readonly ISoundEngine _soundEngine;
        private readonly IWaveformRendererService _waveformRendererService;
        private readonly IWaveformVisualisationCacheService _waveformVisualisationCacheService;

        private static readonly TimeSpan s_waveformResizeDebounceDelay = TimeSpan.FromMilliseconds(200);

        private readonly SemaphoreSlim _waveformRenderGate = new(1, 1);
        private readonly List<string> _currentPlaylistFilePaths = [];
        private readonly CancellationTokenSource
            _lifetimeCancellationTokenSource = new();

        private bool _isWaveformPlayheadRenderingEnabled;
        private DateTime _lastFrameUtc;
        private double _visualSeconds;

        private CancellationTokenSource _waveformRenderCancellationTokenSource;
        private CancellationTokenSource _waveformResizeDebounceCancellationTokenSource;

        private DateTime _lastPlaybackTimerTextUpdateUtc = DateTime.MinValue;

        private string _currentFilePathKey;
        private string _loadedFilePath;
        private int _currentPlaylistIndex = -1;
        private bool _isExplicitStopRequested;
        
        [ObservableProperty] private string _waveformVisualiserLabel;
        [ObservableProperty] private int _waveformPixelWidth;
        [ObservableProperty] private int _waveformPixelHeight;
        [ObservableProperty] private ImageSource _audioWaveformBaseImageSource;
        [ObservableProperty] private ImageSource _audioWaveformOverlayImageSource;
        [ObservableProperty] private Rect _audioWaveformOverlayClip;
        [ObservableProperty] private double _hostWidth;
        [ObservableProperty] private TimeSpan _currentPlaybackTime = TimeSpan.Zero;
        [ObservableProperty] private TimeSpan _totalPlaybackTime = TimeSpan.Zero;
        [ObservableProperty] private bool _hasSelectedAudio;
        [ObservableProperty] private string _playPauseLabel =
            LocalizationManager.Instance.Get("WaveformVisualiser.Play");
        [ObservableProperty] private string _playbackStatus = string.Empty;

        public WaveformVisualiserViewModel(
            IEventHub eventHub,
            ISoundEngine soundEngine,
            IWaveformRendererService waveformRendererService,
            IWaveformVisualisationCacheService waveformVisualisationCacheService)
        {
            _eventHub = eventHub;
            _soundEngine = soundEngine;
            _waveformRendererService = waveformRendererService;
            _waveformVisualisationCacheService = waveformVisualisationCacheService;

            _eventHub.Register<AudioFilesExplorerNodeSelectedEvent>(this, AudioFilesExplorerNodeSelected);
            _eventHub.Register<AudioFilesChangedEvent>(this, OnAudioFilesChanged);
            _eventHub.Register<PlayAudioRequestedEvent>(this, OnPlayAudioRequested);
            _eventHub.Register<CacheWaveformRequestedEvent>(this, OnCacheWaveformRequested);
            _eventHub.Register<DecacheWaveformRequestedEvent>(this, OnDecacheWaveformRequested);
            _eventHub.Register<WaveformCacheInvalidatedEvent>(this, OnWaveformCacheInvalidated);

            _soundEngine.PlaybackStopped += OnPlaybackStopped;

            AudioWaveformOverlayClip = new Rect(0, 0, 0, 0);

            UpdateWaveformVisualiserLabel();
        }

        public void AudioFilesExplorerNodeSelected(AudioFilesExplorerNodeSelectedEvent e) => SetSelectedPlaylist(e.WavFilePaths);

        public void OnAudioFilesChanged(AudioFilesChangedEvent e)
        {
            var wavFilePaths = e.AudioFiles
                .Select(audioFile => audioFile.WavPackFilePath)
                .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
                .ToList();
            SetSelectedPlaylist(wavFilePaths);
        }

        public void OnPlayAudioRequested(PlayAudioRequestedEvent e)
        {
            SetSelectedPlaylist(e.WavFilePaths);
            PlayPause();
        }

        public void OnCacheWaveformRequested(CacheWaveformRequestedEvent e)
        {
            LoadWaveformImagesIntoCacheForCurrentWidth(e.FilePaths);
        }

        public void OnDecacheWaveformRequested(DecacheWaveformRequestedEvent e)
        {
            var filePathsInUse = new HashSet<string>(_currentPlaylistFilePaths, StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_currentFilePathKey))
                filePathsInUse.Add(_currentFilePathKey);

            foreach (var filePath in e.FilePaths)
            {
                if (string.IsNullOrWhiteSpace(filePath))
                    continue;

                if (filePathsInUse.Contains(filePath))
                    continue;

                _waveformVisualisationCacheService.Remove(filePath);
            }
        }

        public void OnWaveformCacheInvalidated(WaveformCacheInvalidatedEvent e)
        {
            ClearWaveformPreview();
            if (!string.IsNullOrWhiteSpace(_currentFilePathKey))
                _ = RenderWaveformPreviewAsync();
        }

        public void SetSelectedPlaylist(List<string> filePaths)
        {
            StopWaveformPlayheadRendering();
            StopPlaybackExplicitly();
            ClearWaveformPreview();

            _currentPlaylistFilePaths.Clear();
            _loadedFilePath = string.Empty;
            PlaybackStatus = string.Empty;
            if (filePaths != null)
            {
                var validDistinctPaths = filePaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase);

                foreach (var path in validDistinctPaths)
                    _currentPlaylistFilePaths.Add(path);
            }

            if (_currentPlaylistFilePaths.Count > 0)
                _currentPlaylistIndex = 0;
            else
                _currentPlaylistIndex = -1;
            HasSelectedAudio = _currentPlaylistIndex >= 0;

            _visualSeconds = 0;

            CurrentPlaybackTime = TimeSpan.Zero;

            if (_currentPlaylistIndex >= 0)
            {
                _currentFilePathKey = _currentPlaylistFilePaths[_currentPlaylistIndex];
                LoadWaveformImagesIntoCacheForCurrentWidth(_currentPlaylistFilePaths);
                UpdateWaveformVisualiserLabel();
                UpdateTotalPlaybackTimeFromFilePath(_currentFilePathKey);
                ResetWaveformPlayheadAndProgress();
                _ = RenderWaveformPreviewAsync();
            }
            else
            {
                _currentFilePathKey = string.Empty;
                UpdateWaveformVisualiserLabel();
                UpdateTotalPlaybackTimeFromFilePath(_currentFilePathKey);
                ResetWaveformPlayheadAndProgress();
            }
        }

        partial void OnHostWidthChanged(double value)
        {
            var previousCancellationToken = Interlocked.Exchange(ref _waveformResizeDebounceCancellationTokenSource, new CancellationTokenSource());
            if (previousCancellationToken != null)
            {
                previousCancellationToken.Cancel();
                previousCancellationToken.Dispose();
            }

            var cancellationToken = _waveformResizeDebounceCancellationTokenSource.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(s_waveformResizeDebounceDelay, cancellationToken).ConfigureAwait(false);
                    RebuildCacheForCurrentWidthExcludingCurrent();

                    if (!string.IsNullOrWhiteSpace(_currentFilePathKey))
                        await RenderWaveformPreviewAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
            });
        }

        private void RebuildCacheForCurrentWidthExcludingCurrent()
        {
            var targetWidth = GetTargetWidth();

            var filePathsNeedingRebuild = _currentPlaylistFilePaths
                .Where(filePath => _waveformVisualisationCacheService.GetWaveformVisualisation(filePath, targetWidth) == null)
                .Where(filePath => !string.Equals(filePath, _currentFilePathKey, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (filePathsNeedingRebuild.Length == 0)
                return;

            LoadWaveformImagesIntoCacheForCurrentWidth(filePathsNeedingRebuild);
        }

        [RelayCommand] private void PlayPause()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePathKey))
                return;

            try
            {
                EnsureCurrentAudioLoaded();

                _soundEngine.PlayPause();

                if (_soundEngine.PlaybackState == PlaybackState.Playing)
                    StartWaveformPlayheadRendering();
                else
                    StopWaveformPlayheadRendering();

                PlaybackStatus = string.Empty;
                UpdatePlayPauseLabel();
            }
            catch (Exception exception)
            {
                StopWaveformPlayheadRendering();
                PlaybackStatus = LocalizationManager.Instance.GetFormat(
                    "WaveformVisualiser.PlaybackFailed",
                    exception.Message);
                UpdatePlayPauseLabel();
            }
        }

        [RelayCommand]
        private void Stop()
        {
            StopWaveformPlayheadRendering();
            StopPlaybackExplicitly();
            _visualSeconds = 0;
            CurrentPlaybackTime = TimeSpan.Zero;
            ResetWaveformPlayheadAndProgress();
            UpdatePlayPauseLabel();
        }

        public void SeekToRatio(double ratio)
        {
            if (string.IsNullOrWhiteSpace(_currentFilePathKey) ||
                TotalPlaybackTime <= TimeSpan.Zero)
            {
                return;
            }

            try
            {
                EnsureCurrentAudioLoaded();
                var clampedRatio = Math.Clamp(ratio, 0, 1);
                var targetTime = TimeSpan.FromTicks(
                    (long)(TotalPlaybackTime.Ticks * clampedRatio));
                _soundEngine.SetPlaybackTime(targetTime);
                _visualSeconds = targetTime.TotalSeconds;
                CurrentPlaybackTime = targetTime;
                AudioWaveformOverlayClip = new Rect(
                    0,
                    0,
                    WaveformPixelWidth * clampedRatio,
                    WaveformPixelHeight);
                PlaybackStatus = string.Empty;
            }
            catch (Exception exception)
            {
                PlaybackStatus = LocalizationManager.Instance.GetFormat(
                    "WaveformVisualiser.PlaybackFailed",
                    exception.Message);
            }
        }

        private void EnsureCurrentAudioLoaded()
        {
            if (string.Equals(
                    _loadedFilePath,
                    _currentFilePathKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _soundEngine.LoadFromFilePath(_currentFilePathKey);
            _loadedFilePath = _currentFilePathKey;
            _visualSeconds = 0;
            CurrentPlaybackTime = TimeSpan.Zero;
            ResetWaveformPlayheadAndProgress();
        }

        private void UpdatePlayPauseLabel()
        {
            PlayPauseLabel = LocalizationManager.Instance.Get(
                _soundEngine.PlaybackState == PlaybackState.Playing
                    ? "WaveformVisualiser.Pause"
                    : "WaveformVisualiser.Play");
        }

        private async Task RenderWaveformPreviewAsync()
        {
            var previousCancellationToken = Interlocked.Exchange(ref _waveformRenderCancellationTokenSource, new CancellationTokenSource());
            if (previousCancellationToken != null)
            {
                previousCancellationToken.Cancel();
                previousCancellationToken.Dispose();
            }

            var cancellationToken = _waveformRenderCancellationTokenSource.Token;
            var filePathKey = _currentFilePathKey;

            if (string.IsNullOrWhiteSpace(filePathKey))
                return;

            var gateEntered = false;
            try
            {
                await _waveformRenderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                gateEntered = true;

                var targetWidth = GetTargetWidth();

                var cachedResult = _waveformVisualisationCacheService.GetWaveformVisualisation(filePathKey, targetWidth);
                if (cachedResult != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!IsCurrentFilePath(filePathKey))
                        return;

                    ApplyWaveformResult(cachedResult);
                    return;
                }

                var result = await _waveformRendererService.RenderAsync(filePathKey, targetWidth, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCurrentFilePath(filePathKey))
                    return;

                _waveformVisualisationCacheService.Store(filePathKey, result);

                ApplyWaveformResult(result);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                if (IsCurrentFilePath(filePathKey))
                {
                    RunOnUiThread(() =>
                        PlaybackStatus = LocalizationManager.Instance.GetFormat(
                            "WaveformVisualiser.RenderFailed",
                            exception.Message));
                }
            }
            finally
            {
                if (gateEntered)
                    _waveformRenderGate.Release();
            }
        }

        private bool IsCurrentFilePath(string filePath) =>
            string.Equals(filePath, _currentFilePathKey, StringComparison.OrdinalIgnoreCase);


        private void ApplyWaveformResult(
            WaveformRenderResult waveformRenderResult)
        {
            void Apply()
            {
                var baseImage =
                    waveformRenderResult.Visualisation.BaseImage;
                var overlayImage =
                    waveformRenderResult.Visualisation.OverlayImage;
                AudioWaveformBaseImageSource = baseImage;
                AudioWaveformOverlayImageSource = overlayImage;

                WaveformPixelWidth = baseImage.PixelWidth;
                WaveformPixelHeight = baseImage.PixelHeight;
                TotalPlaybackTime = waveformRenderResult.TotalTime;
                PlaybackStatus = string.Empty;

                AudioWaveformOverlayClip = new Rect(0, 0, 0, WaveformPixelHeight);
            }

            RunOnUiThread(Apply);
        }

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
            {
                action();
                return;
            }

            if (dispatcher.HasShutdownStarted ||
                dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                dispatcher.Invoke(action);
            }
            catch (TaskCanceledException)
            {
            }
            catch (InvalidOperationException)
                when (dispatcher.HasShutdownStarted ||
                      dispatcher.HasShutdownFinished)
            {
            }
        }

        private int GetTargetWidth()
        {
            var hostWidth = HostWidth;
            if (hostWidth > 0)
                return (int)Math.Max(300, hostWidth);
            return 800;
        }

        private void StartWaveformPlayheadRendering()
        {
            if (_isWaveformPlayheadRenderingEnabled)
                return;

            _lastFrameUtc = DateTime.UtcNow;
            CompositionTarget.Rendering += OnCompositionTargetRenderingForWaveformPlayhead;
            _isWaveformPlayheadRenderingEnabled = true;
        }

        private void StopWaveformPlayheadRendering()
        {
            if (!_isWaveformPlayheadRenderingEnabled)
                return;

            CompositionTarget.Rendering -= OnCompositionTargetRenderingForWaveformPlayhead;
            _isWaveformPlayheadRenderingEnabled = false;
        }

        private void OnCompositionTargetRenderingForWaveformPlayhead(object sender, EventArgs e)
        {
            if (_soundEngine == null || WaveformPixelWidth <= 0)
                return;

            var totalTime = TotalPlaybackTime;
            if (totalTime <= TimeSpan.Zero)
                return;

            var timeNow = DateTime.UtcNow;
            var secondsSinceLastFrame = (timeNow - _lastFrameUtc).TotalSeconds;
            _lastFrameUtc = timeNow;
            if (secondsSinceLastFrame <= 0)
                return;

            var deviceTimeSeconds = _soundEngine.GetDeviceAlignedTimeNow().TotalSeconds;

            _visualSeconds += secondsSinceLastFrame;
            var error = deviceTimeSeconds - _visualSeconds;
            var positionConvergenceGain = 0.15;
            _visualSeconds += positionConvergenceGain * error;

            if (_visualSeconds < 0)
                _visualSeconds = 0;

            if (_visualSeconds > totalTime.TotalSeconds)
                _visualSeconds = totalTime.TotalSeconds;

            var ratio = _visualSeconds / totalTime.TotalSeconds;
            var playedWidthPx = ratio * WaveformPixelWidth;

            AudioWaveformOverlayClip = new Rect(0, 0, playedWidthPx, WaveformPixelHeight);

            if ((timeNow - _lastPlaybackTimerTextUpdateUtc).TotalMilliseconds >= 50)
            {
                _lastPlaybackTimerTextUpdateUtc = timeNow;
                CurrentPlaybackTime = TimeSpan.FromSeconds(_visualSeconds);
            }
        }

        public void SetSelectedFilePath(string filePath)
        {
            StopWaveformPlayheadRendering();
            StopPlaybackExplicitly();
            ClearWaveformPreview();

            _visualSeconds = 0;

            _currentFilePathKey = filePath;
            _loadedFilePath = string.Empty;
            HasSelectedAudio = !string.IsNullOrWhiteSpace(filePath);
            PlaybackStatus = string.Empty;
            UpdateWaveformVisualiserLabel();
            UpdateTotalPlaybackTimeFromFilePath(_currentFilePathKey);

            CurrentPlaybackTime = TimeSpan.Zero;

            ResetWaveformPlayheadAndProgress();
            _ = RenderWaveformPreviewAsync();
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            void HandlePlaybackStopped()
            {
                var wasExplicitStop = _isExplicitStopRequested;
                _isExplicitStopRequested = false;

                ResetWaveformPlayheadAndProgress();
                StopWaveformPlayheadRendering();
                CurrentPlaybackTime = TimeSpan.Zero;

                if (wasExplicitStop)
                {
                    UpdatePlayPauseLabel();
                    return;
                }

                if (e != null && e.Exception != null)
                {
                    PlaybackStatus = LocalizationManager.Instance.GetFormat(
                        "WaveformVisualiser.PlaybackFailed",
                        e.Exception.Message);
                    UpdatePlayPauseLabel();
                    return;
                }

                if (_currentPlaylistFilePaths.Count == 0)
                {
                    UpdatePlayPauseLabel();
                    return;
                }

                var nextIndex = _currentPlaylistIndex + 1;
                if (nextIndex >= 0 && nextIndex < _currentPlaylistFilePaths.Count)
                {
                    _currentPlaylistIndex = nextIndex;
                    var nextPath = _currentPlaylistFilePaths[_currentPlaylistIndex];

                    SetSelectedFilePath(nextPath);
                    PlayPause();
                }
                else
                    UpdatePlayPauseLabel();
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                if (dispatcher.HasShutdownStarted ||
                    dispatcher.HasShutdownFinished)
                {
                    return;
                }

                try
                {
                    dispatcher.Invoke(HandlePlaybackStopped);
                }
                catch (TaskCanceledException)
                {
                }
                catch (InvalidOperationException)
                    when (dispatcher.HasShutdownStarted ||
                          dispatcher.HasShutdownFinished)
                {
                }

                return;
            }

            HandlePlaybackStopped();
        }

        private void StopPlaybackExplicitly()
        {
            _isExplicitStopRequested =
                _soundEngine.PlaybackState != PlaybackState.Stopped;
            _soundEngine.Stop();
        }

        private void ResetWaveformPlayheadAndProgress() => AudioWaveformOverlayClip = new Rect(0, 0, 0, WaveformPixelHeight);

        private void LoadWaveformImagesIntoCacheForCurrentWidth(IEnumerable<string> filePaths)
        {
            var filePathsToPreload = (filePaths ?? [])
                .Where(filePath => !string.Equals(
                    filePath,
                    _currentFilePathKey,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (filePathsToPreload.Length == 0)
                return;

            var targetWidth = GetTargetWidth();
            _ = _waveformVisualisationCacheService.PreloadWaveformVisualisationsAsync(
                filePathsToPreload,
                targetWidth,
                _waveformRendererService,
                _lifetimeCancellationTokenSource.Token);
        }

        private void ClearWaveformPreview()
        {
            AudioWaveformBaseImageSource = null;
            AudioWaveformOverlayImageSource = null;
            WaveformPixelWidth = 0;
            WaveformPixelHeight = 0;
            CurrentPlaybackTime = TimeSpan.Zero;
            TotalPlaybackTime = TimeSpan.Zero;
            AudioWaveformOverlayClip = new Rect(0, 0, 0, 0);
        }

        private void UpdateWaveformVisualiserLabel()
        {
            if (string.IsNullOrWhiteSpace(_currentFilePathKey))
                WaveformVisualiserLabel = LocalizationManager.Instance.Get("AudioEditor.Panel.WaveformVisualiser");
            else
            {
                var fileName = Path.GetFileName(_currentFilePathKey);
                WaveformVisualiserLabel =
                    $"{LocalizationManager.Instance.Get("AudioEditor.Panel.WaveformVisualiser")} - {WpfHelpers.DuplicateUnderscores(fileName)}";
            }
        }

        private void UpdateTotalPlaybackTimeFromFilePath(string filePath)
        {
            var cachedResult = string.IsNullOrWhiteSpace(filePath)
                ? null
                : _waveformVisualisationCacheService.GetWaveformVisualisation(
                    filePath,
                    GetTargetWidth());
            TotalPlaybackTime = cachedResult?.TotalTime ?? TimeSpan.Zero;
        }

        public void Dispose()
        {
            _lifetimeCancellationTokenSource.Cancel();
            StopWaveformPlayheadRendering();
            _eventHub.UnRegister(this);
            _soundEngine.PlaybackStopped -= OnPlaybackStopped;
            _soundEngine.Dispose();

            if (_waveformRenderCancellationTokenSource != null)
            {
                _waveformRenderCancellationTokenSource.Cancel();
                _waveformRenderCancellationTokenSource.Dispose();
            }

            if (_waveformResizeDebounceCancellationTokenSource != null)
            {
                _waveformResizeDebounceCancellationTokenSource.Cancel();
                _waveformResizeDebounceCancellationTokenSource.Dispose();
            }

            _lifetimeCancellationTokenSource.Dispose();
        }

        public void SetSelectedHostWidth(double width) => HostWidth = width;
    }
}
