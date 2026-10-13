using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Editors.Audio.Shared.Storage;
using Shared.GameFormats.Wwise.Enums;
using Shared.GameFormats.Wwise.Hirc;
using Shared.Ui.Common;

namespace Editors.Audio.AudioExplorer
{
    public class ExplorerListSelectionFilter
    {
        private readonly IAudioRepository _audioRepository;

        public FilterCollection<ExplorerListItem> ExplorerList { get; set; }

        public ExplorerListSelectionFilter(IAudioRepository audioRepository)
        {
            _audioRepository = audioRepository;

            ExplorerList = new FilterCollection<ExplorerListItem>([])
            {
                SearchFilter = (value, regex) => regex.Match(value.DisplayName).Success || regex.Match(value.Id.ToString()).Success
            };
        }

        public List<ExplorerListItem> CreateValues(bool searchByActionEvent, bool searchByDialogueEvent, bool searchByHircId, bool searchByVOActor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectedList = new List<ExplorerListItem>();

            if (searchByHircId)
            {
                var hircs = _audioRepository.HircsById.SelectMany(x => x.Value).ToList();

                selectedList = hircs
                    .Select(hirc => new ExplorerListItem
                    {
                        HircItem = hirc,
                        DisplayName = hirc.Id.ToString(),
                        Id = hirc.Id,
                        PackFile = hirc.BnkFilePath,
                        IndexInFile = hirc.ByteIndexInFile
                    })
                    .OrderBy(x => x.Id)
                    .ToList();
            }
            else if (searchByActionEvent || searchByDialogueEvent)
            {
                var hircs = new List<HircItem>();
                if (searchByActionEvent)
                    hircs = _audioRepository.GetHircsByHircType(AkBkHircType.Event);
                else if (searchByDialogueEvent)
                    hircs = _audioRepository.GetHircsByHircType(AkBkHircType.Dialogue_Event);

                selectedList = hircs
                    .Select(hirc => new ExplorerListItem
                    {
                        HircItem = hirc,
                        DisplayName = $"{_audioRepository.GetNameFromId(hirc.Id)} ({Path.GetFileName(hirc.BnkFilePath)})",
                        Id = hirc.Id,
                        PackFile = hirc.BnkFilePath,
                        IndexInFile = hirc.ByteIndexInFile
                    })
                    .OrderBy(hirc => hirc.DisplayName)
                    .ToList();
            }
            else if (searchByVOActor)
            {
                if (!_audioRepository.StatesByStateGroup.TryGetValue("VO_Actor", out var states))
                    states = [];

                selectedList = states
                    .Select(state => new ExplorerListItem
                    {
                        DisplayName = state,
                    })
                    .OrderBy(x => x.DisplayName)
                    .ToList();
            }
            else
            {
                return [];
            }

            cancellationToken.ThrowIfCancellationRequested();
            return selectedList;
        }
    }
}
