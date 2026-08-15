using System.Text.Json;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.PackFiles.Utility;

internal sealed class FolderProjectHistoryCache
{
    private const int SchemaVersion = 1;
    private const string CacheDirectoryName = "history-cache-v1";
    private static readonly ILogger s_logger =
        Logging.Create<FolderProjectHistoryCache>();
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly string _gitDirectory;
    private readonly string _cacheDirectory;

    public FolderProjectHistoryCache(string projectRoot)
    {
        _gitDirectory = Path.Combine(
            Path.GetFullPath(projectRoot),
            ".git");
        _cacheDirectory = Path.Combine(
            _gitDirectory,
            "asseteditor-cn",
            CacheDirectoryName);
    }

    public bool TryReadSummary(
        string commitId,
        out FolderProjectRestorePointChangeSummary summary)
    {
        summary = null!;
        var entry = TryRead<SummaryCacheEntry>(
            GetCachePath(commitId, "summary"));
        if (entry == null ||
            entry.SchemaVersion != SchemaVersion ||
            !string.Equals(
                entry.CommitId,
                commitId,
                StringComparison.OrdinalIgnoreCase) ||
            !IsValidSummary(entry.Summary))
        {
            return false;
        }

        summary = entry.Summary;
        return true;
    }

    public bool TryReadChanges(
        string commitId,
        out IReadOnlyList<FolderProjectRestorePointChange> changes)
    {
        changes = [];
        var entry = TryRead<ChangesCacheEntry>(
            GetCachePath(commitId, "changes"));
        if (entry == null ||
            entry.SchemaVersion != SchemaVersion ||
            !string.Equals(
                entry.CommitId,
                commitId,
                StringComparison.OrdinalIgnoreCase) ||
            entry.Changes == null ||
            entry.Changes.Any(change =>
                change == null ||
                string.IsNullOrWhiteSpace(change.Path)))
        {
            return false;
        }

        changes = entry.Changes;
        return true;
    }

    public void WriteSummary(
        string commitId,
        FolderProjectRestorePointChangeSummary summary)
    {
        if (!IsValidSummary(summary))
            return;

        TryWrite(
            GetCachePath(commitId, "summary"),
            new SummaryCacheEntry(
                SchemaVersion,
                commitId.ToLowerInvariant(),
                summary));
    }

    public void WriteChanges(
        string commitId,
        IReadOnlyList<FolderProjectRestorePointChange> changes)
    {
        TryWrite(
            GetCachePath(commitId, "changes"),
            new ChangesCacheEntry(
                SchemaVersion,
                commitId.ToLowerInvariant(),
                changes));
    }

    private string? GetCachePath(string commitId, string kind)
    {
        if (!IsFullCommitId(commitId))
            return null;

        return Path.Combine(
            _cacheDirectory,
            $"{commitId.ToLowerInvariant()}.{kind}.json");
    }

    private T? TryRead<T>(string? path)
        where T : class
    {
        if (path == null || !File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete);
            return JsonSerializer.Deserialize<T>(stream, s_jsonOptions);
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  JsonException or
                  NotSupportedException)
        {
            s_logger.Warning(
                exception,
                "Could not read folder project history cache at {CachePath}",
                path);
            return null;
        }
    }

    private void TryWrite<T>(string? path, T value)
    {
        if (path == null || !Directory.Exists(_gitDirectory))
            return;

        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            temporaryPath = Path.Combine(
                _cacheDirectory,
                $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, value, s_jsonOptions);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
            temporaryPath = null;
        }
        catch (Exception exception)
            when (exception is IOException or
                  UnauthorizedAccessException or
                  JsonException or
                  NotSupportedException)
        {
            s_logger.Warning(
                exception,
                "Could not write folder project history cache at {CachePath}",
                path);
        }
        finally
        {
            if (temporaryPath != null && File.Exists(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception)
                    when (exception is IOException or
                          UnauthorizedAccessException)
                {
                    s_logger.Warning(
                        exception,
                        "Could not clean temporary folder project history cache at {CachePath}",
                        temporaryPath);
                }
            }
        }
    }

    private static bool IsFullCommitId(string commitId) =>
        !string.IsNullOrWhiteSpace(commitId) &&
        commitId.Length == 40 &&
        commitId.All(character =>
            character is >= '0' and <= '9' or
                >= 'a' and <= 'f' or
                >= 'A' and <= 'F');

    private static bool IsValidSummary(
        FolderProjectRestorePointChangeSummary? summary) =>
        summary != null &&
        summary.Added >= 0 &&
        summary.Modified >= 0 &&
        summary.Deleted >= 0 &&
        summary.Renamed >= 0 &&
        summary.TypeChanged >= 0;

    private sealed record SummaryCacheEntry(
        int SchemaVersion,
        string CommitId,
        FolderProjectRestorePointChangeSummary Summary);

    private sealed record ChangesCacheEntry(
        int SchemaVersion,
        string CommitId,
        IReadOnlyList<FolderProjectRestorePointChange> Changes);
}
