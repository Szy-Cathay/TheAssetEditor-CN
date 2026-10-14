using System.Collections.ObjectModel;
using System.IO;
using GameWorld.Core.Services;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.Animation;

namespace CommonControls.Editors.AnimationBatchExporter
{
    public class AnimationBatchExportViewModel : NotifyPropertyChangedImpl, IEditorInterface
    {
        private readonly ILogger _logger = Logging.Create<AnimationBatchExportViewModel>();
        private readonly IPackFileService _pfs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly IStandardDialogs _standardDialogs;
        private List<AnimationListItem> _animations = [];
        private bool _updatingSelection;
        private bool _closed;
        private int _selectionAnchor = -1;
        private int _dragIndex = -1;
        private bool _dragSelected;

        public ObservableCollection<PackFileListItem> PackfileList { get; } = [];
        public IReadOnlyList<AnimationListItem> Animations => _animations;
        public NotifyAttr<IReadOnlyList<AnimationListItem>> FilteredAnimations { get; } = new([]);
        public IReadOnlyList<OutputFormatOption> PossibleOutputFormats { get; } =
        [
            new(5, Text("AnimBatchExport.Format5Help")),
            new(6, Text("AnimBatchExport.Format6Help")),
            new(7, Text("AnimBatchExport.Format7Help")),
            new(8, Text("AnimBatchExport.Format8Help")),
        ];
        public NotifyAttr<uint> SelectedOutputFormat { get; } = new(7);
        public NotifyAttr<string> FilterText { get; }
        public NotifyAttr<string> SelectionSummary { get; } = new("");
        public NotifyAttr<string> Status { get; } = new("");
        public NotifyAttr<string> CurrentFile { get; } = new("");
        public NotifyAttr<int> CompletedCount { get; } = new(0);
        public NotifyAttr<int> TotalCount { get; } = new(0);
        public NotifyAttr<bool> IsBusy { get; } = new(false);
        public NotifyAttr<bool> CanExport { get; } = new(false);
        public NotifyAttr<string> ExportButtonText { get; } = new("");
        public NotifyAttr<string> EmptyMessage { get; } = new("");
        public NotifyAttr<bool> IsEmpty { get; } = new(true);

        public string DisplayName { get; set; } = Text("DisplayName.AnimationBatchExporter");

        public AnimationBatchExportViewModel(
            IPackFileService pfs,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            IStandardDialogs standardDialogs)
        {
            _pfs = pfs;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _standardDialogs = standardDialogs;
            FilterText = new("", _ => UpdateFilter());
            RefreshSources();
        }

        public void RefreshSources()
        {
            if (IsBusy.Value)
                return;

            _updatingSelection = true;
            PackfileList.Clear();
            var output = _pfs.GetEditablePack();
            foreach (var container in _pfs.GetAllPackfileContainers())
            {
                if (container == output)
                    continue;
                var source = new PackFileListItem(container,
                    PackFileServiceUtility.FindAllWithExtentionIncludePaths(_pfs, ".anim", container),
                    RebuildAnimations);
                PackfileList.Add(source);
            }
            _updatingSelection = false;
            _animations = [];
            RebuildAnimations();
            RefreshWorkspace();
            Status.Value = "";
        }

        public void RefreshWorkspace()
        {
            if (!IsBusy.Value && !PackfileList.Select(x => x.Container).SequenceEqual(
                    _pfs.GetAllPackfileContainers().Where(x => x != _pfs.GetEditablePack())))
            {
                RefreshSources();
                return;
            }
            UpdateSelection();
        }

        private static string DescribeOutput(PackFileContainer? output)
        {
            if (output == null)
                return Text("AnimBatchExport.NoOutput");
            if (output is FolderProjectContainer project)
                return project.ProjectRoot;
            return string.IsNullOrWhiteSpace(output.SystemFilePath)
                ? Format("AnimBatchExport.UnsavedPack", output.Name)
                : output.SystemFilePath;
        }

