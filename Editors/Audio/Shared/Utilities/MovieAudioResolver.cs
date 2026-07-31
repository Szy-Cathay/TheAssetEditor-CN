using System;
using System.IO;
using System.Linq;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.HircExploration;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Utilities
{
    public interface IMovieAudioResolver
    {
        PackFile? ResolveMovieWem(string caVp8PackFilePath);
    }

    public class MovieAudioResolver(
        IAudioRepository audioRepository,
        IPackFileService packFileService) : IMovieAudioResolver
    {
        private readonly IAudioRepository _audioRepository =
            audioRepository;
        private readonly IPackFileService _packFileService =
            packFileService;

        public PackFile? ResolveMovieWem(string caVp8PackFilePath)
        {
            var actionEventName =
                Wh3ActionEventInformation.GetMovieActionEventName(
                    caVp8PackFilePath);
            var actionEventId = WwiseHash.Compute(actionEventName);
            var actionEventHircs =
                _audioRepository.GetHircs(actionEventId);
            if (actionEventHircs.Count == 0)
                return null;

            var parser =
                new HircTreeChildrenParser(_audioRepository);
            var nodes = parser.BuildHierarchyAsFlatList(
                actionEventHircs[0]);
            var soundHirc = nodes
                .Select(node => node.Hirc)
                .FirstOrDefault(hirc => hirc is ICAkSound);
            if (soundHirc is not ICAkSound sound)
            {
                throw new InvalidDataException(
                    $"Cannot find a sound for movie Action Event " +
                    $"'{actionEventName}'.");
            }

            var sourceId = sound.GetSourceId();
            var wem = FindWem(sourceId, soundHirc.LanguageId);
            if (wem == null)
            {
                throw new FileNotFoundException(
                    $"Cannot find movie audio file '{sourceId}.wem'.");
            }

            return wem;
        }

        private PackFile FindWem(uint sourceId, uint languageId)
        {
            var wemName = $"{sourceId}.wem";
            var language = _audioRepository.GetNameFromId(
                languageId,
                out var languageFound);
            if (languageFound &&
                !string.Equals(
                    language,
                    Wh3LanguageInformation.GetLanguageAsString(
                        Wh3Language.Sfx),
                    StringComparison.OrdinalIgnoreCase))
            {
                var localizedWem = _packFileService.FindFile(
                    $"audio\\wwise\\{language}\\{wemName}");
                if (localizedWem != null)
                    return localizedWem;
            }

            var wem = _packFileService.FindFile(
                $"audio\\wwise\\{wemName}");
            wem ??= _packFileService.FindFile($"audio\\{wemName}");
            if (wem != null)
                return wem;

            foreach (var candidateLanguage in
                Wh3LanguageInformation.GetAllLanguages())
            {
                wem = _packFileService.FindFile(
                    $"audio\\wwise\\{candidateLanguage}\\{wemName}");
                if (wem != null)
                    return wem;
            }

            return null;
        }
    }
}
