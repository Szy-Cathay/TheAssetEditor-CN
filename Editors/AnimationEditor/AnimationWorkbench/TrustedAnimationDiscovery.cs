using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.IO;
using GameWorld.Core.Animation;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed record TrustedAnimationSkeletonBone(
    string Name,
    int ParentIndex);

public sealed class TrustedAnimationSkeletonIdentity
{
    private TrustedAnimationSkeletonIdentity(
        PackFile file,
        string name,
        string path,
        string source,
        string bindingFingerprint,
        IReadOnlyList<TrustedAnimationSkeletonBone> bones)
    {
        File = file;
        Name = name;
        Path = path;
        Source = source;
        BindingFingerprint = bindingFingerprint;
        Bones = bones;
    }

    public PackFile File { get; }

    public string Name { get; }

    public string Path { get; }

    public string Source { get; }

    public string BindingFingerprint { get; }

    public IReadOnlyList<TrustedAnimationSkeletonBone> Bones { get; }

    public static TrustedAnimationSkeletonIdentity Create(
        PackFile skeleton,
        string path,
        string source)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var file = AnimationFile.Create(skeleton);
        var player = new AnimationPlayer();
        var gameSkeleton = new GameSkeleton(file, player);
        var fingerprintText = new StringBuilder();
        var bones = new TrustedAnimationSkeletonBone[
            gameSkeleton.BoneCount];
        fingerprintText.Append(file.Header.SkeletonName).Append('\n');
        for (var boneIndex = 0;
             boneIndex < gameSkeleton.BoneCount;
             boneIndex++)
        {
            var bone = new TrustedAnimationSkeletonBone(
                gameSkeleton.BoneNames[boneIndex],
                gameSkeleton.GetParentBoneIndex(boneIndex));
            bones[boneIndex] = bone;
            var translation = gameSkeleton.Translation[boneIndex];
            var rotation = gameSkeleton.Rotation[boneIndex];
            fingerprintText
                .Append(boneIndex).Append('|')
                .Append(bone.Name).Append('|')
                .Append(bone.ParentIndex).Append('|')
                .Append(BitConverter.SingleToInt32Bits(translation.X))
                .Append('|')
                .Append(BitConverter.SingleToInt32Bits(translation.Y))
                .Append('|')
                .Append(BitConverter.SingleToInt32Bits(translation.Z))
                .Append('|')
                .Append(BitConverter.SingleToInt32Bits(rotation.X))
                .Append('|')
                .Append(BitConverter.SingleToInt32Bits(rotation.Y))
                .Append('|')
                .Append(BitConverter.SingleToInt32Bits(rotation.Z))
                .Append('|')
                .Append(BitConverter.SingleToInt32Bits(rotation.W))
                .Append('\n');
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(fingerprintText.ToString())));
        return new TrustedAnimationSkeletonIdentity(
            skeleton,
            file.Header.SkeletonName.Trim(),
            TrustedAnimationEffectiveResources.NormalizePath(path),
            source,
            fingerprint,
            bones);
    }

    public bool Matches(TrustedAnimationSkeletonIdentity other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (!string.Equals(
                Name,
                other.Name,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                BindingFingerprint,
                other.BindingFingerprint,
                StringComparison.Ordinal) ||
            Bones.Count != other.Bones.Count)
        {
            return false;
        }

        for (var index = 0; index < Bones.Count; index++)
        {
            if (!string.Equals(
                    Bones[index].Name,
                    other.Bones[index].Name,
                    StringComparison.Ordinal) ||
                Bones[index].ParentIndex != other.Bones[index].ParentIndex)
            {
                return false;
            }
        }
        return true;
    }
}

public sealed record TrustedAnimationCandidate(
    PackFile File,
    string Name,
    string Path,
    string SourcePack,
    string SourceLocation,
    TrustedAnimationModelSourceRole SourceRole,
    uint Version,
    int FrameCount,
    double DurationSeconds,
    double FramesPerSecond,
    int PartCount = 1,
    bool HasStaticFrame = false,
    bool IsStaticPose = false)
{
    public string Metadata => string.Format(
        LocalizationManager.Instance.Get(
            "AnimationWorkbench.AnimationPicker.Metadata"),
        Version,
        TrustedAnimationFormatText.Get(
            PartCount,
            HasStaticFrame,
            IsStaticPose),
        FrameCount,
        DurationSeconds,
        FramesPerSecond);
}