        private void RebuildAnimations()
        {
            if (_updatingSelection || IsBusy.Value)
                return;

            var selected = _animations.Where(x => x.IsSelected.Value)
                .Select(x => (x.Container, x.Path)).ToHashSet();
            _updatingSelection = true;
            _animations = PackfileList.Where(x => x.Process.Value)
                .SelectMany(source => source.Files.Select(file =>
                    new AnimationListItem(source.Container, file.FileName, file.Pack, UpdateSelection)
                    {
                        IsSelected = { Value = selected.Contains((source.Container, file.FileName)) }
                    }))
                .OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _updatingSelection = false;
            UpdateFilter();
        }

        private void UpdateFilter()
        {
            EndAnimationSelection();
            _selectionAnchor = -1;
            var query = FilterText.Value.Trim();
            FilteredAnimations.Value = _animations.Where(x =>
                query.Length == 0 ||
                x.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            IsEmpty.Value = FilteredAnimations.Value.Count == 0;
            EmptyMessage.Value = Text(PackfileList.Any(x => x.Process.Value)
                ? (_animations.Count == 0 ? "AnimBatchExport.NoAnimations" : "AnimBatchExport.NoMatches")
                : "AnimBatchExport.ChooseSource");
            UpdateSelection();
        }

        private void UpdateSelection()
        {
            if (_updatingSelection)
                return;
            var selectedCount = _animations.Count(x => x.IsSelected.Value);
            var visibleSelected = FilteredAnimations.Value.Count(x => x.IsSelected.Value);
            SelectionSummary.Value = Format("AnimBatchExport.SelectionSummary",
                FilteredAnimations.Value.Count, _animations.Count, selectedCount, selectedCount - visibleSelected);
            ExportButtonText.Value = Format("AnimBatchExport.ExportSelected", selectedCount);
            CanExport.Value = selectedCount > 0 && _pfs.GetEditablePack() != null && !IsBusy.Value;
        }

        public void SelectFiltered()
        {
            EndAnimationSelection();
            _selectionAnchor = -1;
            SetSelection(FilteredAnimations.Value, true);
        }

        public void ClearSelection()
        {
            EndAnimationSelection();
            _selectionAnchor = -1;
            SetSelection(_animations, false);
        }

        public bool BeginAnimationSelection(AnimationListItem item, bool extendRange)
        {
            EndAnimationSelection();
            var index = FindVisibleIndex(item);
            if (IsBusy.Value || _closed || index < 0)
                return false;

            _dragSelected = extendRange || !item.IsSelected.Value;
            if (!extendRange || _selectionAnchor < 0)
                _selectionAnchor = index;
            SetSelectionRange(_selectionAnchor, index, _dragSelected);
            _dragIndex = index;
            return true;
        }

        public bool ContinueAnimationSelection(AnimationListItem item)
        {
            var index = FindVisibleIndex(item);
            if (IsBusy.Value || _closed || _dragIndex < 0 || index < 0)
                return false;
            if (_dragIndex != index)
                SetSelectionRange(_dragIndex, index, _dragSelected);
            _dragIndex = index;
            return true;
        }

        public void EndAnimationSelection() => _dragIndex = -1;

        private int FindVisibleIndex(AnimationListItem item)
        {
            var visible = FilteredAnimations.Value;
            for (var index = 0; index < visible.Count; index++)
            {
                if (ReferenceEquals(visible[index], item))
                    return index;
            }
            return -1;
        }

        private void SetSelectionRange(int start, int end, bool selected)
        {
            SetSelection(FilteredAnimations.Value.Skip(Math.Min(start, end)).Take(Math.Abs(end - start) + 1), selected);
        }

        private void SetSelection(IEnumerable<AnimationListItem> items, bool selected)
        {
            if (IsBusy.Value)
                return;
            _updatingSelection = true;
            foreach (var item in items)
                item.IsSelected.Value = selected;
            _updatingSelection = false;
            UpdateSelection();
        }

        public async Task<ErrorList?> Process()
        {
            if (IsBusy.Value || _closed)
                return null;

            var output = _pfs.GetEditablePack();
            RefreshWorkspace();
            if (output == null)
            {
                _standardDialogs.ShowDialogBox(Text("AnimBatchExport.NoOutput"), DisplayName);
                return null;
            }
            var selected = _animations.Where(x => x.IsSelected.Value).ToArray();
            if (selected.Length == 0)
            {
                Status.Value = Text("AnimBatchExport.ChooseAnimations");
                return null;
            }
            if (!PossibleOutputFormats.Any(option => option.Version == SelectedOutputFormat.Value))
            {
                Status.Value = Text("AnimBatchExport.InvalidFormat");
                return null;
            }
            var duplicate = selected.GroupBy(x => x.Path.Replace('/', '\\'), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicate != null)
            {
                Status.Value = Format("AnimBatchExport.DuplicatePath", duplicate.Key);
                _standardDialogs.ShowDialogBox(Status.Value, DisplayName);
                return null;
            }
            var format = SelectedOutputFormat.Value;
            if (_standardDialogs.ShowYesNoBox(Format("AnimBatchExport.Confirm",
                    selected.Length, format, DescribeOutput(output)), DisplayName) != ShowMessageBoxResult.OK)
                return null;

            var errors = new ErrorList();
            IsBusy.Value = true;
            TotalCount.Value = selected.Length;
            CompletedCount.Value = 0;
            Status.Value = Text("AnimBatchExport.Converting");
            UpdateSelection();
            try
            {
                EnsureSourcesAvailable(selected, output);
                var progress = new Progress<(int Count, string Path)>(value =>
                {
                    if (!IsBusy.Value)
                        return;
                    CompletedCount.Value = value.Count;
                    CurrentFile.Value = value.Path;
                });
                var converted = await Task.Run(() => ConvertAnimations(selected, format, errors, progress));
                EnsureSourcesAvailable(selected, output);
                if (converted.Writes.Count > 0)
                {
                    Status.Value = Text("AnimBatchExport.Writing");
                    await _pfs.ApplyFileWritesAsync(output, converted.Writes, true, CancellationToken.None);
                }
                CompletedCount.Value = selected.Length;
                Status.Value = Format("AnimBatchExport.Completed",
                    converted.AnimationCount, selected.Length - converted.AnimationCount, DescribeOutput(output));
            }
            catch (Exception exception)
            {
                _logger.Error(exception, "Animation batch export failed");
                errors.Error(DescribeOutput(output), exception.Message);
                Status.Value = Format("AnimBatchExport.Failed", exception.Message);
            }
            finally
            {
                IsBusy.Value = false;
                UpdateSelection();
            }
            return errors;
        }

        private void EnsureSourcesAvailable(AnimationListItem[] selected, PackFileContainer output)
        {
            var containers = _pfs.GetAllPackfileContainers();
            if (_closed || _pfs.GetEditablePack() != output ||
                selected.Any(x => !containers.Contains(x.Container) ||
                    !ReferenceEquals(_pfs.FindFile(x.Path, x.Container), x.File)))
                throw new InvalidOperationException(Text("AnimBatchExport.SourceChanged"));
        }

        private (List<PackFileWrite> Writes, int AnimationCount) ConvertAnimations(
            AnimationListItem[] selected, uint format, ErrorList errors,
            IProgress<(int Count, string Path)> progress)
        {
            var writes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var matrices = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var matrixFiles = selected.Select(x => x.Container).Distinct().ToDictionary(
                container => container,
                container => PackFileServiceUtility.FindAllWithExtentionIncludePaths(
                    _pfs, ".bone_inv_trans_mats", container).ToLookup(
                        x => Path.GetFileNameWithoutExtension(x.FileName), StringComparer.OrdinalIgnoreCase));
            var animationCount = 0;
            for (var index = 0; index < selected.Length; index++)
            {
                var item = selected[index];
                try
                {
                    var animation = AnimationFile.Create(item.File);
                    if (format == 8)
                    {
                        // The v8 writer preserves static/absent tracks and all animation parts.
                        animation.Header.Version = 8;
                    }
                    else
                    {
                        var skeleton = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(animation.Header.SkeletonName);
                        if (skeleton == null && animation.AnimationParts.Any(part =>
                                part.DynamicFrames.Count > 0 &&
                                (part.TranslationMappings.Any(mapping => !mapping.HasValue) ||
                                 part.RotationMappings.Any(mapping => !mapping.HasValue))))
                            throw new InvalidOperationException(
                                Format("AnimBatchExport.MissingSkeleton", animation.Header.SkeletonName));
                        animation.ConvertToVersion(format, skeleton!, _pfs);
                    }
                    var bytes = AnimationFile.ConvertToBytes(animation);
                    // Only bring along matrices belonging to a selected animation's skeleton.
                    var skeletonName = Path.GetFileNameWithoutExtension(animation.Header.SkeletonName);
                    var relatedMatrices = matrixFiles[item.Container][skeletonName]
                        .Select(matrix => (matrix.FileName, Data: matrix.Pack.DataSource.ReadData())).ToArray();
                    foreach (var matrix in relatedMatrices)
                    {
                        if (matrices.TryGetValue(matrix.FileName, out var previous) && !previous.SequenceEqual(matrix.Data))
                            throw new InvalidOperationException(Format("AnimBatchExport.DuplicatePath", matrix.FileName));
                    }
                    foreach (var matrix in relatedMatrices)
                        matrices[matrix.FileName] = matrix.Data;
                    writes.Add(item.Path, bytes);
                    animationCount++;
                }
                catch (Exception exception)
                {
                    _logger.Error(exception, "Could not convert {Source}: {Path}", item.SourceName, item.Path);
                    errors.Error(item.SourceName + ": " + item.Path, exception.Message);
                }
                progress.Report((index + 1, item.Path));
            }
            var result = writes.Concat(matrices).Select(x => new PackFileWrite(x.Key, x.Value)).ToList();
            return (result, animationCount);
        }

        public void ShowResults(ErrorList errors)
        {
            if (!_closed && errors.HasData)
                _standardDialogs.ShowErrorViewDialog(Text("AnimBatchExport.Results"), errors, true);
        }

        public void Close()
        {
            EndAnimationSelection();
            _closed = true;
        }

        public sealed record OutputFormatOption(uint Version, string Description);

        private static string Text(string key) => LocalizationManager.Instance.Get(key);
        private static string Format(string key, params object[] values) => LocalizationManager.Instance.GetFormat(key, values);

        public class PackFileListItem
        {
            public PackFileContainer Container { get; }
            public IReadOnlyList<(string FileName, PackFile Pack)> Files { get; }
            public string Name => Container.Name;
            public string Details { get; }
            public NotifyAttr<bool> Process { get; }

            public PackFileListItem(PackFileContainer container,
                IReadOnlyList<(string FileName, PackFile Pack)> files, Action changed)
            {
                Container = container;
                Files = files;
                Details = Format("AnimBatchExport.ReadOnlySource", files.Count);
                Process = new(false, _ => changed());
            }
        }

        public class AnimationListItem
        {
            public PackFileContainer Container { get; }
            public PackFile File { get; }
            public string Path { get; }
            public string SourceName => Container.Name;
            public NotifyAttr<bool> IsSelected { get; }

            public AnimationListItem(PackFileContainer container, string path, PackFile file, Action changed)
            {
                Container = container;
                Path = path;
                File = file;
                IsSelected = new(false, _ => changed());
            }
        }
    }
}
