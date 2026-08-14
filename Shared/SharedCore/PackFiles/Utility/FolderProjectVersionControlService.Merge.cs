using LibGit2Sharp;
using Shared.Core.PackFiles.Models;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shared.Core.PackFiles.Utility;

internal sealed partial class FolderProjectVersionControlService
{
    private static readonly ConcurrentDictionary<string, object> s_mergeGates =
        new(StringComparer.OrdinalIgnoreCase);

    public FolderProjectMergeState GetMergeState(string projectRoot)
    {
        return ExecuteMergeLocked(
            projectRoot,
            root => Execute(
                () =>
                {
                    using var repository = OpenRepository(root);
                    return GetMergeStateCore(repository);
                }));
    }

    public FolderProjectMergeStartResult BeginMerge(
        string projectRoot,
        string sourceLocalBranch)
    {
        ValidateBranchName(sourceLocalBranch);
        return ExecuteMergeLocked(
            projectRoot,
            root => Execute(
                () => BeginMergeCore(root, sourceLocalBranch, null)));
    }

    public FolderProjectMergeStartResult BeginMerge(
        string projectRoot,
        string sourceLocalBranch,
        Action<FolderProjectVersionControlProgress> reportProgress)
    {
        ArgumentNullException.ThrowIfNull(reportProgress);
        ValidateBranchName(sourceLocalBranch);
        return ExecuteMergeLocked(
            projectRoot,
            root => Execute(
                () => BeginMergeCore(
                    root,
                    sourceLocalBranch,
                    reportProgress)));
    }

    public FolderProjectMergeState ResolveMergeConflict(
        string projectRoot,
        string conflictId,
        FolderProjectMergeChoice choice)
    {
        if (!Enum.IsDefined(choice))
            throw new ArgumentOutOfRangeException(nameof(choice));

        return ExecuteMergeLocked(
            projectRoot,
            root => Execute(
                () => ResolveMergeConflictCore(
                    root,
                    conflictId,
                    choice)));
    }

