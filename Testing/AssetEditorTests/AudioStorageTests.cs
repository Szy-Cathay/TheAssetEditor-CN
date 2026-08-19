using Editors.Audio.AudioEditor.Core;
using Editors.Audio.Shared.AudioProject;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise;
using Editors.Audio.Shared.Wwise.Generators;
using Moq;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.Core.Services;
using Shared.GameFormats.Wwise;
using Shared.GameFormats.Wwise.Bkhd;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace AssetEditorTests
{
    [TestClass]
    public class AudioStorageTests
    {
        [TestMethod]
        public void LoadBnkFiles_CollectsEveryParallelResult()
        {
            const int bankCount = 4000;
            var container = new PackFileContainer("audio");
            for (var i = 0; i < bankCount; i++)
            {
                var fileName = $"audio\\test_{i}.bnk";
                container.FileList[fileName] = PackFile.CreateFromBytes($"test_{i}.bnk", []);
            }

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var loader = new TestBnkLoader(packFileService.Object);

            var result = loader.LoadBnkFiles([]);

            Assert.AreEqual(bankCount, result.PackFileByBnkName.Count);
        }

        [TestMethod]
        public void LoadBnkFiles_ReportsProgressForEveryBank()
        {
            const int bankCount = 20;
            var container = new PackFileContainer("audio");
            for (var i = 0; i < bankCount; i++)
            {
                var fileName = $"audio\\progress_{i}.bnk";
                container.FileList[fileName] = PackFile.CreateFromBytes($"progress_{i}.bnk", []);
            }

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var progress = new RecordingProgress<AudioLoadProgress>();
            var loader = new TestBnkLoader(packFileService.Object);

            loader.LoadBnkFiles([], progress, CancellationToken.None);

            Assert.AreEqual(bankCount, progress.Values.Count);
            Assert.AreEqual(bankCount, progress.Values.Max(value => value.Completed));
            Assert.IsTrue(progress.Values.All(value => value.Total == bankCount));
        }

        [TestMethod]
        public void LoadBnkFiles_HonoursCancellation()
        {
            var container = new PackFileContainer("audio");
            container.FileList["audio\\cancel.bnk"] =
                PackFile.CreateFromBytes("cancel.bnk", []);
            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            var loader = new TestBnkLoader(packFileService.Object);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsException<OperationCanceledException>(
                () => loader.LoadBnkFiles([], null, cancellation.Token));
        }

        [TestMethod]
        public void AudioRepository_UnsupportedGameExposesEmptyCollections()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer2);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));

            repository.Load([]);

            Assert.IsFalse(repository.IsCurrentGameSupported);
            Assert.AreEqual(0, repository.GetHircsByHircType(AkBkHircType.Event).Count);
            Assert.AreEqual(0, repository.GetHircs(1).Count);
            Assert.AreEqual(0, repository.PackFileByBnkName.Count);
        }

        [TestMethod]
        public void AudioRepository_ReloadsWhenReturningToAnEarlierLanguage()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var container = new PackFileContainer("audio");
            container.FileList["audio\\wwise\\english(uk)\\english.bnk"] =
                PackFile.CreateFromBytes("english.bnk", []);
            container.FileList["audio\\wwise\\french(france)\\french.bnk"] =
                PackFile.CreateFromBytes("french.bnk", []);

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            Assert.IsTrue(repository.IsCurrentGameSupported);
            repository.Load(["english(uk)"]);
            repository.Load(["french(france)"]);
            repository.Load(["english(uk)"]);

            Assert.AreEqual(3, loader.LoadCount);
        }

        [TestMethod]
        public void AudioRepository_AlwaysIncludesSharedSfxBanks()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var container = new PackFileContainer("audio");
            container.FileList["audio\\wwise\\english(uk)\\english.bnk"] =
                PackFile.CreateFromBytes("english.bnk", []);
            container.FileList["audio\\wwise\\french(france)\\french.bnk"] =
                PackFile.CreateFromBytes("french.bnk", []);
            container.FileList["audio\\wwise\\loading_screen_sfx__core.bnk"] =
                PackFile.CreateFromBytes("loading_screen_sfx__core.bnk", []);

            var packFileService = new Mock<IPackFileService>();
            packFileService
                .Setup(x => x.GetAllPackfileContainers())
                .Returns([container]);
            packFileService
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns(container);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            repository.Load(["english(uk)"]);

            Assert.AreEqual(2, loader.LoadCount);
        }

        [TestMethod]
        public void AudioRepository_DialogueMergerUsesLanguageFolderOverInternalLanguage()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var gameContainer = new PackFileContainer("audio")
            {
                IsCaPackFile = true
            };
            gameContainer.FileList["audio\\wwise\\english(uk)\\english.bnk"] =
                CreateHeaderOnlyBank("english.bnk", "english(uk)");
            gameContainer.FileList["audio\\wwise\\french(france)\\french.bnk"] =
                CreateHeaderOnlyBank("french.bnk", "french(france)");

            const string modBankPath =
                "audio\\wwise\\french(france)\\custom_for_merging.bnk";
            var modContainer = new PackFileContainer("mod");
            modContainer.FileList[modBankPath] =
                CreateHeaderOnlyBank(
                    "custom_for_merging.bnk",
                    "english(uk)");

            var packFileService = CreatePackFileService(
                gameContainer,
                modContainer);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            repository.LoadDialogueEventMergerData(
                "for_merging",
                null,
                CancellationToken.None);

            CollectionAssert.Contains(
                loader.LoadedPaths,
                "audio\\wwise\\french(france)\\french.bnk");
            CollectionAssert.Contains(loader.LoadedPaths, modBankPath);
            CollectionAssert.DoesNotContain(
                loader.LoadedPaths,
                "audio\\wwise\\english(uk)\\english.bnk");
        }

        [TestMethod]
        public void AudioRepository_DialogueMergerRootBankLoadsEveryVoiceLanguage()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var voiceLanguages = Wh3LanguageInformation.GetAllLanguages()
                .Where(language => language != "sfx")
                .ToList();
            var gameContainer = new PackFileContainer("audio")
            {
                IsCaPackFile = true
            };
            foreach (var language in voiceLanguages)
            {
                var path = $"audio\\wwise\\{language}\\{language}.bnk";
                gameContainer.FileList[path] =
                    CreateHeaderOnlyBank($"{language}.bnk", language);
            }

            const string modBankPath =
                "audio\\wwise\\universal_for_merging.bnk";
            var modContainer = new PackFileContainer("mod");
            modContainer.FileList[modBankPath] =
                CreateHeaderOnlyBank(
                    "universal_for_merging.bnk",
                    "english(uk)");

            var packFileService = CreatePackFileService(
                gameContainer,
                modContainer);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            repository.LoadDialogueEventMergerData(
                "for_merging",
                null,
                CancellationToken.None);

            foreach (var language in voiceLanguages)
            {
                CollectionAssert.Contains(
                    loader.LoadedPaths,
                    $"audio\\wwise\\{language}\\{language}.bnk");
            }
            CollectionAssert.Contains(loader.LoadedPaths, modBankPath);
        }

        [TestMethod]
        public void AudioRepository_DialogueMergerFallsBackForUnknownLanguage()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var gameContainer = new PackFileContainer("audio")
            {
                IsCaPackFile = true
            };
            gameContainer.FileList["audio\\wwise\\english(uk)\\english.bnk"] =
                CreateHeaderOnlyBank("english.bnk", "english(uk)");
            gameContainer.FileList["audio\\wwise\\french(france)\\french.bnk"] =
                CreateHeaderOnlyBank("french.bnk", "french(france)");

            var modContainer = new PackFileContainer("mod");
            modContainer.FileList[
                "audio\\wwise\\custom\\unknown_for_merging.bnk"] =
                CreateHeaderOnlyBank(
                    "unknown_for_merging.bnk",
                    123456789u);

            var packFileService = CreatePackFileService(
                gameContainer,
                modContainer);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            repository.LoadDialogueEventMergerData(
                "for_merging",
                null,
                CancellationToken.None);

            CollectionAssert.Contains(
                loader.LoadedPaths,
                "audio\\wwise\\english(uk)\\english.bnk");
            CollectionAssert.Contains(
                loader.LoadedPaths,
                "audio\\wwise\\french(france)\\french.bnk");
        }

        [TestMethod]
        public void AudioRepository_DialogueMergerSkipsLoadWhenNoInputsExist()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var container = new PackFileContainer("audio")
            {
                IsCaPackFile = true
            };
            container.FileList["audio\\wwise\\english(uk)\\english.bnk"] =
                CreateHeaderOnlyBank("english.bnk", "english(uk)");
            var packFileService = CreatePackFileService(container);
            var loader = new TestBnkLoader(packFileService.Object);
            using var repository = new AudioRepository(
                settings,
                loader,
                new DatLoader(packFileService.Object, settings));

            var soundBanks = repository.LoadDialogueEventMergerData(
                "for_merging",
                null,
                CancellationToken.None);

            Assert.AreEqual(0, soundBanks.Count);
            Assert.AreEqual(0, loader.LoadCount);
        }

        [TestMethod]
        public void AudioRepository_DialogueMergerGroupsCopiedBanksByVoiceLanguageFolder()
        {
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var englishLanguageId = WwiseHash.Compute("english(uk)");
            repository.NameById[englishLanguageId] = "english(uk)";

            var voiceLanguages = Wh3LanguageInformation.GetAllLanguages()
                .Where(language => language != "sfx")
                .ToList();
            var copiedBanks = voiceLanguages
                .Select((language, index) => CreateModdedDialogueEvent(
                    (uint)index + 1,
                    englishLanguageId,
                    $"audio\\wwise\\{language}\\copied_for_merging.bnk"))
                .ToList();
            var secondChineseBank = CreateModdedDialogueEvent(
                (uint)copiedBanks.Count + 1,
                englishLanguageId,
                "audio\\wwise\\chinese\\other_for_merging.bnk");
            var rootBank = CreateModdedDialogueEvent(
                secondChineseBank.Id + 1,
                englishLanguageId,
                "audio\\wwise\\root_for_merging.bnk");
            copiedBanks.Add(secondChineseBank);
            copiedBanks.Add(rootBank);
            repository.HircsById = copiedBanks.ToDictionary(
                bank => bank.Id,
                bank => new List<HircItem> { bank });
            var selectedBanks = repository.HircsById.Values
                .SelectMany(items => items)
                .Select(item => item.BnkFilePath)
                .ToList();

            var hircsByBnkByLanguage =
                repository.GetModdedHircsByBnkByLanguage();
            var dialogueEventsByLanguage =
                repository.GetModdedDialogueEventsByLanguage(selectedBanks);

            CollectionAssert.AreEquivalent(
                voiceLanguages,
                hircsByBnkByLanguage.Keys.ToList());
            Assert.AreEqual(2, hircsByBnkByLanguage["english(uk)"].Count);
            Assert.AreEqual(3, hircsByBnkByLanguage["chinese"].Count);
            Assert.AreEqual(2, hircsByBnkByLanguage["french(france)"].Count);
            Assert.AreEqual(2, dialogueEventsByLanguage["english(uk)"].Count);
            Assert.AreEqual(3, dialogueEventsByLanguage["chinese"].Count);
            Assert.AreEqual(2, dialogueEventsByLanguage["french(france)"].Count);
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerAllowsCopiedIdsAcrossVoiceLanguages()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var englishLanguageId = WwiseHash.Compute("english(uk)");
            repository.NameById[englishLanguageId] = "english(uk)";

            var englishBank = new UnknownHircItem
            {
                Id = 1470542480,
                LanguageId = englishLanguageId,
                BnkFilePath =
                    "audio\\wwise\\english(uk)\\copied_for_merging.bnk",
                IsCAHircItem = false
            };
            var chineseBank = new UnknownHircItem
            {
                Id = englishBank.Id,
                LanguageId = englishLanguageId,
                BnkFilePath =
                    "audio\\wwise\\chinese\\copied_for_merging.bnk",
                IsCAHircItem = false
            };
            repository.HircsById[englishBank.Id] =
                [englishBank, chineseBank];
            var integrityService = new AudioEditorIntegrityService(
                packFileService.Object,
                repository,
                Mock.Of<IStandardDialogs>());

            var result = integrityService.CheckMergingSoundBanksIdIntegrity(
                [englishBank.BnkFilePath, chineseBank.BnkFilePath]);

            Assert.IsTrue(result);
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerAllowsSharedSourceWhenBanksUseSameWem()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            const uint sourceId = 157925570;
            const string battleBankPath =
                "audio\\wwise\\battle_vo_orders_surtr_arts_fighter_for_merging.bnk";
            const string campaignBankPath =
                "audio\\wwise\\campaign_vo_surtr_arts_fighter_for_merging.bnk";
            var container = new PackFileContainer("surtr");
            var battleBank = PackFile.CreateFromBytes("battle.bnk", []);
            var campaignBank = PackFile.CreateFromBytes("campaign.bnk", []);
            container.FileList[battleBankPath] = battleBank;
            container.FileList[campaignBankPath] = campaignBank;
            container.FileList[$"audio\\wwise\\{sourceId}.wem"] =
                PackFile.CreateFromBytes($"{sourceId}.wem", [1, 2, 3]);
            var packFileService = CreatePackFileService(container);
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var languageId = WwiseHash.Compute("chinese");
            repository.NameById[languageId] = "chinese";
            repository.PackFileByBnkName[battleBankPath] = battleBank;
            repository.PackFileByBnkName[campaignBankPath] = campaignBank;
            var battleSound = CreateModdedSound(
                1,
                sourceId,
                languageId,
                battleBankPath);
            var campaignSound = CreateModdedSound(
                2,
                sourceId,
                languageId,
                campaignBankPath);
            repository.HircsById[battleSound.Id] = [battleSound];
            repository.HircsById[campaignSound.Id] = [campaignSound];
            var dialogs = new Mock<IStandardDialogs>();
            var integrityService = new AudioEditorIntegrityService(
                packFileService.Object,
                repository,
                dialogs.Object);

            var result = integrityService.CheckMergingSoundBanksIdIntegrity(
                [battleBankPath, campaignBankPath]);

            Assert.IsTrue(result);
            dialogs.VerifyNoOtherCalls();
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerAllowsSharedSourceWithIdenticalWemCopies()
        {
            var scenario = CreateSourceIntegrityScenario(
                [1, 2, 3],
                [1, 2, 3]);
            using var repository = scenario.Repository;

            var result = scenario.IntegrityService
                .CheckMergingSoundBanksIdIntegrity(
                    [scenario.FirstBankPath, scenario.SecondBankPath]);

            Assert.IsTrue(result);
            scenario.Dialogs.VerifyNoOtherCalls();
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerRejectsSharedSourceWithDifferentWems()
        {
            var scenario = CreateSourceIntegrityScenario(
                [1, 2, 3],
                [4, 5, 6]);
            using var repository = scenario.Repository;

            var result = scenario.IntegrityService
                .CheckMergingSoundBanksIdIntegrity(
                    [scenario.FirstBankPath, scenario.SecondBankPath]);

            Assert.IsFalse(result);
            scenario.Dialogs.Verify(dialogs => dialogs.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerRejectsSharedHircIdWithinLanguage()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            const string firstBankPath =
                "audio\\wwise\\first_for_merging.bnk";
            const string secondBankPath =
                "audio\\wwise\\second_for_merging.bnk";
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var languageId = WwiseHash.Compute("chinese");
            repository.NameById[languageId] = "chinese";
            repository.HircsById[42] =
            [
                new UnknownHircItem
                {
                    Id = 42,
                    LanguageId = languageId,
                    BnkFilePath = firstBankPath,
                    IsCAHircItem = false
                },
                new UnknownHircItem
                {
                    Id = 42,
                    LanguageId = languageId,
                    BnkFilePath = secondBankPath,
                    IsCAHircItem = false
                }
            ];
            var dialogs = new Mock<IStandardDialogs>();
            var integrityService = new AudioEditorIntegrityService(
                packFileService.Object,
                repository,
                dialogs.Object);

            var result = integrityService.CheckMergingSoundBanksIdIntegrity(
                [firstBankPath, secondBankPath]);

            Assert.IsFalse(result);
            dialogs.Verify(value => value.ShowDialogBox(
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerAllowsIdenticalSharedHirc()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            const uint hircId = 42;
            const uint sourceId = 157925570;
            const string firstBankPath =
                "audio\\wwise\\first_for_merging.bnk";
            const string secondBankPath =
                "audio\\wwise\\second_for_merging.bnk";
            var container = new PackFileContainer("mod");
            var firstBank = PackFile.CreateFromBytes("first.bnk", []);
            var secondBank = PackFile.CreateFromBytes("second.bnk", []);
            container.FileList[firstBankPath] = firstBank;
            container.FileList[secondBankPath] = secondBank;
            container.FileList[$"audio\\wwise\\{sourceId}.wem"] =
                PackFile.CreateFromBytes($"{sourceId}.wem", [1, 2, 3]);
            var packFileService = CreatePackFileService(container);
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var languageId = WwiseHash.Compute("chinese");
            repository.NameById[languageId] = "chinese";
            repository.PackFileByBnkName[firstBankPath] = firstBank;
            repository.PackFileByBnkName[secondBankPath] = secondBank;
            repository.HircsById[hircId] =
            [
                CreateModdedSound(
                    hircId,
                    sourceId,
                    languageId,
                    firstBankPath),
                CreateModdedSound(
                    hircId,
                    sourceId,
                    languageId,
                    secondBankPath)
            ];
            var dialogs = new Mock<IStandardDialogs>();
            var integrityService = new AudioEditorIntegrityService(
                packFileService.Object,
                repository,
                dialogs.Object);

            var result = integrityService.CheckMergingSoundBanksIdIntegrity(
                [firstBankPath, secondBankPath]);

            Assert.IsTrue(result);
            dialogs.VerifyNoOtherCalls();
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerRejectsConflictingDialoguePath()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            const uint dialogueEventId = 1470542480;
            const string firstBankPath =
                "audio\\wwise\\first_for_merging.bnk";
            const string secondBankPath =
                "audio\\wwise\\second_for_merging.bnk";
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var languageId = WwiseHash.Compute("chinese");
            repository.NameById[languageId] = "chinese";
            repository.HircsById[dialogueEventId] =
            [
                CreateModdedDialogueEvent(
                    dialogueEventId,
                    languageId,
                    firstBankPath,
                    10,
                    100),
                CreateModdedDialogueEvent(
                    dialogueEventId,
                    languageId,
                    secondBankPath,
                    10,
                    200)
            ];
            var dialogs = new Mock<IStandardDialogs>();
            var integrityService = new AudioEditorIntegrityService(
                packFileService.Object,
                repository,
                dialogs.Object);

            var result = integrityService.CheckMergingSoundBanksIdIntegrity(
                [firstBankPath, secondBankPath]);

            Assert.IsFalse(result);
            dialogs.Verify(value => value.ShowDialogBox(
                It.Is<string>(message => message.Contains(
                    dialogueEventId.ToString(),
                    StringComparison.Ordinal)),
                It.IsAny<string>()), Times.Once);
        }

        [TestMethod]
        public void AudioIntegrity_DialogueMergerAllowsDisjointDialoguePaths()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            const uint dialogueEventId = 1470542480;
            const string firstBankPath =
                "audio\\wwise\\first_for_merging.bnk";
            const string secondBankPath =
                "audio\\wwise\\second_for_merging.bnk";
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var languageId = WwiseHash.Compute("chinese");
            repository.NameById[languageId] = "chinese";
            repository.HircsById[dialogueEventId] =
            [
                CreateModdedDialogueEvent(
                    dialogueEventId,
                    languageId,
                    firstBankPath,
                    10,
                    100),
                CreateModdedDialogueEvent(
                    dialogueEventId,
                    languageId,
                    secondBankPath,
                    20,
                    200)
            ];
            var dialogs = new Mock<IStandardDialogs>();
            var integrityService = new AudioEditorIntegrityService(
                packFileService.Object,
                repository,
                dialogs.Object);

            var result = integrityService.CheckMergingSoundBanksIdIntegrity(
                [firstBankPath, secondBankPath]);

            Assert.IsTrue(result);
            dialogs.VerifyNoOtherCalls();
        }

        [TestMethod]
        public async Task DialogueMerger_ConflictDoesNotSaveOutputs()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            const uint dialogueEventId = 1470542480;
            const string firstBankPath =
                "audio\\wwise\\first_for_merging.bnk";
            const string secondBankPath =
                "audio\\wwise\\second_for_merging.bnk";
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var languageId = WwiseHash.Compute("chinese");
            repository.NameById[languageId] = "chinese";
            repository.HircsById[dialogueEventId] =
            [
                CreateModdedDialogueEvent(
                    dialogueEventId,
                    languageId,
                    firstBankPath,
                    10,
                    100),
                CreateModdedDialogueEvent(
                    dialogueEventId,
                    languageId,
                    secondBankPath,
                    10,
                    200)
            ];
            var outputService = new Mock<IAudioPackOutputService>();
            var generator = new SoundBankGeneratorService(
                outputService.Object,
                settings,
                repository,
                new AudioEditorIntegrityService(
                    packFileService.Object,
                    repository,
                    Mock.Of<IStandardDialogs>()));

            var result = await generator
                .GenerateMergedDialogueEventSoundBanksAsync(
                    [firstBankPath, secondBankPath],
                    "merged",
                    CancellationToken.None);

            Assert.IsFalse(result);
            outputService.Verify(service => service.SaveBatch(
                It.IsAny<IReadOnlyCollection<AudioPackOutput>>(),
                It.IsAny<bool>()), Times.Never);
        }

        [TestMethod]
        public async Task DialogueMerger_CopiedFrontendBankGeneratesEveryVoiceLanguage()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var voiceLanguages = Wh3LanguageInformation.GetAllLanguages()
                .Where(language => language != "sfx")
                .ToList();
            const string dialogueEventName = "frontend_vo_character_select";
            var dialogueEventId = WwiseHash.Compute(dialogueEventName);
            var englishLanguageId = WwiseHash.Compute("english(uk)");
            repository.NameById[dialogueEventId] = dialogueEventName;

            var hircs = new List<HircItem>();
            var selectedBanks = new List<string>();
            foreach (var language in voiceLanguages)
            {
                var languageId = WwiseHash.Compute(language);
                repository.NameById[languageId] = language;
                hircs.Add(new CAkDialogueEvent_V136
                {
                    Id = dialogueEventId,
                    HircType = AkBkHircType.Dialogue_Event,
                    LanguageId = languageId,
                    BnkFilePath =
                        $"audio\\wwise\\{language}\\frontend_vo.bnk",
                    IsCAHircItem = true
                });

                var moddedBankPath =
                    $"audio\\wwise\\{language}\\copied_for_merging.bnk";
                selectedBanks.Add(moddedBankPath);
                hircs.Add(new CAkDialogueEvent_V136
                {
                    Id = dialogueEventId,
                    HircType = AkBkHircType.Dialogue_Event,
                    LanguageId = englishLanguageId,
                    BnkFilePath = moddedBankPath,
                    IsCAHircItem = false
                });
            }

            repository.HircsById[dialogueEventId] = hircs;
            IReadOnlyCollection<AudioPackOutput>? savedOutputs = null;
            var outputService = new Mock<IAudioPackOutputService>();
            outputService
                .Setup(service => service.SaveBatch(
                    It.IsAny<IReadOnlyCollection<AudioPackOutput>>(),
                    true))
                .Callback((
                    IReadOnlyCollection<AudioPackOutput> outputs,
                    bool _) => savedOutputs = outputs)
                .Returns(true);
            var generator = new SoundBankGeneratorService(
                outputService.Object,
                settings,
                repository,
                new AudioEditorIntegrityService(
                    packFileService.Object,
                    repository,
                    Mock.Of<IStandardDialogs>()));

            var result = await generator
                .GenerateMergedDialogueEventSoundBanksAsync(
                    selectedBanks,
                    "merged",
                    CancellationToken.None);

            Assert.IsTrue(result);
            Assert.IsNotNull(savedOutputs);
            var outputFileName =
                $"{Wh3SoundBankInformation.GetName(Wh3SoundBank.FrontendVO)}_0_merged.bnk";
            CollectionAssert.AreEquivalent(
                voiceLanguages
                    .Select(language =>
                        $"audio\\wwise\\{language}\\{outputFileName}")
                    .ToList(),
                savedOutputs.Select(output => output.FilePath).ToList());
        }

        [TestMethod]
        public async Task DialogueMerger_RootBankGeneratesEveryVoiceLanguage()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var packFileService = new Mock<IPackFileService>();
            using var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var voiceLanguages = Wh3LanguageInformation.GetAllLanguages()
                .Where(language => language != "sfx")
                .ToList();
            const string dialogueEventName = "frontend_vo_character_select";
            const string moddedBankPath =
                "audio\\wwise\\universal_for_merging.bnk";
            var dialogueEventId = WwiseHash.Compute(dialogueEventName);
            var englishLanguageId = WwiseHash.Compute("english(uk)");
            repository.NameById[dialogueEventId] = dialogueEventName;

            var hircs = new List<HircItem>();
            foreach (var language in voiceLanguages)
            {
                var languageId = WwiseHash.Compute(language);
                repository.NameById[languageId] = language;
                hircs.Add(new CAkDialogueEvent_V136
                {
                    Id = dialogueEventId,
                    HircType = AkBkHircType.Dialogue_Event,
                    LanguageId = languageId,
                    BnkFilePath =
                        $"audio\\wwise\\{language}\\frontend_vo.bnk",
                    IsCAHircItem = true
                });
            }

            hircs.Add(new CAkDialogueEvent_V136
            {
                Id = dialogueEventId,
                HircType = AkBkHircType.Dialogue_Event,
                LanguageId = englishLanguageId,
                BnkFilePath = moddedBankPath,
                IsCAHircItem = false
            });
            repository.HircsById[dialogueEventId] = hircs;

            IReadOnlyCollection<AudioPackOutput>? savedOutputs = null;
            var outputService = new Mock<IAudioPackOutputService>();
            outputService
                .Setup(service => service.SaveBatch(
                    It.IsAny<IReadOnlyCollection<AudioPackOutput>>(),
                    true))
                .Callback((
                    IReadOnlyCollection<AudioPackOutput> outputs,
                    bool _) => savedOutputs = outputs)
                .Returns(true);
            var generator = new SoundBankGeneratorService(
                outputService.Object,
                settings,
                repository,
                new AudioEditorIntegrityService(
                    packFileService.Object,
                    repository,
                    Mock.Of<IStandardDialogs>()));

            var result = await generator
                .GenerateMergedDialogueEventSoundBanksAsync(
                    [moddedBankPath],
                    "merged",
                    CancellationToken.None);

            Assert.IsTrue(result);
            Assert.IsNotNull(savedOutputs);
            var outputFileName =
                $"{Wh3SoundBankInformation.GetName(Wh3SoundBank.FrontendVO)}_0_merged.bnk";
            CollectionAssert.AreEquivalent(
                voiceLanguages
                    .Select(language =>
                        $"audio\\wwise\\{language}\\{outputFileName}")
                    .ToList(),
                savedOutputs.Select(output => output.FilePath).ToList());
        }

        private static Mock<IPackFileService> CreatePackFileService(
            params PackFileContainer[] containers)
        {
            var containerByFile = containers
                .SelectMany(container => container.FileList.Values
                    .Select(file => (file, container)))
                .ToDictionary(entry => entry.file, entry => entry.container);
            var service = new Mock<IPackFileService>();
            service
                .Setup(x => x.GetAllPackfileContainers())
                .Returns(containers.ToList());
            service
                .Setup(x => x.GetPackFileContainer(It.IsAny<PackFile>()))
                .Returns((PackFile file) => containerByFile[file]);
            service
                .Setup(x => x.FindFile(
                    It.IsAny<string>(),
                    It.IsAny<PackFileContainer>()))
                .Returns((string path, PackFileContainer container) =>
                    container.FileList
                        .FirstOrDefault(entry => string.Equals(
                            entry.Key,
                            path,
                            StringComparison.OrdinalIgnoreCase))
                        .Value);
            return service;
        }

        private static (
            AudioEditorIntegrityService IntegrityService,
            AudioRepository Repository,
            Mock<IStandardDialogs> Dialogs,
            string FirstBankPath,
            string SecondBankPath) CreateSourceIntegrityScenario(
                byte[] firstWemData,
                byte[] secondWemData)
        {
            const uint sourceId = 157925570;
            const string firstBankPath =
                "audio\\wwise\\first_for_merging.bnk";
            const string secondBankPath =
                "audio\\wwise\\second_for_merging.bnk";
            var firstContainer = new PackFileContainer("first");
            var secondContainer = new PackFileContainer("second");
            var firstBank = PackFile.CreateFromBytes("first_for_merging.bnk", []);
            var secondBank = PackFile.CreateFromBytes("second_for_merging.bnk", []);
            firstContainer.FileList[firstBankPath] = firstBank;
            firstContainer.FileList[$"audio\\wwise\\{sourceId}.wem"] =
                PackFile.CreateFromBytes($"{sourceId}.wem", firstWemData);
            secondContainer.FileList[secondBankPath] = secondBank;
            secondContainer.FileList[$"audio\\wwise\\{sourceId}.wem"] =
                PackFile.CreateFromBytes($"{sourceId}.wem", secondWemData);
            var packFileService = CreatePackFileService(
                firstContainer,
                secondContainer);
            var settings = new ApplicationSettingsService(GameTypeEnum.Warhammer3);
            var repository = new AudioRepository(
                settings,
                new BnkLoader(packFileService.Object),
                new DatLoader(packFileService.Object, settings));
            var languageId = WwiseHash.Compute("chinese");
            repository.NameById[languageId] = "chinese";
            repository.PackFileByBnkName[firstBankPath] = firstBank;
            repository.PackFileByBnkName[secondBankPath] = secondBank;
            var firstSound = CreateModdedSound(
                1,
                sourceId,
                languageId,
                firstBankPath);
            var secondSound = CreateModdedSound(
                2,
                sourceId,
                languageId,
                secondBankPath);
            repository.HircsById[firstSound.Id] = [firstSound];
            repository.HircsById[secondSound.Id] = [secondSound];
            var dialogs = new Mock<IStandardDialogs>();
            var integrityService = new AudioEditorIntegrityService(
                packFileService.Object,
                repository,
                dialogs.Object);
            return (
                integrityService,
                repository,
                dialogs,
                firstBankPath,
                secondBankPath);
        }

        private static PackFile CreateHeaderOnlyBank(
            string fileName,
            string language) =>
            CreateHeaderOnlyBank(fileName, WwiseHash.Compute(language));

        private static PackFile CreateHeaderOnlyBank(
            string fileName,
            uint languageId)
        {
            var header = new BkhdChunk
            {
                ChunkHeader = new ChunkHeader
                {
                    Tag = BankChunkTypes.BKHD,
                    ChunkSize = 20
                },
                AkBankHeader = new AkBankHeader
                {
                    BankGeneratorVersion = 136,
                    SoundBankId = 1,
                    LanguageId = languageId,
                    AltValues = 0x10,
                    ProjectId = 1
                }
            };
            return PackFile.CreateFromBytes(
                fileName,
                BkhdChunk.WriteData(header));
        }

        private static CAkDialogueEvent_V136 CreateModdedDialogueEvent(
            uint id,
            uint languageId,
            string bnkFilePath) =>
            new()
            {
                Id = id,
                LanguageId = languageId,
                BnkFilePath = bnkFilePath,
                IsCAHircItem = false
            };

        private static CAkDialogueEvent_V136 CreateModdedDialogueEvent(
            uint id,
            uint languageId,
            string bnkFilePath,
            uint stateId,
            uint audioNodeId)
        {
            var rootNode = new AkDecisionTree_V136.Node_V136
            {
                Nodes =
                [
                    new AkDecisionTree_V136.Node_V136
                    {
                        Key = stateId,
                        AudioNodeId = audioNodeId
                    }
                ]
            };
            var decisionTree = new AkDecisionTree_V136
            {
                DecisionTree = rootNode,
                Nodes = AkDecisionTree_V136.FlattenDecisionTree(rootNode)
            };
            return new CAkDialogueEvent_V136
            {
                Id = id,
                HircType = AkBkHircType.Dialogue_Event,
                LanguageId = languageId,
                BnkFilePath = bnkFilePath,
                IsCAHircItem = false,
                TreeDepth = 1,
                Arguments =
                [
                    new AkGameSync_V136
                    {
                        GroupId = 1,
                        GroupType = AkGroupType.State
                    }
                ],
                AkDecisionTree = decisionTree,
                TreeDataSize = decisionTree.GetSize()
            };
        }

        private static CAkSound_V136 CreateModdedSound(
            uint id,
            uint sourceId,
            uint languageId,
            string bnkFilePath) =>
            new()
            {
                Id = id,
                LanguageId = languageId,
                BnkFilePath = bnkFilePath,
                IsCAHircItem = false,
                AkBankSourceData = new AkBankSourceData_V136
                {
                    AkMediaInformation = new AkBankSourceData_V136
                        .AkMediaInformation_V136
                    {
                        SourceId = sourceId
                    }
                }
            };

        private sealed class TestBnkLoader(IPackFileService packFileService) : BnkLoader(packFileService)
        {
            private int _loadCount;
            public int LoadCount => _loadCount;
            public List<string> LoadedPaths { get; } = [];

            public override ParsedBnkFile LoadBnkFile(
                PackFile bnkFile,
                string bnkFilePath,
                bool isCAHircItem,
                bool printData = false)
            {
                Interlocked.Increment(ref _loadCount);
                lock (LoadedPaths)
                    LoadedPaths.Add(bnkFilePath);
                return new ParsedBnkFile();
            }
        }

        private sealed class RecordingProgress<T> : IProgress<T>
        {
            private readonly object _lock = new();
            public List<T> Values { get; } = [];

            public void Report(T value)
            {
                lock (_lock)
                    Values.Add(value);
            }
        }
    }
}
