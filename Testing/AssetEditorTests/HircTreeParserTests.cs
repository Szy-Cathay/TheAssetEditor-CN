using Editors.Audio.Shared.Storage;
using Editors.Audio.Shared.Wwise.HircExploration;
using Moq;
using Shared.Core.Services;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.GameFormats.Wwise.Hirc.V112;
using Shared.GameFormats.Wwise.Hirc.V136;
using Shared.GameFormats.Wwise.Hirc.V136.Shared;

namespace AssetEditorTests
{
    [TestClass]
    public class HircTreeParserTests
    {
        [TestInitialize]
        public void InitializeLocalization()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
        }

        [TestMethod]
        public void BuildHierarchy_PrefersReferencedHircFromTheSameBankAndLanguage()
        {
            const uint actionId = 20;
            var actionEvent = new CAkEvent_V136
            {
                Id = 10,
                HircType = AkBkHircType.Event,
                BnkFilePath = "audio\\wwise\\english(uk)\\events.bnk",
                LanguageId = 100,
                Actions = [new CAkEvent_V136.Action_V136 { ActionId = actionId }]
            };
            var wrongAction = new CAkAction_V136
            {
                Id = actionId,
                HircType = AkBkHircType.Action,
                BnkFilePath = "audio\\wwise\\french(france)\\events.bnk",
                LanguageId = 200
            };
            var expectedAction = new CAkAction_V136
            {
                Id = actionId,
                HircType = AkBkHircType.Action,
                BnkFilePath = actionEvent.BnkFilePath,
                LanguageId = actionEvent.LanguageId
            };
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.GetHircs(actionId))
                .Returns([wrongAction, expectedAction]);
            repository
                .Setup(x => x.GetNameFromId(It.IsAny<uint>()))
                .Returns((uint id) => id.ToString());
            var parser = new HircTreeChildrenParser(repository.Object);

            var root = parser.BuildHierarchy(actionEvent);

            Assert.AreEqual("动作事件 - 10", root.DisplayName);
            Assert.AreSame(expectedAction, root.Children.Single().Hirc);
        }

        [TestMethod]
        public void BuildHierarchy_MusicTrackNodesKeepTheirOwnSourceIds()
        {
            var segment = new CAkMusicSegment_V136
            {
                Id = 1,
                HircType = AkBkHircType.Music_Segment,
                BnkFilePath = "music.bnk",
                MusicNodeParams =
                {
                    Children = { ChildIds = [2] }
                }
            };
            var track = new CAkMusicTrack_V136
            {
                Id = 2,
                HircType = AkBkHircType.Music_Track,
                BnkFilePath = segment.BnkFilePath,
                SourceList =
                [
                    new AkBankSourceData_V136
                    {
                        AkMediaInformation = { SourceId = 101 }
                    },
                    new AkBankSourceData_V136
                    {
                        AkMediaInformation = { SourceId = 202 }
                    }
                ]
            };
            var repository = new Mock<IAudioRepository>();
            repository.Setup(x => x.GetHircs(track.Id)).Returns([track]);
            var parser = new HircTreeChildrenParser(repository.Object);

            var root = parser.BuildHierarchy(segment);

            Assert.AreEqual("音乐片段", root.DisplayName);
            Assert.AreEqual("音乐轨道 - 101.wem", root.Children[0].DisplayName);
            CollectionAssert.AreEqual(
                new uint?[] { 101, 202 },
                root.Children.Select(node => node.SourceId).ToArray());
        }

        [TestMethod]
        public void BuildHierarchy_AttilaSetStateSupportsV112SwitchContainers()
        {
            var action = new CAkAction_V112
            {
                Id = 1,
                HircType = AkBkHircType.Action,
                ActionType = AkActionType.SetState,
                StateActionParams = new CAkAction_V112.StateActionParams_V112
                {
                    StateGroupId = 5
                }
            };
            var switchContainer = new CAkSwitchCntr_V112
            {
                Id = 2,
                HircType = AkBkHircType.SwitchContainer,
                GroupId = action.StateActionParams.StateGroupId
            };
            var hircs = new Dictionary<uint, List<HircItem>>
            {
                [action.Id] = [action],
                [switchContainer.Id] = [switchContainer]
            };
            var repository = new Mock<IAudioRepository>();
            repository.SetupGet(x => x.HircsById).Returns(hircs);
            repository.Setup(x => x.GetHircs(switchContainer.Id)).Returns([switchContainer]);
            repository
                .Setup(x => x.GetNameFromId(It.IsAny<uint>()))
                .Returns((uint id) => id.ToString());
            var parser = new HircTreeChildrenParser(repository.Object);

            var root = parser.BuildHierarchy(action);

            Assert.AreSame(switchContainer, root.Children.Single().Hirc);
        }

        [TestMethod]
        public void BuildHierarchy_MissingHircIsMarkedAsUnavailable()
        {
            const uint missingActionId = 20;
            var actionEvent = new CAkEvent_V136
            {
                Id = 10,
                HircType = AkBkHircType.Event,
                Actions = [new CAkEvent_V136.Action_V136 { ActionId = missingActionId }]
            };
            var repository = new Mock<IAudioRepository>();
            repository
                .Setup(x => x.GetHircs(missingActionId))
                .Returns([]);
            repository
                .Setup(x => x.GetNameFromId(actionEvent.Id))
                .Returns("event");
            var parser = new HircTreeChildrenParser(repository.Object);

            var root = parser.BuildHierarchy(actionEvent);
            var missingNode = root.Children.Single();

            Assert.IsTrue(missingNode.IsMissingReference);
            Assert.AreEqual(missingActionId, missingNode.MissingHircId);
            StringAssert.Contains(missingNode.DisplayName, "游戏文件中没有对应对象");
        }
    }
}
