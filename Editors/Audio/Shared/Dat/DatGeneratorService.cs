using System.Collections.Generic;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.AudioProject;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Dat;

namespace Editors.Audio.Shared.Dat
{
    public interface IDatGeneratorService
    {
        AudioPackOutput GenerateEventDatFile(
            string audioProjectNameWithoutExtension,
            List<ActionEvent> actionEvents = null,
            List<StateGroup> stateGroups = null);
    }

    public class DatGeneratorService : IDatGeneratorService
    {
        public AudioPackOutput GenerateEventDatFile(
            string audioProjectNameWithoutExtension,
            List<ActionEvent> actionEvents = null,
            List<StateGroup> stateGroups = null)
        {
            var datFile = new SoundDatFile();

            if (actionEvents != null && actionEvents.Count > 0)
            {
                foreach (var actionEvent in actionEvents)
                    datFile.EventWithStateGroup.Add(new SoundDatFile.DatEventWithStateGroup() { Event = actionEvent.Name, Value = 400 });
            }

            if (stateGroups != null && stateGroups.Count > 0)
            {

                foreach (var stateGroup in stateGroups)
                {
                    var states = new List<string>();
                    foreach (var state in stateGroup.States)
                        states.Add(state.Name);

                    datFile.StateGroupsWithStates1.Add(new SoundDatFile.DatStateGroupsWithStates() { StateGroup = stateGroup.Name, States = states });
                }
            }

            var datFileName = $"event_data__{audioProjectNameWithoutExtension}.dat";
            var datFilePath = $"audio\\wwise\\{datFileName}";
            return CreateDatOutput(datFile, datFileName, datFilePath);
        }

        private static AudioPackOutput CreateDatOutput(
            SoundDatFile datFile,
            string datFileName,
            string datFilePath)
        {
            var bytes = DatFileParser.WriteData(datFile);
            var packFile = new PackFile(datFileName, new MemorySource(bytes));
            var reparsedSanityFile = DatFileParser.Parse(packFile, false);
            return new AudioPackOutput(
                datFileName,
                datFilePath,
                packFile.DataSource.ReadData());
        }
    }
}
