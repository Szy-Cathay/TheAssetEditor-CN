using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;

namespace Editors.Audio.Shared.Utilities
{
    public class VgStreamWrapper
    {
        readonly ILogger _logger = Logging.Create<VgStreamWrapper>();

        private static string VgStreamFolder => $"{DirectoryHelper.Temp}\\VgStream";
        private static string AudioFolder => $"{DirectoryHelper.Temp}\\Audio";
        static void EnsureCreated() => GetCliPath();

        public VgStreamWrapper()
        {
            try
            {
                EnsureCreated();
            }
            catch (Exception e)
            {
                _logger.Here().Error($"Unable to create VgStreamWrapper: {e.Message}");
            }
        }

        public virtual Result<string> ConvertFileUsingVgStream(string sourceFileName, string targetFileName)
        {
            return ConvertFileUsingVgStream(
                sourceFileName,
                targetFileName,
                CancellationToken.None);
        }

        public virtual Result<string> ConvertFileUsingVgStream(
            string sourceFileName,
            string targetFileName,
            CancellationToken cancellationToken)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var cliPath = GetCliPath();
                _logger.Here().Information($"VgSteam path is '{cliPath}'");
                _logger.Here().Information($"Trying to convert {sourceFileName} to {targetFileName}");
                if (File.Exists(targetFileName))
                    File.Delete(targetFileName);

                var arguments = $"-o \"{targetFileName}\" \"{sourceFileName}\"";
                _logger.Here().Information($"{cliPath} {arguments}");

                using var process = new System.Diagnostics.Process();
                process.StartInfo.FileName = cliPath;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                process.StartInfo.CreateNoWindow = true;
                process.Start();
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                try
                {
                    process.WaitForExitAsync(cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
                catch (OperationCanceledException)
                {
                    TryTerminate(process);
                    TryDelete(targetFileName);
                    throw;
                }

                var output = outputTask.GetAwaiter().GetResult();
                var error = errorTask.GetAwaiter().GetResult();
                _logger.Here().Information(output);

                var outputSoundFilePath = targetFileName;
                var isValidOutput = process.ExitCode == 0 &&
                    File.Exists(outputSoundFilePath) &&
                    new FileInfo(outputSoundFilePath).Length > 0;
                _logger.Here().Information($"File readback result for converted file {outputSoundFilePath} is : {isValidOutput}");
                if (!isValidOutput)
                {
                    if (File.Exists(outputSoundFilePath))
                        File.Delete(outputSoundFilePath);

                    return Result<string>.FromError(
                        "VgSteam",
                        $"Failed to convert file. Exit code: {process.ExitCode}. {error}".Trim());
                }
                return Result<string>.FromOk(outputSoundFilePath);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                _logger.Here().Error(e.Message);
                return Result<string>.FromError("Convert error", e.Message);
            }
        }

        private static void TryTerminate(System.Diagnostics.Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
            }
            catch
            {
                // The process may have exited between the checks.
            }
        }

        private static void TryDelete(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);
            }
            catch
            {
                // Cleanup failure must not hide the cancellation request.
            }
        }

        private static string GetCliPath()
        {
            DirectoryHelper.EnsureCreated(VgStreamFolder);
            DirectoryHelper.EnsureCreated(AudioFolder);

            var vgStreamCli = Path.Combine(VgStreamFolder, "vgstream.exe");
            if (File.Exists(vgStreamCli))
                return vgStreamCli;

            var assemblyName = "Shared.EmbeddedResources";
            var assembly = Assembly.Load(assemblyName);
            var resourceRootNamespace = $"{assemblyName}.Resources.AudioConversion.vgstream";
            var vgStreamFiles = assembly
                .GetManifestResourceNames()
                .Where(r => r.StartsWith(resourceRootNamespace, StringComparison.InvariantCultureIgnoreCase)).ToArray();

            foreach (var file in vgStreamFiles)
            {
                var fileName = file.Substring(resourceRootNamespace.Length + 1);
                var outputFileName = $"{VgStreamFolder}\\{fileName}";

                using var resourceStream = assembly.GetManifestResourceStream(file);
                if (resourceStream == null)
                    throw new Exception($"Failed to retrieve resource stream for {file}");

                using var fileStream = new FileStream(outputFileName, FileMode.OpenOrCreate);
                resourceStream.CopyTo(fileStream);
            }

            return vgStreamCli;
        }
    }
}
