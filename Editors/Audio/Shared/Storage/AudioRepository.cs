using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Wwise;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.Wwise.Didx;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Storage
{
    public sealed record AudioRepositorySnapshot(
        Dictionary<uint, List<HircItem>> HircsById,
        Dictionary<uint, List<DidxAudio>> DidxAudioListById,
        Dictionary<string, PackFile> PackFileByBnkName,
        Dictionary<uint, string> NameById,
        Dictionary<string, List<string>> StateGroupsByDialogueEvent,
        Dictionary<string, Dictionary<string, string>>
            QualifiedStateGroupByStateGroupByDialogueEvent,
        Dictionary<string, List<string>> StatesByStateGroup,
        HashSet<string> LoadedBnkDataLanguages,
        bool IsDatDataLoaded);

    public interface IAudioRepository
    {
        Dictionary<uint, List<HircItem>> HircsById { get; }
        Dictionary<uint, List<DidxAudio>> DidxAudioListById { get; }
        Dictionary<string, PackFile> PackFileByBnkName { get; }
        Dictionary<uint, string> NameById { get; }
        Dictionary<string, List<string>> StateGroupsByDialogueEvent { get; }
        Dictionary<string, Dictionary<string, string>> QualifiedStateGroupByStateGroupByDialogueEvent { get; }
        Dictionary<string, List<string>> StatesByStateGroup { get; }
        bool IsCurrentGameSupported { get; }

        void Load(
            List<string> languages,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default);
        List<string> LoadDialogueEventMergerData(
            string bankNameSubstring,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default);
        void Clear();
        AudioRepositorySnapshot CreateSnapshot();
        void Restore(AudioRepositorySnapshot snapshot);
        List<T> GetHircsByType<T>() where T : class;
        List<HircItem> GetHircsByHircType(AkBkHircType type);
        List<HircItem> GetHircs(uint id);
        List<HircItem> GetHircs(uint id, string owningFileName);
        string GetNameFromId(uint value);
        string GetNameFromId(uint value, out bool found);
        string GetNameFromId(uint? key);
        HashSet<uint> GetUsedVanillaHircIdsByLanguageId(uint languageId);
        HashSet<uint> GetUsedVanillaSourceIdsByLanguageId(uint languageId);
        Dictionary<string, Dictionary<string, List<HircItem>>> GetVanillaDialogueEventsByBnkByLanguage();
        Dictionary<string, Dictionary<string, List<HircItem>>> GetModdedHircsByBnkByLanguage();
        Dictionary<string, List<HircItem>> GetModdedDialogueEventsByLanguage(List<string> moddedSoundBanks);
        List<string> GetModdedSoundBankFilePaths(string bnkNameSubstring);
    }

    public class AudioRepository(ApplicationSettingsService applicationSettingsService, BnkLoader bnkLoader, DatLoader datLoader) : IAudioRepository, IDisposable
    {
        private readonly ApplicationSettingsService _applicationSettingsService = applicationSettingsService;
        private readonly BnkLoader _bnkLoader = bnkLoader;
        private readonly DatLoader _datLoader = datLoader;
        private readonly HashSet<string> _loadedBnkDataLanguages = new(StringComparer.OrdinalIgnoreCase);
        private bool _isDatDataLoaded = false;

        public Dictionary<uint, List<HircItem>> HircsById { get; set; } = [];
        public Dictionary<uint, List<DidxAudio>> DidxAudioListById { get; set; } = [];
        public Dictionary<string, PackFile> PackFileByBnkName { get; set; } = [];
        public Dictionary<uint, string> NameById { get; set; } = [];
        public Dictionary<string, List<string>> StateGroupsByDialogueEvent { get; set; } = [];
        public Dictionary<string, Dictionary<string, string>> QualifiedStateGroupByStateGroupByDialogueEvent { get; set; } = [];
        public Dictionary<string, List<string>> StatesByStateGroup { get; set; } = [];
        public bool IsCurrentGameSupported =>
            GameInformationDatabase.Games.TryGetValue(
                _applicationSettingsService.CurrentSettings.CurrentGame,
                out var gameInformation) &&
            gameInformation.BankGeneratorVersion != GameBnkVersion.Unsupported;

        public void Load(
            List<string> languages,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var loadedData = false;

            if (!IsCurrentGameSupported)
                return;

            var loadDatData = !_isDatDataLoaded;
            var loadBnkData = !_loadedBnkDataLanguages.SetEquals(languages);

            if (loadDatData || loadBnkData)
                MemoryOptimiser.LogMemory("Before loading AudioRepository");

            if (loadDatData)
            {
                LoadDatData();
                loadedData = true;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (loadBnkData)
            {
                LoadBnkData(languages, progress, cancellationToken);
                loadedData = true;
            }

            if (loadedData)
            {
                MemoryOptimiser.Optimise();
                MemoryOptimiser.LogMemory("After loading AudioRepository");
            }
        }

        public List<string> LoadDialogueEventMergerData(
            string bankNameSubstring,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentGameSupported)
                return [];

            var discovery = _bnkLoader.DiscoverModdedSoundBanks(
                bankNameSubstring,
                cancellationToken);
            if (discovery.BankPaths.Count == 0)
                return [];

            var voiceLanguages = DialogueEventMergerBankScopeResolver
                .VoiceLanguages
                .ToList();
            var languageById = voiceLanguages.ToDictionary(
                WwiseHash.Compute);
            var useAllLanguages =
                discovery.HasUniversalBanks ||
                discovery.HasUnreadableLanguageIds ||
                discovery.LanguageIds.Any(
                    languageId => !languageById.ContainsKey(languageId));
            var languages = useAllLanguages
                ? voiceLanguages
                : discovery.LanguageFolders
                    .Concat(discovery.LanguageIds.Select(
                        languageId => languageById[languageId]))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

            MemoryOptimiser.LogMemory(
                "Before loading Dialogue Event Merger AudioRepository");
            if (!_isDatDataLoaded)
                LoadDatData();
            cancellationToken.ThrowIfCancellationRequested();
            LoadBnkData(
                languages,
                progress,
                cancellationToken,
                discovery.BankPaths);
            MemoryOptimiser.Optimise();
            MemoryOptimiser.LogMemory(
                "After loading Dialogue Event Merger AudioRepository");

            return GetModdedSoundBankFilePaths(bankNameSubstring);
        }

        private void LoadDatData()
        {
            var result = _datLoader.LoadDatData();
            NameById = result.NameById ?? [];
            StateGroupsByDialogueEvent = result.StateGroupsByDialogueEvent ?? [];
            QualifiedStateGroupByStateGroupByDialogueEvent = result.QualifiedStateGroupByStateGroupByDialogueEvent ?? [];
            StatesByStateGroup = result.StatesByStateGroup ?? [];

            _isDatDataLoaded = true;
        }

        private void LoadBnkData(
            List<string> languages,
            IProgress<AudioLoadProgress> progress,
            CancellationToken cancellationToken,
            IReadOnlyCollection<string> requiredBankPaths = null)
        {
            var allLanguages = Wh3LanguageInformation.GetAllLanguages();
            var sharedSfxLanguage = Wh3LanguageInformation.GetLanguageAsString(
                Wh3Language.Sfx);
            var languageToFilterOut = allLanguages
                .Where(language =>
                    !string.Equals(
                        language,
                        sharedSfxLanguage,
                        StringComparison.OrdinalIgnoreCase) &&
                    !languages.Contains(language))
                .ToList();
            var result = requiredBankPaths is null
                ? _bnkLoader.LoadBnkFiles(
                    languageToFilterOut,
                    progress,
                    cancellationToken)
                : _bnkLoader.LoadBnkFiles(
                    languageToFilterOut,
                    requiredBankPaths,
                    progress,
                    cancellationToken);
            HircsById = result.HircsById ?? [];
            DidxAudioListById = result.DidxAudioListById ?? [];
            PackFileByBnkName = result.PackFileByBnkName ?? [];

            _loadedBnkDataLanguages.Clear();
            _loadedBnkDataLanguages.UnionWith(languages);
        }

        public List<T> GetHircsByType<T>() where T : class
        {
            return HircsById.Values
                .SelectMany(items => items)
                .OfType<T>()
                .ToList();
        }

        public List<HircItem> GetHircsByHircType(AkBkHircType hircType)
        {
            return HircsById.SelectMany(x => x.Value)
                .Where(hirc => hirc.HircType == hircType)
                .ToList();
        }

        public List<HircItem> GetHircs(uint id)
        {
            if (HircsById.TryGetValue(id, out var value))
                return value;
            return [];
        }

        public List<HircItem> GetHircs(uint id, string owningFileName) => GetHircs(id).Where(x => x.BnkFilePath == owningFileName).ToList();

        public string GetNameFromId(uint value, out bool found)
        {
            found = NameById.ContainsKey(value);
            if (found)
                return NameById[value];
            return value.ToString();
        }

        public string GetNameFromId(uint value) => GetNameFromId(value, out var _);

        public string GetNameFromId(uint? key)
        {
            if (key.HasValue)
                return GetNameFromId(key.Value);
            else
                throw new Exception("Cannot get name from ID");
        }

        public HashSet<uint> GetUsedVanillaHircIdsByLanguageId(uint languageId)
        {
            return HircsById
                .SelectMany(hircLookupEntry => hircLookupEntry.Value
                    .Where(hirc => hirc.LanguageId == languageId && hirc.IsCAHircItem == true)
                    .Select(_ => hircLookupEntry.Key))
                .ToHashSet();
        }

        public HashSet<uint> GetUsedVanillaSourceIdsByLanguageId(uint languageId)
        {
            return HircsById
                .SelectMany(hircLookupEntry => hircLookupEntry.Value
                    .Where(hirc => hirc.LanguageId == languageId && hirc is ICAkSound && hirc.IsCAHircItem == true)
                    .Select(hirc => ((ICAkSound)hirc).GetSourceId()))
                .ToHashSet();
        }

        public Dictionary<string, Dictionary<string, List<HircItem>>> GetVanillaDialogueEventsByBnkByLanguage()
        {
            return GetHircsByType<ICAkDialogueEvent>()
                .Select(hirc => hirc as HircItem)
                .Where(hirc => hirc.IsCAHircItem)
                .GroupBy(hirc => GetNameFromId(hirc.LanguageId))
                .ToDictionary(
                    languageGroup => languageGroup.Key,
                    languageGroup => languageGroup
                        .GroupBy(hirc => hirc.BnkFilePath)
                        .ToDictionary(bnkGroup => bnkGroup.Key, bnkGroup => bnkGroup.ToList())
                );
        }

        public Dictionary<string, Dictionary<string, List<HircItem>>> GetModdedHircsByBnkByLanguage()
        {
            return HircsById
                .SelectMany(hirc => hirc.Value)
                .Where(hirc => hirc.IsCAHircItem == false)
                .SelectMany(hirc => GetDialogueMergerLanguages(hirc)
                    .Select(language => (Language: language, Hirc: hirc)))
                .GroupBy(entry => entry.Language)
                .ToDictionary(
                    languageGroup => languageGroup.Key,
                    languageGroup => languageGroup
                        .GroupBy(entry => entry.Hirc.BnkFilePath)
                        .ToDictionary(
                            bnkGroup => bnkGroup.Key,
                            bnkGroup => bnkGroup
                                .Select(entry => entry.Hirc)
                                .ToList())
                );
        }

        public Dictionary<string, List<HircItem>> GetModdedDialogueEventsByLanguage(List<string> moddedSoundBanks)
        {
            var selectedSoundBanks = new HashSet<string>(
                moddedSoundBanks,
                StringComparer.OrdinalIgnoreCase);
            return GetHircsByType<ICAkDialogueEvent>()
                .Select(hirc => hirc as HircItem)
                .Where(hirc =>
                    hirc.IsCAHircItem == false &&
                    selectedSoundBanks.Contains(hirc.BnkFilePath))
                .SelectMany(hirc => GetDialogueMergerLanguages(hirc)
                    .Select(language => (Language: language, Hirc: hirc)))
                .GroupBy(entry => entry.Language)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(entry => entry.Hirc).ToList());
        }

        private IEnumerable<string> GetDialogueMergerLanguages(HircItem hirc)
        {
            var scope = DialogueEventMergerBankScopeResolver.Resolve(
                hirc.BnkFilePath);
            if (scope.Kind ==
                DialogueEventMergerBankScopeKind.AllVoiceLanguages)
            {
                return DialogueEventMergerBankScopeResolver.VoiceLanguages;
            }

            return scope.Kind ==
                DialogueEventMergerBankScopeKind.SpecificLanguage
                ? [scope.Language]
                : [GetNameFromId(hirc.LanguageId)];
        }

        public List<string> GetModdedSoundBankFilePaths(string bnkNameSubstring)
        {
            return HircsById
                .SelectMany(hircDictionaryEntry => hircDictionaryEntry.Value) 
                .Where(hirc => hirc.IsCAHircItem == false && hirc.BnkFilePath.Contains(bnkNameSubstring))
                .Select(hirc => hirc.BnkFilePath ) 
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(bnkFilePath => bnkFilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void Clear()
        {
            MemoryOptimiser.LogMemory("Before clearing AudioRepository");

            if (HircsById != null)
            {
                foreach (var list in HircsById.Values)
                {
                    list?.Clear();
                    list?.TrimExcess();
                }
                HircsById.Clear();
                HircsById = [];
            }

            if (DidxAudioListById != null)
            {
                foreach (var list in DidxAudioListById.Values)
                {
                    list?.Clear();
                    list?.TrimExcess();
                }
                DidxAudioListById.Clear();
                DidxAudioListById = [];
            }

            _loadedBnkDataLanguages?.Clear();
            _isDatDataLoaded = false;
            PackFileByBnkName?.Clear();
            PackFileByBnkName = [];
            NameById?.Clear();
            NameById = [];
            StateGroupsByDialogueEvent?.Clear();
            StateGroupsByDialogueEvent = [];
            QualifiedStateGroupByStateGroupByDialogueEvent?.Clear();
            QualifiedStateGroupByStateGroupByDialogueEvent = [];
            StatesByStateGroup?.Clear();
            StatesByStateGroup = [];

            MemoryOptimiser.Optimise();
            MemoryOptimiser.LogMemory("After clearing AudioRepository");
        }

        public AudioRepositorySnapshot CreateSnapshot()
        {
            return new AudioRepositorySnapshot(
                HircsById,
                DidxAudioListById,
                PackFileByBnkName,
                NameById,
                StateGroupsByDialogueEvent,
                QualifiedStateGroupByStateGroupByDialogueEvent,
                StatesByStateGroup,
                new HashSet<string>(
                    _loadedBnkDataLanguages,
                    StringComparer.OrdinalIgnoreCase),
                _isDatDataLoaded);
        }

        public void Restore(AudioRepositorySnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            HircsById = snapshot.HircsById;
            DidxAudioListById = snapshot.DidxAudioListById;
            PackFileByBnkName = snapshot.PackFileByBnkName;
            NameById = snapshot.NameById;
            StateGroupsByDialogueEvent =
                snapshot.StateGroupsByDialogueEvent;
            QualifiedStateGroupByStateGroupByDialogueEvent =
                snapshot.QualifiedStateGroupByStateGroupByDialogueEvent;
            StatesByStateGroup = snapshot.StatesByStateGroup;
            _loadedBnkDataLanguages.Clear();
            _loadedBnkDataLanguages.UnionWith(
                snapshot.LoadedBnkDataLanguages);
            _isDatDataLoaded = snapshot.IsDatDataLoaded;
        }

        public void Dispose() => Clear();
    }
}
