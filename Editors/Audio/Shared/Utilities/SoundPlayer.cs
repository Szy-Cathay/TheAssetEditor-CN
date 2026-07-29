using System;
using System.IO;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise;
using NAudio.Wave;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Editors.Audio.Shared.Utilities
{
    public enum AudioExportFormat
    {
        Wav,
        Wem
    }

    public sealed record AudioSource(
        uint SourceId,
        uint LanguageId,
        uint? DataSoundbankId = null,
        int FileOffset = 0,
        int ByteCount = 0);

    public sealed record AudioPlaybackData(
        AudioSource Source,
        byte[] WavData);

    public interface ISoundPlayer
    {
        TimeSpan CurrentPlaybackTime { get; }
        PlaybackState PlaybackState { get; }
        event EventHandler<StoppedEventArgs> PlaybackStopped;
        AudioPlaybackData Prepare(AudioSource source);
        bool Play(AudioSource source);
        bool Play(AudioPlaybackData playbackData);
        bool Seek(AudioPlaybackData playbackData, TimeSpan playbackTime);
        void Stop();
        bool Export(
            AudioSource source,
            AudioExportFormat format,
            string outputFilePath);
    }

    public class SoundPlayer(
        IPackFileService packFileService,
        IAudioRepository audioRepository,
        VgStreamWrapper vgStreamWrapper,
        ISoundEngine soundEngine) : ISoundPlayer, IDisposable
    {
        private readonly IPackFileService _packFileService = packFileService;
        private readonly IAudioRepository _audioRepository = audioRepository;
        private readonly VgStreamWrapper _vgStreamWrapper = vgStreamWrapper;
        private readonly ISoundEngine _soundEngine = soundEngine;

        private readonly ILogger _logger = Logging.Create<SoundPlayer>();
        private readonly object _prepareLock = new();
        private AudioPlaybackData _preparedPlaybackData;
        private AudioSource _currentSource;

        private static string AudioFolderName => $"{DirectoryHelper.Temp}\\Audio";

        public TimeSpan CurrentPlaybackTime => _soundEngine.CurrentPlaybackTime;
        public PlaybackState PlaybackState => _soundEngine.PlaybackState;
        public event EventHandler<StoppedEventArgs> PlaybackStopped
        {
            add => _soundEngine.PlaybackStopped += value;
            remove => _soundEngine.PlaybackStopped -= value;
        }

        public AudioPlaybackData Prepare(AudioSource source)
        {
            ArgumentNullException.ThrowIfNull(source);

            lock (_prepareLock)
            {
                if (_preparedPlaybackData?.Source == source)
                    return _preparedPlaybackData;

                try
                {
                    if (!TryReadWemBytes(source, out var wemBytes))
                        return null;

                    var result = ConvertWemToWav(
                        source.SourceId.ToString(),
                        wemBytes);
                    if (!result.IsSuccess)
                        return null;

                    _preparedPlaybackData = new AudioPlaybackData(
                        source,
                        File.ReadAllBytes(result.Item));
                    return _preparedPlaybackData;
                }
                catch (Exception e)
                {
                    _logger.Here().Error(e.Message);
                    if (_preparedPlaybackData?.Source == source)
                        _preparedPlaybackData = null;
                    return null;
                }
            }
        }

        public bool Play(AudioSource source)
        {
            ArgumentNullException.ThrowIfNull(source);

            var playbackData = Prepare(source);
            return playbackData != null && Play(playbackData);
        }

        public bool Play(AudioPlaybackData playbackData)
        {
            ArgumentNullException.ThrowIfNull(playbackData);

            try
            {
                if (playbackData.Source != _currentSource)
                {
                    _soundEngine.LoadFromWavData(playbackData.WavData);
                    _currentSource = playbackData.Source;
                }

                _soundEngine.PlayPause();
                _logger.Here().Information(
                    $"Playing '{playbackData.Source.SourceId}' in the internal audio engine.");
                return true;
            }
            catch (Exception e)
            {
                _logger.Here().Error(e.Message);
                _currentSource = null;
                lock (_prepareLock)
                {
                    if (_preparedPlaybackData?.Source == playbackData.Source)
                        _preparedPlaybackData = null;
                }
            }

            _logger.Here().Error("Unable to play wav file.");
            return false;
        }

        public bool Seek(
            AudioPlaybackData playbackData,
            TimeSpan playbackTime)
        {
            ArgumentNullException.ThrowIfNull(playbackData);

            try
            {
                if (playbackData.Source != _currentSource)
                {
                    _soundEngine.LoadFromWavData(playbackData.WavData);
                    _currentSource = playbackData.Source;
                }

                _soundEngine.SetPlaybackTime(playbackTime);
                return true;
            }
            catch (Exception e)
            {
                _logger.Here().Error(e.Message);
                _currentSource = null;
                return false;
            }
        }

        public void Stop()
        {
            _soundEngine.Stop();
        }

        public bool Export(
            AudioSource source,
            AudioExportFormat format,
            string outputFilePath)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (string.IsNullOrWhiteSpace(outputFilePath))
                return false;

            lock (_prepareLock)
            {
                try
                {
                    var outputDirectory = Path.GetDirectoryName(outputFilePath);
                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                        Directory.CreateDirectory(outputDirectory);

                    if (format == AudioExportFormat.Wav &&
                        _preparedPlaybackData?.Source == source)
                    {
                        File.WriteAllBytes(
                            outputFilePath,
                            _preparedPlaybackData.WavData);
                        return true;
                    }

                    if (!TryReadWemBytes(source, out var wemBytes))
                        return false;

                    if (format == AudioExportFormat.Wem)
                    {
                        File.WriteAllBytes(outputFilePath, wemBytes);
                        return true;
                    }

                    var result = ConvertWemToWav(
                        source.SourceId.ToString(),
                        wemBytes);
                    if (!result.IsSuccess)
                        return false;

                    var sourcePath = Path.GetFullPath(result.Item);
                    var destinationPath = Path.GetFullPath(outputFilePath);
                    if (!string.Equals(
                        sourcePath,
                        destinationPath,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(sourcePath, destinationPath, true);
                    }

                    return File.Exists(destinationPath) &&
                        new FileInfo(destinationPath).Length > 0;
                }
                catch (Exception e)
                {
                    _logger.Here().Error(e.Message);
                    return false;
                }
            }
        }

        private Result<string> ConvertWemToWav(string sourceId, byte[] wemBytes)
        {
            _logger.Here().Information(
                $"Trying to export '{sourceId}.wem' - {wemBytes.Length} bytes");

            var wemFileName = $"{sourceId}.wem";
            var wavFileName = $"{sourceId}.wav";
            var wemFilePath = $"{AudioFolderName}\\{wemFileName}";
            var wavFilePath = $"{AudioFolderName}\\{wavFileName}";

            if (!ExportFileToAEFolder(wemFileName, wemBytes))
            {
                return Result<string>.FromError(
                    "Export error",
                    $"Unable to write {wemFilePath}");
            }

            return _vgStreamWrapper.ConvertFileUsingVgStream(
                wemFilePath,
                wavFilePath);
        }

        private PackFile FindWemFile(uint sourceId, uint languageId)
        {
            var wemId = sourceId.ToString();
            var language = _audioRepository.GetNameFromId(
                languageId,
                out var languageFound);
            PackFile wemFile = null;

            if (languageFound &&
                !string.Equals(
                    language,
                    Wh3LanguageInformation.GetLanguageAsString(Wh3Language.Sfx),
                    StringComparison.OrdinalIgnoreCase))
            {
                wemFile = _packFileService.FindFile(
                    $"audio\\wwise\\{language}\\{wemId}.wem");
            }

            wemFile ??= _packFileService.FindFile(
                $"audio\\wwise\\{wemId}.wem");
            wemFile ??= _packFileService.FindFile($"audio\\{wemId}.wem");
            return wemFile;
        }

        private bool TryReadWemBytes(AudioSource source, out byte[] wemBytes)
        {
            wemBytes = null;

            if (source.DataSoundbankId is uint dataSoundbankId)
            {
                var dataSoundbankName = _audioRepository.GetNameFromId(
                    dataSoundbankId,
                    out var found);
                if (!found)
                {
                    _logger.Here().Warning(
                        $"Unable to find a name from hash '{dataSoundbankId}'.");
                    return false;
                }

                var dataSoundbankFileName = $"{dataSoundbankName}.bnk";
                if (!_audioRepository.PackFileByBnkName.TryGetValue(
                    dataSoundbankFileName,
                    out var packFile))
                {
                    _logger.Here().Warning(
                        $"Unable to find packfile with name '{dataSoundbankFileName}'.");
                    return false;
                }

                if (source.FileOffset < 0 || source.ByteCount <= 0)
                    return false;

                var byteChunk = packFile.DataSource.ReadDataAsChunk();
                if (source.FileOffset > byteChunk.Buffer.Length - source.ByteCount)
                {
                    _logger.Here().Warning(
                        $"Embedded audio '{source.SourceId}' exceeds the data soundbank bounds.");
                    return false;
                }

                byteChunk.Advance(source.FileOffset);
                wemBytes = byteChunk.ReadBytes(source.ByteCount);
                return true;
            }

            var wemFile = FindWemFile(source.SourceId, source.LanguageId);
            if (wemFile == null)
            {
                _logger.Here().Error(
                    $"Unable to find wem file '{source.SourceId}'.");
                return false;
            }

            wemBytes = wemFile.DataSource.ReadData();
            return true;
        }

        public bool ExportFileToAEFolder(string fileName, byte[] bytes)
        {
            try
            {
                var wemFilePath = $"{AudioFolderName}\\{fileName}";
                DirectoryHelper.EnsureFileFolderCreated(wemFilePath);
                File.WriteAllBytes(wemFilePath, bytes);
                return true;
            }
            catch (Exception e)
            {
                _logger.Here().Error(e.Message);
                return false;
            }
        }

        public void Dispose() => _soundEngine.Dispose();
    }
}
