using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Editors.Audio.AudioEditor.Presentation.WaveformVisualiser
{
    public interface IWaveformVisualisationCacheService
    {
        WaveformRenderResult GetWaveformVisualisation(string filePath, int targetWidth);
        void Store(string filePath, WaveformRenderResult waveformRenderResult);
        void Remove(string filePath);
        void Clear();
        Task PreloadWaveformVisualisationsAsync(IEnumerable<string> filePaths, int targetWidth, IWaveformRendererService renderService, CancellationToken cancellationToken);
    }

    public sealed class WaveformVisualisationCacheService : IWaveformVisualisationCacheService
    {
        private readonly record struct WaveformCacheKey(
            string FilePath,
            int TargetWidth);

        private sealed class WaveformCacheKeyComparer : IEqualityComparer<WaveformCacheKey>
        {
            public bool Equals(WaveformCacheKey x, WaveformCacheKey y) =>
                x.TargetWidth == y.TargetWidth &&
                StringComparer.OrdinalIgnoreCase.Equals(x.FilePath, y.FilePath);

            public int GetHashCode(WaveformCacheKey obj) =>
                HashCode.Combine(
                    StringComparer.OrdinalIgnoreCase.GetHashCode(obj.FilePath),
                    obj.TargetWidth);
        }

        private readonly record struct PreloadRequest(
            WaveformCacheKey Key,
            long Token,
            long CacheGeneration,
            long PathVersion);

        private readonly ConcurrentDictionary<WaveformCacheKey, WaveformRenderResult>
            _visualisationByKey = new(new WaveformCacheKeyComparer());
        private readonly ConcurrentDictionary<WaveformCacheKey, long>
            _preloadInProgressByKey = new(new WaveformCacheKeyComparer());
        private readonly ConcurrentDictionary<string, long> _pathVersions =
            new(StringComparer.OrdinalIgnoreCase);
        private long _cacheGeneration;
        private long _nextPreloadToken;

        public WaveformRenderResult GetWaveformVisualisation(string filePath, int targetWidth)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return null;

            _visualisationByKey.TryGetValue(
                new WaveformCacheKey(filePath, targetWidth),
                out var cached);
            return cached;
        }

        public void Store(string filePath, WaveformRenderResult waveformRenderResult)
        {
            if (string.IsNullOrWhiteSpace(filePath) || waveformRenderResult == null)
                return;

            Store(
                new WaveformCacheKey(
                    filePath,
                    waveformRenderResult.PixelWidth),
                waveformRenderResult);
        }

        public void Remove(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            _pathVersions.AddOrUpdate(
                filePath,
                1,
                (_, version) => unchecked(version + 1));

            foreach (var key in _visualisationByKey.Keys.Where(
                key => StringComparer.OrdinalIgnoreCase.Equals(
                    key.FilePath,
                    filePath)))
            {
                _visualisationByKey.TryRemove(key, out _);
            }

            foreach (var key in _preloadInProgressByKey.Keys.Where(
                key => StringComparer.OrdinalIgnoreCase.Equals(
                    key.FilePath,
                    filePath)))
            {
                _preloadInProgressByKey.TryRemove(key, out _);
            }
        }

        public void Clear()
        {
            Interlocked.Increment(ref _cacheGeneration);
            _visualisationByKey.Clear();
            _preloadInProgressByKey.Clear();
            _pathVersions.Clear();
        }

        public async Task PreloadWaveformVisualisationsAsync(IEnumerable<string> filePaths, int targetWidth, IWaveformRendererService renderService, CancellationToken cancellationToken)
        {
            var requests = new List<PreloadRequest>();
            var uniqueFilePaths = (filePaths ?? [])
                .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var filePath in uniqueFilePaths)
            {
                var key = new WaveformCacheKey(filePath, targetWidth);
                if (_visualisationByKey.ContainsKey(key))
                    continue;

                var token = Interlocked.Increment(ref _nextPreloadToken);
                if (!_preloadInProgressByKey.TryAdd(key, token))
                    continue;

                requests.Add(new PreloadRequest(
                    key,
                    token,
                    Volatile.Read(ref _cacheGeneration),
                    _pathVersions.GetOrAdd(filePath, 0)));
            }

            if (requests.Count == 0)
                return;

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Clamp(
                    Environment.ProcessorCount / 2,
                    1,
                    4),
                CancellationToken = cancellationToken
            };

            try
            {
                await Parallel.ForEachAsync(requests, options, async (request, cancellationToken) =>
                {
                    try
                    {
                        var waveformRenderResult = await renderService.RenderAsync(
                            request.Key.FilePath,
                            request.Key.TargetWidth,
                            cancellationToken).ConfigureAwait(false);

                        if (request.CacheGeneration != Volatile.Read(ref _cacheGeneration))
                            return;

                        if (request.PathVersion != _pathVersions.GetOrAdd(
                            request.Key.FilePath,
                            0))
                        {
                            return;
                        }

                        Store(request.Key, waveformRenderResult);
                    }
                    catch (OperationCanceledException)
                        when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // A bad audio file must not stop other previews loading.
                    }
                    finally
                    {
                        ((ICollection<KeyValuePair<WaveformCacheKey, long>>)
                            _preloadInProgressByKey).Remove(
                                new KeyValuePair<WaveformCacheKey, long>(
                                    request.Key,
                                    request.Token));
                    }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        private void Store(
            WaveformCacheKey key,
            WaveformRenderResult waveformRenderResult)
        {
            foreach (var existingKey in _visualisationByKey.Keys.Where(
                existingKey =>
                    existingKey.TargetWidth != key.TargetWidth &&
                    StringComparer.OrdinalIgnoreCase.Equals(
                        existingKey.FilePath,
                        key.FilePath)))
            {
                _visualisationByKey.TryRemove(existingKey, out _);
            }

            _visualisationByKey[key] = waveformRenderResult;
        }
    }
}
