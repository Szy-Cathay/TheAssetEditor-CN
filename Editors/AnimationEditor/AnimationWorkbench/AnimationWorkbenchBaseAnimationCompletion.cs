using System.IO;
using GameWorld.Core.Animation;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;
using Shared.Ui.Common.OperationProgress;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using AnimationBinEntry = Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3.AnimationBinEntry;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum AnimationWorkbenchBaseAnimationRole
{
    Idle,
    Walk,
    Run,
    HitReaction,
    Death,
    Other,
}

public enum AnimationWorkbenchBaseAnimationStyleMode
{
    None,
    PreserveStanceAndMotion,
    PreserveMotion,
}

public enum AnimationWorkbenchBaseAnimationItemStatus
{
    Ready,
    Saved,
    Skipped,
    Failed,
    NotProcessed,
}

public sealed record AnimationWorkbenchBaseAnimationRecipeItem(
    string SourcePath,
    string OutputPath,
    AnimationWorkbenchBaseAnimationRole Role,
    AnimationWorkbenchSourceInput Source);

public sealed record AnimationWorkbenchBaseAnimationRequest(
    IReadOnlyList<AnimationWorkbenchBaseAnimationRecipeItem> Items,
    GameSkeleton TargetSkeleton,
    AnimationWorkbenchSourceInput? StyleReference,
    AnimationWorkbenchBaseAnimationStyleMode StyleMode,
    double StyleWeight,
    bool IncludeRootMotion,
    IReadOnlyList<AnimationWorkbenchRetargetBoneMapping>? Mappings,
    AnimationWorkbenchSourceFormat OutputFormat)
{
    public string? AnimationSetOutputPath { get; init; }
}

public sealed record AnimationWorkbenchBaseAnimationSetCandidate(
    string OutputPath,
    AnimationWorkbenchBaseAnimationItemStatus Status,
    byte[]? Bytes,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics,
    string? ErrorMessage = null);

public sealed record AnimationWorkbenchBaseAnimationCandidate(
    string SourcePath,
    string OutputPath,
    AnimationWorkbenchBaseAnimationRole Role,
    AnimationWorkbenchBaseAnimationItemStatus Status,
    byte[]? Bytes,
    AnimationClip? PreviewAnimation,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics,
    string? ErrorMessage = null);

public sealed class AnimationWorkbenchBaseAnimationCompletionResult(
    IReadOnlyList<AnimationWorkbenchBaseAnimationCandidate> items,
    AnimationWorkbenchBaseAnimationSetCandidate? animationSet = null)
{
    public IReadOnlyList<AnimationWorkbenchBaseAnimationCandidate> Items
    {
        get;
    } = items;

    public AnimationWorkbenchBaseAnimationSetCandidate? AnimationSet
    {
        get;
    } = animationSet;

    public int ReadyCount => Count(
        AnimationWorkbenchBaseAnimationItemStatus.Ready);

    public int SavedCount => Count(
        AnimationWorkbenchBaseAnimationItemStatus.Saved);

    public int SkippedCount => Count(
        AnimationWorkbenchBaseAnimationItemStatus.Skipped);

    public int FailedCount => Count(
        AnimationWorkbenchBaseAnimationItemStatus.Failed);

    public int NotProcessedCount => Count(
        AnimationWorkbenchBaseAnimationItemStatus.NotProcessed);

    private int Count(AnimationWorkbenchBaseAnimationItemStatus status) =>
        Items.Count(item => item.Status == status);
}

