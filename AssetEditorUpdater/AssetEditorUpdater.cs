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
                var workspace = UpdateInstaller.ValidateDirectoryLayout(
                    invocation.InstallationDirectory,
                    updateDirectory,
                    UpdaterWorkspaceFactory.IsProcessElevated());
                await UpdateAsync(
                    invocation.InstallationDirectory,
                    workspace);
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
                AcquireRelaunchWorkspaceLease,
                processStartInfo => Process.Start(processStartInfo),
                workspaceToClean =>
                    UpdaterWorkspaceFactory.CleanupFreshProtectedTransaction(
                        workspaceToClean));
        }

        internal static void RelaunchFromUpdateDirectory(
            string updaterPayloadDirectory,
            UpdaterWorkspace workspace,
            string installationDirectory,
            Func<string, UpdaterWorkspace, string, IReadOnlyDictionary<string, string>> copyAndVerify,
            Func<bool, string, UpdaterWorkspace> validateWorkspace,
            Func<UpdaterWorkspace, IDisposable?> acquireLaunchLease,
            Action<ProcessStartInfo> startProcess,
            Action<UpdaterWorkspace> cleanupFreshProtectedTransaction)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            ArgumentNullException.ThrowIfNull(copyAndVerify);
            ArgumentNullException.ThrowIfNull(validateWorkspace);
            ArgumentNullException.ThrowIfNull(acquireLaunchLease);
            ArgumentNullException.ThrowIfNull(startProcess);
            ArgumentNullException.ThrowIfNull(cleanupFreshProtectedTransaction);

            Console.WriteLine($"正在将更新器复制到：{workspace.UpdateDirectory}");

            try
            {
                validateWorkspace(
                    workspace.IsProtected,
                    workspace.UpdateDirectory);
                copyAndVerify(
                    updaterPayloadDirectory,
                    workspace,
                    installationDirectory);
                var newUpdaterPath = Path.Combine(
                    workspace.UpdateDirectory,
                    AssetEditorUpdaterExe);
                var processStartInfo = CreateUpdaterProcessStartInfo(
                    newUpdaterPath,
                    workspace.UpdateDirectory,
                    installationDirectory,
                    workspace.UpdateDirectory);

                validateWorkspace(
                    workspace.IsProtected,
                    workspace.UpdateDirectory);
                using var launchLease = acquireLaunchLease(workspace);
                startProcess(processStartInfo);
            }
            catch
            {
                if (workspace.IsProtected)
                {
                    try
                    {
                        cleanupFreshProtectedTransaction(workspace);
                    }
                    catch (Exception cleanupException)
                    {
                        Console.Error.WriteLine(
                            $"Updater cleanup failed; preserving the original failure: {cleanupException}");
                    }
                }

                throw;
            }
        }

        private static IDisposable AcquireRelaunchWorkspaceLease(
            UpdaterWorkspace workspace)
        {
            WindowsDirectoryIdentityLease? transactionIdentity = null;
            WindowsDirectoryIdentityLease? updateIdentity = null;
            try
            {
                transactionIdentity = WindowsPathIdentity.OpenExistingDirectory(
                    workspace.TransactionRoot,
                    nameof(workspace),
                    WindowsDirectoryLeaseMode.Pinned);
                updateIdentity = WindowsPathIdentity.OpenExistingDirectory(
                    workspace.UpdateDirectory,
                    nameof(workspace),
                    WindowsDirectoryLeaseMode.Pinned);
                WindowsPathIdentity.RequireDirectChild(
                    transactionIdentity,
                    updateIdentity,
                    "Update must be a direct child of the transaction root.");
                return new RelaunchWorkspaceLease(
                    transactionIdentity,
                    updateIdentity);
            }
            catch
            {
                updateIdentity?.Dispose();
                transactionIdentity?.Dispose();
                throw;
            }
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

            using var installationIdentity = WindowsPathIdentity.OpenExistingDirectory(
                installationDirectory,
                nameof(installationDirectory));
            using var transactionIdentity = WindowsPathIdentity.OpenExistingDirectory(
                validatedWorkspace.TransactionRoot,
                nameof(validatedWorkspace.TransactionRoot));
            WindowsPathIdentity.RequireDisjoint(
                installationIdentity,
                transactionIdentity,
                "The updater transaction root must not overlap the installation directory.");

            using var sourceIdentity = WindowsPathIdentity.OpenExistingDirectory(
                updaterPayloadDirectory,
                nameof(updaterPayloadDirectory));
            using var destinationIdentity = WindowsPathIdentity.OpenExistingDirectory(
                validatedWorkspace.UpdateDirectory,
                nameof(validatedWorkspace.UpdateDirectory));
            WindowsPathIdentity.RequireDisjoint(
                sourceIdentity,
                destinationIdentity,
                "The updater payload source and destination must not overlap.");

            // Root leases stay open through cleanup and copy. Same-integrity child
            // mutation still requires the DFS rechecks below and remains out of scope.

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

        private static async Task UpdateAsync(
            string installationDirectory,
            UpdaterWorkspace workspace)
        {
            ArgumentNullException.ThrowIfNull(workspace);
            workspace = UpdateInstaller.ValidateWorkspace(
                installationDirectory,
                workspace);
            var updateDirectory = workspace.UpdateDirectory;
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
            workspace = UpdateInstaller.ValidateWorkspace(
                installationDirectory,
                workspace);
            updateDirectory = workspace.UpdateDirectory;
            var assetPath = GetAssetDownloadPath(updateDirectory, asset.Name);
            var downloadResult = await DownloadAssetAsync(
                asset.BrowserDownloadUrl,
                assetPath,
                installationDirectory,
                workspace);
            if (downloadResult == false)
                return;

            UpdateInstaller.Install(assetPath, installationDirectory, workspace);

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

        internal static string GetAssetDownloadPath(
            string updateDirectory,
            string assetName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(updateDirectory);

            if (string.IsNullOrWhiteSpace(assetName)
                || Path.IsPathRooted(assetName)
                || Path.IsPathFullyQualified(assetName)
                || assetName is "." or ".."
                || assetName.Contains(Path.DirectorySeparatorChar)
                || assetName.Contains(Path.AltDirectorySeparatorChar)
                || assetName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || assetName.EndsWith('.')
                || assetName.EndsWith(' ')
                || !IsZipAssetName(assetName))
            {
                throw new InvalidDataException(
                    $"The release asset name is not a safe .zip leaf: {assetName}");
            }

            var fullUpdateDirectory = Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(updateDirectory));
            var assetPath = Path.GetFullPath(
                Path.Combine(fullUpdateDirectory, assetName));
            if (!string.Equals(
                    Path.GetDirectoryName(assetPath),
                    fullUpdateDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The release asset path escaped the Update directory: {assetName}");
            }

            return assetPath;
        }

        internal static async Task CopyAssetDownloadAsync(
            Stream source,
            string downloadPath,
            string installationDirectory,
            UpdaterWorkspace workspace,
            string? localApplicationDataRoot = null,
            string? commonApplicationDataRoot = null,
            Action? afterDestinationCreated = null)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentException.ThrowIfNullOrWhiteSpace(downloadPath);
            ArgumentNullException.ThrowIfNull(workspace);

            var validatedWorkspace = UpdateInstaller.ValidateWorkspace(
                installationDirectory,
                workspace,
                localApplicationDataRoot,
                commonApplicationDataRoot);
            var paths = UpdaterWorkspaceFactory.GetTransactionPaths(
                validatedWorkspace.UpdateDirectory);
            var canonicalDownloadPath = GetAssetDownloadPath(
                paths.UpdateDirectory,
                Path.GetFileName(downloadPath));
            if (!PathsEqual(downloadPath, canonicalDownloadPath))
            {
                throw new InvalidDataException(
                    "The release asset path is outside the validated Update directory.");
            }

            using var transactionIdentity =
                WindowsPathIdentity.OpenExistingDirectory(
                    paths.TransactionRoot,
                    nameof(workspace),
                    WindowsDirectoryLeaseMode.Pinned);
            using var updateIdentity = WindowsPathIdentity.OpenExistingDirectory(
                paths.UpdateDirectory,
                nameof(workspace),
                WindowsDirectoryLeaseMode.Pinned);
            WindowsPathIdentity.RequireDirectChild(
                transactionIdentity,
                updateIdentity,
                "Update must be a direct child of the transaction root.");

            var destinationCreated = false;
            try
            {
                await CopyAssetDownloadFileAsync(
                    source,
                    canonicalDownloadPath,
                    () =>
                    {
                        destinationCreated = true;
                        afterDestinationCreated?.Invoke();
                    });
            }
            catch
            {
                if (destinationCreated)
                {
                    try
                    {
                        UpdateInstaller.DeleteOwnedArchive(
                            validatedWorkspace,
                            canonicalDownloadPath,
                            transactionIdentity,
                            updateIdentity);
                    }
                    catch (Exception cleanupException)
                    {
                        Console.Error.WriteLine(
                            $"Updater cleanup failed; preserving the original failure: {cleanupException}");
                    }
                }

                throw;
            }
        }

        private static async Task CopyAssetDownloadFileAsync(
            Stream source,
            string downloadPath,
            Action afterDestinationCreated)
        {
            await using var fileStream = new FileStream(
                downloadPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            afterDestinationCreated();
            await source.CopyToAsync(fileStream);
            fileStream.Flush(flushToDisk: true);
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

        private static async Task<bool> DownloadAssetAsync(
            string downloadUrl,
            string downloadPath,
            string installationDirectory,
            UpdaterWorkspace workspace)
        {
            Console.WriteLine("正在下载最新版本……");

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("AssetEditor.CN");

                using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                await using var responseStream = await response.Content.ReadAsStreamAsync();
                await CopyAssetDownloadAsync(
                    responseStream,
                    downloadPath,
                    installationDirectory,
                    workspace);

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

        private sealed class RelaunchWorkspaceLease : IDisposable
        {
            private readonly WindowsDirectoryIdentityLease transactionIdentity;
            private readonly WindowsDirectoryIdentityLease updateIdentity;
            private bool isDisposed;

            internal RelaunchWorkspaceLease(
                WindowsDirectoryIdentityLease transactionIdentity,
                WindowsDirectoryIdentityLease updateIdentity)
            {
                this.transactionIdentity = transactionIdentity;
                this.updateIdentity = updateIdentity;
            }

            public void Dispose()
            {
                if (isDisposed)
                    return;

                isDisposed = true;
                updateIdentity.Dispose();
                transactionIdentity.Dispose();
            }
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
