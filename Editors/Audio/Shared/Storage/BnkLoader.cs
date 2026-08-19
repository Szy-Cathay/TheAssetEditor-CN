using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Bkhd;
using Shared.GameFormats.Wwise.Didx;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;

namespace Editors.Audio.Shared.Storage
{
    public sealed record DialogueEventMergerBankDiscovery(
        List<string> BankPaths,
        HashSet<uint> LanguageIds,
        HashSet<string> LanguageFolders,
        bool HasUniversalBanks,
        bool HasUnreadableLanguageIds);

    public class BnkLoader(IPackFileService packFileService)
    {
        public class Result
        {
            public Dictionary<uint, List<HircItem>> HircsById { get; internal set; } = [];
            public Dictionary<uint, List<DidxAudio>> DidxAudioListById { get; internal set; } = [];
            public Dictionary<string, PackFile> PackFileByBnkName { get; internal set; } = [];
        }

        private readonly IPackFileService _packFileService = packFileService;
        readonly ILogger _logger = Logging.Create<BnkLoader>();

        public virtual ParsedBnkFile LoadBnkFile(PackFile bnkFile, string bnkFilePath, bool isCAHircItem, bool printData = false)
        {
            var soundDb = BnkParser.Parse(bnkFile, bnkFilePath, isCAHircItem);
            if (printData)
                PrintHircList(soundDb.HircChunk.HircItems, bnkFilePath);
            return soundDb;
        }

        public Result LoadBnkFiles(
            List<string> languageToFilterOut,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            return LoadBnkFiles(
                languageToFilterOut,
                [],
                progress,
                cancellationToken);
        }

        public Result LoadBnkFiles(
            List<string> languageToFilterOut,
            IReadOnlyCollection<string> requiredBankPaths,
            IProgress<AudioLoadProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bankFiles = PackFileServiceUtility.FindAllWithExtentionIncludePaths(_packFileService, ".bnk");
            var bankFilesAsDictionary = bankFiles
                .GroupBy(
                    file => file.FileName,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group.Last().Pack,
                    StringComparer.OrdinalIgnoreCase);

            var removeFilter = new List<string>() { "media", "init.bnk", "animation_blood_data.bnk" };
            removeFilter.AddRange(languageToFilterOut);

            var wantedBnkFiles = PackFileUtil.FilterUnvantedFiles(bankFilesAsDictionary, removeFilter.ToArray(), out var removedFiles);
            foreach (var requiredPath in requiredBankPaths)
            {
                if (bankFilesAsDictionary.TryGetValue(
                        requiredPath,
                        out var requiredBank))
                {
                    wantedBnkFiles[requiredPath] = requiredBank;
                }
            }
            _logger.Here().Information($"Parsing game sounds. {bankFiles.Count} bnk files found. {wantedBnkFiles.Count} after filtering");

            var parsedBnks = new ConcurrentBag<ParsedBnkFile>();
            var bnksWithUnknownHircs = new ConcurrentBag<string>();
            var failedBnks = new ConcurrentBag<(string bnkFile, string Error)>();
            var packFileByBnkName = new ConcurrentDictionary<string, PackFile>();
            var result = new Result();
            var startedCount = 0;
            var completedCount = 0;

            var parallelOptions = new ParallelOptions { CancellationToken = cancellationToken };
            Parallel.ForEach(wantedBnkFiles, parallelOptions, bnkFile =>
            {
                var filePath = bnkFile.Key;
                var started = Interlocked.Increment(ref startedCount);
                _logger.Here().Information($"{started}/{wantedBnkFiles.Count} - {filePath}");

                try
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    var packFile = bnkFile.Value;
                    var packFileContainer = _packFileService.GetPackFileContainer(packFile);
                    packFileByBnkName.TryAdd(packFile.Name, packFile);

                    var parsedBnk = LoadBnkFile(packFile, filePath, packFileContainer.IsCaPackFile);
                    if (parsedBnk.HircChunk.HircItems.Any(hicItem => hicItem is UnknownHircItem == true || hicItem.HasError))
                        bnksWithUnknownHircs.Add(filePath);

                    parsedBnks.Add(parsedBnk);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    failedBnks.Add((filePath, e.Message));
                }
                finally
                {
                    var completed = Interlocked.Increment(ref completedCount);
                    progress?.Report(new AudioLoadProgress(completed, wantedBnkFiles.Count, filePath));
                }
            });

            cancellationToken.ThrowIfCancellationRequested();
            result.PackFileByBnkName = new Dictionary<string, PackFile>(packFileByBnkName);

            var allHircItems = parsedBnks.SelectMany(x => x.HircChunk.HircItems);
            PrintHircList(allHircItems, "All");
            if (failedBnks.Count != 0)
                _logger.Here().Error($"{failedBnks.Count} banks failed: {string.Join("\n", failedBnks)}");

            result.HircsById = parsedBnks
                .Where(parsedBnk => parsedBnk.HircChunk is not null)
                .SelectMany(parsedBnk => parsedBnk.HircChunk.HircItems)
                .GroupBy(item => item.Id)
                .ToDictionary(group => group.Key, group => group.ToList());