    public FolderProjectCommitSummary CompleteMerge(
        string projectRoot,
        string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.EmptyCommitMessage,
                "A merge commit message is required.");
        }

        return ExecuteMergeLocked(
            projectRoot,
            root => Execute(
                () => CompleteMergeCore(root, message.Trim())));
    }

    public void AbortMerge(string projectRoot)
    {
        ExecuteMergeLocked(
            projectRoot,
            root => Execute(
                () =>
                {
                    AbortMergeCore(root);
                    return true;
                }));
    }

    private FolderProjectMergeStartResult BeginMergeCore(
        string projectRoot,
        string sourceLocalBranch,
        Action<FolderProjectVersionControlProgress>? reportProgress)
    {
        using var repository = OpenRepository(projectRoot);
        var existingState = GetMergeStateCore(repository);
        if (existingState.Phase != FolderProjectMergePhase.None)
        {
            throw new FolderProjectVersionControlException(
                existingState.Phase == FolderProjectMergePhase.RecoveryRequired
                    ? FolderProjectVersionControlError.MergeRecoveryRequired
                    : FolderProjectVersionControlError.MergeAlreadyActive,
                "A folder-project merge is already active.");
        }

        EnsureMergeStartStateSupported(repository, projectRoot);
        var currentBranch = repository.Head;
        var sourceBranch = FindLocalBranch(repository, sourceLocalBranch);
        if (sourceBranch.CanonicalName.Equals(
                currentBranch.CanonicalName,
                StringComparison.Ordinal))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeSourceIsCurrent,
                "The current branch cannot be merged into itself.");
        }

        var currentTip = currentBranch.Tip!;
        var sourceTip = sourceBranch.Tip;
        var mergeDetail =
            $"{sourceBranch.FriendlyName} → {currentBranch.FriendlyName}";
        reportProgress?.Invoke(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.PreparingMerge,
            mergeDetail));
        EnsureMergeTreeSupported(repository, currentTip.Tree);
        EnsureMergeTreeSupported(repository, sourceTip.Tree);
        EnsureNoIgnoredTargetCollisions(
            repository,
            projectRoot,
            sourceBranch);
        var identity = ReadLocalIdentity(repository);
        ValidateIdentity(identity);
        var divergence = repository.ObjectDatabase
            .CalculateHistoryDivergence(currentTip, sourceTip);
        if (divergence.CommonAncestor == null ||
            divergence.AheadBy == null ||
            divergence.BehindBy == null)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnrelatedHistories,
                "The branches do not share a common history.");
        }

        if (divergence.BehindBy == 0)
        {
            ReportMergeVerificationCompleted(reportProgress, mergeDetail);
            return new FolderProjectMergeStartResult(
                FolderProjectMergeOutcome.UpToDate,
                ToSummary(currentTip),
                CreateIdleMergeState(repository));
        }

        var fastForward = divergence.AheadBy == 0;
        var expectedNativeIndexFingerprint = fastForward
            ? null
            : CalculateExpectedNativeMergeIndexFingerprint(
                repository,
                currentTip,
                sourceTip);
        var store = new FolderProjectMergeSessionStore(
            repository.Info.Path,
            _platform);
        var session = new FolderProjectMergeSession(
            1,
            Guid.NewGuid().ToString("N"),
            FolderProjectMergeSessionPhase.Prepared,
            currentBranch.CanonicalName,
            currentTip.Sha,
            sourceBranch.CanonicalName,
            sourceTip.Sha,
            expectedNativeIndexFingerprint,
            new Dictionary<string, string>(StringComparer.Ordinal),
            DateTimeOffset.UtcNow);
        store.Write(session);
        session = session with
        {
            Phase = FolderProjectMergeSessionPhase.NativeMergeRunning,
        };
        store.Write(session);

        try
        {
            EnsureMergeStartStateSupported(repository, projectRoot);
            EnsureNoIgnoredTargetCollisions(
                repository,
                projectRoot,
                sourceBranch);
        }
        catch (Exception failure)
        {
            try
            {
                store.Delete();
            }
            catch (Exception cleanupFailure)
            {
                throw new AggregateException(
                    "The native merge was not started, but session cleanup failed.",
                    [failure, cleanupFailure]);
            }

            throw;
        }

        var targetPaths = TargetTreePaths.Create(sourceTip.Tree);
        var result = _platform.Merge(
            repository,
            sourceTip,
            CreateSignature(identity),
            new MergeOptions
            {
                CommitOnSuccess = false,
                FastForwardStrategy = fastForward
                    ? FastForwardStrategy.FastForwardOnly
                    : FastForwardStrategy.NoFastForward,
                CheckoutNotifyFlags =
                    CheckoutNotifyFlags.Dirty |
                    CheckoutNotifyFlags.Untracked |
                    CheckoutNotifyFlags.Ignored,
                OnCheckoutNotify = (path, flags) =>
                    CheckoutMayProceed(
                        projectRoot,
                        targetPaths,
                        path,
                        flags),
                OnCheckoutProgress = (path, completedSteps, totalSteps) =>
                    reportProgress?.Invoke(
                        new FolderProjectVersionControlProgress(
                            FolderProjectVersionControlProgressStage
                                .MergingFiles,
                            path?.Replace('\\', '/'),
                            completedSteps,
                            totalSteps)),
            });
        if (fastForward)
        {
            if (result.Status != MergeStatus.FastForward ||
                repository.Head.Tip?.Sha != sourceTip.Sha ||
                repository.Info.CurrentOperation != CurrentOperation.None ||
                RetrieveWorkingStatus(repository).IsDirty ||
                HasUnreadableWorkingPaths(repository))
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.MergeRecoveryRequired,
                    "The fast-forward merge did not reach a verifiable state.");
            }

            store.Delete();
            ReportMergeVerificationCompleted(reportProgress, mergeDetail);
            return new FolderProjectMergeStartResult(
                FolderProjectMergeOutcome.FastForwarded,
                ToSummary(sourceTip),
                CreateIdleMergeState(repository));
        }

        if (repository.Head.Tip?.Sha != currentTip.Sha ||
            repository.Info.CurrentOperation != CurrentOperation.Merge ||
            session.ExpectedIndexFingerprint !=
            CalculateIndexFingerprint(repository.Index) ||
            result.Status is not (
                MergeStatus.NonFastForward or MergeStatus.Conflicts))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The non-fast-forward merge did not reach a verifiable state.");
        }

        session = session with
        {
            Phase = FolderProjectMergeSessionPhase.AwaitingUser,
            ExpectedIndexFingerprint =
                CalculateIndexFingerprint(repository.Index),
            ConflictWorktreeFingerprints =
                CaptureConflictWorktreeFingerprints(
                    repository,
                    projectRoot),
        };
        store.Write(session);
        var state = CreateActiveMergeState(repository, session);
        if (state.Phase == FolderProjectMergePhase.RecoveryRequired)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge conflict state could not be verified.");
        }
        ReportMergeVerificationCompleted(reportProgress, mergeDetail);
        return new FolderProjectMergeStartResult(
            state.Phase == FolderProjectMergePhase.Conflicts
                ? FolderProjectMergeOutcome.Conflicts
                : FolderProjectMergeOutcome.ReadyToCommit,
            null,
            state);
    }

    private static void ReportMergeVerificationCompleted(
        Action<FolderProjectVersionControlProgress>? reportProgress,
        string detail)
    {
        reportProgress?.Invoke(new FolderProjectVersionControlProgress(
            FolderProjectVersionControlProgressStage.VerifyingMerge,
            detail,
            1,
            1));
    }

    private FolderProjectMergeState ResolveMergeConflictCore(
        string projectRoot,
        string conflictId,
        FolderProjectMergeChoice choice)
    {
        GitIndexSnapshot? indexSnapshot = null;
        IReadOnlyList<MergeWorktreeFileSnapshot>? worktreeSnapshots = null;
        IReadOnlyDictionary<string, IReadOnlySet<string>>?
            allowedWorktreeRollbackFingerprints = null;
        FolderProjectMergeSessionStore? store = null;
        FolderProjectMergeSession? originalSession = null;
        FolderProjectMergeSession? attemptedSession = null;
        string? plannedIndexFingerprint = null;
        FolderProjectMergeState? result = null;
        Exception? failure = null;

        using (var repository = OpenRepository(projectRoot))
        {
            var state = GetMergeStateCore(repository);
            EnsureOwnedActiveMerge(state);
            if (state.Phase != FolderProjectMergePhase.Conflicts)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.MergeConflictNotFound,
                    "The active merge has no unresolved conflicts.");
            }

            store = new FolderProjectMergeSessionStore(
                repository.Info.Path,
                _platform);
            originalSession = store.Read() ??
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.MergeRecoveryRequired,
                    "The active merge session is missing.");
            var group = GetMergeConflictGroups(repository)
                .SingleOrDefault(
                    candidate => candidate.Conflict.Id.Equals(
                        conflictId,
                        StringComparison.Ordinal));
            if (group == null)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.MergeConflictNotFound,
                    "The requested merge conflict no longer exists.");
            }

            var selectedEntry = choice == FolderProjectMergeChoice.Current
                ? group.Current
                : group.Incoming;
            var affectedPaths = GetConflictPaths(group);
            indexSnapshot = GitIndexSnapshot.Capture(
                Path.Combine(repository.Info.Path, "index"));
            worktreeSnapshots = affectedPaths
                .Select(
                    path => MergeWorktreeFileSnapshot.Capture(
                        projectRoot,
                        path,
                        _platform))
                .ToList();
            allowedWorktreeRollbackFingerprints =
                CreateAllowedWorktreeRollbackFingerprints(
                    repository,
                    worktreeSnapshots,
                    selectedEntry);
            EnsureAwaitingSessionMatchesRepository(
                repository,
                originalSession);

            try
            {
                ResolveConflictInIndexAndWorktree(
                    repository,
                    projectRoot,
                    affectedPaths,
                    selectedEntry,
                    fingerprint => plannedIndexFingerprint = fingerprint);
                attemptedSession = originalSession with
                {
                    ExpectedIndexFingerprint =
                        CalculateIndexFingerprint(repository.Index),
                    ConflictWorktreeFingerprints =
                        CaptureWorktreeFingerprints(
                            projectRoot,
                            originalSession
                                .ConflictWorktreeFingerprints
                                .Keys),
                };
                store.Write(attemptedSession);
                result = CreateActiveMergeState(
                    repository,
                    attemptedSession);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure == null)
            return result!;

        var rollbackFailures = new List<Exception>();
        foreach (var snapshot in worktreeSnapshots!)
        {
            try
            {
                snapshot.Restore(
                    projectRoot,
                    _platform,
                    allowedWorktreeRollbackFingerprints![
                        snapshot.RepositoryPath]);
            }
            catch (Exception exception)
            {
                rollbackFailures.Add(exception);
            }
        }
        try
        {
            RestoreIndexIfOwned(
                projectRoot,
                indexSnapshot!,
                originalSession!,
                plannedIndexFingerprint);
        }
        catch (Exception exception)
        {
            rollbackFailures.Add(exception);
        }
        try
        {
            RestoreSessionIfOwned(
                store!,
                originalSession!,
                attemptedSession);
        }
        catch (Exception exception)
        {
            rollbackFailures.Add(exception);
        }

        if (rollbackFailures.Count != 0)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryFailure,
                "Conflict resolution failed and rollback was incomplete.",
                new AggregateException(
                    new[] { failure }
                        .Concat(rollbackFailures)));
        }

        throw failure;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>>
        CreateAllowedWorktreeRollbackFingerprints(
            Repository repository,
            IReadOnlyList<MergeWorktreeFileSnapshot> snapshots,
            IndexEntry? selectedEntry)
    {
        var result = snapshots.ToDictionary(
            snapshot => snapshot.RepositoryPath,
            snapshot => new HashSet<string>(
                [snapshot.Fingerprint, "missing"],
                StringComparer.Ordinal),
            StringComparer.OrdinalIgnoreCase);
        if (selectedEntry != null)
        {
            var selectedPath = ValidateResourcePath(selectedEntry.Path);
            var blob = repository.Lookup<Blob>(selectedEntry.Id) ??
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryFailure,
                    "The selected merge side references a missing blob.");
            result[selectedPath].Add(
                CalculateBlobWorktreeFingerprint(blob));
        }

        return result.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlySet<string>)entry.Value,
            StringComparer.OrdinalIgnoreCase);
    }

    private void RestoreIndexIfOwned(
        string projectRoot,
        GitIndexSnapshot snapshot,
        FolderProjectMergeSession originalSession,
        string? plannedIndexFingerprint)
    {
        var allowedFingerprints = new HashSet<string>(
            StringComparer.Ordinal)
        {
            originalSession.ExpectedIndexFingerprint!,
        };
        if (plannedIndexFingerprint != null)
            allowedFingerprints.Add(plannedIndexFingerprint);

        string currentFingerprint;
        using (var repository = OpenRepository(projectRoot))
        {
            currentFingerprint =
                CalculateIndexFingerprint(repository.Index);
        }
        if (!allowedFingerprints.Contains(currentFingerprint))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge index changed while conflict resolution was rolling back.");
        }
        if (currentFingerprint ==
            originalSession.ExpectedIndexFingerprint)
        {
            return;
        }

        snapshot.Restore(_platform);
        using var verifiedRepository = OpenRepository(projectRoot);
        if (CalculateIndexFingerprint(verifiedRepository.Index) !=
            originalSession.ExpectedIndexFingerprint)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryFailure,
                "The merge index rollback could not be verified.");
        }
    }

    private static void RestoreSessionIfOwned(
        FolderProjectMergeSessionStore store,
        FolderProjectMergeSession originalSession,
        FolderProjectMergeSession? attemptedSession)
    {
        var currentSession = store.Read() ??
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge session disappeared during conflict resolution.");
        if (SessionsMatch(currentSession, originalSession))
            return;
        if (attemptedSession == null ||
            !SessionsMatch(currentSession, attemptedSession))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge session changed while conflict resolution was rolling back.");
        }

        store.Write(originalSession);
        var verifiedSession = store.Read();
        if (verifiedSession == null ||
            !SessionsMatch(verifiedSession, originalSession))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryFailure,
                "The merge session rollback could not be verified.");
        }
    }

    private static bool SessionsMatch(
        FolderProjectMergeSession first,
        FolderProjectMergeSession second)
    {
        return first.SchemaVersion == second.SchemaVersion &&
               first.OperationId == second.OperationId &&
               first.Phase == second.Phase &&
               first.OriginalBranchReference ==
               second.OriginalBranchReference &&
               first.OriginalHeadCommitId ==
               second.OriginalHeadCommitId &&
               first.SourceBranchReference ==
               second.SourceBranchReference &&
               first.SourceHeadCommitId ==
               second.SourceHeadCommitId &&
               first.ExpectedIndexFingerprint ==
               second.ExpectedIndexFingerprint &&
               first.ExpectedCommitTreeId ==
               second.ExpectedCommitTreeId &&
               first.StartedAtUtc == second.StartedAtUtc &&
               first.ConflictWorktreeFingerprints.Count ==
               second.ConflictWorktreeFingerprints.Count &&
               first.ConflictWorktreeFingerprints.All(
                   entry =>
                       second.ConflictWorktreeFingerprints.TryGetValue(
                           entry.Key,
                           out var value) &&
                       value == entry.Value);
    }

    private static string CalculateBlobWorktreeFingerprint(Blob blob)
    {
        using var stream = blob.GetContentStream();
        return "file:" +
               Convert.ToHexString(SHA256.HashData(stream))
                   .ToLowerInvariant();
    }

    private FolderProjectCommitSummary CompleteMergeCore(
        string projectRoot,
        string message)
    {
        using var repository = OpenRepository(projectRoot);
        var state = GetMergeStateCore(repository);
        EnsureOwnedActiveMerge(state);
        if (state.Phase == FolderProjectMergePhase.Conflicts ||
            repository.Index.Conflicts.Any())
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnresolvedMergeConflicts,
                "All merge conflicts must be resolved before committing.");
        }
        if (state.Phase != FolderProjectMergePhase.ReadyToCommit)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge is not ready to commit.");
        }
        EnsureMergeRepositoryIsUnlocked(repository);

        var store = new FolderProjectMergeSessionStore(
            repository.Info.Path,
            _platform);
        var session = store.Read() ??
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The active merge session is missing.");
        EnsureAwaitingSessionMatchesRepository(repository, session);
        var identity = ReadLocalIdentity(repository);
        ValidateIdentity(identity);
        var expectedCommitTreeId = repository.Index.WriteToTree().Sha;
        var completingSession = session with
        {
            Phase = FolderProjectMergeSessionPhase.Completing,
            ExpectedCommitTreeId = expectedCommitTreeId,
        };
        var completingWritten = false;
        Commit? commit = null;
        try
        {
            store.Write(completingSession);
            completingWritten = true;
            commit = _platform.Commit(
                repository,
                message,
                CreateSignature(identity));
        }
        catch (Exception failure)
        {
            var completedCommit = GetVerifiedCompletedMergeCommit(
                repository,
                completingSession);
            if (completedCommit != null)
            {
                try
                {
                    store.Delete();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "The merge commit completed, but session cleanup failed.",
                        [failure, cleanupFailure]);
                }

                return ToSummary(completedCommit);
            }

            if (completingWritten &&
                AwaitingSessionMatchesRepository(repository, session))
            {
                try
                {
                    store.Write(session);
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(
                        "The merge commit failed and the session could not be restored.",
                        [failure, rollbackFailure]);
                }
            }

            throw;
        }

        if (commit == null ||
            !IsCompletedMergeCommit(commit, completingSession) ||
            repository.Head.Tip?.Sha != commit.Sha ||
            repository.Info.CurrentOperation != CurrentOperation.None ||
            RetrieveWorkingStatus(repository).IsDirty ||
            HasUnreadableWorkingPaths(repository))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge commit did not reach a verifiable state.");
        }

        store.Delete();
        return ToSummary(commit);
    }

    private void AbortMergeCore(string projectRoot)
    {
        using var repository = OpenRepository(projectRoot);
        var store = new FolderProjectMergeSessionStore(
            repository.Info.Path,
            _platform);
        FolderProjectMergeSession? session;
        try
        {
            session = store.Read();
        }
        catch (Exception exception)
            when (exception is InvalidDataException or
                  JsonException or
                  IOException or
                  UnauthorizedAccessException)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge session is damaged and cannot be aborted safely.",
                exception);
        }
        if (session == null)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeNotActive,
                "There is no active folder-project merge.");
        }

        var completedCommit = GetVerifiedCompletedMergeCommit(
            repository,
            session);
        if (completedCommit != null)
        {
            TryDeleteCompletedSession(store);
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeNotActive,
                "The folder-project merge has already completed.");
        }
        if (session.Phase == FolderProjectMergeSessionPhase.Aborting &&
            IsVerifiedAbortResult(repository, session))
        {
            TryDeleteCompletedSession(store);
            return;
        }
        if (session.Phase == FolderProjectMergeSessionPhase.Aborting)
        {
            var awaitingSession = session with
            {
                Phase = FolderProjectMergeSessionPhase.AwaitingUser,
            };
            if (AwaitingSessionMatchesRepository(
                    repository,
                    awaitingSession,
                    allowUntracked: true))
            {
                store.Write(awaitingSession);
                session = awaitingSession;
            }
        }
        EnsureAbortableSessionMatchesRepository(repository, session);
        EnsureMergeRepositoryIsUnlocked(repository);
        var originalCommit = repository.Lookup<Commit>(
            session.OriginalHeadCommitId)!;
        EnsureMergeTreeSupported(repository, originalCommit.Tree);
        var targetPaths = TargetTreePaths.Create(originalCommit.Tree);

        var abortingSession = session with
        {
            Phase = FolderProjectMergeSessionPhase.Aborting,
        };
        var abortingWritten = false;
        try
        {
            store.Write(abortingSession);
            abortingWritten = true;
            EnsureNoIgnoredTargetCollisions(
                repository,
                projectRoot,
                repository.Head);
            EnsureNoUntrackedTargetCollisions(
                repository,
                projectRoot,
                originalCommit.Tree);
            _platform.Reset(
                repository,
                originalCommit,
                new CheckoutOptions
                {
                    CheckoutModifiers = CheckoutModifiers.Force,
                    CheckoutNotifyFlags =
                        CheckoutNotifyFlags.Dirty |
                        CheckoutNotifyFlags.Untracked |
                        CheckoutNotifyFlags.Ignored,
                    OnCheckoutNotify = (path, flags) =>
                        CheckoutMayProceed(
                            projectRoot,
                            targetPaths,
                            path,
                            flags),
                });
        }
        catch (Exception failure)
        {
            if (IsVerifiedAbortResult(repository, session))
            {
                try
                {
                    store.Delete();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException(
                        "The merge abort completed, but session cleanup failed.",
                        [failure, cleanupFailure]);
                }
                return;
            }

            if (abortingWritten &&
                AwaitingSessionMatchesRepository(
                    repository,
                    session,
                    allowUntracked: true))
            {
                try
                {
                    store.Write(session);
                }
                catch (Exception rollbackFailure)
                {
                    throw new AggregateException(
                        "The merge abort failed and the session could not be restored.",
                        [failure, rollbackFailure]);
                }
            }

            throw;
        }

        if (!IsVerifiedAbortResult(repository, session))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge abort did not reach a verifiable state.");
        }

        store.Delete();
    }

    private static void EnsureOwnedActiveMerge(
        FolderProjectMergeState state)
    {
        if (state.Phase == FolderProjectMergePhase.None)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeNotActive,
                "There is no active folder-project merge.");
        }
        if (state.Phase == FolderProjectMergePhase.RecoveryRequired)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The merge state requires recovery before it can be changed.");
        }
    }

    private static void EnsureMergeRepositoryIsUnlocked(
        Repository repository)
    {
        if (File.Exists(Path.Combine(repository.Info.Path, "index.lock")))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryBusy,
                "The folder-project repository is busy.");
        }
    }

    private void EnsureAwaitingSessionMatchesRepository(
        Repository repository,
        FolderProjectMergeSession session)
    {
        if (!AwaitingSessionMatchesRepository(repository, session))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The active merge changed outside Asset Editor.");
        }
    }

    private bool AwaitingSessionMatchesRepository(
        Repository repository,
        FolderProjectMergeSession session,
        bool allowUntracked = false)
    {
        if (session.Phase != FolderProjectMergeSessionPhase.AwaitingUser ||
            repository.Info.IsHeadDetached ||
            !repository.Head.CanonicalName.Equals(
                session.OriginalBranchReference,
                StringComparison.Ordinal) ||
            repository.Head.Tip?.Sha != session.OriginalHeadCommitId ||
            repository.Lookup<Commit>(session.SourceHeadCommitId) == null ||
            repository.Info.CurrentOperation != CurrentOperation.Merge ||
            !MergeHeadMatchesSession(repository, session) ||
            session.ExpectedIndexFingerprint !=
            CalculateIndexFingerprint(repository.Index) ||
            !MergeWorktreeMatchesSession(
                repository,
                session,
                allowUntracked))
        {
            return false;
        }

        return true;
    }

    private void EnsureAbortableSessionMatchesRepository(
        Repository repository,
        FolderProjectMergeSession session)
    {
        if (!AwaitingSessionMatchesRepository(
                repository,
                session,
                allowUntracked: true))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.MergeRecoveryRequired,
                "The active merge changed and cannot be aborted safely.");
        }
    }

    private static bool MergeHeadMatchesSession(
        Repository repository,
        FolderProjectMergeSession session)
    {
        var mergeHeadPath = Path.Combine(
            repository.Info.Path,
            "MERGE_HEAD");
        try
        {
            if (!File.Exists(mergeHeadPath))
                return false;

            var mergeHeads = File.ReadAllLines(mergeHeadPath)
                .Select(line => line.Trim())
                .Where(line => line.Length != 0)
                .ToList();
            return mergeHeads.Count == 1 &&
                   mergeHeads[0].Equals(
                       session.SourceHeadCommitId,
                       StringComparison.Ordinal);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private Commit? GetVerifiedCompletedMergeCommit(
        Repository repository,
        FolderProjectMergeSession session)
    {
        var head = repository.Head.Tip;
        return repository.Info.CurrentOperation == CurrentOperation.None &&
               head != null &&
               IsCompletedMergeCommit(head, session) &&
               !RetrieveWorkingStatus(repository).IsDirty &&
               !HasUnreadableWorkingPaths(repository)
            ? head
            : null;
    }

    private bool IsVerifiedAbortResult(
        Repository repository,
        FolderProjectMergeSession session)
    {
        return !repository.Info.IsHeadDetached &&
               repository.Head.CanonicalName.Equals(
                   session.OriginalBranchReference,
                   StringComparison.Ordinal) &&
               repository.Head.Tip?.Sha == session.OriginalHeadCommitId &&
               repository.Info.CurrentOperation == CurrentOperation.None &&
               !repository.Index.Conflicts.Any() &&
               !HasTrackedWorkingChanges(repository) &&
               !HasUnreadableWorkingPaths(repository);
    }

    private void ResolveConflictInIndexAndWorktree(
        Repository repository,
        string projectRoot,
        IReadOnlyList<string> affectedPaths,
        IndexEntry? selectedEntry,
        Action<string> onPlannedIndexFingerprint)
    {
        foreach (var repositoryPath in affectedPaths)
        {
            var fullPath = ResolveRestoreTarget(
                projectRoot,
                repositoryPath);
            EnsureNoReparsePoints(
                projectRoot,
                fullPath,
                includeLeaf: true);
            var attributes = TryGetAttributes(fullPath);
            if (attributes != null &&
                ((attributes & FileAttributes.Directory) != 0 ||
                 (attributes & FileAttributes.ReparsePoint) != 0))
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.WorkingTreeNotClean,
                    "A conflict path is occupied by a directory or reparse point.");
            }
            if (attributes != null)
            {
                File.SetAttributes(fullPath, FileAttributes.Normal);
                _platform.DeleteFile(fullPath);
            }
            repository.Index.Remove(repositoryPath);
        }

        if (selectedEntry != null)
        {
            if (selectedEntry.Mode is not (
                    Mode.NonExecutableFile or Mode.ExecutableFile))
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.UnsupportedCommitPath,
                    "The selected merge side has an unsupported file mode.");
            }
            var selectedPath = ValidateResourcePath(selectedEntry.Path);
            var blob = repository.Lookup<Blob>(selectedEntry.Id) ??
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryFailure,
                    "The selected merge side references a missing blob.");
            var selectedTarget = ResolveRestoreTarget(
                projectRoot,
                selectedPath);
            EnsureNoReparsePoints(
                projectRoot,
                selectedTarget,
                includeLeaf: true);
            WriteBlobAtomically(
                blob,
                projectRoot,
                selectedPath,
                selectedTarget);
            repository.Index.Add(
                blob,
                selectedPath,
                selectedEntry.Mode);
        }

        onPlannedIndexFingerprint(
            CalculateIndexFingerprint(repository.Index));
        repository.Index.Write();
    }

    private static IReadOnlyList<string> GetConflictPaths(
        MergeConflictGroup group)
    {
        return group.RawConflicts
            .SelectMany(
                conflict => new[]
                {
                    conflict.Ancestor?.Path,
                    conflict.Ours?.Path,
                    conflict.Theirs?.Path,
                })
            .Append(group.Ancestor?.Path)
            .Append(group.Current?.Path)
            .Append(group.Incoming?.Path)
            .Where(path => path != null)
            .Select(path => ValidateResourcePath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    private void EnsureNoUntrackedTargetCollisions(
        Repository repository,
        string projectRoot,
        Tree targetTree)
    {
        var targetPaths = TargetTreePaths.Create(targetTree);
        var status = repository.RetrieveStatus(
            new StatusOptions
            {
                IncludeIgnored = false,
                IncludeUntracked = true,
                RecurseUntrackedDirs = true,
            });
        foreach (var entry in status.Where(
                     entry => HasAny(
                         entry.State,
                         FileStatus.NewInWorkdir)))
        {
            var repositoryPath = entry.FilePath
                .Replace('\\', '/')
                .TrimEnd('/');
            if (repositoryPath.Length == 0)
                continue;
            if (targetPaths.NonDirectoryPaths.Contains(repositoryPath) ||
                targetPaths.HasNonDirectoryAncestor(repositoryPath))
            {
                ThrowUntrackedTargetCollision();
            }
            if (!targetPaths.ParentDirectories.Contains(repositoryPath))
                continue;

            var fullPath = ResolveRestoreTarget(
                projectRoot,
                repositoryPath);
            var attributes = _platform.GetAttributes(fullPath);
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
            {
                ThrowUntrackedTargetCollision();
            }
        }
    }

    private static void ThrowUntrackedTargetCollision()
    {
        throw new FolderProjectVersionControlException(
            FolderProjectVersionControlError.WorkingTreeNotClean,
            "An untracked file would be overwritten while aborting the merge.");
    }

    private FolderProjectMergeState GetMergeStateCore(
        Repository repository)
    {
        var store = new FolderProjectMergeSessionStore(
            repository.Info.Path,
            _platform);
        FolderProjectMergeSession? session;
        try
        {
            session = store.Read();
        }
        catch (Exception exception)
            when (exception is InvalidDataException or
                  JsonException or
                  IOException or
                  UnauthorizedAccessException)
        {
            return CreateRecoveryMergeState(
                repository,
                "The Asset Editor merge session is damaged.");
        }

        if (session == null)
        {
            if (repository.Info.CurrentOperation == CurrentOperation.Revert &&
                !repository.Index.Conflicts.Any() &&
                repository.Head.Tip is { } revertHead)
            {
                try
                {
                    repository.Reset(ResetMode.Mixed, revertHead);
                }
                catch (Exception exception)
                    when (exception is LibGit2SharpException or
                          IOException or
                          UnauthorizedAccessException)
                {
                    return CreateRecoveryMergeState(
                        repository,
                        "The pending revert could not be finalized safely.");
                }

            }

            if (repository.Info.CurrentOperation == CurrentOperation.None &&
                !repository.Index.Conflicts.Any())
            {
                return CreateIdleMergeState(repository);
            }

            return CreateRecoveryMergeState(
                repository,
                "The repository operation was not started by Asset Editor.");
        }

        var original = repository.Lookup<Commit>(
            session.OriginalHeadCommitId);
        var source = repository.Lookup<Commit>(
            session.SourceHeadCommitId);
        var sourceBranch = repository.Branches.FirstOrDefault(
            branch =>
                !branch.IsRemote &&
                branch.CanonicalName.Equals(
                    session.SourceBranchReference,
                    StringComparison.Ordinal));
        if (original == null ||
            source == null ||
            sourceBranch?.Tip?.Sha != source.Sha ||
            repository.Info.IsHeadDetached ||
            !repository.Head.CanonicalName.Equals(
                session.OriginalBranchReference,
                StringComparison.Ordinal))
        {
            return CreateRecoveryMergeState(
                repository,
                "The merge session no longer matches the current branch.",
                session);
        }

        var head = repository.Head.Tip;
        if (session.Phase ==
                FolderProjectMergeSessionPhase.NativeMergeRunning &&
            repository.Info.CurrentOperation == CurrentOperation.None &&
            head?.Sha == source.Sha &&
            IsAncestor(repository, original, source) &&
            !RetrieveWorkingStatus(repository).IsDirty &&
            !HasUnreadableWorkingPaths(repository))
        {
            TryDeleteCompletedSession(store);
            return CreateIdleMergeState(repository);
        }

        if (session.Phase == FolderProjectMergeSessionPhase.Completing &&
            repository.Info.CurrentOperation == CurrentOperation.None &&
            head != null &&
            IsCompletedMergeCommit(head, session) &&
            !RetrieveWorkingStatus(repository).IsDirty &&
            !HasUnreadableWorkingPaths(repository))
        {
            TryDeleteCompletedSession(store);
            return CreateIdleMergeState(repository);
        }

        if (repository.Info.CurrentOperation == CurrentOperation.None &&
            head?.Sha == original.Sha &&
            !RetrieveWorkingStatus(repository).IsDirty &&
            !HasUnreadableWorkingPaths(repository) &&
            session.Phase == FolderProjectMergeSessionPhase.Prepared)
        {
            TryDeleteCompletedSession(store);
            return CreateIdleMergeState(repository);
        }

        if (session.Phase == FolderProjectMergeSessionPhase.Aborting &&
            IsVerifiedAbortResult(repository, session))
        {
            TryDeleteCompletedSession(store);
            return CreateIdleMergeState(repository);
        }

        if (session.Phase ==
                FolderProjectMergeSessionPhase.NativeMergeRunning &&
            repository.Info.CurrentOperation == CurrentOperation.Merge &&
            head?.Sha == original.Sha &&
            session.ExpectedIndexFingerprint != null &&
            session.ExpectedIndexFingerprint ==
            CalculateIndexFingerprint(repository.Index) &&
            !repository.Index.Conflicts.Any() &&
            MergeHeadMatchesSession(repository, session))
        {
            try
            {
                var reconciled = session with
                {
                    Phase = FolderProjectMergeSessionPhase.AwaitingUser,
                    ConflictWorktreeFingerprints =
                        CaptureConflictWorktreeFingerprints(
                            repository,
                            repository.Info.WorkingDirectory),
                };
                if (!MergeWorktreeMatchesSession(repository, reconciled))
                {
                    return CreateRecoveryMergeState(
                        repository,
                        "The native merge did not reach a verifiable state.",
                        session);
                }

                store.Write(reconciled);
                return CreateActiveMergeState(repository, reconciled);
            }
            catch (Exception exception)
                when (exception is FolderProjectVersionControlException or
                      InvalidDataException or
                      IOException or
                      UnauthorizedAccessException)
            {
                return CreateRecoveryMergeState(
                    repository,
                    "The recovered merge state could not be recorded safely.",
                    session);
            }
        }

        if (session.Phase is FolderProjectMergeSessionPhase.Completing or
                FolderProjectMergeSessionPhase.Aborting)
        {
            var reconciled = session with
            {
                Phase = FolderProjectMergeSessionPhase.AwaitingUser,
                ExpectedCommitTreeId = null,
            };
            var allowUntracked =
                session.Phase == FolderProjectMergeSessionPhase.Aborting;
            if (AwaitingSessionMatchesRepository(
                    repository,
                    reconciled,
                    allowUntracked))
            {
                try
                {
                    store.Write(reconciled);
                }
                catch (Exception exception)
                    when (exception is InvalidDataException or
                          IOException or
                          UnauthorizedAccessException)
                {
                    return CreateRecoveryMergeState(
                        repository,
                        "The interrupted merge session could not be saved.",
                        session);
                }
                return CreateActiveMergeState(repository, reconciled);
            }
        }

        if (AwaitingSessionMatchesRepository(repository, session))
        {
            return CreateActiveMergeState(repository, session);
        }

        return CreateRecoveryMergeState(
            repository,
            "The repository changed outside the active merge session.",
            session);
    }

    private void EnsureMergeStartStateSupported(
        Repository repository,
        string projectRoot)
    {
        if (repository.Info.IsHeadDetached ||
            repository.Info.IsHeadUnborn ||
            repository.Head.Tip == null ||
            repository.Info.CurrentOperation != CurrentOperation.None ||
            repository.Index.Conflicts.Any())
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedOperationState,
                "Merging is unavailable in the current repository state.");
        }
        if (File.Exists(Path.Combine(repository.Info.Path, "index.lock")))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryBusy,
                "The folder-project repository is busy.");
        }
        if (RetrieveWorkingStatus(repository).IsDirty ||
            GetWorkingChanges(repository, projectRoot).Any(
                change => HasAny(
                    change.Kind,
                    FolderProjectWorkingChangeKind.Unreadable)))
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.WorkingTreeNotClean,
                "The working tree must be clean before merging.");
        }
    }

    private static void EnsureMergeTreeSupported(
        Repository repository,
        Tree tree)
    {
        foreach (var entry in tree)
        {
            if (entry.TargetType == TreeEntryTargetType.Tree &&
                entry.Target is Tree childTree)
            {
                EnsureMergeTreeSupported(repository, childTree);
                continue;
            }
            if (entry.TargetType == TreeEntryTargetType.Blob &&
                entry.Mode is Mode.NonExecutableFile or Mode.ExecutableFile)
            {
                continue;
            }

            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedCommitPath,
                "The merge contains a file mode that folder projects cannot preserve safely.");
        }

        if (repository.ObjectDatabase
                .CreateTree(TreeDefinition.From(tree))
                .Id != tree.Id)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedCommitPath,
                "The merge contains a file mode that folder projects cannot preserve safely.");
        }
    }

    private static string CalculateExpectedNativeMergeIndexFingerprint(
        Repository repository,
        Commit currentTip,
        Commit sourceTip)
    {
        using var expectedIndex =
            repository.ObjectDatabase.MergeCommitsIntoIndex(
                currentTip,
                sourceTip,
                new MergeTreeOptions()) ??
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryFailure,
                "The expected merge index could not be calculated.");
        return CalculateIndexFingerprint(expectedIndex);
    }

    private bool CheckoutMayProceed(
        string projectRoot,
        TargetTreePaths targetPaths,
        string checkoutPath,
        CheckoutNotifyFlags flags)
    {
        if ((flags & (CheckoutNotifyFlags.Dirty |
                      CheckoutNotifyFlags.Untracked |
                      CheckoutNotifyFlags.Ignored)) == 0)
        {
            return true;
        }

        string repositoryPath;
        try
        {
            repositoryPath = ValidateResourcePath(
                checkoutPath.TrimEnd('/', '\\'));
        }
        catch (FolderProjectVersionControlException)
        {
            return false;
        }

        if (targetPaths.NonDirectoryPaths.Contains(repositoryPath) ||
            targetPaths.HasNonDirectoryAncestor(repositoryPath))
        {
            return false;
        }
        if (!targetPaths.ParentDirectories.Contains(repositoryPath))
            return true;

        try
        {
            var attributes = _platform.GetAttributes(
                ResolveRestoreTarget(projectRoot, repositoryPath));
            return (attributes & FileAttributes.Directory) != 0 &&
                   (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private FolderProjectMergeState CreateActiveMergeState(
        Repository repository,
        FolderProjectMergeSession session)
    {
        IReadOnlyList<FolderProjectMergeConflict> conflicts;
        try
        {
            conflicts = GetMergeConflicts(repository);
        }
        catch (Exception exception)
            when (exception is FolderProjectVersionControlException or
                  LibGit2SharpException or
                  IOException or
                  UnauthorizedAccessException)
        {
            return CreateRecoveryMergeState(
                repository,
                "The merge conflict metadata cannot be read safely.",
                session);
        }
        return new FolderProjectMergeState(
            conflicts.Count == 0
                ? FolderProjectMergePhase.ReadyToCommit
                : FolderProjectMergePhase.Conflicts,
            repository.Head.FriendlyName,
            session.SourceBranchReference["refs/heads/".Length..],
            session.OriginalHeadCommitId,
            session.SourceHeadCommitId,
            $"合并分支 '{session.SourceBranchReference["refs/heads/".Length..]}'",
            conflicts,
            null);
    }

    private static FolderProjectMergeState CreateIdleMergeState(
        Repository repository)
    {
        return new FolderProjectMergeState(
            FolderProjectMergePhase.None,
            repository.Info.IsHeadDetached
                ? null
                : repository.Head.FriendlyName,
            null,
            null,
            null,
            null,
            [],
            null);
    }

    private static FolderProjectMergeState CreateRecoveryMergeState(
        Repository repository,
        string reason,
        FolderProjectMergeSession? session = null)
    {
        return new FolderProjectMergeState(
            FolderProjectMergePhase.RecoveryRequired,
            repository.Info.IsHeadDetached
                ? null
                : repository.Head.FriendlyName,
            session?.SourceBranchReference["refs/heads/".Length..],
            session?.OriginalHeadCommitId,
            session?.SourceHeadCommitId,
            null,
            [],
            reason);
    }

    private static IReadOnlyList<FolderProjectMergeConflict>
        GetMergeConflicts(Repository repository)
    {
        return GetMergeConflictGroups(repository)
            .Select(group => group.Conflict)
            .OrderBy(conflict => conflict.Id, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<MergeConflictGroup>
        GetMergeConflictGroups(Repository repository)
    {
        var rawConflicts = repository.Index.Conflicts.ToList();
        var remaining = new HashSet<Conflict>(rawConflicts);
        var groups = new List<MergeConflictGroup>();
        foreach (var nameEntry in repository.Index.Conflicts.Names)
        {
            var paths = new[]
                {
                    nameEntry.Ancestor,
                    nameEntry.Ours,
                    nameEntry.Theirs,
                }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!.Replace('\\', '/'))
                .ToHashSet(StringComparer.Ordinal);
            var members = remaining
                .Where(conflict => ConflictTouchesAnyPath(conflict, paths))
                .ToList();
            if (members.Count == 0)
                continue;

            var ancestor = FindConflictEntry(
                members,
                conflict => conflict.Ancestor,
                nameEntry.Ancestor);
            var current = FindConflictEntry(
                members,
                conflict => conflict.Ours,
                nameEntry.Ours);
            var incoming = FindConflictEntry(
                members,
                conflict => conflict.Theirs,
                nameEntry.Theirs);
            if (!RenameSideWasMapped(nameEntry.Ancestor, ancestor) ||
                !RenameSideWasMapped(nameEntry.Ours, current) ||
                !RenameSideWasMapped(nameEntry.Theirs, incoming) ||
                members.SelectMany(GetRawConflictEntries)
                    .Any(
                        entry => !paths.Contains(
                            entry.Path.Replace('\\', '/'))))
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.RepositoryFailure,
                    "The rename conflict metadata is inconsistent.");
            }

            foreach (var member in members)
                remaining.Remove(member);
            groups.Add(CreateMergeConflictGroup(
                repository,
                ancestor,
                current,
                incoming,
                members));
        }

        groups.AddRange(
            remaining.Select(
                conflict => CreateMergeConflictGroup(
                    repository,
                    conflict.Ancestor,
                    conflict.Ours,
                    conflict.Theirs,
                    [conflict])));
        return groups;
    }

    private static bool ConflictTouchesAnyPath(
        Conflict conflict,
        IReadOnlySet<string> paths)
    {
        return new[]
            {
                conflict.Ancestor?.Path,
                conflict.Ours?.Path,
                conflict.Theirs?.Path,
            }
            .Any(
                path => path != null &&
                        paths.Contains(path.Replace('\\', '/')));
    }

    private static IndexEntry? FindConflictEntry(
        IEnumerable<Conflict> conflicts,
        Func<Conflict, IndexEntry?> selector,
        string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var normalizedPath = path.Replace('\\', '/');
        var matches = conflicts
            .Select(selector)
            .Where(
                entry => entry?.Path.Replace('\\', '/').Equals(
                    normalizedPath,
                    StringComparison.Ordinal) == true)
            .ToList();
        if (matches.Count != 1)
            return null;

        return matches[0];
    }

    private static bool RenameSideWasMapped(
        string? path,
        IndexEntry? entry)
    {
        return string.IsNullOrWhiteSpace(path)
            ? entry == null
            : entry != null;
    }

    private static IEnumerable<IndexEntry> GetRawConflictEntries(
        Conflict conflict)
    {
        if (conflict.Ancestor != null)
            yield return conflict.Ancestor;
        if (conflict.Ours != null)
            yield return conflict.Ours;
        if (conflict.Theirs != null)
            yield return conflict.Theirs;
    }

    private static MergeConflictGroup CreateMergeConflictGroup(
        Repository repository,
        IndexEntry? ancestor,
        IndexEntry? current,
        IndexEntry? incoming,
        IReadOnlyList<Conflict> rawConflicts)
    {
        return new MergeConflictGroup(
            new FolderProjectMergeConflict(
                CalculateConflictId(ancestor, current, incoming),
                ToMergeSide(repository, ancestor),
                ToMergeSide(repository, current),
                ToMergeSide(repository, incoming)),
            ancestor,
            current,
            incoming,
            rawConflicts);
    }

    private static FolderProjectMergeSide? ToMergeSide(
        Repository repository,
        IndexEntry? entry)
    {
        if (entry == null)
            return null;

        var repositoryPath = ValidateResourcePath(entry.Path);
        var mode = ToDomainFileMode(entry.Mode);
        var blob = repository.Lookup<Blob>(entry.Id);
        if (blob == null)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.RepositoryFailure,
                "A merge conflict references a missing blob.");
        }
        return new FolderProjectMergeSide(
            repositoryPath,
            entry.Id.Sha,
            mode,
            blob.Size,
            blob.IsBinary);
    }

    private static FolderProjectGitFileMode ToDomainFileMode(Mode mode)
    {
        return mode switch
        {
            Mode.NonExecutableFile =>
                FolderProjectGitFileMode.NonExecutable,
            Mode.NonExecutableGroupWritableFile =>
                FolderProjectGitFileMode.NonExecutableGroupWritable,
            Mode.ExecutableFile =>
                FolderProjectGitFileMode.Executable,
            _ => throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedCommitPath,
                "The merge contains an unsupported file mode."),
        };
    }

    private static string CalculateConflictId(
        IndexEntry? ancestor,
        IndexEntry? current,
        IndexEntry? incoming)
    {
        var value = string.Join(
            "\n",
            SerializeConflictEntry(ancestor),
            SerializeConflictEntry(current),
            SerializeConflictEntry(incoming));
        return Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private static string SerializeConflictEntry(IndexEntry? entry)
    {
        return entry == null
            ? "-"
            : $"{entry.Path.Replace('\\', '/')}\0{entry.Id.Sha}\0{(int)entry.Mode}";
    }

    private static string CalculateIndexFingerprint(
        LibGit2Sharp.Index index)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in index
                     .OrderBy(entry => entry.Path, StringComparer.Ordinal)
                     .ThenBy(entry => entry.StageLevel))
        {
            AppendFingerprintValue(hash, entry.Path.Replace('\\', '/'));
            AppendFingerprintValue(hash, ((int)entry.StageLevel).ToString());
            AppendFingerprintValue(hash, ((int)entry.Mode).ToString());
            AppendFingerprintValue(hash, entry.Id.Sha);
        }

        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();
    }

    private static void AppendFingerprintValue(
        IncrementalHash hash,
        string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        hash.AppendData(BitConverter.GetBytes(bytes.Length));
        hash.AppendData(bytes);
    }

    private IReadOnlyDictionary<string, string>
        CaptureConflictWorktreeFingerprints(
            Repository repository,
            string projectRoot)
    {
        var result = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var repositoryPath in repository.Index.Conflicts
                     .SelectMany(
                         conflict => new[]
                         {
                             conflict.Ancestor?.Path,
                             conflict.Ours?.Path,
                             conflict.Theirs?.Path,
                         })
                     .Where(path => path != null)
                     .Select(path => ValidateResourcePath(path!))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = ResolveRestoreTarget(projectRoot, repositoryPath);
            EnsureNoReparsePoints(
                projectRoot,
                fullPath,
                includeLeaf: true);
            result[repositoryPath] =
                CalculateWorktreeFingerprint(fullPath);
        }

        return result;
    }

    private IReadOnlyDictionary<string, string>
        CaptureWorktreeFingerprints(
            string projectRoot,
            IEnumerable<string> repositoryPaths)
    {
        var result = new Dictionary<string, string>(
            StringComparer.Ordinal);
        foreach (var repositoryPath in repositoryPaths
                     .Select(ValidateResourcePath)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = ResolveRestoreTarget(projectRoot, repositoryPath);
            EnsureNoReparsePoints(
                projectRoot,
                fullPath,
                includeLeaf: true);
            result[repositoryPath] =
                CalculateWorktreeFingerprint(fullPath);
        }

        return result;
    }

    private static string CalculateWorktreeFingerprint(string fullPath)
    {
        FileAttributes? attributes;
        try
        {
            attributes = File.GetAttributes(fullPath);
        }
        catch (FileNotFoundException)
        {
            attributes = null;
        }
        catch (DirectoryNotFoundException)
        {
            attributes = null;
        }
        if (attributes != null &&
            (attributes & FileAttributes.Directory) != 0)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.WorkingTreeNotClean,
                "A merge path is occupied by a directory.");
        }
        if (attributes == null)
            return "missing";
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new FolderProjectVersionControlException(
                FolderProjectVersionControlError.UnsupportedCommitPath,
                "A merge path is occupied by a reparse point.");
        }

        return "file:" +
               Convert.ToHexString(
                       SHA256.HashData(File.ReadAllBytes(fullPath)))
                   .ToLowerInvariant();
    }

    private FileAttributes? TryGetAttributes(string path)
    {
        return TryGetAttributes(path, _platform);
    }

    private static FileAttributes? TryGetAttributes(
        string path,
        FolderProjectVersionControlPlatform platform)
    {
        try
        {
            return platform.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private void EnsureNoReparsePoints(
        string projectRoot,
        string fullPath,
        bool includeLeaf)
    {
        EnsureNoReparsePoints(
            projectRoot,
            fullPath,
            includeLeaf,
            _platform);
    }

    private static void EnsureNoReparsePoints(
        string projectRoot,
        string fullPath,
        bool includeLeaf,
        FolderProjectVersionControlPlatform platform)
    {
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        var target = Path.GetFullPath(fullPath);
        var relativePath = Path.GetRelativePath(root, target);
        var parts = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var partCount = includeLeaf
            ? parts.Length
            : Math.Max(0, parts.Length - 1);
        var current = root;
        for (var index = 0; index < partCount; index++)
        {
            current = Path.Combine(current, parts[index]);
            var attributes = TryGetAttributes(current, platform);
            if (attributes == null)
                continue;
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.UnsupportedCommitPath,
                    "A merge path traverses a reparse point.");
            }
            if (index < parts.Length - 1 &&
                (attributes & FileAttributes.Directory) == 0)
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.WorkingTreeNotClean,
                    "A merge path is blocked by a file.");
            }
        }
    }

    private bool MergeWorktreeMatchesSession(
        Repository repository,
        FolderProjectMergeSession session,
        bool allowUntracked = false)
    {
        foreach (var entry in session.ConflictWorktreeFingerprints)
        {
            try
            {
                var fullPath = ResolveRestoreTarget(
                    repository.Info.WorkingDirectory,
                    entry.Key);
                EnsureNoReparsePoints(
                    repository.Info.WorkingDirectory,
                    fullPath,
                    includeLeaf: true);
                if (!string.Equals(
                        CalculateWorktreeFingerprint(fullPath),
                        entry.Value,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            catch (Exception exception)
                when (exception is FolderProjectVersionControlException or
                      IOException or
                      UnauthorizedAccessException)
            {
                return false;
            }
        }

        var conflictPaths = session.ConflictWorktreeFingerprints.Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (HasUnreadableWorkingPaths(repository))
                return false;

            return RetrieveWorkingStatus(repository).All(
                entry =>
                {
                    const FileStatus worktreeChanges =
                        FileStatus.NewInWorkdir |
                        FileStatus.ModifiedInWorkdir |
                        FileStatus.DeletedFromWorkdir |
                        FileStatus.RenamedInWorkdir |
                        FileStatus.TypeChangeInWorkdir |
                        FileStatus.Unreadable;
                    var worktreeState = entry.State & worktreeChanges;
                    if (worktreeState == FileStatus.Unaltered)
                        return true;
                    if (allowUntracked &&
                        worktreeState == FileStatus.NewInWorkdir)
                    {
                        return true;
                    }

                    return conflictPaths.Contains(
                        entry.FilePath.Replace('\\', '/')) &&
                           HasAny(entry.State, FileStatus.Conflicted);
                });
        }
        catch (Exception exception)
            when (exception is LibGit2SharpException or
                  IOException or
                  UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool HasUnreadableWorkingPaths(Repository repository)
    {
        return GetWorkingChanges(
                repository,
                repository.Info.WorkingDirectory)
            .Any(
                change => HasAny(
                    change.Kind,
                    FolderProjectWorkingChangeKind.Unreadable));
    }

    private static bool HasTrackedWorkingChanges(Repository repository)
    {
        return repository.RetrieveStatus(
                new StatusOptions
                {
                    IncludeIgnored = false,
                    IncludeUntracked = true,
                    RecurseUntrackedDirs = true,
                })
            .Any(entry => entry.State != FileStatus.NewInWorkdir);
    }

    private static void TryDeleteCompletedSession(
        FolderProjectMergeSessionStore store)
    {
        try
        {
            store.Delete();
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsCompletedMergeCommit(
        Commit commit,
        FolderProjectMergeSession session)
    {
        var parents = commit.Parents.Select(parent => parent.Sha).ToList();
        return parents.Count == 2 &&
               parents[0] == session.OriginalHeadCommitId &&
               parents[1] == session.SourceHeadCommitId &&
               session.ExpectedCommitTreeId != null &&
               commit.Tree.Id.Sha == session.ExpectedCommitTreeId;
    }

    private static bool IsAncestor(
        Repository repository,
        Commit possibleAncestor,
        Commit commit)
    {
        var divergence = repository.ObjectDatabase
            .CalculateHistoryDivergence(possibleAncestor, commit);
        return divergence.CommonAncestor?.Sha == possibleAncestor.Sha &&
               divergence.AheadBy == 0;
    }

    private T ExecuteMergeLocked<T>(
        string projectRoot,
        Func<string, T> action)
    {
        var root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(projectRoot));
        var gate = s_mergeGates.GetOrAdd(root, _ => new object());
        lock (gate)
            return action(root);
    }

    private sealed record MergeConflictGroup(
        FolderProjectMergeConflict Conflict,
        IndexEntry? Ancestor,
        IndexEntry? Current,
        IndexEntry? Incoming,
        IReadOnlyList<Conflict> RawConflicts);

    private sealed record MergeWorktreeFileSnapshot(
        string RepositoryPath,
        string FullPath,
        bool Exists,
        byte[] Bytes,
        FileAttributes Attributes)
    {
        public string Fingerprint => Exists
            ? "file:" +
              Convert.ToHexString(SHA256.HashData(Bytes))
                  .ToLowerInvariant()
            : "missing";

        public static MergeWorktreeFileSnapshot Capture(
            string projectRoot,
            string repositoryPath,
            FolderProjectVersionControlPlatform platform)
        {
            repositoryPath = ValidateResourcePath(repositoryPath);
            var fullPath = ResolveRestoreTarget(
                projectRoot,
                repositoryPath);
            EnsureNoReparsePoints(
                projectRoot,
                fullPath,
                includeLeaf: true,
                platform);
            var attributes = TryGetAttributes(fullPath, platform);
            if (attributes != null &&
                ((attributes & FileAttributes.Directory) != 0 ||
                 (attributes & FileAttributes.ReparsePoint) != 0))
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.WorkingTreeNotClean,
                    "A conflict path is occupied by a directory or reparse point.");
            }
            if (attributes == null)
            {
                return new MergeWorktreeFileSnapshot(
                    repositoryPath,
                    fullPath,
                    false,
                    [],
                    FileAttributes.Normal);
            }

            return new MergeWorktreeFileSnapshot(
                repositoryPath,
                fullPath,
                true,
                File.ReadAllBytes(fullPath),
                attributes.Value);
        }

        public void Restore(
            string projectRoot,
            FolderProjectVersionControlPlatform platform,
            IReadOnlySet<string> allowedCurrentFingerprints)
        {
            EnsureNoReparsePoints(
                projectRoot,
                FullPath,
                includeLeaf: true,
                platform);
            var currentFingerprint =
                CalculateWorktreeFingerprint(FullPath);
            if (!allowedCurrentFingerprints.Contains(currentFingerprint))
            {
                throw new FolderProjectVersionControlException(
                    FolderProjectVersionControlError.MergeRecoveryRequired,
                    "A conflict path changed while resolution was rolling back.");
            }
            if (currentFingerprint == Fingerprint)
                return;

            if (!Exists)
            {
                File.SetAttributes(FullPath, FileAttributes.Normal);
                platform.DeleteFile(FullPath);
                return;
            }

            var directoryPath = Path.GetDirectoryName(FullPath)!;
            Directory.CreateDirectory(directoryPath);
            EnsureNoReparsePoints(
                projectRoot,
                FullPath,
                includeLeaf: true,
                platform);
            if (TryGetAttributes(FullPath, platform) != null)
                File.SetAttributes(FullPath, FileAttributes.Normal);
            var temporaryPath = Path.Combine(
                directoryPath,
                $".{Path.GetFileName(FullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporaryPath, Bytes);
                File.SetAttributes(temporaryPath, Attributes);
                EnsureNoReparsePoints(
                    projectRoot,
                    FullPath,
                    includeLeaf: true,
                    platform);
                if (!allowedCurrentFingerprints.Contains(
                        CalculateWorktreeFingerprint(FullPath)))
                {
                    throw new FolderProjectVersionControlException(
                        FolderProjectVersionControlError.MergeRecoveryRequired,
                        "A conflict path changed while resolution was rolling back.");
                }
                platform.MoveFile(
                    temporaryPath,
                    FullPath,
                    overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.SetAttributes(
                        temporaryPath,
                        FileAttributes.Normal);
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
