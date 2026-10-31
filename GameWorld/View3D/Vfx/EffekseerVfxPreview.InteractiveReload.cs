using Microsoft.Xna.Framework;

namespace GameWorld.Core.Vfx;

public sealed partial class EffekseerVfxPreview
{
    private VfxInteractiveConversion? _reloadConversion;
    private VfxPreviewSnapshot? _reloadSnapshot;
    private VfxPreviewSnapshot? _queuedReloadSnapshot;
    private VfxPreparedConversion? _preparedReload;
    private Task<VfxPreparedResources>? _reloadResourcesPreparation;
    private CancellationTokenSource? _reloadResourcesCancellation;
    private EffekseerVfxPreview? _reloadUploadPreview;
    private IEnumerator<int>? _reloadUpload;
    private IEnumerator<int>? _scheduledReloadUpload;
    private Action<Action>? _scheduleReloadOwnerWork;
    private Exception? _reloadUploadFailure;
    private long _reloadUploadOwnerTimestamp;
    private bool _reloadUploadReady;
    private VfxPreviewEmitter[]? _reloadUploadEmitters;
    private TotalWarVfxSimulation? _reloadSimulation;
    private VfxInteractiveReloadFrame? _reloadFrame;
    private VfxInteractiveBounds? _reloadBounds;
    private VfxInteractivePrewarm? _reloadPrewarm;
    private VfxConstantReloadPreparation? _reloadConstantFrame;
    private bool _reloadConstantFallback;
    private VfxInteractivePrewarmKey? _reloadPrewarmKey;
    private BoundingBox? _preparedReloadBounds;
    private bool _reloadBoundsReady, _reloadUploaded, _applyingInteractiveReload;
    private bool _reloadRetainsPlayback;
    private string[] _reloadResourcePaths = [];

    public bool HasPendingInteractiveReload => _reloadSnapshot != null;

