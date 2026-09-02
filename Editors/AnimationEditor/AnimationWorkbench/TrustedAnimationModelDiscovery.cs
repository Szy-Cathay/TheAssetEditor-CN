using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public enum TrustedAnimationModelSourceRole
{
    FolderProject,
    ReferencePack,
    CaPack,
}

public sealed record TrustedAnimationModelCandidate(
    PackFile File,
    string Path,
    string SourcePack,
    string SourceLocation,
    TrustedAnimationModelSourceRole SourceRole)
{
    public string SourceGroup => SourceRole switch
    {
        TrustedAnimationModelSourceRole.FolderProject =>
            LocalizationManager.Instance.Get(
                "AnimationWorkbench.ModelPicker.SourceProject"),
        TrustedAnimationModelSourceRole.ReferencePack =>
            LocalizationManager.Instance.Get(
                "AnimationWorkbench.ModelPicker.SourceReference"),
        TrustedAnimationModelSourceRole.CaPack =>
            LocalizationManager.Instance.Get(
                "AnimationWorkbench.ModelPicker.SourceCa"),
        _ => string.Empty,
    };
}

public interface ITrustedAnimationModelDiscovery
{
    IAsyncEnumerable<IReadOnlyList<TrustedAnimationModelCandidate>>
        DiscoverAsync(CancellationToken cancellationToken);
}

public sealed class TrustedAnimationModelDiscovery(
    IPackFileService packFileService) : ITrustedAnimationModelDiscovery
{
    private const int BatchSize = 128;

    private static readonly TrustedAnimationModelSourceRole[] s_roleOrder =
    [
        TrustedAnimationModelSourceRole.FolderProject,
        TrustedAnimationModelSourceRole.ReferencePack,
        TrustedAnimationModelSourceRole.CaPack,
    ];

    public async IAsyncEnumerable<
        IReadOnlyList<TrustedAnimationModelCandidate>> DiscoverAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<
            IReadOnlyList<TrustedAnimationModelCandidate>>(
            new BoundedChannelOptions(4)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
        var producer = Task.Run(
            () => ProduceAsync(channel.Writer, cancellationToken),
            CancellationToken.None);

        await foreach (var batch in channel.Reader.ReadAllAsync(
                           cancellationToken))
        {
            yield return batch;
        }

        await producer;
    }

    private async Task ProduceAsync(
        ChannelWriter<IReadOnlyList<TrustedAnimationModelCandidate>> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            var containers = packFileService
                .GetAllPackfileContainers();
            var effectivePaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var batch = new List<TrustedAnimationModelCandidate>(BatchSize);

            foreach (var role in s_roleOrder)
            {
                for (var index = containers.Count - 1; index >= 0; index--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var container = containers[index];
                    if (GetSourceRole(container) != role)
                        continue;

                    var entries = packFileService
                        .GetFileEntriesSnapshot(container) ??
                        container.FileList.ToArray();
                    foreach (var entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var path = NormalizePath(entry.Key);
                        if (!IsSupportedModel(path) ||
                            !effectivePaths.Add(path))
                        {
                            continue;
                        }

                        batch.Add(new TrustedAnimationModelCandidate(
                            entry.Value,
                            path,
                            container.Name,
                            container.SystemFilePath ?? string.Empty,
                            role));
                        if (batch.Count < BatchSize)
                            continue;

                        await writer.WriteAsync(
                            batch.ToArray(),
                            cancellationToken);
                        batch.Clear();
                    }
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

    private static TrustedAnimationModelSourceRole? GetSourceRole(
        PackFileContainer container)
    {
        if (container.Role == PackFileContainerRole.ProjectWorkspace)
        {
            return TrustedAnimationModelSourceRole.FolderProject;
        }
        if (container.Role == PackFileContainerRole.Reference)
            return TrustedAnimationModelSourceRole.ReferencePack;
        if (container.IsCaPackFile)
            return TrustedAnimationModelSourceRole.CaPack;
        return null;
    }

    private static bool IsSupportedModel(string path) =>
        path.EndsWith(
            ".rigid_model_v2",
            StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(
            ".wsmodel",
            StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(
            ".variantmeshdefinition",
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path) =>
        path.Replace('/', '\\').Trim();
}
