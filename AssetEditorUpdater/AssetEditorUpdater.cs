using System.Diagnostics;
using Octokit;
using SharpCompress.Archives;
using FileMode = System.IO.FileMode;

namespace AssetEditorUpdater
{
    public class AssetEditorUpdater
    {
        private const string GitHubOwner = "Szy-Cathay";
        private const string GitHubRepository = "TheAssetEditor-CN";
        private const string AssetEditorExe = "AssetEditor.CN.exe";
        private const string AssetEditorUpdaterExe = "AssetEditor.CN.Updater.exe";
        // We assume the release RAR contains a single folder named AssetEditor.CN with all the files for the update in it
        private const string UpdateFilesDirectoryName = "AssetEditor.CN";
        private const string InstallationUpdateBackupDirectoryName = "UpdateBackup";
         
        public static async Task Main(string[] args)
        {
            // The first time the updater is run it should be from the installation directory (with no args)
            // so we use the app's base directory to get the installation directory. When we rerun the updater
            // from the update directory we pass the installation directory as an arg and access it that way.

            var currentDirectory = AppContext.BaseDirectory;
            var userDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var updateDirectory = Path.Combine(userDirectory, "AssetEditor.CN", "Temp", "Update");

            var isInitialLaunch = args.Length == 0;
            var installationDirectory = isInitialLaunch ? currentDirectory : args[0];

            Console.WriteLine($"国区版更新器运行目录：{currentDirectory}");

            if (isInitialLaunch)
                RelaunchFromUpdateDirectory(updateDirectory, installationDirectory);
            else
                await UpdateAsync(installationDirectory, updateDirectory);
        }

        private static void RelaunchFromUpdateDirectory(string updateDirectory, string installationDirectory)
        {
            Console.WriteLine($"正在将更新器复制到：{updateDirectory}");

            if (!Directory.Exists(updateDirectory))
                Directory.CreateDirectory(updateDirectory);

            var currentUpdaterPath = Path.Combine(installationDirectory, AssetEditorUpdaterExe);
            var newUpdaterPath = Path.Combine(updateDirectory, AssetEditorUpdaterExe);
            File.Copy(currentUpdaterPath, newUpdaterPath, true);

            LaunchUpdater(newUpdaterPath, updateDirectory, installationDirectory);
        }

        private static void LaunchUpdater(string updaterPath, string workingDirectory, string installationDirectory)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = updaterPath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                ArgumentList = { installationDirectory }
            };
            Process.Start(processStartInfo);
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

            BackupOldFiles(installationDirectory);

            ExtractRar(assetPath, installationDirectory);

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
            if (parsedVersion.Build == 0 && parsedVersion.Revision == 0)
                return new Version(parsedVersion.Major, parsedVersion.Minor);

            if (parsedVersion.Revision == 0)
                return new Version(parsedVersion.Major, parsedVersion.Minor, parsedVersion.Build);

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
            if (extension != ".rar")
                throw new InvalidOperationException($"Asset has extension {extension}, expected .rar.");

            return asset;
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

        private static void BackupOldFiles(string installationDirectory)
        {
            var updateBackupDirectory = Path.Combine(installationDirectory, InstallationUpdateBackupDirectoryName);
            if (Directory.Exists(updateBackupDirectory))
                Directory.Delete(updateBackupDirectory, true);
            Directory.CreateDirectory(updateBackupDirectory);

            Console.WriteLine($"正在备份 {installationDirectory} 到 {updateBackupDirectory}……");

            foreach (var entryPath in Directory.EnumerateFileSystemEntries(installationDirectory))
            {
                var entryName = Path.GetFileName(entryPath);
                if (entryName == InstallationUpdateBackupDirectoryName)
                    continue;

                var destinationPath = Path.Combine(updateBackupDirectory, entryName);
                if (Directory.Exists(entryPath))
                    Directory.Move(entryPath, destinationPath);
                else
                    File.Move(entryPath, destinationPath);
            }
        }

        private static void ExtractRar(string rarPath, string installationDirectory)
        {
            Console.WriteLine($"正在解压更新文件到 {installationDirectory}……");

            var prefix = UpdateFilesDirectoryName + Path.DirectorySeparatorChar;

            using var archive = ArchiveFactory.OpenArchive(rarPath);
            foreach (var entry in archive.Entries)
            {
                if (entry.Key == null || entry.Key == UpdateFilesDirectoryName || entry.IsDirectory)
                    continue;

                var entryKeyWithoutPrefix = entry.Key[prefix.Length..];
                var destinationPath = Path.Combine(installationDirectory, entryKeyWithoutPrefix);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                entry.WriteToFile(destinationPath);
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
    }
}