    public bool BeginInteractiveReload(VfxPreviewSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_reloadSnapshot is { GameBytes.Length: > 0 } active && SameReloadScope(active, snapshot))
        {
            // Finish one useful version while retaining only the newest following edit.
            _queuedReloadSnapshot = CaptureQueuedSnapshot(snapshot);
            return false;
        }
        CancelInteractiveReload();
        if (_simulation != null) CancelPreparation();
        _reloadSnapshot = snapshot;
        var dispatcher = System.Windows.Threading.Dispatcher.FromThread(Thread.CurrentThread);
        _scheduleReloadOwnerWork = dispatcher == null ? null : action =>
        {
            if (dispatcher.HasShutdownStarted) throw new InvalidOperationException("VFX resource owner is shutting down.");
            dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, action);
        };
        _reloadConversion = new(snapshot, _interactivePrewarmEnabled,
            string.Equals(snapshot.SourcePath, _sourcePath, StringComparison.OrdinalIgnoreCase) ? _reloadResourcePaths : null,
            _resources?.ResourceIdentity, _scheduleReloadOwnerWork);
        return false;
    }

    private static VfxPreviewSnapshot CaptureQueuedSnapshot(VfxPreviewSnapshot snapshot) => snapshot with
    {
        GameBytes = snapshot.GameBytes.ToArray(),
        RootSelection = snapshot.RootSelection is { } selected
            ? new(selected.EmitterIds.ToArray(), selected.ReferenceNames.ToArray()) : null,
    };

    private static bool SameReloadScope(VfxPreviewSnapshot left, VfxPreviewSnapshot right)
        => string.Equals(left.SourcePath, right.SourcePath, StringComparison.OrdinalIgnoreCase) && left.Quality == right.Quality
            && (left.RootSelection == null || right.RootSelection == null ? left.RootSelection == right.RootSelection
                : left.RootSelection.EmitterIds.SequenceEqual(right.RootSelection.EmitterIds, StringComparer.Ordinal)
                    && left.RootSelection.ReferenceNames.SequenceEqual(right.RootSelection.ReferenceNames, StringComparer.Ordinal));

    public bool BeginInteractiveRestart()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_conversion == null) return true;
        CancelInteractiveReload();
        _reloadSnapshot = new([], _sourcePath, _ => null);
        _preparedReload = new(_conversion, _resources?.ResourceIdentity, _reloadResourcePaths);
        _reloadUploaded = true;
        _reloadRetainsPlayback = _simulation?.IsInitialized == true;
        _reloadBoundsReady = true;
        _preparedReloadBounds = PreviewBounds;
        if (VfxInteractivePrewarm.CanPrepare(_conversion))
            _reloadPrewarm = VfxInteractivePrewarm.AfterPlayback(_conversion, PreviewBounds, false);
        return false;
    }

    public bool ContinueInteractiveReload(VfxInteractiveReloadFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _reloadUploadOwnerTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_reloadSnapshot == null) return true;
        if (_preparedReload == null)
        {
            if (!_reloadConversion!.TryTake(out _preparedReload)) return false;
            _reloadResourcePaths = _preparedReload!.ResourcePaths;
            _reloadConversion.Dispose();
            _reloadConversion = null;
            if (!CanPrepareConstantReload(frame) && VfxInteractivePrewarm.CanPrepare(_preparedReload.Conversion))
                _reloadPrewarm = VfxInteractivePrewarm.ForReload(_preparedReload.Conversion,
                    frame.InitializeDuration, _interactivePrewarmEnabled && _interactiveSeekHistoryConstant);
        }
        var conversion = _preparedReload!.Conversion;
        if (_reloadConstantFrame is { } constant)
        {
            if (!CanPrepareConstantReload(frame) || !constant.Context.Equals(VfxConstantReloadPreparation.Capture(frame)))
            {
                constant.Dispose();
                _reloadConstantFrame = null;
                _reloadSimulation = null;
                _reloadFrame = null;
                _reloadConstantFallback = true;
            }
            else
            {
                if (!constant.TryTake(out var prepared))
                {
                    if (constant.Failure is { } failure) throw new InvalidOperationException("VFX captured-frame preparation failed.", failure);
                    return false;
                }
                _reloadSimulation = prepared!.Simulation;
                constant.Dispose();
                _reloadConstantFrame = null;
                var target = Math.Max(0, frame.Time);
                if (frame.InitializeDuration) target = Math.Min(target, _reloadSimulation.CycleDuration);
                if (target < prepared.Time || target - prepared.Time > 1f / 40)
                {
                    // Transfer the private complete state back to the worker for exact forward or reverse catch-up.
                    _reloadConstantFrame = new(conversion, VfxConstantReloadPreparation.Capture(frame), frame.Time,
                        _interactivePrewarmEnabled && _interactiveSeekHistoryConstant, _reloadSimulation);
                    _reloadSimulation = null;
                    _reloadFrame = null;
                    return false;
                }
                _reloadFrame = frame with { Time = prepared.Time };
            }
        }
        if (CanPrepareConstantReload(frame) && _reloadSimulation == null)
        {
            _reloadPrewarm?.Dispose();
            _reloadPrewarm = null;
            _reloadConstantFrame = new(conversion, VfxConstantReloadPreparation.Capture(frame), frame.Time,
                _interactivePrewarmEnabled && _interactiveSeekHistoryConstant, null);
            return false;
        }
        if (_reloadPrewarm is { } prewarm)
        {
            if (_reloadPrewarmKey is { } previousKey && previousKey.EmissionEnd != frame.EmissionEnd)
            {
                prewarm.Dispose();
                prewarm = _reloadPrewarm = VfxInteractivePrewarm.ForReload(conversion,
                    frame.InitializeDuration, _interactivePrewarmEnabled && _interactiveSeekHistoryConstant);
                _reloadPrewarmKey = null;
            }
            _reloadPrewarmKey ??= new(frame.History?.Invoke(0) ?? frame.Attachment, frame.Attachment,
                frame.Attachment.Translation.Y, frame.EmissionEnd);
            if (!prewarm.TryTake(_reloadPrewarmKey.Value, out var prepared))
            {
                if (prewarm.Failure is { } failure) throw new InvalidOperationException("VFX reload prewarm failed.", failure);
                return false;
            }
            _reloadSimulation = prepared!.Simulation;
            prewarm.Dispose();
            _reloadPrewarm = null;
            _reloadFrame = null;
        }
        if (_reloadSimulation == null || _reloadSimulation.EmissionEndTimeSeconds != frame.EmissionEnd
            || _reloadSimulation.NeedsGroundReplay(frame.GroundHeight))
        {
            _reloadSimulation = CreateSimulation(conversion);
            _reloadSimulation.EmissionEndTimeSeconds = frame.EmissionEnd;
            _reloadSimulation.PrepareInitialGround(frame.GroundHeight, frame.Attachment, frame.History,
                frame.UseCurrentAttachmentForFrame, frame.ContinuousTrail, frame.NextAttachmentSample);
            // Independent editors read duration before their first seek. META starts directly at its real attachment.
            if (frame.InitializeDuration) _ = _reloadSimulation.CycleDuration;
            _reloadFrame = null;
        }
        var time = Math.Max(0, frame.Time);
        if (frame.InitializeDuration && !frame.ContinuousTrail) time = Math.Min(time, _reloadSimulation.CycleDuration);
        _reloadFrame ??= frame with { Time = time };
        var request = _reloadFrame;
        if (!_reloadSimulation.PrepareInteractiveSeek(request.Time, request.Attachment, request.History,
            request.UseCurrentAttachmentForFrame, request.ContinuousTrail, request.NextAttachmentSample)) return false;
        // Catch up in bounded operations. The final ordinary seek covers at most three source ticks.
        if (time < request.Time || time - request.Time > 1f / 40)
        {
            _reloadFrame = frame with { Time = time };
            return false;
        }
        if (!_reloadUploaded)
        {
            if (_reloadResourcesPreparation is { } resources)
            {
                if (!resources.IsCompleted) return false;
                try { _preparedReload = _preparedReload with { Resources = resources.GetAwaiter().GetResult() }; }
                finally
                {
                    _reloadResourcesPreparation = null;
                    _reloadResourcesCancellation!.Dispose();
                    _reloadResourcesCancellation = null;
                }
            }
            _reloadUploadEmitters ??= conversion.Emitters.ToArray();
            _applyingInteractiveReload = true;
            try
            {
                if (_reloadUpload == null && _preparedReload.ResourceIdentity is { } identity
                    && _resources?.ResourceIdentity == identity && _resources.HasExclusiveHandle)
                    ReloadConverted(_reloadSnapshot, conversion, identity, _preparedReload.Resources, allowSynchronousPreparation: false);
                else
                {
                    if (_reloadUpload == null)
                    {
                        _reloadUploadPreview = new(device)
                        {
                            _applyingInteractiveReload = true, _identification = _identification,
                            _emissionEndTimeSeconds = _emissionEndTimeSeconds,
                        };
                        _reloadUpload = _reloadUploadPreview.BeginConvertedUpload(_reloadSnapshot, conversion,
                            _preparedReload.ResourceIdentity, _preparedReload.Resources, allowSynchronousPreparation: false);
                    }
                    if (!PumpInteractiveUpload(_reloadUpload, ownerFrame: true)) return false;
                    var candidate = _reloadUploadPreview!;
                    if ((_reloadUploadFailure ?? candidate._convertedUploadFailure) is { } uploadFailure)
                    {
                        Diagnostics = candidate.Diagnostics;
                        if (!Diagnostics.Any(diagnostic => diagnostic.ReasonKey == "Vfx.Preview.RuntimeUnavailable"))
                            Diagnostics = [.. Diagnostics, new(_reloadSnapshot.SourcePath, "", "", "Vfx.Preview.RuntimeUnavailable")];
                        CancelConvertedUpload();
                        throw new InvalidOperationException("VFX candidate upload failed.", uploadFailure);
                    }
                    AdoptConvertedUpload();
                }
            }
            catch (VfxResourcesPreparationRequiredException)
            {
                CancelConvertedUpload();
                if (_preparedReload.Resources != null) throw;
                _reloadResourcesCancellation = new();
                _reloadResourcesPreparation = VfxInteractiveConversion.PrepareResourcesAsync(conversion,
                    _reloadResourcesCancellation.Token);
                return false;
            }
            finally { _applyingInteractiveReload = false; }
            _reloadUploaded = true;
            // Failed GPU resources may remove emitters. Replay only the actual surviving projection.
            var emittersChanged = !_reloadUploadEmitters.SequenceEqual(conversion.Emitters);
            _reloadUploadEmitters = null;
            if (emittersChanged)
            {
                _reloadSimulation = null;
                _reloadBoundsReady = false;
                _reloadBounds?.Dispose();
                _reloadBounds = null;
                return false;
            }
        }
        _simulation = _reloadSimulation;
        _emissionEndTimeSeconds = frame.EmissionEnd;
        // A restart keeps this conversion's current bounds, including a result that arrived while replaying.
        _preparedReloadBounds = _reloadBoundsReady ? PreviewBounds : null;
        PreviewBounds = _preparedReloadBounds;
        _resources?.PublishBounds(_preparedReloadBounds);
        if (!_reloadBoundsReady)
        {
            // Upload can remove unsupported emitters; only estimate the final immutable projection.
            _interactiveBounds?.Dispose();
            _interactiveBounds = _reloadBounds ?? new(conversion);
            _reloadBounds = null;
        }
        if (_interactivePrewarmEnabled && VfxInteractivePrewarm.CanPrepare(conversion))
            _interactivePrewarm = VfxInteractivePrewarm.AfterPlayback(conversion, _preparedReloadBounds, _interactiveSeekHistoryConstant);
        _reloadSimulation = null;
        _reloadFrame = null;
        _reloadSnapshot = null;
        _preparedReload = null;
        _reloadBoundsReady = false;
        _reloadUploaded = false;
        _reloadRetainsPlayback = false;
        if (_queuedReloadSnapshot is { } queued)
        {
            _queuedReloadSnapshot = null;
            BeginInteractiveReload(queued);
            return false;
        }
        return true;
    }

    private bool CanPrepareConstantReload(VfxInteractiveReloadFrame frame)
        => !_reloadConstantFallback && _interactiveSeekHistoryConstant && !frame.ContinuousTrail
            && _reloadSnapshot is { GameBytes.Length: > 0 } && float.IsFinite(frame.Time) && frame.Time <= 600;

    // An idle owner turn may prepare only the private GPU candidate. Publication stays in Continue's frame context.
    private bool PumpInteractiveUpload(IEnumerator<int> upload, bool ownerFrame = false)
    {
        if (_reloadUploadReady) return true;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            while (_reloadUploadPreview!.ContinueConvertedUpload(upload, _reloadSnapshot!.SourcePath))
            {
                if (System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds < 2) continue;
                if (ownerFrame) _reloadUploadOwnerTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                ScheduleInteractiveUpload(upload);
                return false;
            }
        }
        catch (Exception exception) { _reloadUploadFailure = exception; }
        _reloadUploadReady = true;
        return true;
    }

    private void ScheduleInteractiveUpload(IEnumerator<int> upload)
    {
        // Shared META nodes keep their viewport's existing aggregate preparation budget.
        if (!_interactivePrewarmEnabled || _scheduleReloadOwnerWork == null || _reloadUploadReady
            || ReferenceEquals(_scheduledReloadUpload, upload)
            || System.Diagnostics.Stopwatch.GetElapsedTime(_reloadUploadOwnerTimestamp).TotalMilliseconds > 20) return;
        _scheduledReloadUpload = upload;
        try
        {
            _scheduleReloadOwnerWork(() =>
            {
                if (_disposed || !ReferenceEquals(_reloadUpload, upload)
                    || !ReferenceEquals(_scheduledReloadUpload, upload)) return;
                _scheduledReloadUpload = null;
                if (System.Diagnostics.Stopwatch.GetElapsedTime(_reloadUploadOwnerTimestamp).TotalMilliseconds <= 20)
                    PumpInteractiveUpload(upload);
            });
        }
        catch
        {
            if (ReferenceEquals(_scheduledReloadUpload, upload)) _scheduledReloadUpload = null;
            _scheduleReloadOwnerWork = null;
        }
    }

    private void AdoptConvertedUpload()
    {
        var candidate = _reloadUploadPreview!;
        _reloadUpload!.Dispose();
        _reloadUpload = null;
        _scheduledReloadUpload = null;
        _reloadUploadReady = false;
        _reloadUploadFailure = null;
        ReleaseResources();
        _resources = candidate._resources;
        _handle = candidate._handle;
        _conversion = candidate._conversion;
        _simulation = candidate._simulation;
        _renderLayers = candidate._renderLayers;
        _opaqueLayers.Clear();
        _opaqueLayers.UnionWith(candidate._opaqueLayers);
        _sourcePath = candidate._sourcePath;
        _spriteSpinRangeDiagnostic = null;
        PreviewBounds = candidate.PreviewBounds;
        Diagnostics = candidate.Diagnostics;
        UpdateIdentificationMask();
        candidate._resources = null;
        candidate._handle = null;
        candidate.Dispose();
        _reloadUploadPreview = null;
    }

    private void CancelConvertedUpload()
    {
        _scheduledReloadUpload = null;
        _reloadUpload?.Dispose();
        _reloadUpload = null;
        _reloadUploadReady = false;
        _reloadUploadFailure = null;
        _reloadUploadPreview?.Dispose();
        _reloadUploadPreview = null;
    }

    private void CancelInteractiveReload()
    {
        CancelNormalFrameSeek();
        CancelConvertedUpload();
        _reloadUploadEmitters = null;
        _reloadConversion?.Dispose();
        _reloadConversion = null;
        if (_reloadResourcesPreparation is { } resources)
        {
            var cancellation = _reloadResourcesCancellation!;
            cancellation.Cancel();
            _ = resources.ContinueWith(completed => { _ = completed.Exception; cancellation.Dispose(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _reloadResourcesPreparation = null;
            _reloadResourcesCancellation = null;
        }
        _reloadBounds?.Dispose();
        _reloadBounds = null;
        _reloadPrewarm?.Dispose();
        _reloadPrewarm = null;
        _reloadConstantFrame?.Dispose();
        _reloadConstantFrame = null;
        _reloadConstantFallback = false;
        _reloadPrewarmKey = null;
        _reloadSimulation = null;
        _reloadFrame = null;
        _reloadSnapshot = null;
        _queuedReloadSnapshot = null;
        _preparedReload = null;
        _reloadBoundsReady = false;
        _reloadUploaded = false;
        _reloadRetainsPlayback = false;
        _preparedReloadBounds = null;
        _scheduleReloadOwnerWork = null;
    }
}
