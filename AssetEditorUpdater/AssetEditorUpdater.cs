using System.Diagnostics;
using Octokit;
using FileMode = System.IO.FileMode;

namespace AssetEditorUpdater
{
    internal sealed record UpdaterInvocation(
        bool IsInitialLaunch,
        string InstallationDirectory,
        string? UpdateDirectory);

    public class AssetEditorUpdater
    {
        private const string GitHubOwner = "Szy-Cathay";
        private const string GitHubRepository = "TheAssetEditor-CN";
        private const string AssetEditorExe = "AssetEditor.CN.exe";
        private const string AssetEditorUpdaterExe = "AssetEditor.CN.Updater.exe";
         
        public static async Task Main(string[] args)
        {
            var currentDirectory = AppContext.BaseDirectory;
            var invocation = ParseInvocation(currentDirectory, args);

            Console.WriteLine($"国区版更新器运行目录：{currentDirectory}");

            if (invocation.IsInitialLaunch)
            {
                var layout = UpdaterWorkspaceFactory.GetLayout(
                    UpdaterWorkspaceFactory.IsProcessElevated(),
                    Guid.NewGuid());
                var workspace = UpdaterWorkspaceFactory.Create(layout);
                RelaunchFromUpdateDirectory(
                    currentDirectory,
                    workspace,
                    invocation.InstallationDirectory);
            }
            else
            {
                var updateDirectory = invocation.UpdateDirectory!;
                UpdateInstaller.ValidateDirectoryLayout(
                    invocation.InstallationDirectory,
                    updateDirectory,
                    UpdaterWorkspaceFactory.IsProcessElevated());
                await UpdateAsync(invocation.InstallationDirectory, updateDirectory);
            }
        }

        private static void RelaunchFromUpdateDirectory(
            string updaterPayloadDirectory,
            UpdaterWorkspace workspace,
            string installationDirectory)
        {
            RelaunchFromUpdateDirectory(
                updaterPayloadDirectory,
                workspace,
                installationDirectory,
                (sourceDirectory, destinationWorkspace, targetInstallationDirectory) =>
                    CopyUpdaterPayload(
                        sourceDirectory,
                        destinationWorkspace,
                        targetInstallationDirectory),
                (isProtected, updateDirectory) =>
                    UpdaterWorkspaceFactory.ValidateExisting(isProtected, updateDirectory),
                processStartInfo => Process.Start(processStartInfo));
        }

        internal static void RelaunchFromUpdateDirectory(
            string updaterPayloadDirectory,
            UpdaterWorkspace workspace,
            string installationDirectory,
            Func<string, UpdaterWorkspace, string, IReadOnlyDictionary<string, string>> copyAndVerify,
            Func<bool, string, UpdaterWorkspace> validateWorkspace,
            Action<ProcessStartInfo> startProcess)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(copyAndVerify);
            ArgumentNullException.ThrowIfNull(validateWorkspace);
            ArgumentNullException.ThrowIfNull(startProcess);

            Console.WriteLine($"正在将更新器复制到：{workspace.UpdateDirectory}");

            validateWorkspace(workspace.IsProtected, workspace.UpdateDirectory);
            copyAndVerify(updaterPayloadDirectory, workspace, installationDirectory);
            var newUpdaterPath = Path.Combine(workspace.UpdateDirectory, AssetEditorUpdaterExe);
            var processStartInfo = CreateUpdaterProcessStartInfo(
                newUpdaterPath,
                workspace.UpdateDirectory,
                installationDirectory,
                workspace.UpdateDirectory);

            validateWorkspace(workspace.IsProtected, workspace.UpdateDirectory);
            startProcess(processStartInfo);
        }

        internal static IReadOnlyDictionary<string, string> CopyUpdaterPayload(
            string updaterPayloadDirectory,
            UpdaterWorkspace workspace,
            string installationDirectory,
            string? localApplicationDataRoot = null,
            string? commonApplicationDataRoot = null)
        {
            ArgumentNullException.ThrowIfNull(workspace);

            var validatedWorkspace = UpdateInstaller.ValidateDirectoryLayout(
                installationDirectory,
                workspace.UpdateDirectory,
                workspace.IsProtected,
                localApplicationDataRoot,
                commonApplicationDataRoot);
            if (workspace.IsProtected != validatedWorkspace.IsProtected
                || !PathsEqual(workspace.TransactionRoot, validatedWorkspace.TransactionRoot))
            {
                throw new InvalidOperationException(
                    "The updater workspace does not match the validated update directory.");
            }

            UpdaterPayloadCopier.ValidateNonOverlappingRoots(
                updaterPayloadDirectory,
                validatedWorkspace.UpdateDirectory);

            if (!validatedWorkspace.IsProtected)
            {
                ClearLegacyUpdatePayload(validatedWorkspace.UpdateDirectory);
                UpdaterWorkspaceFactory.ValidateExisting(
                    false,
                    validatedWorkspace.UpdateDirectory,
                    localApplicationDataRoot,
                    commonApplicationDataRoot);
            }

            return UpdaterPayloadCopier.CopyAndVerify(
                updaterPayloadDirectory,
                validatedWorkspace.UpdateDirectory);
        }

        internal static UpdaterInvocation ParseInvocation(
            string currentDirectory,
            string[] args)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(currentDirectory);
            ArgumentNullException.ThrowIfNull(args);