            result.DidxAudioListById = parsedBnks
                .Where(parsedBnk => parsedBnk.DataChunk is not null && parsedBnk.DidxChunk is not null)
                .SelectMany(parsedBnk =>
                    parsedBnk.DidxChunk.MediaList.Select(didx => new DidxAudio()
                    {
                        Id = didx.Id,
                        ByteArray = parsedBnk.DataChunk.GetBytesFromBuffer((int)didx.Offset, (int)didx.Size),
                        OwnerFilePath = parsedBnk.BkhdChunk.OwnerFilePath,
                        LanguageId = parsedBnk.BkhdChunk.AkBankHeader.LanguageId
                    }))
                .GroupBy(didxAudio => didxAudio.Id)
                .ToDictionary(group => group.Key, group => group.ToList());

            return result;
        }

        public DialogueEventMergerBankDiscovery DiscoverModdedSoundBanks(
            string bankNameSubstring,
            CancellationToken cancellationToken = default)
        {
            var bankFiles = PackFileServiceUtility
                .FindAllWithExtentionIncludePaths(
                    _packFileService,
                    ".bnk")
                .GroupBy(
                    file => file.FileName,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .Where(file => file.FileName.Contains(
                    bankNameSubstring,
                    StringComparison.OrdinalIgnoreCase))
                .Where(file =>
                    _packFileService.GetPackFileContainer(file.Pack)
                        is { IsCaPackFile: false })
                .OrderBy(
                    file => file.FileName,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();

            var bankPaths = new List<string>(bankFiles.Count);
            var languageIds = new HashSet<uint>();
            var languageFolders = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var hasUniversalBanks = false;
            var hasUnreadableLanguageIds = false;
            foreach (var bankFile in bankFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bankPaths.Add(bankFile.FileName);
                var scope = DialogueEventMergerBankScopeResolver.Resolve(
                    bankFile.FileName);
                if (scope.Kind ==
                    DialogueEventMergerBankScopeKind.AllVoiceLanguages)
                {
                    hasUniversalBanks = true;
                    continue;
                }

                if (scope.Kind ==
                    DialogueEventMergerBankScopeKind.SpecificLanguage)
                {
                    languageFolders.Add(scope.Language);
                    continue;
                }

                try
                {
                    languageIds.Add(ReadLanguageId(bankFile));
                }
                catch (Exception exception)
                {
                    hasUnreadableLanguageIds = true;
                    _logger.Here().Warning(
                        exception,
                        "Unable to read the language from {BankPath}",
                        bankFile.FileName);
                }
            }

            return new DialogueEventMergerBankDiscovery(
                bankPaths,
                languageIds,
                languageFolders,
                hasUniversalBanks,
                hasUnreadableLanguageIds);
        }

        private static uint ReadLanguageId(
            (string FileName, PackFile Pack) bankFile)
        {
            var chunk = bankFile.Pack.DataSource.ReadDataAsChunk();
            if (chunk.BytesLeft < ChunkHeader.ChunkHeaderSize)
                throw new InvalidDataException("SoundBank header is missing.");

            var chunkHeader = ChunkHeader.PeekFromBytes(chunk);
            if (chunkHeader.Tag != BankChunkTypes.BKHD)
            {
                throw new InvalidDataException(
                    "SoundBank does not start with a BKHD chunk.");
            }

            return BkhdChunk
                .ReadData(bankFile.FileName, chunk)
                .AkBankHeader
                .LanguageId;
        }

        void PrintHircList(IEnumerable<HircItem> hircItems, string header)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine($"\n Result: {header}");
            var unknownHirc = hircItems.Where(hircItem => hircItem is UnknownHircItem).Count();
            var errorHirc = hircItems.Where(hircItem => hircItem.HasError).Count();
            stringBuilder.AppendLine($"\t Total Hirc Items: {hircItems.Count()} Unknown: {unknownHirc} Decoding Errors:{errorHirc}");

            var grouped = hircItems.GroupBy(hircItem => hircItem.HircType);
            var groupedWithError = grouped.Where(groupedHircItems => groupedHircItems.Any(y => y is UnknownHircItem == true || y.HasError));
            var groupedWithoutError = grouped.Where(groupedHircItems => groupedHircItems.Any(y => y is UnknownHircItem == false && y.HasError == false));

            stringBuilder.AppendLine("\t\t Succeeded:");
            foreach (var group in groupedWithoutError)
                stringBuilder.AppendLine($"\t\t\t {group.Key}: Count: {group.Count()}");

            if (groupedWithError.Any())
            {
                stringBuilder.AppendLine("\t\t Failed:");
                foreach (var group in groupedWithError)
                    stringBuilder.AppendLine($"\t\t\t {group.Key}: {group.Where(x => x is UnknownHircItem == true || x.HasError).Count()}/{group.Count()} Failed");
            }

            _logger.Here().Information(stringBuilder.ToString());
        }
    }
}
