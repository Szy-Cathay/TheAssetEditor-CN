using System.Net;
using System.Security.Cryptography;
using AssetEditorUpdater;
using UpdaterProgram = AssetEditorUpdater.AssetEditorUpdater;

namespace AssetEditorUpdaterTests;

public class GiteeUpdateSourceTests
{
    [Test]
    public void CreateDownloadPlan_UsesManifestOrderAndRequiresEveryPart()
    {
        var first = "first"u8.ToArray();
        var second = "second"u8.ToArray();
        var archive = first.Concat(second).ToArray();
        var release = CreateRelease("v2.4.2", "part02.zip", "part01.zip");
        var manifest = new GiteeUpdateManifest(
            "2.4.2",
            "AssetEditor-CN-v2.4.2.zip",
            archive.Length,
            Hash(archive),
            [
                new GiteeUpdatePart("part01.zip", first.Length, Hash(first)),
                new GiteeUpdatePart("part02.zip", second.Length, Hash(second))
            ]);

        var plan = GiteeUpdateSource.CreateDownloadPlan(release, manifest);

        Assert.That(
            plan.Parts.Select(part => part.Name),
            Is.EqualTo(new[] { "part01.zip", "part02.zip" }));

        var missingPartRelease = CreateRelease("v2.4.2", "part01.zip");
        Assert.Throws<InvalidDataException>(() =>
            GiteeUpdateSource.CreateDownloadPlan(missingPartRelease, manifest));
    }

    [Test]
    public async Task DownloadArchiveAsync_AssemblesPartsAndVerifiesHashes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var testData = CreateTestDownload(root);
            using var client = new HttpClient(new PartResponseHandler(testData.Responses));

            await GiteeUpdateSource.DownloadArchiveAsync(
                client,
                testData.Plan,
                testData.ArchivePath,
                testData.InstallationDirectory,
                testData.Workspace,
                testData.LocalRoot,
                testData.CommonRoot);

            Assert.That(
                await File.ReadAllBytesAsync(testData.ArchivePath),
                Is.EqualTo(testData.Archive));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DownloadArchiveAsync_HashFailureDeletesPartialArchive(bool corruptArchiveHash)
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var testData = CreateTestDownload(root, corruptArchiveHash);
            if (!corruptArchiveHash)
                testData.Responses[new Uri("https://gitee.com/download/part02.zip")][0] ^= 0xff;
            using var client = new HttpClient(new PartResponseHandler(testData.Responses));

            Assert.ThrowsAsync<InvalidDataException>(async () =>
                await GiteeUpdateSource.DownloadArchiveAsync(
                    client,
                    testData.Plan,
                    testData.ArchivePath,
                    testData.InstallationDirectory,
                    testData.Workspace,
                    testData.LocalRoot,
                    testData.CommonRoot));

            Assert.That(File.Exists(testData.ArchivePath), Is.False);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    [Explicit("Downloads and installs the current public Gitee release.")]
    public async Task LiveGiteeRelease_DownloadsVerifiesAndInstallsInIsolation()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
            var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
            var installationDirectory = Directory.CreateDirectory(
                Path.Combine(root, "installation")).FullName;
            File.WriteAllText(Path.Combine(installationDirectory, "old-only.txt"), "old");
            var workspace = UpdaterWorkspaceFactory.Create(
                UpdaterWorkspaceFactory.GetLayout(
                    false,
                    Guid.Empty,
                    localRoot,
                    commonRoot));
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AssetEditor.CN.Updater.Tests");
            var latestRelease = await GiteeUpdateSource.GetLatestReleaseAsync(
                client,
                new Uri(
                    "https://gitee.com/api/v5/repos/szy-cathay/AssetEditor-CN-Downloads/releases/latest"));
            Assert.That(latestRelease, Is.Not.Null);
            var plan = await GiteeUpdateSource.CreateDownloadPlanAsync(client, latestRelease!);
            var archivePath = UpdaterProgram.GetAssetDownloadPath(
                workspace.UpdateDirectory,
                plan.ArchiveName);

