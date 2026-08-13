using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Visualisation
{
    internal sealed class MetaDataResourceResolver
    {
        private readonly ILogger _logger =
            Logging.Create<MetaDataResourceResolver>();
        private readonly IPackFileService _packFileService;

        public MetaDataResourceResolver(IPackFileService packFileService)
        {
            _packFileService = packFileService;
        }

        public PackFile? FindModel(
            ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner,
            string path,
            ICollection<MetaDataBuildDiagnostic> diagnostics) =>
            FindRequired(
                source,
                owner,
                path,
                "SuperView.Diagnostics.MissingModel",
                diagnostics);

        public PackFile? FindAnimation(
            ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner,
            string path,
            ICollection<MetaDataBuildDiagnostic> diagnostics) =>
            FindRequired(
                source,
                owner,
                path,
                "SuperView.Diagnostics.MissingAnimation",
                diagnostics);

        public void CheckEffect(
            ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner,
            string effectName,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            var path = string.IsNullOrWhiteSpace(effectName)
                ? ""
                : $@"vfx\{effectName}.xml";
            FindRequired(
                source,
                owner,
                path,
                "SuperView.Diagnostics.MissingEffect",
                diagnostics);
        }

        private PackFile? FindRequired(
            ParsedMetadataAttribute source,
            MetaDataDocumentOwner owner,
            string path,
            string reasonKey,
            ICollection<MetaDataBuildDiagnostic> diagnostics)
        {
            PackFile? resource = null;
            try
            {
                resource = string.IsNullOrWhiteSpace(path)
                    ? null
                    : _packFileService.FindFile(path);
            }
            catch
            {
                // Resource lookup failures are isolated to the current META.
            }
            if (resource == null)
            {
                _logger.Here().Warning(
                    $"Metadata preview resource was not found: '{path}'.");
                diagnostics.Add(MetaDataDiagnosticFactory.Create(
                    source,
                    owner,
                    reasonKey,
                    path));
            }

            return resource;
        }
    }
}
