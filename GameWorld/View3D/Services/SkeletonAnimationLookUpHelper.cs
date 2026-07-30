using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.Animation;

namespace GameWorld.Core.Services
{
    public interface ISkeletonAnimationLookUpHelper
    {
        void Dispose();
        AnimationReference? FindAnimationRefFromPackFile(PackFile animation);
        ObservableCollection<string> GetAllSkeletonFileNames();
        ObservableCollection<AnimationReference> GetAnimationsForSkeleton(string skeletonName);
        AnimationFile? GetSkeletonFileFromName(string skeletonName);
    }

    public class SkeletonAnimationLookUpHelper : IDisposable, ISkeletonAnimationLookUpHelper
    {
        private static readonly HashSet<string> s_knownBrokenFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "rigidmodels\\buildings\\roman_aqueduct_straight\\roman_aqueduct_straight_piece01_destruct01_anim.anim",
            "animations\\battle\\raptor02\\subset\\colossal_squig\\deaths\\rp2_colossalsquig_death_01.anim",
            "animations\\battle\\humanoid13b\\golgfag\\docking\\hu13b_golgfag_docking_armed_02.anim",
            "animations\\battle\\humanoid13\\ogre\\rider\\hq3b_stonehorn_wb\\sword_and_crossbow\\missile_action\\crossbow\\hu13_hq3b_swc_rider1_shoot_back_crossbow_01.anim",
            "animations\\battle\\humanoid13\\ogre\\rider\\hq3b_stonehorn_wb\\sword_and_crossbow\\missile_action\\crossbow\\hu13_hq3b_swc_rider1_reload_crossbow_01.anim",
            "animations\\battle\\humanoid13\\ogre\\rider\\hq3b_stonehorn_wb\\sword_and_crossbow\\missile_action\\crossbow\\hu13_hq3b_sp_rider1_shoot_ready_crossbow_01.anim",
            "animations\\battle\\humanoid01c\\sayl_staff_and_skull\\stand\\props\\hu1c_sayl_staff_and_skull_staff_stand_idle_02.anim"
        };

        private readonly ILogger _logger = Logging.Create<SkeletonAnimationLookUpHelper>();
        private readonly object _threadLock = new();
        private readonly IPackFileService _packFileService;
        private readonly IGlobalEventHub _globalEventHub;

        private readonly Dictionary<string, ObservableCollection<AnimationReference>> _skeletonNameToAnimationMap =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly ObservableCollection<string> _skeletonFileNames = [];
        private readonly Dictionary<string, int> _skeletonPathReferenceCounts =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<IndexedAnimation>> _skeletonFilesByName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<PackFileContainer, ContainerIndex> _containerIndexes = [];
        private readonly Dictionary<PackFile, IndexedAnimation> _animationIndexByFile = [];

        public SkeletonAnimationLookUpHelper(IPackFileService packFileService, IGlobalEventHub globalEventHub)
        {
            _packFileService = packFileService;
            _globalEventHub = globalEventHub;

            _globalEventHub.Register<PackFileContainerAddedEvent>(this, x => PackfileContainerRefresh(x.Container));
            _globalEventHub.Register<PackFileContainerFilesAddedEvent>(
                this,
                x => RefreshIfAnimationFilesChanged(x.Container, x.AddedFiles));
            _globalEventHub.Register<PackFileContainerFolderRenamedEvent>(this, x => PackfileContainerRefresh(x.Container));
            _globalEventHub.Register<PackFileContainerFilesUpdatedEvent>(
                this,
                x => RefreshIfAnimationFilesChanged(x.Container, x.ChangedFiles));

            _globalEventHub.Register<PackFileContainerRemovedEvent>(this, x => UnloadAnimationFromContainer(x.Container));
            _globalEventHub.Register<PackFileContainerFilesRemovedEvent>(
                this,
                x => RemoveFilesFromContainer(x.Container, x.RemovedFiles));
            _globalEventHub.Register<PackFileContainerFolderRemovedEvent>(
                this,
                x => RemoveFolderFromContainer(x.Container, x.Folder));

            foreach (var container in packFileService.GetAllPackfileContainers())
                LoadFromPackFileContainer(container);
        }

