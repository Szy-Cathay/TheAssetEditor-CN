using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AssetEditorUpdater;

internal sealed record GiteeRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("assets")] IReadOnlyList<GiteeAsset> Assets);

internal sealed record GiteeAsset(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);

internal sealed record GiteeUpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("archive")] string Archive,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("parts")] IReadOnlyList<GiteeUpdatePart> Parts);

internal sealed record GiteeUpdatePart(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

internal sealed record GiteePartDownload(string Name, long Size, string Sha256, Uri DownloadUri);

internal sealed record GiteeDownloadPlan(
    string ArchiveName,
    long Size,
    string Sha256,
    IReadOnlyList<GiteePartDownload> Parts);

internal static class GiteeUpdateSource
{
    private const string ManifestSuffix = ".manifest.json";

    internal static async Task<GiteeRelease?> GetLatestReleaseAsync(
        HttpClient httpClient,
        Uri latestReleaseUri)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(latestReleaseUri);

        using var response = await httpClient.GetAsync(latestReleaseUri);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<GiteeRelease>(responseStream);
    }

    internal static async Task<GiteeDownloadPlan> CreateDownloadPlanAsync(
        HttpClient httpClient,
        GiteeRelease release)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(release);
        ValidateReleaseMetadata(release);

        var manifestAssets = release.Assets
            .Where(asset => asset.Name.EndsWith(ManifestSuffix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (manifestAssets.Count != 1)
            throw new InvalidDataException($"Expected 1 update manifest, found {manifestAssets.Count}.");

        var manifestUri = ValidateDownloadUri(manifestAssets[0].BrowserDownloadUrl);
        using var response = await httpClient.GetAsync(manifestUri);
        response.EnsureSuccessStatusCode();
        await using var responseStream = await response.Content.ReadAsStreamAsync();
        var manifest = await JsonSerializer.DeserializeAsync<GiteeUpdateManifest>(responseStream)
            ?? throw new InvalidDataException("The update manifest is empty.");
        return CreateDownloadPlan(release, manifest);
    }

    internal static GiteeDownloadPlan CreateDownloadPlan(
        GiteeRelease release,
        GiteeUpdateManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(manifest);
        ValidateReleaseMetadata(release);

        var releaseVersion = release.TagName.Trim().TrimStart('v', 'V');
        if (!string.Equals(manifest.Version, releaseVersion, StringComparison.Ordinal))
            throw new InvalidDataException("The update manifest version does not match the release tag.");

        ValidateSafeLeaf(manifest.Archive, requireZip: true);
        ValidateHash(manifest.Sha256, "archive");
        if (manifest.Size <= 0)
            throw new InvalidDataException("The archive size must be positive.");
        if (manifest.Parts == null
            || manifest.Parts.Count == 0
            || manifest.Parts.Any(part => part is null))
            throw new InvalidDataException("The update manifest does not contain any parts.");

        var assetsByName = release.Assets
            .GroupBy(asset => asset.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var partNames = new HashSet<string>(StringComparer.Ordinal);
        var partDownloads = new List<GiteePartDownload>(manifest.Parts.Count);
        long totalSize = 0;
        foreach (var part in manifest.Parts)
        {
            ValidateSafeLeaf(part.Name, requireZip: true);
            ValidateHash(part.Sha256, part.Name);
            if (part.Size <= 0)
                throw new InvalidDataException($"The update part size must be positive: {part.Name}");
            if (!partNames.Add(part.Name))
                throw new InvalidDataException($"The update manifest contains a duplicate part: {part.Name}");
            if (!assetsByName.TryGetValue(part.Name, out var matchingAssets)
                || matchingAssets.Count != 1)
            {
                throw new InvalidDataException($"Expected 1 release asset named {part.Name}.");
            }

            totalSize = checked(totalSize + part.Size);
            partDownloads.Add(new GiteePartDownload(
                part.Name,
                part.Size,
                part.Sha256,
                ValidateDownloadUri(matchingAssets[0].BrowserDownloadUrl)));
        }

        if (totalSize != manifest.Size)
            throw new InvalidDataException("The update part sizes do not match the archive size.");

        return new GiteeDownloadPlan(manifest.Archive, manifest.Size, manifest.Sha256, partDownloads);
    }

    internal static async Task DownloadArchiveAsync(
        HttpClient httpClient,
        GiteeDownloadPlan plan,
        string downloadPath,
        string installationDirectory,
        UpdaterWorkspace workspace,
        string? localApplicationDataRoot = null,
        string? commonApplicationDataRoot = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadPath);
        ArgumentNullException.ThrowIfNull(workspace);

        var validatedWorkspace = UpdateInstaller.ValidateWorkspace(
            installationDirectory,
            workspace,
            localApplicationDataRoot,
            commonApplicationDataRoot);
        var paths = UpdaterWorkspaceFactory.GetTransactionPaths(validatedWorkspace.UpdateDirectory);
        var canonicalDownloadPath = AssetEditorUpdater.GetAssetDownloadPath(
            paths.UpdateDirectory,
            plan.ArchiveName);
        if (!PathsEqual(downloadPath, canonicalDownloadPath))
            throw new InvalidDataException("The release asset path is outside the validated Update directory.");

        using var transactionIdentity = WindowsPathIdentity.OpenExistingDirectory(
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
            await using var destination = new FileStream(
                canonicalDownloadPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);
            destinationCreated = true;
            using var archiveHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long archiveSize = 0;

            foreach (var part in plan.Parts)
            {
                using var response = await httpClient.GetAsync(
                    part.DownloadUri,
                    HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long contentLength
                    && contentLength != part.Size)
                {
                    throw new InvalidDataException($"The downloaded part has an unexpected size: {part.Name}");
                }

                await using var source = await response.Content.ReadAsStreamAsync();
                using var partHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long partSize = 0;
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(buffer)) > 0)
                {
                    await destination.WriteAsync(buffer.AsMemory(0, bytesRead));
                    partHash.AppendData(buffer, 0, bytesRead);
                    archiveHash.AppendData(buffer, 0, bytesRead);
                    partSize = checked(partSize + bytesRead);
                    archiveSize = checked(archiveSize + bytesRead);
                }

                if (partSize != part.Size || !HashMatches(partHash.GetHashAndReset(), part.Sha256))
                    throw new InvalidDataException($"The downloaded part failed verification: {part.Name}");
            }

            destination.Flush(flushToDisk: true);
            if (archiveSize != plan.Size || !HashMatches(archiveHash.GetHashAndReset(), plan.Sha256))
                throw new InvalidDataException("The assembled update archive failed verification.");
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

    private static void ValidateSafeLeaf(string fileName, bool requireZip)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || Path.IsPathRooted(fileName)
            || Path.IsPathFullyQualified(fileName)
            || fileName is "." or ".."
            || fileName.Contains(Path.DirectorySeparatorChar)
            || fileName.Contains(Path.AltDirectorySeparatorChar)
            || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || fileName.EndsWith('.')
            || fileName.EndsWith(' ')
            || (requireZip && !AssetEditorUpdater.IsZipAssetName(fileName)))
        {
            throw new InvalidDataException($"The update file name is not a safe leaf: {fileName}");
        }
    }

    private static void ValidateReleaseMetadata(GiteeRelease release)
    {
        if (string.IsNullOrWhiteSpace(release.TagName)
            || release.Assets == null
            || release.Assets.Any(asset =>
                asset is null
                || string.IsNullOrWhiteSpace(asset.Name)
                || string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl)))
        {
            throw new InvalidDataException("The Gitee release metadata is incomplete.");
        }
    }

    private static Uri ValidateDownloadUri(string downloadUrl)
    {
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "gitee.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The update download URL is not an HTTPS Gitee URL.");
        }

        return uri;
    }

    private static void ValidateHash(string sha256, string itemName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sha256)
                || Convert.FromHexString(sha256).Length != 32)
                throw new InvalidDataException($"The SHA-256 value is invalid: {itemName}");
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"The SHA-256 value is invalid: {itemName}", exception);
        }
    }

    private static bool HashMatches(byte[] actualHash, string expectedHash)
    {
        return CryptographicOperations.FixedTimeEquals(actualHash, Convert.FromHexString(expectedHash));
    }

    private static bool PathsEqual(string firstPath, string secondPath)
    {
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(firstPath)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(secondPath)),
            StringComparison.OrdinalIgnoreCase);
    }
}
