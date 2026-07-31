using System.Text.Json;
using System.Text.Json.Serialization;

namespace Shared.Core.PackFiles.Utility;

internal enum FolderProjectMergeSessionPhase
{
    Prepared,
    NativeMergeRunning,
    AwaitingUser,
    Completing,
    Aborting,
}

internal sealed record FolderProjectMergeSession(
    int SchemaVersion,
    string OperationId,
    FolderProjectMergeSessionPhase Phase,
    string OriginalBranchReference,
    string OriginalHeadCommitId,
    string SourceBranchReference,
    string SourceHeadCommitId,
    string? ExpectedIndexFingerprint,
    IReadOnlyDictionary<string, string> ConflictWorktreeFingerprints,
    DateTimeOffset StartedAtUtc,
    string? ExpectedCommitTreeId = null);

internal sealed class FolderProjectMergeSessionStore
{
    internal const string FileName = "ae-folder-project-merge.json";
    private const int SchemaVersion = 1;
    private const int MaximumSessionBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        MaxDepth = 16,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private readonly string _sessionPath;
    private readonly FolderProjectVersionControlPlatform _platform;

    public FolderProjectMergeSessionStore(
        string metadataPath,
        FolderProjectVersionControlPlatform platform)
    {
        _sessionPath = Path.Combine(metadataPath, FileName);
        _platform = platform;
    }

    public string SessionPath => _sessionPath;

    public bool Exists => File.Exists(_sessionPath);

    public FolderProjectMergeSession? Read()
    {
        if (!File.Exists(_sessionPath))
            return null;

        using var stream = new FileStream(
            _sessionPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        if (stream.Length <= 0 || stream.Length > MaximumSessionBytes)
            throw new InvalidDataException("The merge session has an invalid size.");

        var session = JsonSerializer.Deserialize<FolderProjectMergeSession>(
            stream,
            s_jsonOptions);
        Validate(session);
        return session;
    }

    public void Write(FolderProjectMergeSession session)
    {
        Validate(session);
        var directoryPath = Path.GetDirectoryName(_sessionPath)!;
        var temporaryPath = Path.Combine(
            directoryPath,
            $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, session, s_jsonOptions);
                if (stream.Length > MaximumSessionBytes)
                {
                    throw new InvalidDataException(
                        "The merge session is too large.");
                }
                stream.Flush(flushToDisk: true);
            }

            _platform.MoveFile(
                temporaryPath,
                _sessionPath,
                overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.SetAttributes(temporaryPath, FileAttributes.Normal);
                File.Delete(temporaryPath);
            }
        }
    }

    public void Delete()
    {
        if (!File.Exists(_sessionPath))
            return;

        _platform.DeleteFile(_sessionPath);
    }

    private static void Validate(FolderProjectMergeSession? session)
    {
        if (session == null ||
            session.SchemaVersion != SchemaVersion ||
            !Guid.TryParseExact(session.OperationId, "N", out _) ||
            !Enum.IsDefined(session.Phase) ||
            !IsLocalBranchReference(session.OriginalBranchReference) ||
            !IsLocalBranchReference(session.SourceBranchReference) ||
            session.OriginalBranchReference.Equals(
                session.SourceBranchReference,
                StringComparison.Ordinal) ||
            !IsCommitId(session.OriginalHeadCommitId) ||
            !IsCommitId(session.SourceHeadCommitId) ||
            session.ExpectedIndexFingerprint != null &&
            !IsSha256(session.ExpectedIndexFingerprint) ||
            session.Phase == FolderProjectMergeSessionPhase.Completing &&
            (session.ExpectedCommitTreeId == null ||
             !IsCommitId(session.ExpectedCommitTreeId)) ||
            session.Phase != FolderProjectMergeSessionPhase.Completing &&
            session.ExpectedCommitTreeId != null ||
            (session.Phase is FolderProjectMergeSessionPhase.AwaitingUser or
                FolderProjectMergeSessionPhase.Completing or
                FolderProjectMergeSessionPhase.Aborting) &&
            session.ExpectedIndexFingerprint == null ||
            session.ConflictWorktreeFingerprints == null ||
            session.ConflictWorktreeFingerprints.Count > 4096 ||
            session.StartedAtUtc == default ||
            session.StartedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new InvalidDataException("The merge session is invalid.");
        }

        var normalizedPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (var entry in session.ConflictWorktreeFingerprints)
        {
            if (!FolderProjectPathPolicy.TryNormalizeResourcePath(
                    entry.Key,
                    out var normalizedPath) ||
                !entry.Key.Equals(
                    normalizedPath.Replace('\\', '/'),
                    StringComparison.Ordinal) ||
                !normalizedPaths.Add(entry.Key) ||
                entry.Key.Length > 4096 ||
                entry.Value == null ||
                !IsWorktreeFingerprint(entry.Value))
            {
                throw new InvalidDataException(
                    "The merge session contains an invalid worktree fingerprint.");
            }
        }
    }

    private static bool IsLocalBranchReference(string value)
    {
        return value is { Length: > 11 and <= 1024 } &&
               value.StartsWith("refs/heads/", StringComparison.Ordinal) &&
               LibGit2Sharp.Reference.IsValidName(value);
    }

    private static bool IsCommitId(string value)
    {
        return value is { Length: 40 } &&
               value.All(IsLowerHexDigit);
    }

    private static bool IsSha256(string value)
    {
        return value is { Length: 64 } &&
               value.All(IsLowerHexDigit);
    }

    private static bool IsWorktreeFingerprint(string value)
    {
        return value == "missing" ||
               value is { Length: 69 } &&
               value.StartsWith("file:", StringComparison.Ordinal) &&
               IsSha256(value[5..]);
    }

    private static bool IsLowerHexDigit(char value)
    {
        return value is >= '0' and <= '9' or >= 'a' and <= 'f';
    }
}