        public void Dispose()
        {
            _globalEventHub.UnRegister(this);
        }

        private void PackfileContainerRefresh(PackFileContainer packFileContainer)
        {
            UnloadAnimationFromContainer(packFileContainer);
            LoadFromPackFileContainer(packFileContainer);
        }

        private void RefreshIfAnimationFilesChanged(
            PackFileContainer packFileContainer,
            IReadOnlyCollection<PackFile> files)
        {
            bool containsAnimation;
            lock (_threadLock)
            {
                containsAnimation = files.Any(x =>
                    string.Equals(Path.GetExtension(x.Name), ".anim", StringComparison.OrdinalIgnoreCase) ||
                    _animationIndexByFile.ContainsKey(x));
            }

            if (containsAnimation)
                PackfileContainerRefresh(packFileContainer);
        }

        private void LoadFromPackFileContainer(PackFileContainer packFileContainer)
        {
            var discoveredAnimations = DiscoverAnimations(packFileContainer);

            lock (_threadLock)
            {
                var containerIndex = new ContainerIndex();
                foreach (var discovered in discoveredAnimations)
                {
                    var animationReference = new AnimationReference(discovered.FullPath, packFileContainer);
                    var indexedAnimation = new IndexedAnimation(
                        discovered.PackFile,
                        discovered.FullPath,
                        discovered.SkeletonName,
                        animationReference,
                        IsSkeletonPath(discovered.FullPath));

                    containerIndex.Animations.Add(indexedAnimation);
                    _animationIndexByFile[discovered.PackFile] = indexedAnimation;

                    if (_skeletonNameToAnimationMap.TryGetValue(discovered.SkeletonName, out var references) == false)
                    {
                        references = [];
                        _skeletonNameToAnimationMap[discovered.SkeletonName] = references;
                    }
                    references.Add(animationReference);

                    if (indexedAnimation.IsSkeletonFile)
                        AddSkeletonFile(indexedAnimation);
                }

                _containerIndexes[packFileContainer] = containerIndex;
            }
        }

