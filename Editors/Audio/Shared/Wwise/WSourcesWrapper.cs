using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.Settings;


namespace Editors.Audio.Shared.Wwise
{
    public class WSourcesWrapper
    {
        private readonly ApplicationSettingsService _applicationSettingsService;

        private readonly ILogger _logger = Logging.Create<WSourcesWrapper>();

        public WSourcesWrapper(ApplicationSettingsService applicationSettingsService)
        {
            _applicationSettingsService = applicationSettingsService;
        }

        public void CreateWsourcesFile(
            List<string> wavFilesNames,
            string audioFolderPath,
            string wsourcesPath)
        {
            var sources = from wavFileName in wavFilesNames 
                          select new XElement("Source", new XAttribute("Path", wavFileName), new XAttribute("Conversion", "Vorbis Quality High"));

            var document = new XDocument(
                new XDeclaration("1.0", "UTF-8", null), 
                new XElement("ExternalSourcesList", 
                new XAttribute("SchemaVersion", "1"), 
                new XAttribute("Root", audioFolderPath),
                sources));

            document.Save(wsourcesPath);

            _logger.Here().Information(
                $"Saved WSources file to {wsourcesPath}");
        }

        public async Task RunExternalCommandAsync(
            string arguments,
            CancellationToken cancellationToken = default)
        {
            _logger.Here().Information($"Running WwiseCLI.exe with arguments {arguments}");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wwiseCliPath =
                    _applicationSettingsService.CurrentSettings.WwisePath;
                if (string.IsNullOrWhiteSpace(wwiseCliPath) ||
                    !File.Exists(wwiseCliPath))
                {
                    throw new FileNotFoundException(
                        "WwiseCLI.exe was not found. Configure a valid Wwise path in Settings.",
                        wwiseCliPath);
                }

                var startInfo = new ProcessStartInfo()
                {
                    FileName = wwiseCliPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = Process.Start(startInfo) ??
                    throw new InvalidOperationException("WwiseCLI.exe failed to start.");
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);

                        using var cleanupCancellation =
                            new CancellationTokenSource(
                                TimeSpan.FromSeconds(2));
                        await process.WaitForExitAsync(
                            cleanupCancellation.Token);
                        await Task.WhenAll(outputTask, errorTask);
                    }
                    catch
                    {
                        // Preserve the original cancellation during best-effort cleanup.
                    }

                    throw;
                }

                var output = await outputTask;
                var error = await errorTask;

                _logger.Here().Information($"{output}");
                if (!string.IsNullOrEmpty(error))
                    _logger.Here().Error($"{error}");

                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"WwiseCLI.exe exited with code {process.ExitCode}.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Error running command: {e.Message}", e);
            }
        }

        public void DeleteExcessStuff(string audioFolderPath)
        {
            var excessFilesFolderPath = Path.Combine(
                audioFolderPath,
                "Windows");
            if (Directory.Exists(excessFilesFolderPath))
            {
                var wemFiles = Directory.GetFiles(
                    excessFilesFolderPath,
                    "*.wem");
                foreach (var file in wemFiles)
                {
                    var fileName = Path.GetFileName(file);
                    var fileDestination = Path.Combine(
                        audioFolderPath,
                        fileName);

                    if (!File.Exists(fileDestination))
                        File.Move(file, fileDestination);
                }

                var remainingFiles =
                    Directory.GetFiles(excessFilesFolderPath);
                foreach (var file in remainingFiles)
                    File.Delete(file);

                try
                {
                    Directory.Delete(excessFilesFolderPath, true);
                }
                catch (IOException e)
                {
                    throw new InvalidOperationException($"The directory could not be deleted or another error occurred: {e.Message}");
                }
            }
            else
            {
                _logger.Here().Information(
                    $"The directory {excessFilesFolderPath} does not exist.");
            }
        }

        public void InitialiseWwiseProject(
            string wavToWemFolderPath)
        {
            var assemblyName = "Shared.EmbeddedResources";
            var assembly = Assembly.Load(assemblyName);
            var resourceRootNamespace = $"{assemblyName}.Resources.AudioConversion";
            var wavToWemWwiseProjectPath = Path.Combine(
                wavToWemFolderPath,
                "WavToWemWwiseProject");

            if (Directory.Exists(wavToWemWwiseProjectPath))
                return;

            // TODO: Update this for game abstraction, currently it's set to the WH3 Wwise Project resource but for future games will need abstraction.
            var resourceFolderPath = $"{resourceRootNamespace}.Wh3WavToWemWwiseProject.Wh3WavToWemWwiseProject.zip";
            DirectoryHelper.EnsureCreated(wavToWemFolderPath);
            var tempZipPath = Path.Combine(
                wavToWemFolderPath,
                $"Wh3WavToWemWwiseProject-{Guid.NewGuid():N}.zip");

            using (var resourceStream = assembly.GetManifestResourceStream(resourceFolderPath))
            {
                if (resourceStream == null)
                    throw new InvalidOperationException($"Failed to retrieve resource stream for {resourceFolderPath}.");

                using var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write);
                resourceStream.CopyTo(fileStream);
            }

            ZipFile.ExtractToDirectory(
                tempZipPath,
                wavToWemFolderPath,
                overwriteFiles: true);
            File.Delete(tempZipPath);
        }
    }
}