            if (args.Length == 0)
            {
                var installationDirectory = Directory.GetParent(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDirectory)))?.FullName
                    ?? throw new InvalidOperationException("Unable to determine the installation directory.");
                return new UpdaterInvocation(true, installationDirectory, null);
            }

            if (args.Length == 2)
                return new UpdaterInvocation(false, args[0], args[1]);

            throw new ArgumentException(
                "The updater accepts either zero arguments or exactly installation and update directories.",
                nameof(args));
        }

        internal static ProcessStartInfo CreateUpdaterProcessStartInfo(
            string updaterPath,
            string workingDirectory,
            string installationDirectory,
            string updateDirectory)
        {
            return new ProcessStartInfo
            {
                FileName = updaterPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                ArgumentList = { installationDirectory, updateDirectory }
            };
        }

        private static async Task UpdateAsync(string installationDirectory, string updateDirectory)
        {
            var latestRelease = await GetLatestReleaseAsync();
            if (latestRelease == null)
                return;

            var installedVersion = GetAssetEditorVersion(installationDirectory);
            var latestVersion = ParseReleaseVersion(latestRelease.TagName);
            if (installedVersion >= latestVersion)
            {
                Console.WriteLine("当前已是最新版本。");
                return;
            }

            Console.WriteLine($"正在将 Asset Editor 国区版从 {installedVersion} 更新到 {latestVersion}。");

            var asset = GetAsset(latestRelease);
            var assetPath = Path.Combine(updateDirectory, asset.Name);
            var downloadResult = await DownloadAssetAsync(asset.BrowserDownloadUrl, assetPath);
            if (downloadResult == false)
                return;

            UpdateInstaller.Install(assetPath, installationDirectory, updateDirectory);

            var assetEditorPath = Path.Combine(installationDirectory, AssetEditorExe);
            if (File.Exists(assetEditorPath))
                LaunchAssetEditor(installationDirectory, assetEditorPath);
            else
                Console.WriteLine("更新失败：未找到 AssetEditor.CN.exe。");

            Console.WriteLine("按任意键关闭。");
            Console.ReadKey();
        }

        private static async Task<Release?> GetLatestReleaseAsync()
        {
            try
            {
                var gitHubClient = new GitHubClient(new ProductHeaderValue("AssetEditor.CN"));
                var releases = await gitHubClient.Repository.Release.GetAll(GitHubOwner, GitHubRepository);
                return releases.Count > 0 ? releases[0] : null;
            }
            catch (ApiException exception)
            {
                Console.WriteLine($"无法从 GitHub 获取最新版本：{exception.Message}");
                return null;
            }
        }

        private static Version GetAssetEditorVersion(string installationDirectory)
        {
            var assetEditorPath = Path.Combine(installationDirectory, AssetEditorExe);
            var versionInfo = FileVersionInfo.GetVersionInfo(assetEditorPath);
            if (string.IsNullOrWhiteSpace(versionInfo.FileVersion))
                return new Version();

            var parsedVersion = new Version(versionInfo.FileVersion);
            if (parsedVersion.Revision <= 0)
                return new Version(parsedVersion.Major, parsedVersion.Minor, Math.Max(parsedVersion.Build, 0));

            return parsedVersion;
        }

        private static Version ParseReleaseVersion(string tagName)
        {
            var cleanedVersion = tagName.Trim().TrimStart('v', 'V');
            return new Version(cleanedVersion);
        }

        private static ReleaseAsset GetAsset(Release latestRelease)
        {
            if (latestRelease.Assets.Count != 1)
                throw new InvalidOperationException($"Expected 1 asset, found {latestRelease.Assets.Count}.");

            var asset = latestRelease.Assets[0];
            var extension = Path.GetExtension(asset.Name);
            if (!IsZipAssetName(asset.Name))
                throw new InvalidOperationException($"Asset has extension {extension}, expected .zip.");

            return asset;
        }

        internal static bool IsZipAssetName(string assetName)
        {
            return string.Equals(Path.GetExtension(assetName), ".zip", StringComparison.OrdinalIgnoreCase);
        }

        internal static string GetUpdateDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AssetEditor.CN",
                "Temp",
                "Update");
        }

        internal static string GetUpdateBackupRootDirectory()
        {
            return UpdateInstaller.GetBackupRootDirectory(GetUpdateDirectory());
        }

        private static async Task<bool> DownloadAssetAsync(string downloadUrl, string downloadPath)
        {
            Console.WriteLine("正在下载最新版本……");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AssetEditor.CN");

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var responseStream = await response.Content.ReadAsStreamAsync();
                await using var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await responseStream.CopyToAsync(fileStream);
                
                return true;
            }
            catch
            {
                Console.WriteLine("无法从 GitHub 下载最新版本。");
                return false;
            }
        }

        private static void LaunchAssetEditor(string installationDirectory, string assetEditorPath)
        {
            Console.WriteLine("更新完成。");
            Console.WriteLine("正在重新启动 Asset Editor 国区版……");

            // We launch using explorer.exe as that ensures admin priveliges aren't used as they
            // would otherwise automatically be used if the AssetEditor.CN.Updater.exe required them.
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                WorkingDirectory = installationDirectory,
                UseShellExecute = true,
                Arguments = $"\"{assetEditorPath}\"".Trim()
            };
            Process.Start(processStartInfo);
        }

        private static void ClearLegacyUpdatePayload(string updateDirectory)
        {
            foreach (var entry in new DirectoryInfo(updateDirectory).EnumerateFileSystemInfos())
            {
                entry.Refresh();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"Legacy updater payload entry cannot be a reparse point: {entry.FullName}");
                }

                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    ClearLegacyUpdatePayload(entry.FullName);
                    Directory.Delete(entry.FullName);
                }
                else
                {
                    File.Delete(entry.FullName);
                }
            }

            if (Directory.EnumerateFileSystemEntries(updateDirectory).Any())
                throw new InvalidDataException("Legacy updater payload changed while it was being cleared.");
        }

        private static bool PathsEqual(string firstPath, string secondPath)
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