        private List<DiscoveredAnimation> DiscoverAnimations(PackFileContainer packFileContainer)
        {
            var allAnimations = packFileContainer.FileList
                .Where(x => string.Equals(Path.GetExtension(x.Key), ".anim", StringComparison.OrdinalIgnoreCase))
                .Select(x => new AnimationCandidate(x.Key, x.Value))
                .ToList();

            var packedAnimations = allAnimations
                .Where(x => x.PackFile.DataSource is PackedFileSource)
                .Select(x => new PackedAnimationCandidate(
                    x.FullPath,
                    x.PackFile,
                    (PackedFileSource)x.PackFile.DataSource))
                .ToList();
            var otherAnimations = allAnimations
                .Where(x => x.PackFile.DataSource is not PackedFileSource)
                .ToList();
            var discoveredAnimations = new ConcurrentBag<DiscoveredAnimation>();

            var groupedPackedAnimations = packedAnimations
                .GroupBy(x => x.DataSource.Parent.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();
            Parallel.ForEach(groupedPackedAnimations, group =>
            {
                using var stream = new FileStream(group.Key, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                foreach (var animation in group)
                {
                    var bytes = animation.DataSource.ReadData(stream);
                    if (bytes.Length > 100)
                        Array.Resize(ref bytes, 100);

                    TryDiscoverAnimation(
                        bytes,
                        animation.PackFile,
                        animation.FullPath,
                        discoveredAnimations);
                }
            });

            Parallel.ForEach(otherAnimations, animation =>
            {
                var size = (int)Math.Min(100L, animation.PackFile.DataSource.Size);
                var bytes = size == 0 ? [] : animation.PackFile.DataSource.PeekData(size);
                TryDiscoverAnimation(bytes, animation.PackFile, animation.FullPath, discoveredAnimations);
            });

            return discoveredAnimations
                .OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void TryDiscoverAnimation(
            byte[] byteChunk,
            PackFile packFile,
            string fullPath,
            ConcurrentBag<DiscoveredAnimation> discoveredAnimations)
        {
            if (s_knownBrokenFiles.Contains(fullPath))
            {
                _logger.Here().Warning("Skipping loading of known broken file - " + fullPath);
                return;
            }

            try
            {
                if (byteChunk.Length == 0)
                    throw new Exception("File empty.");

                discoveredAnimations.Add(
                    new DiscoveredAnimation(
                        packFile,
                        fullPath,
                        AnimationFile.GetAnimationName(byteChunk)));
            }
            catch (Exception e)
            {
                _logger.Here().Error("Parsing failed for " + fullPath + "\n" + e);
            }
        }

        private void AddSkeletonFile(IndexedAnimation animation)
        {
            var skeletonName = Path.GetFileNameWithoutExtension(animation.FullPath);
            if (_skeletonFilesByName.TryGetValue(skeletonName, out var entries) == false)
            {
                entries = [];
                _skeletonFilesByName[skeletonName] = entries;
            }
            entries.Add(animation);

            if (_skeletonPathReferenceCounts.TryGetValue(animation.FullPath, out var referenceCount))
            {
                _skeletonPathReferenceCounts[animation.FullPath] = referenceCount + 1;
                return;
            }

            _skeletonPathReferenceCounts[animation.FullPath] = 1;
            _skeletonFileNames.Add(animation.FullPath);
        }

        private void UnloadAnimationFromContainer(PackFileContainer packFileContainer)
        {
            lock (_threadLock)
            {
                if (_containerIndexes.TryGetValue(packFileContainer, out var containerIndex) == false)
                    return;

                foreach (var animation in containerIndex.Animations.ToList())
                    RemoveIndexedAnimation(containerIndex, animation);

                _containerIndexes.Remove(packFileContainer);
            }
        }

        private void RemoveFilesFromContainer(PackFileContainer packFileContainer, IReadOnlyCollection<PackFile> files)
        {
            lock (_threadLock)
            {
                if (_containerIndexes.TryGetValue(packFileContainer, out var containerIndex) == false)
                    return;

                foreach (var file in files)
                {
                    if (_animationIndexByFile.TryGetValue(file, out var animation) &&
                        ReferenceEquals(animation.Reference.Container, packFileContainer))
                    {
                        RemoveIndexedAnimation(containerIndex, animation);
                    }
                }
            }
        }

        private void RemoveFolderFromContainer(PackFileContainer packFileContainer, string folder)
        {
            lock (_threadLock)
            {
                if (_containerIndexes.TryGetValue(packFileContainer, out var containerIndex) == false)
                    return;

                var animationsToRemove = containerIndex.Animations
                    .Where(x => Path.GetDirectoryName(x.FullPath)?
                        .StartsWith(folder, StringComparison.InvariantCultureIgnoreCase) == true)
                    .ToList();

                foreach (var animation in animationsToRemove)
                    RemoveIndexedAnimation(containerIndex, animation);
            }
        }

        private void RemoveIndexedAnimation(ContainerIndex containerIndex, IndexedAnimation animation)
        {
            containerIndex.Animations.Remove(animation);

            if (_animationIndexByFile.TryGetValue(animation.PackFile, out var indexed) &&
                ReferenceEquals(indexed, animation))
            {
                _animationIndexByFile.Remove(animation.PackFile);
            }

            if (_skeletonNameToAnimationMap.TryGetValue(animation.SkeletonName, out var references))
                references.Remove(animation.Reference);

            if (animation.IsSkeletonFile == false)
                return;

            var skeletonName = Path.GetFileNameWithoutExtension(animation.FullPath);
            if (_skeletonFilesByName.TryGetValue(skeletonName, out var skeletonEntries))
            {
                skeletonEntries.Remove(animation);
                if (skeletonEntries.Count == 0)
                    _skeletonFilesByName.Remove(skeletonName);
            }

            var referenceCount = _skeletonPathReferenceCounts[animation.FullPath] - 1;
            if (referenceCount > 0)
            {
                _skeletonPathReferenceCounts[animation.FullPath] = referenceCount;
                return;
            }

            _skeletonPathReferenceCounts.Remove(animation.FullPath);
            var pathToRemove = _skeletonFileNames.FirstOrDefault(
                x => string.Equals(x, animation.FullPath, StringComparison.OrdinalIgnoreCase));
            if (pathToRemove != null)
                _skeletonFileNames.Remove(pathToRemove);
        }

        public ObservableCollection<AnimationReference> GetAnimationsForSkeleton(string skeletonName)
        {
            lock (_threadLock)
            {
                if (_skeletonNameToAnimationMap.TryGetValue(skeletonName, out var references) == false)
                {
                    references = [];
                    _skeletonNameToAnimationMap[skeletonName] = references;
                }

                return references;
            }
        }

        public ObservableCollection<string> GetAllSkeletonFileNames() => _skeletonFileNames;

        public AnimationFile? GetSkeletonFileFromName(string skeletonName)
        {
            lock (_threadLock)
            {
                var lookupName = Path.GetFileNameWithoutExtension(skeletonName);
                if (_skeletonFilesByName.TryGetValue(lookupName, out var skeletonFiles))
                {
                    var containers = _packFileService.GetAllPackfileContainers();
                    for (var containerIndex = containers.Count - 1; containerIndex >= 0; containerIndex--)
                    {
                        for (var fileIndex = skeletonFiles.Count - 1; fileIndex >= 0; fileIndex--)
                        {
                            var skeletonFile = skeletonFiles[fileIndex];
                            if (ReferenceEquals(skeletonFile.Reference.Container, containers[containerIndex]) == false ||
                                PathContainsSegment(skeletonFile.FullPath, "tech") ||
                                PathContainsSegment(skeletonFile.FullPath, "reference_poses"))
                            {
                                continue;
                            }

                            return AnimationFile.Create(skeletonFile.PackFile);
                        }
                    }
                }

                var path = $"animations\\skeletons\\{lookupName}.anim";
                var animationFile = _packFileService.FindFile(path);
                return animationFile == null ? null : AnimationFile.Create(animationFile);
            }
        }

        public AnimationReference? FindAnimationRefFromPackFile(PackFile animation)
        {
            lock (_threadLock)
            {
                if (_animationIndexByFile.TryGetValue(animation, out var indexedAnimation))
                    return indexedAnimation.Reference;

                var container = _packFileService.GetPackFileContainer(animation);
                if (container == null)
                    return null;

                var fullPath = _packFileService.GetFullPath(animation, container);
                var foundFile = _packFileService.FindFile(fullPath, container);
                return ReferenceEquals(foundFile, animation)
                    ? new AnimationReference(fullPath, container)
                    : null;
            }
        }

        private static bool IsSkeletonPath(string path)
        {
            var normalizedPath = path.Replace('/', '\\');
            return normalizedPath.Contains("animations\\skeletons\\", StringComparison.OrdinalIgnoreCase) ||
                   PathContainsSegment(normalizedPath, "tech");
        }

        private static bool PathContainsSegment(string path, string segment)
        {
            return path
                .Replace('/', '\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Contains(segment, StringComparer.OrdinalIgnoreCase);
        }

        private sealed class ContainerIndex
        {
            public List<IndexedAnimation> Animations { get; } = [];
        }

        private sealed record AnimationCandidate(string FullPath, PackFile PackFile);

        private sealed record PackedAnimationCandidate(
            string FullPath,
            PackFile PackFile,
            PackedFileSource DataSource);

        private sealed record DiscoveredAnimation(
            PackFile PackFile,
            string FullPath,
            string SkeletonName);

        private sealed record IndexedAnimation(
            PackFile PackFile,
            string FullPath,
            string SkeletonName,
            AnimationReference Reference,
            bool IsSkeletonFile);
    }

    // Delete this piece of shit
    public class AnimationReference
    {
        public AnimationReference(string animationFile, PackFileContainer container)
        {
            AnimationFile = animationFile;
            Container = container;
        }

        public string AnimationFile { get; set; }
        public PackFileContainer Container { get; set; }

        public override string ToString()
        {
            return $"[{Container?.Name}] {AnimationFile}";
        }
    }
}