            await GiteeUpdateSource.DownloadArchiveAsync(
                client,
                plan,
                archivePath,
                installationDirectory,
                workspace,
                localRoot,
                commonRoot);

            Assert.Multiple(() =>
            {
                Assert.That(new FileInfo(archivePath).Length, Is.EqualTo(plan.Size));
                Assert.That(Hash(File.ReadAllBytes(archivePath)), Is.EqualTo(plan.Sha256));
            });

            UpdateInstaller.Install(
                archivePath,
                installationDirectory,
                workspace,
                UpdateInstaller.CopyDirectory,
                localRoot,
                commonRoot);

            Assert.Multiple(() =>
            {
                Assert.That(
                    File.Exists(Path.Combine(installationDirectory, "AssetEditor.CN.exe")),
                    Is.True);
                Assert.That(
                    File.Exists(Path.Combine(
                        installationDirectory,
                        "Updater",
                        "AssetEditor.CN.Updater.exe")),
                    Is.True);
                Assert.That(
                    File.Exists(Path.Combine(installationDirectory, "old-only.txt")),
                    Is.False);
            });
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static TestDownload CreateTestDownload(string root, bool corruptArchiveHash = false)
    {
        var localRoot = Directory.CreateDirectory(Path.Combine(root, "local")).FullName;
        var commonRoot = Directory.CreateDirectory(Path.Combine(root, "common")).FullName;
        var installationDirectory = Directory.CreateDirectory(
            Path.Combine(root, "installation")).FullName;
        var workspace = UpdaterWorkspaceFactory.Create(
            UpdaterWorkspaceFactory.GetLayout(
                false,
                Guid.Empty,
                localRoot,
                commonRoot));
        var first = "first-part-"u8.ToArray();
        var second = "second-part"u8.ToArray();
        var archive = first.Concat(second).ToArray();
        var archiveHash = corruptArchiveHash ? new string('0', 64) : Hash(archive);
        var plan = new GiteeDownloadPlan(
            "AssetEditor-CN-v2.4.2.zip",
            archive.Length,
            archiveHash,
            [
                new GiteePartDownload(
                    "part01.zip",
                    first.Length,
                    Hash(first),
                    new Uri("https://gitee.com/download/part01.zip")),
                new GiteePartDownload(
                    "part02.zip",
                    second.Length,
                    Hash(second),
                    new Uri("https://gitee.com/download/part02.zip"))
            ]);
        var responses = new Dictionary<Uri, byte[]>
        {
            [new Uri("https://gitee.com/download/part01.zip")] = first,
            [new Uri("https://gitee.com/download/part02.zip")] = second
        };
        var archivePath = UpdaterProgram.GetAssetDownloadPath(
            workspace.UpdateDirectory,
            plan.ArchiveName);
        return new TestDownload(
            localRoot,
            commonRoot,
            workspace,
            installationDirectory,
            archivePath,
            archive,
            plan,
            responses);
    }

    private static GiteeRelease CreateRelease(string tagName, params string[] partNames)
    {
        return new GiteeRelease(
            tagName,
            tagName,
            string.Empty,
            partNames.Select(name => new GiteeAsset(
                name,
                $"https://gitee.com/download/{name}"))
                .Append(new GiteeAsset(
                    "release.manifest.json",
                    "https://gitee.com/download/release.manifest.json"))
                .ToList());
    }

    private static string Hash(byte[] contents)
    {
        return Convert.ToHexString(SHA256.HashData(contents)).ToLowerInvariant();
    }

    private static string CreateTemporaryDirectory()
    {
        return Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), $"gitee-update-{Guid.NewGuid():N}"))
            .FullName;
    }

    private sealed class PartResponseHandler(IReadOnlyDictionary<Uri, byte[]> responses)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri == null || !responses.TryGetValue(request.RequestUri, out var bytes))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            });
        }
    }

    private sealed record TestDownload(
        string LocalRoot,
        string CommonRoot,
        UpdaterWorkspace Workspace,
        string InstallationDirectory,
        string ArchivePath,
        byte[] Archive,
        GiteeDownloadPlan Plan,
        Dictionary<Uri, byte[]> Responses);
}