public static class AnimationWorkbenchBaseAnimationClassifier
{
    private static readonly HashSet<string> s_roleFolders = new(
        [
            "stand",
            "idle",
            "walk",
            "run",
            "locomotion",
            "move",
            "movement",
            "hit",
            "hits",
            "reaction",
            "reactions",
            "flinch",
            "death",
            "deaths",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static AnimationWorkbenchBaseAnimationRole Classify(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var tokens = path
            .Split(
                ['\\', '/', '_', '-', '.', ' '],
                StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ContainsAny(tokens, "death", "deaths", "die", "dying", "dead"))
            return AnimationWorkbenchBaseAnimationRole.Death;
        if (ContainsAny(
                tokens,
                "hit",
                "hits",
                "reaction",
                "reactions",
                "stagger",
                "flinch",
                "knockback",
                "impact"))
        {
            return AnimationWorkbenchBaseAnimationRole.HitReaction;
        }
        if (ContainsAny(tokens, "idle"))
            return AnimationWorkbenchBaseAnimationRole.Idle;
        if (ContainsAny(
                tokens,
                "to",
                "turn",
                "jump",
                "attack",
                "attacks",
                "combat",
                "aim",
                "shoot",
                "reload",
                "charge"))
        {
            return AnimationWorkbenchBaseAnimationRole.Other;
        }
        if (ContainsAny(tokens, "walk", "march"))
            return AnimationWorkbenchBaseAnimationRole.Walk;
        if (ContainsAny(tokens, "run", "sprint"))
            return AnimationWorkbenchBaseAnimationRole.Run;
        if (ContainsAny(tokens, "stand"))
            return AnimationWorkbenchBaseAnimationRole.Idle;
        return AnimationWorkbenchBaseAnimationRole.Other;
    }

    private static bool ContainsAny(
        IReadOnlySet<string> tokens,
        params string[] values) => values.Any(tokens.Contains);

    public static string GetFamilyRoot(string selectedAnimationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedAnimationPath);
        var normalized = selectedAnimationPath.Replace('/', '\\');
        var segments = normalized.Split(
            '\\',
            StringSplitOptions.RemoveEmptyEntries);
        var fileSegmentIndex = segments.Length - 1;
        var roleSegmentIndex = Array.FindIndex(
            segments,
            0,
            fileSegmentIndex,
            segment => s_roleFolders.Contains(segment));
        var rootLength = roleSegmentIndex >= 0
            ? roleSegmentIndex
            : Math.Max(0, fileSegmentIndex);
        return string.Join('\\', segments.Take(rootLength));
    }

    public static bool IsInFamily(
        string animationPath,
        string familyRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(animationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(familyRoot);
        var normalizedPath = animationPath.Replace('/', '\\');
        var normalizedRoot = familyRoot.TrimEnd('\\', '/');
        return normalizedPath.StartsWith(
            normalizedRoot + "\\",
            StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AnimationWorkbenchBaseAnimationCompletionModule
{
    private static readonly AnimationWorkbenchBaseAnimationRole[]
        s_requiredRoles =
        [
            AnimationWorkbenchBaseAnimationRole.Idle,
            AnimationWorkbenchBaseAnimationRole.Walk,
            AnimationWorkbenchBaseAnimationRole.Run,
            AnimationWorkbenchBaseAnimationRole.HitReaction,
            AnimationWorkbenchBaseAnimationRole.Death,
        ];

    public AnimationWorkbenchBaseAnimationCompletionResult Generate(
        AnimationWorkbenchBaseAnimationRequest request,
        IProgress<OperationProgressUpdate>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Items);
        ArgumentNullException.ThrowIfNull(request.TargetSkeleton);
        ArgumentNullException.ThrowIfNull(request.OutputFormat);

        var normalizedPaths = NormalizeOutputPaths(request.Items);
        var duplicatePaths = normalizedPaths
            .Where(item => item.Path != null)
            .GroupBy(item => item.Path!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var protectedInputPaths = GetProtectedInputPaths(request);
        var results = new List<AnimationWorkbenchBaseAnimationCandidate>(
            request.Items.Count);

        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            if (cancellationToken.IsCancellationRequested)
            {
                AddNotProcessedItems(
                    request.Items,
                    normalizedPaths,
                    results,
                    index);
                break;
            }

            var normalizedPath = normalizedPaths[index];
            AnimationWorkbenchBaseAnimationCandidate result;
            if (normalizedPath.Path == null)
            {
                result = CreateFailure(
                    item,
                    item.OutputPath,
                    AnimationWorkbenchDiagnosticCode
                        .BaseAnimationOutputPathInvalid,
                    normalizedPath.ErrorMessage);
            }
            else if (duplicatePaths.Contains(normalizedPath.Path))
            {
                result = CreateFailure(
                    item,
                    normalizedPath.Path,
                    AnimationWorkbenchDiagnosticCode
                        .BaseAnimationOutputPathDuplicate);
            }
            else if (protectedInputPaths.Contains(normalizedPath.Path))
            {
                result = CreateFailure(
                    item,
                    normalizedPath.Path,
                    AnimationWorkbenchDiagnosticCode
                        .BaseAnimationSourceOverwriteBlocked);
            }
            else
            {
                result = GenerateItem(
                    request,
                    item,
                    normalizedPath.Path,
                    cancellationToken);
            }
            results.Add(result);
            progress?.Report(new OperationProgressUpdate(
                string.Empty,
                item.SourcePath,
                index + 1,
                request.Items.Count));
        }

        var animationSet = BuildAnimationSet(request, results);
        return new AnimationWorkbenchBaseAnimationCompletionResult(
            results,
            animationSet);
    }

    public async Task<AnimationWorkbenchBaseAnimationCompletionResult>
        SaveReadyCandidatesAsync(
            IPackFileService packFileService,
            FolderProjectContainer project,
            AnimationWorkbenchBaseAnimationCompletionResult generated,
            bool overwriteExisting,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packFileService);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(generated);

        var writable = generated.Items
            .Where(item =>
                (item.Status ==
                    AnimationWorkbenchBaseAnimationItemStatus.Ready ||
                 overwriteExisting && item.Status ==
                    AnimationWorkbenchBaseAnimationItemStatus.Skipped) &&
                item.Bytes != null)
            .ToArray();
        var writableAnimationSet = generated.AnimationSet is
        {
            Bytes: not null,
        } animationSet &&
            (animationSet.Status ==
                AnimationWorkbenchBaseAnimationItemStatus.Ready ||
             overwriteExisting && animationSet.Status ==
                AnimationWorkbenchBaseAnimationItemStatus.Skipped)
                ? animationSet
                : null;

        Exception? writeFailure = null;
        IReadOnlySet<string> conflictPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        if (writable.Length != 0 || writableAnimationSet != null)
        {
            try
            {
                await packFileService.ApplyFileWritesAsync(
                    project,
                    writable.Select(item => new PackFileWrite(
                            item.OutputPath,
                            item.Bytes!))
                        .Concat(writableAnimationSet == null
                            ? []
                            :
                            [
                                new PackFileWrite(
                                    writableAnimationSet.OutputPath,
                                    writableAnimationSet.Bytes!),
                            ])
                        .ToArray(),
                    overwriteExisting,
                    cancellationToken);
            }
            catch (FolderProjectFileConflictException exception)
            {
                conflictPaths = exception.Paths.ToHashSet(
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception exception)
            {
                writeFailure = exception;
            }
        }

        var updated = generated.Items.Select(item =>
        {
            if (conflictPaths.Contains(item.OutputPath))
            {
                return item with
                {
                    Status = AnimationWorkbenchBaseAnimationItemStatus.Skipped,
                };
            }
            if (!writable.Contains(item) || conflictPaths.Count != 0)
                return item;
            return writeFailure == null
                ? item with
                {
                    Status = AnimationWorkbenchBaseAnimationItemStatus.Saved,
                }
                : item with
                {
                    Status = AnimationWorkbenchBaseAnimationItemStatus.Failed,
                    ErrorMessage = writeFailure.Message,
                };
        }).ToArray();
        var updatedAnimationSet = generated.AnimationSet;
        if (updatedAnimationSet != null &&
            conflictPaths.Contains(updatedAnimationSet.OutputPath))
        {
            updatedAnimationSet = updatedAnimationSet with
            {
                Status = AnimationWorkbenchBaseAnimationItemStatus.Skipped,
            };
        }
        else if (writableAnimationSet != null && conflictPaths.Count == 0)
        {
            updatedAnimationSet = writeFailure == null
                ? writableAnimationSet with
                {
                    Status = AnimationWorkbenchBaseAnimationItemStatus.Saved,
                }
                : writableAnimationSet with
                {
                    Status = AnimationWorkbenchBaseAnimationItemStatus.Failed,
                    ErrorMessage = writeFailure.Message,
                };
        }
        return new AnimationWorkbenchBaseAnimationCompletionResult(
            updated,
            updatedAnimationSet);
    }

    private static AnimationWorkbenchBaseAnimationSetCandidate?
        BuildAnimationSet(
            AnimationWorkbenchBaseAnimationRequest request,
            IReadOnlyList<AnimationWorkbenchBaseAnimationCandidate> items)
    {
        if (string.IsNullOrWhiteSpace(request.AnimationSetOutputPath))
            return null;

        string outputPath;
        try
        {
            outputPath = FolderProjectPathPolicy.EnsureResourcePath(
                request.AnimationSetOutputPath);
            if (!outputPath.EndsWith(
                    ".animpack",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "The animation set destination must use the .animpack extension.");
            }
        }
        catch (Exception exception)
            when (exception is ArgumentException or
                  InvalidDataException or
                  NotSupportedException)
        {
            return new AnimationWorkbenchBaseAnimationSetCandidate(
                request.AnimationSetOutputPath,
                AnimationWorkbenchBaseAnimationItemStatus.Failed,
                null,
                [
                    new AnimationWorkbenchDiagnostic(
                        AnimationWorkbenchDiagnosticCode
                            .BaseAnimationSetOutputPathInvalid,
                        AnimationWorkbenchDiagnosticSeverity.Error),
                ],
                exception.Message);
        }

        var interrupted = items.Any(item => item.Status ==
            AnimationWorkbenchBaseAnimationItemStatus.NotProcessed);
        var failed = items.Any(item => item.Status ==
            AnimationWorkbenchBaseAnimationItemStatus.Failed);
        if (interrupted || failed)
        {
            return new AnimationWorkbenchBaseAnimationSetCandidate(
                outputPath,
                interrupted
                    ? AnimationWorkbenchBaseAnimationItemStatus.NotProcessed
                    : AnimationWorkbenchBaseAnimationItemStatus.Failed,
                null,
                [
                    new AnimationWorkbenchDiagnostic(
                        AnimationWorkbenchDiagnosticCode
                            .BaseAnimationSetIncomplete,
                        AnimationWorkbenchDiagnosticSeverity.Error),
                ]);
        }

        var readyItems = items.Where(item =>
                item.Status == AnimationWorkbenchBaseAnimationItemStatus.Ready &&
                item.Bytes != null &&
                item.Role != AnimationWorkbenchBaseAnimationRole.Other)
            .ToArray();
        var availableRoles = readyItems
            .Select(item => item.Role)
            .ToHashSet();
        if (s_requiredRoles.Any(role => !availableRoles.Contains(role)))
        {
            return new AnimationWorkbenchBaseAnimationSetCandidate(
                outputPath,
                AnimationWorkbenchBaseAnimationItemStatus.Failed,
                null,
                [
                    new AnimationWorkbenchDiagnostic(
                        AnimationWorkbenchDiagnosticCode
                            .BaseAnimationSetIncomplete,
                        AnimationWorkbenchDiagnosticSeverity.Error),
                ]);
        }

        var setName = Path.GetFileNameWithoutExtension(outputPath);
        var database = new AnimationPackFileDatabase(outputPath);
        var bin = new AnimationBinWh3(
            $"animations/database/battle/bin/{setName}_tables.bin")
        {
            TableVersion = 4,
            TableSubVersion = 3,
            Name = setName,
            SkeletonName = request.TargetSkeleton.SkeletonName,
            LocomotionGraph =
                @"animations/locomotion_graphs/entity_locomotion_graph.xml",
        };
        foreach (var roleGroup in readyItems.GroupBy(item => item.Role))
        {
            var slotName = GetSlotName(roleGroup.Key);
            var slot = AnimationSlotTypeHelperWh3.GetfromValue(slotName);
            if (slot == null)
                continue;
            bin.AnimationTableEntries.Add(new AnimationBinEntry
            {
                AnimationId = (uint)slot.Id,
                BlendIn = GetBlendIn(roleGroup.Key),
                SelectionWeight = 1,
                WeaponBools = 1,
                AnimationRefs = roleGroup.Select(item =>
                    new AnimationBinEntry.AnimationRef
                    {
                        AnimationFile = item.OutputPath,
                        AnimationMetaFile = string.Empty,
                        AnimationSoundMetaFile = string.Empty,
                    }).ToList(),
            });
        }
        database.AddFile(bin);
        return new AnimationWorkbenchBaseAnimationSetCandidate(
            outputPath,
            AnimationWorkbenchBaseAnimationItemStatus.Ready,
            AnimationPackSerializer.ConvertToBytes(database),
            Array.Empty<AnimationWorkbenchDiagnostic>());
    }

    private static string GetSlotName(
        AnimationWorkbenchBaseAnimationRole role) => role switch
        {
            AnimationWorkbenchBaseAnimationRole.Idle => "STAND_IDLE_1",
            AnimationWorkbenchBaseAnimationRole.Walk => "WALK_1",
            AnimationWorkbenchBaseAnimationRole.Run => "RUN_1",
            AnimationWorkbenchBaseAnimationRole.HitReaction =>
                "HIT_REACTION_STAND_1",
            AnimationWorkbenchBaseAnimationRole.Death => "DEATH_STAND_1",
            _ => throw new ArgumentOutOfRangeException(nameof(role)),
        };

    private static float GetBlendIn(
        AnimationWorkbenchBaseAnimationRole role) => role switch
        {
            AnimationWorkbenchBaseAnimationRole.HitReaction or
                AnimationWorkbenchBaseAnimationRole.Death => 0.1f,
            _ => 0.2f,
        };

    private static AnimationWorkbenchBaseAnimationCandidate GenerateItem(
        AnimationWorkbenchBaseAnimationRequest request,
        AnimationWorkbenchBaseAnimationRecipeItem item,
        string normalizedOutputPath,
        CancellationToken cancellationToken)
    {
        if (request.StyleMode !=
                AnimationWorkbenchBaseAnimationStyleMode.None &&
            request.StyleReference == null)
        {
            return CreateFailure(
                item,
                normalizedOutputPath,
                AnimationWorkbenchDiagnosticCode
                    .BaseAnimationStyleReferenceMissing);
        }

        using var document = new AnimationWorkbenchDocument();
        try
        {
            document.Load(new AnimationWorkbenchLoadRequest(
                item.Source,
                request.StyleMode ==
                    AnimationWorkbenchBaseAnimationStyleMode.None
                        ? null
                        : request.StyleReference,
                GameTypeEnum.Warhammer3,
                request.TargetSkeleton));
            var mappings = request.Mappings ?? document.CreateRetargetMapping(
                AnimationWorkbenchSourceSlot.AnimationA).Mappings;
            var retarget = document.PreviewRetarget(
                new AnimationWorkbenchRetargetRequest(
                    AnimationWorkbenchSourceSlot.AnimationA,
                    mappings));
            if (!retarget.Succeeded)
            {
                return CreateFailure(
                    item,
                    normalizedOutputPath,
                    retarget.Diagnostics);
            }
            var retargetCommit = document.CommitRetargetPreview();
            if (!retargetCommit.Succeeded)
            {
                return CreateFailure(
                    item,
                    normalizedOutputPath,
                    retargetCommit.Diagnostics);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (request.StyleMode !=
                AnimationWorkbenchBaseAnimationStyleMode.None)
            {
                var styleBones = Enumerable.Range(
                        0,
                        request.TargetSkeleton.BoneCount)
                    .Where(index => request.IncludeRootMotion ||
                        request.TargetSkeleton.GetParentBoneIndex(index) >= 0)
                    .Select(index => request.TargetSkeleton.BoneNames[index])
                    .ToArray();
                if (styleBones.Length == 0)
                {
                    return CreateFailure(
                        item,
                        normalizedOutputPath,
                        AnimationWorkbenchDiagnosticCode
                            .BaseAnimationStyleMaskEmpty);
                }

                var layer = document.PreviewLayer(
                    new AnimationWorkbenchLayerRequest(
                        new AnimationWorkbenchBoneMask(
                            request.TargetSkeleton.SkeletonName,
                            styleBones),
                        AnimationWorkbenchLayerMode.Additive,
                        request.StyleWeight,
                        request.StyleMode ==
                            AnimationWorkbenchBaseAnimationStyleMode
                                .PreserveMotion
                            ? AnimationWorkbenchAdditiveReferencePose
                                .AnimationBFirstFrame
                            : AnimationWorkbenchAdditiveReferencePose
                                .TargetSkeletonRestPose));
                if (!layer.Succeeded)
                {
                    return CreateFailure(
                        item,
                        normalizedOutputPath,
                        layer.Diagnostics);
                }
                var layerCommit = document.CommitLayerPreview();
                if (!layerCommit.Succeeded)
                {
                    return CreateFailure(
                        item,
                        normalizedOutputPath,
                        layerCommit.Diagnostics);
                }
            }

            document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
            var preview = document.GetState().CurrentPreview;
            if (preview == null)
            {
                return CreateFailure(
                    item,
                    normalizedOutputPath,
                    AnimationWorkbenchDiagnosticCode.ResultMissing);
            }
            var candidate = AnimationWorkbenchCandidateBuilder.Build(
                preview.Animation,
                request.TargetSkeleton,
                request.OutputFormat);
            if (!candidate.Succeeded)
            {
                return CreateFailure(
                    item,
                    normalizedOutputPath,
                    candidate.Diagnostics);
            }

            return new AnimationWorkbenchBaseAnimationCandidate(
                item.SourcePath,
                normalizedOutputPath,
                item.Role,
                AnimationWorkbenchBaseAnimationItemStatus.Ready,
                candidate.Bytes,
                preview.Animation.Clone(),
                Array.Empty<AnimationWorkbenchDiagnostic>());
        }
        catch (OperationCanceledException)
        {
            return CreateFailure(
                item,
                normalizedOutputPath,
                AnimationWorkbenchDiagnosticCode
                    .BaseAnimationGenerationCancelled,
                status: AnimationWorkbenchBaseAnimationItemStatus
                    .NotProcessed);
        }
        catch (Exception exception)
        {
            return CreateFailure(
                item,
                normalizedOutputPath,
                AnimationWorkbenchDiagnosticCode
                    .RetargetResultTransformInvalid,
                exception.Message);
        }
    }

    private static IReadOnlyList<NormalizedOutputPath> NormalizeOutputPaths(
        IReadOnlyList<AnimationWorkbenchBaseAnimationRecipeItem> items) =>
        items.Select(item =>
        {
            try
            {
                var normalized = FolderProjectPathPolicy.EnsureResourcePath(
                    item.OutputPath);
                if (!normalized.EndsWith(
                        ".anim",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "The destination must use the .anim extension.");
                }
                return new NormalizedOutputPath(normalized, null);
            }
            catch (Exception exception)
                when (exception is ArgumentException or
                      InvalidDataException or
                      NotSupportedException)
            {
                return new NormalizedOutputPath(null, exception.Message);
            }
        }).ToArray();

    private static IReadOnlySet<string> GetProtectedInputPaths(
        AnimationWorkbenchBaseAnimationRequest request)
    {
        var paths = request.Items.Select(item => item.SourcePath).ToList();
        if (request.StyleReference?.Name is { } stylePath)
            paths.Add(stylePath);
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths.Where(path => path.EndsWith(
                     ".anim",
                     StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                normalized.Add(
                    FolderProjectPathPolicy.EnsureResourcePath(path));
            }
            catch (Exception exception)
                when (exception is ArgumentException or
                      InvalidDataException or
                      NotSupportedException)
            {
            }
        }
        return normalized;
    }

    private static void AddNotProcessedItems(
        IReadOnlyList<AnimationWorkbenchBaseAnimationRecipeItem> items,
        IReadOnlyList<NormalizedOutputPath> normalizedPaths,
        ICollection<AnimationWorkbenchBaseAnimationCandidate> results,
        int startIndex)
    {
        for (var index = startIndex; index < items.Count; index++)
        {
            results.Add(CreateFailure(
                items[index],
                normalizedPaths[index].Path ?? items[index].OutputPath,
                AnimationWorkbenchDiagnosticCode
                    .BaseAnimationGenerationCancelled,
                status: AnimationWorkbenchBaseAnimationItemStatus
                    .NotProcessed));
        }
    }

    private static AnimationWorkbenchBaseAnimationCandidate CreateFailure(
        AnimationWorkbenchBaseAnimationRecipeItem item,
        string outputPath,
        AnimationWorkbenchDiagnosticCode code,
        string? errorMessage = null,
        AnimationWorkbenchBaseAnimationItemStatus status =
            AnimationWorkbenchBaseAnimationItemStatus.Failed) =>
        CreateFailure(
            item,
            outputPath,
            [
                new AnimationWorkbenchDiagnostic(
                    code,
                    AnimationWorkbenchDiagnosticSeverity.Error),
            ],
            errorMessage,
            status);

    private static AnimationWorkbenchBaseAnimationCandidate CreateFailure(
        AnimationWorkbenchBaseAnimationRecipeItem item,
        string outputPath,
        IReadOnlyList<AnimationWorkbenchDiagnostic> diagnostics,
        string? errorMessage = null,
        AnimationWorkbenchBaseAnimationItemStatus status =
            AnimationWorkbenchBaseAnimationItemStatus.Failed) => new(
        item.SourcePath,
        outputPath,
        item.Role,
        status,
        null,
        null,
        diagnostics,
        errorMessage);

    private sealed record NormalizedOutputPath(
        string? Path,
        string? ErrorMessage);
}
