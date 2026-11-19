using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.GameFormats.Dat;

namespace Editors.Reports.Audio
{
    public class GenerateDatDumperReportCommand(DatDumper generator) : IUiCommand
    {
        public void Execute() => generator.Create();
    }

    public class DatDumper
    {
        private readonly IPackFileService _packFileService;
        private readonly ApplicationSettingsService _applicationSettingsService;

        public DatDumper(IPackFileService packFileService, ApplicationSettingsService applicationSettingsService)
        {
            _packFileService = packFileService;
            _applicationSettingsService = applicationSettingsService;
        }

        public static void Generate(IPackFileService packFileService, ApplicationSettingsService applicationSettingsService)
        {
            var instance = new DatDumper(packFileService, applicationSettingsService);
            instance.Create();
        }

        public void Create(ReportExecutionContext? context = null)
        {
            var datDumper = new DatDumper(_packFileService, _applicationSettingsService);
            datDumper.LoadDatFiles(_packFileService, _applicationSettingsService, out var _, context);
        }

        private void LoadDatFiles(IPackFileService packFileService, ApplicationSettingsService applicationSettingsService, out List<string> failedFiles, ReportExecutionContext? context)
        {
            var reportsFolderName = context?.OutputDirectory ?? $"{DirectoryHelper.ReportsDirectory}\\";
            DirectoryHelper.EnsureCreated(reportsFolderName);

            var datFiles = context?.Files(".dat") ?? PackFileServiceUtility.FindAllWithExtention(packFileService, ".dat");

            var failedDatParsing = new List<(string, string)>();
            var masterDat = new SoundDatFile();

            foreach (var datFile in datFiles)
            {
                context?.Check();
                var datDump = context == null ? $"{reportsFolderName}\\dat_dump_{datFile}.txt" : Path.Combine(reportsFolderName, context.PathOf(datFile) + ".txt");
                try
                {
                    var parsedFile = LoadDatFile(datFile, context?.Game);
                    masterDat.Merge(parsedFile);
                    ReportExecutionContext.WriteText(context, datDump, parsedFile.Format());
                }
                catch (Exception e)
                {
                    var fullPath = context?.PathOf(datFile) ?? packFileService.GetFullPath(datFile);
                    failedDatParsing.Add((fullPath, e.Message));
                    context?.Error(fullPath, e);
                }
            }

            failedFiles = failedDatParsing.Select(x => x.Item1).ToList();

            var masterDatDump = $"{reportsFolderName}\\dat_dump_master.txt";
            ReportExecutionContext.WriteText(context, masterDatDump, masterDat.Format());
        }

        private SoundDatFile LoadDatFile(PackFile datFile, GameTypeEnum? game)
        {
            if ((game ?? _applicationSettingsService.CurrentSettings.CurrentGame) == GameTypeEnum.Attila)
                return DatFileParser.Parse(datFile, true);
            else
                return DatFileParser.Parse(datFile, false);
        }
    }
}
