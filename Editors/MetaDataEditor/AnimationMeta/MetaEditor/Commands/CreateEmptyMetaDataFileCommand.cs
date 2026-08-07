using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.MetaEditor.Commands
{
    internal class CreateEmptyMetaDataFileCommand : IUiCommand
    {
        private readonly IEventHub _eventHub;
        private readonly IFileSaveService _fileSaveService;
        private readonly MetaDataFileParser _metaDataFileParser;

        public CreateEmptyMetaDataFileCommand(
            IEventHub eventHub,
            IFileSaveService fileSaveService,
            MetaDataFileParser metaDataFileParser)
        {
            _eventHub = eventHub;
            _fileSaveService = fileSaveService;
            _metaDataFileParser = metaDataFileParser;
        }

        public bool Execute(MetaDataEditorViewModel controller, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            var metadata = new ParsedMetadataFile { Version = 2 };
            var bytes = _metaDataFileParser.GenerateBytes(metadata.Version, metadata);
            var createdFile = _fileSaveService.Save(path, bytes, false);
            if (createdFile == null)
                return false;

            controller.LoadFile(createdFile);
            _eventHub.Publish(new ScopedFileSavedEvent
            {
                FileOwner = controller,
                NewPath = path,
            });
            return true;
        }
    }
}