internal static class TrustedAnimationFormatText
{
    public static string Get(
        int partCount,
        bool hasStaticFrame,
        bool isStaticPose)
    {
        if (isStaticPose)
        {
            return LocalizationManager.Instance.Get(
                "AnimationWorkbench.AnimationPicker.FormatStaticPose");
        }
        if (partCount > 1 && hasStaticFrame)
        {
            return string.Format(
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.AnimationPicker.FormatMultipartStatic"),
                partCount);
        }
        if (partCount > 1)
        {
            return string.Format(
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.AnimationPicker.FormatMultipart"),
                partCount);
        }
        if (hasStaticFrame)
        {
            return LocalizationManager.Instance.Get(
                "AnimationWorkbench.AnimationPicker.FormatStaticTracks");
        }
        return LocalizationManager.Instance.Get(
            "AnimationWorkbench.AnimationPicker.FormatDynamic");
    }
}

public interface ITrustedAnimationDiscovery
{
    IAsyncEnumerable<IReadOnlyList<TrustedAnimationCandidate>>
        DiscoverAsync(
            TrustedAnimationSkeletonIdentity requiredSkeleton,
            CancellationToken cancellationToken);
}

public sealed class TrustedAnimationDiscovery(
    IPackFileService packFileService) : ITrustedAnimationDiscovery
{
    private const int BatchSize = 64;

    public async IAsyncEnumerable<IReadOnlyList<TrustedAnimationCandidate>>
        DiscoverAsync(
            TrustedAnimationSkeletonIdentity requiredSkeleton,
            [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requiredSkeleton);
        var channel = Channel.CreateBounded<
            IReadOnlyList<TrustedAnimationCandidate>>(
            new BoundedChannelOptions(4)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
        var producer = Task.Run(
            () => ProduceAsync(
                channel.Writer,
                requiredSkeleton,
                cancellationToken),
            CancellationToken.None);

        await foreach (var batch in channel.Reader.ReadAllAsync(
                           cancellationToken))
        {
            yield return batch;
        }
        await producer;
    }

    private async Task ProduceAsync(
        ChannelWriter<IReadOnlyList<TrustedAnimationCandidate>> writer,
        TrustedAnimationSkeletonIdentity requiredSkeleton,
        CancellationToken cancellationToken)
    {
        try
        {
            var effectiveSkeleton = TrustedAnimationEffectiveResources.Find(
                packFileService,
                requiredSkeleton.Path);
            if (effectiveSkeleton == null)
            {
                writer.TryComplete();
                return;
            }

            var currentIdentity = TrustedAnimationSkeletonIdentity.Create(
                effectiveSkeleton.File,
                effectiveSkeleton.Path,
                effectiveSkeleton.Container.Name);
            if (!requiredSkeleton.Matches(currentIdentity))
            {
                writer.TryComplete();
                return;
            }

            var gameSkeleton = new GameSkeleton(
                AnimationFile.Create(requiredSkeleton.File),
                new AnimationPlayer());
            var effectivePaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var batch = new List<TrustedAnimationCandidate>(BatchSize);
            foreach (var source in TrustedAnimationEffectiveResources
                         .EnumerateSources(packFileService))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entries = packFileService.GetFileEntriesSnapshot(
                                  source.Container) ??
                              source.Container.FileList.ToArray();
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = TrustedAnimationEffectiveResources
                        .NormalizePath(entry.Key);
                    if (!IsAnimation(path) ||
                        !effectivePaths.Add(path) ||
                        !TryCreateCandidate(
                            entry.Value,
                            path,
                            source,
                            requiredSkeleton,
                            gameSkeleton,
                            out var candidate))
                    {
                        continue;
                    }

                    batch.Add(candidate);
                    if (batch.Count < BatchSize)
                        continue;
                    await writer.WriteAsync(
                        batch.ToArray(),
                        cancellationToken);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await writer.WriteAsync(
                    batch.ToArray(),
                    cancellationToken);
            }
            writer.TryComplete();
        }
        catch (Exception exception)
        {
            writer.TryComplete(exception);
        }
    }

    internal static bool TryCreateCandidate(
        PackFile file,
        string path,
        TrustedAnimationEffectiveSource source,
        TrustedAnimationSkeletonIdentity requiredSkeleton,
        GameSkeleton gameSkeleton,
        out TrustedAnimationCandidate candidate)
    {
        candidate = null!;
        try
        {
            var animation = AnimationFile.Create(file);
            if (animation.Header.Version is not 7 and not 8 ||
                !float.IsFinite(animation.Header.FrameRate) ||
                animation.Header.FrameRate < 0 ||
                !TrustedAnimationCompatibility.HasExactBoneIdentity(
                    animation,
                    requiredSkeleton,
                    out _))
            {
                return false;
            }

            var clip = new AnimationClip(animation, gameSkeleton);
            if (!TrustedAnimationCompatibility.HasFiniteClip(
                    clip,
                    gameSkeleton.BoneCount,
                    out _))
            {
                return false;
            }

            candidate = new TrustedAnimationCandidate(
                file,
                Path.GetFileNameWithoutExtension(path),
                path,
                source.Container.Name,
                source.Container.SystemFilePath ?? string.Empty,
                source.Role,
                animation.Header.Version,
                clip.DynamicFrames.Count,
                clip.Duration.TotalSeconds,
                animation.Header.FrameRate,
                animation.AnimationParts.Count,
                animation.AnimationParts.Any(part =>
                    part.StaticFrame != null),
                animation.AnimationParts.Count > 0 &&
                animation.AnimationParts.All(part =>
                    part.DynamicFrames.Count == 0) &&
                animation.AnimationParts.Any(part =>
                    part.StaticFrame != null));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAnimation(string path) =>
        path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase) &&
        !path.StartsWith(
            "animations\\skeletons\\",
            StringComparison.OrdinalIgnoreCase);
}

public sealed record TrustedAnimationEffectiveSource(
    PackFileContainer Container,
    TrustedAnimationModelSourceRole Role);

public sealed record TrustedAnimationEffectiveResource(
    PackFile File,
    string Path,
    PackFileContainer Container,
    TrustedAnimationModelSourceRole Role);

internal static class TrustedAnimationEffectiveResources
{
    private static readonly TrustedAnimationModelSourceRole[] s_roleOrder =
    [
        TrustedAnimationModelSourceRole.FolderProject,
        TrustedAnimationModelSourceRole.ReferencePack,
        TrustedAnimationModelSourceRole.CaPack,
    ];

    public static IEnumerable<TrustedAnimationEffectiveSource>
        EnumerateSources(IPackFileService packFileService)
    {
        var containers = packFileService.GetAllPackfileContainers() ?? [];
        foreach (var role in s_roleOrder)
        {
            for (var index = containers.Count - 1; index >= 0; index--)
            {
                var actualRole = GetRole(containers[index]);
                if (actualRole == role)
                {
                    yield return new TrustedAnimationEffectiveSource(
                        containers[index],
                        role);
                }
            }
        }
    }

    public static TrustedAnimationEffectiveResource? Find(
        IPackFileService packFileService,
        string path)
    {
        var normalizedPath = NormalizePath(path);
        foreach (var source in EnumerateSources(packFileService))
        {
            var entries = packFileService.GetFileEntriesSnapshot(
                              source.Container) ??
                          source.Container.FileList.ToArray();
            var entry = entries.FirstOrDefault(candidate =>
                string.Equals(
                    NormalizePath(candidate.Key),
                    normalizedPath,
                    StringComparison.OrdinalIgnoreCase));
            if (entry.Value != null)
            {
                return new TrustedAnimationEffectiveResource(
                    entry.Value,
                    normalizedPath,
                    source.Container,
                    source.Role);
            }
        }
        return null;
    }

    public static string NormalizePath(string path) =>
        path.Replace('/', '\\').Trim();

    private static TrustedAnimationModelSourceRole? GetRole(
        PackFileContainer container)
    {
        if (container.Role == PackFileContainerRole.ProjectWorkspace)
            return TrustedAnimationModelSourceRole.FolderProject;
        if (container.Role == PackFileContainerRole.Reference)
            return TrustedAnimationModelSourceRole.ReferencePack;
        if (container.IsCaPackFile)
            return TrustedAnimationModelSourceRole.CaPack;
        return null;
    }
}

internal static class TrustedAnimationCompatibility
{
    public static bool HasExactBoneIdentity(
        AnimationFile animation,
        TrustedAnimationSkeletonIdentity requiredSkeleton,
        out string conflict)
    {
        var declaredName = animation.Header.SkeletonName.Trim();
        if (!string.Equals(
                declaredName,
                requiredSkeleton.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            conflict = $"Skeleton '{declaredName}' does not match " +
                       $"'{requiredSkeleton.Name}'.";
            return false;
        }

        if (animation.Bones.Length != requiredSkeleton.Bones.Count)
        {
            conflict = $"Skeleton '{declaredName}' has " +
                       $"{animation.Bones.Length} bones; expected " +
                       $"{requiredSkeleton.Bones.Count}.";
            return false;
        }

        for (var boneIndex = 0;
             boneIndex < requiredSkeleton.Bones.Count;
             boneIndex++)
        {
            var actual = animation.Bones[boneIndex];
            var expected = requiredSkeleton.Bones[boneIndex];
            if (!string.Equals(
                    actual.Name,
                    expected.Name,
                    StringComparison.Ordinal) ||
                actual.ParentId != expected.ParentIndex)
            {
                conflict = $"Bone {boneIndex} '{actual.Name}' parent " +
                           $"{actual.ParentId} does not match " +
                           $"'{expected.Name}' parent " +
                           $"{expected.ParentIndex}.";
                return false;
            }
        }

        conflict = string.Empty;
        return true;
    }

    public static bool HasFiniteClip(
        AnimationClip clip,
        int boneCount,
        out string conflict)
    {
        if (clip.DynamicFrames.Count == 0 ||
            clip.Duration < TimeSpan.Zero ||
            !double.IsFinite(clip.Duration.TotalSeconds))
        {
            conflict = LocalizationManager.Instance.Get(
                "AnimationWorkbench.TrustedPreview.AnimationNoPlayableSamples");
            return false;
        }

        for (var frameIndex = 0;
             frameIndex < clip.DynamicFrames.Count;
             frameIndex++)
        {
            var frame = clip.DynamicFrames[frameIndex];
            if (frame.Position.Count != boneCount ||
                frame.Rotation.Count != boneCount ||
                frame.Scale.Count != boneCount)
            {
                conflict = string.Format(
                    LocalizationManager.Instance.Get(
                        "AnimationWorkbench.TrustedPreview.AnimationFrameBoneCountInvalid"),
                    frameIndex,
                    boneCount);
                return false;
            }

            for (var boneIndex = 0; boneIndex < boneCount; boneIndex++)
            {
                var position = frame.Position[boneIndex];
                var rotation = frame.Rotation[boneIndex];
                var scale = frame.Scale[boneIndex];
                if (!float.IsFinite(position.X) ||
                    !float.IsFinite(position.Y) ||
                    !float.IsFinite(position.Z) ||
                    !float.IsFinite(rotation.X) ||
                    !float.IsFinite(rotation.Y) ||
                    !float.IsFinite(rotation.Z) ||
                    !float.IsFinite(rotation.W) ||
                    !float.IsFinite(scale.X) ||
                    !float.IsFinite(scale.Y) ||
                    !float.IsFinite(scale.Z))
                {
                    conflict = string.Format(
                        LocalizationManager.Instance.Get(
                            "AnimationWorkbench.TrustedPreview.AnimationTransformInvalid"),
                        frameIndex,
                        boneIndex);
                    return false;
                }
            }
        }

        conflict = string.Empty;
        return true;
    }
}
