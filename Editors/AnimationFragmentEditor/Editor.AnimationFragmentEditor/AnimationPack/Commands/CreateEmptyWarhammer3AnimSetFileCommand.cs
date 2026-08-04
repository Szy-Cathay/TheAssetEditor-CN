using CommonControls.Editors.AnimationPack;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using static Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3.AnimationBinEntry;
using AnimationBinEntry = Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3.AnimationBinEntry;

namespace Editors.AnimationFragmentEditor.AnimationPack.Commands
{
    public class CreateEmptyWarhammer3AnimSetFileCommand : IUiCommand
    {
        private readonly IStandardDialogs _standardDialogs;

        public CreateEmptyWarhammer3AnimSetFileCommand(IStandardDialogs standardDialogs)
        {
            _standardDialogs = standardDialogs;
        }

        public void Execute(AnimPackViewModel editor)
        {
            var fileName = GetAnimSetFileName();
            if (fileName == null)
                return;

            var animSet = CreateExampleWarhammer3AnimSet(fileName);
            editor.AnimationPackItems.PossibleValues.Add(animSet);
            editor.AnimationPackItems.UpdatePossibleValues(editor.AnimationPackItems.PossibleValues);
            editor.HasUnsavedChanges = true;
        }

        protected virtual string? GetAnimSetFileName()
        {
            var input = _standardDialogs.ShowTextInputDialog("Fragment name", "");
            if (input.Result)
            {
                var filename = SaveUtility.EnsureEnding(input.Text, ".frg");
                return filename;
            }

            return null;
        }

        static IAnimationPackFile CreateExampleWarhammer3AnimSet(string binName)
        {
            var filename = SaveUtility.EnsureEnding(binName, ".bin");
            var filePath = @"animations/database/battle/bin/" + filename;
            var outputFile = new AnimationBinWh3(filePath)
            {
                TableVersion = 4,
                TableSubVersion = 3,
                Name = binName,
                Unknown = "",
                MountBin = "",
                SkeletonName = "humanoid01",
                LocomotionGraph = "animations/locomotion_graphs/entity_locomotion_graph.xml",
                UnknownValue1 = 0,
            };

            outputFile.AnimationTableEntries.Add(new AnimationBinEntry()
            {
                AnimationId = 1, // STAND
                BlendIn = 0.5f,
                SelectionWeight = 1,
                WeaponBools = 1,
                Unk = false,

                AnimationRefs = new List<AnimationRef>()
                {
                    new AnimationRef()
                    {
                        AnimationFile = @"animations/battle/humanoid01/sword_and_shield/stand/hu1_sws_stand_01.anim",
                        AnimationMetaFile = @"",
                        AnimationSoundMetaFile = @"" ,
                    },
                }
            });

            outputFile.AnimationTableEntries.Add(new AnimationBinEntry()
            {
                AnimationId = 453,// Attack_1
                BlendIn = 0.3f,
                SelectionWeight = 1,
                WeaponBools = 1,
                Unk = false,

                AnimationRefs = new List<AnimationRef>()
                {
                    new AnimationRef()
                    {
                        AnimationFile = @"animations/battle/humanoid01/sword_and_shield/attacks/hu1_sws_attack_01.anim",
                        AnimationMetaFile = @"animations/battle/humanoid01/sword_and_shield/attacks/hu1_sws_attack_01.meta",
                        AnimationSoundMetaFile = @"animations/audio/battle/humanoid01/sword_and_shield/attacks/hu1_sws_attack_01.{27tsfy}.snd.meta",
                    },
                    new AnimationRef()
                    {
                          AnimationFile = @"animations/battle/humanoid01/sword_and_shield/attacks/hu1_sws_attack_02.anim",
                        AnimationMetaFile = @"animations/battle/humanoid01/sword_and_shield/attacks/hu1_sws_attack_02.anm.meta",
                        AnimationSoundMetaFile = @"animations/audio/battle/humanoid01/sword_and_shield/attacks/hu1_sws_attack_02.{1o2asvr}.snd.meta",
                    }
                }
            });

            return outputFile;
        }

    }

}
