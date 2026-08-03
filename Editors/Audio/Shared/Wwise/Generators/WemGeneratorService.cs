using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.AudioProject.Models;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Editors.Audio.Shared.Wwise.Generators
{
    public interface IWemGeneratorService
    {
        Task GenerateWemsAsync(
            List<AudioFile> audioFiles,
            string workspacePath,
            CancellationToken cancellationToken = default);
        List<AudioPackOutput> CreateWemOutputs(
            List<AudioFile> audioFiles);
    }

    public class WemGeneratorService(IPackFileService packFileService, WSourcesWrapper wSourcesWrapper) : IWemGeneratorService
    {
        private readonly IPackFileService _packFileService = packFileService;
        private readonly WSourcesWrapper _wSourcesWrapper = wSourcesWrapper;

        public async Task GenerateWemsAsync(
            List<AudioFile> audioFiles,
            string workspacePath,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var wavToWemFolderPath = Path.Combine(
                workspacePath,
                "WavToWem");
            var wprojPath = Path.Combine(
                wavToWemFolderPath,
                "WavToWemWwiseProject",
                "WavToWem.wproj");
            var wsourcesPath = Path.Combine(
                wavToWemFolderPath,
                "wav_to_wem.wsources");

            var wavFileNames = new List<string>(audioFiles.Count);
            var preparationCompleted = await Task.Run(() =>
            {
                foreach (var audioFile in audioFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return false;

                    var wavFile =
                        _packFileService.FindFile(
                            audioFile.WavPackFilePath) ??
                        throw new FileNotFoundException(
                            LocalizationManager.Instance.GetFormat(
                                "Msg.PackFileNotFound",
                                audioFile.WavPackFilePath),
                            audioFile.WavPackFilePath);
                    var wavFileName = $"{audioFile.Id}.wav";
                    var wavFilePath = Path.Combine(
                        workspacePath,
                        wavFileName);
                    ExportWav(wavFilePath, wavFile.DataSource);
                    wavFileNames.Add(wavFileName);
                }

                if (cancellationToken.IsCancellationRequested)
                    return false;

                _wSourcesWrapper.InitialiseWwiseProject(
                    wavToWemFolderPath);
                if (cancellationToken.IsCancellationRequested)
                    return false;

                _wSourcesWrapper.CreateWsourcesFile(
                    wavFileNames,
                    workspacePath,
                    wsourcesPath);
                return true;
            });
            if (!preparationCompleted)
                return;

            var arguments = $"\"{wprojPath}\" -ConvertExternalSources \"{wsourcesPath}\" -ExternalSourcesOutput \"{workspacePath}\"";
            try
            {
                await _wSourcesWrapper.RunExternalCommandAsync(
                    arguments,
                    cancellationToken);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
                return;

            await Task.Run(
                () => _wSourcesWrapper.DeleteExcessStuff(workspacePath));
        }

        public List<AudioPackOutput> CreateWemOutputs(
            List<AudioFile> audioFiles)
        {
            var outputs = new List<AudioPackOutput>();
            foreach (var audioFile in audioFiles)
            {
                outputs.Add(
                    new AudioPackOutput(
                        audioFile.WemPackFileName,
                        audioFile.WemPackFilePath,
                        File.ReadAllBytes(audioFile.WemDiskFilePath)));
            }

            return outputs;
        }

        private static void ExportWav(
            string filePath,
            IDataSource dataSource)
        {
            DirectoryHelper.EnsureFileFolderCreated(filePath);
            using var destination = new FileStream(
                filePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                128 * 1024,
                FileOptions.SequentialScan);
            dataSource.CopyTo(destination);
        }
    }
}
