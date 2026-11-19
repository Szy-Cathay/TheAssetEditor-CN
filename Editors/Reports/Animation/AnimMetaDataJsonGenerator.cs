using System.Diagnostics;
using System.Windows;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Newtonsoft.Json;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Editors.Reports.Animation
{
    public class GenerateMetaJsonDataReportCommand(AnimMetaDataJsonGenerator generator) : IUiCommand
    {
        public void Execute() => generator.Create();
    }

    public class AnimMetaDataJsonGenerator
    {
        private readonly ILogger _logger = Logging.Create<AnimMetaDataJsonGenerator>();
        private readonly IPackFileService _pfs;
        private readonly ApplicationSettingsService _settingsService;

        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly JsonSerializerSettings _jsonOptions;

        public AnimMetaDataJsonGenerator(IPackFileService pfs, ApplicationSettingsService settingsService, MetaDataFileParser metaDataFileParser)
        {
            _pfs = pfs;
            _settingsService = settingsService;
            _metaDataFileParser = metaDataFileParser;
            _jsonOptions = new JsonSerializerSettings { Formatting = Formatting.Indented };
        }

        public static void Generate(IPackFileService pfs, ApplicationSettingsService settingsService,MetaDataFileParser metaDataFileParser)
        {
            var instance = new AnimMetaDataJsonGenerator(pfs, settingsService, metaDataFileParser);
            instance.Create();
        }

        void DumpAsJson(string gameOutputDir, string fileName, object data, ReportExecutionContext? context)
        {
            var jsonString = JsonConvert.SerializeObject(data, _jsonOptions);
            var jsonFilePath = Path.Join(gameOutputDir, fileName);
            if (context == null) Directory.CreateDirectory(Path.GetDirectoryName(jsonFilePath));
            ReportExecutionContext.WriteText(context, jsonFilePath, jsonString);
        }

        public void Create(ReportExecutionContext? context = null)
        {
            var gameName = GameInformationDatabase.GetGameById(_settingsService.CurrentSettings.CurrentGame).DisplayName;
            var timeStamp = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            var gameOutputDir = context?.OutputDirectory ?? $"{DirectoryHelper.ReportsDirectory}\\MetaDataJsons\\{gameName}_{timeStamp}\\";
            if (context == null && Directory.Exists(gameOutputDir))
                Directory.Delete(gameOutputDir, true);
            DirectoryHelper.EnsureCreated(gameOutputDir);

            //dump animationTable
            var packFileContainer = _pfs.GetAllPackfileContainers();
            var packs = context?.Files(".animpack") ?? [packFileContainer[0].FileList["animations\\database\\battle\\bin\\animation_tables.animpack"]];
            foreach (var animPack in packs)
            {
                context?.Check();
                var animPackFile = AnimationPackSerializer.Load(animPack, _pfs, sourcePath: context?.PathOf(animPack));
                var converter = new AnimationBinWh3FileToXmlConverter(null, _metaDataFileParser, null);
                foreach (var animFile in animPackFile.Files)
                {
                    context?.Check();
                    if (animFile is AnimationBinWh3)
                    {
                        var text = converter.GetText(animFile.ToByteArray());
                        var xmlFilePath = Path.Join(gameOutputDir, animFile.FileName + ".xml");
                        if (context == null) Directory.CreateDirectory(Path.GetDirectoryName(xmlFilePath));
                        ReportExecutionContext.WriteText(context, xmlFilePath, text);
                    }
                    else context?.Diagnostics.Add(animFile.FileName + ": " + global::Shared.Core.ModAssistant.AssistantJson.Text("ReportWh3TablesOnly"));
                }
            }
            var allMeta = context?.WithPaths(".meta") ?? PackFileServiceUtility.FindAllWithExtentionIncludePaths(_pfs, ".meta");
            foreach (var (fileName, packFile) in allMeta)
            {
                try
                {
                    context?.Check();
                    var data = packFile.DataSource.ReadData();
                    if (data.Length == 0)
                        continue;

                    var metaData = _metaDataFileParser.ParseFile(data);
                    DumpAsJson(gameOutputDir, fileName + ".json", metaData, context);
                }
                catch (Exception e)
                {
                    context?.Error(fileName, e);
                    _logger.Here().Information($"Meta parsing failed {fileName} - {e.Message}");
                }
            }

            var allAnimations = context?.WithPaths(".anim") ?? PackFileServiceUtility.FindAllWithExtentionIncludePaths(_pfs, ".anim");
            foreach (var (fileName, packFile) in allAnimations)
            {
                try
                {
                    context?.Check();
                    var animationHeader = AnimationFile.GetAnimationHeader(packFile);
                    DumpAsJson(gameOutputDir, fileName + ".header.json", animationHeader, context);
                }
                catch (Exception e)
                {
                    context?.Error(fileName, e);
                    _logger.Here().Information($"Animation parsing failed {fileName} - {e.Message}");
                }
            }

            if (context == null)
            {
                MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.ReportCreated", gameOutputDir));
                Process.Start("explorer.exe", gameOutputDir);
            }
        }
    }
}
