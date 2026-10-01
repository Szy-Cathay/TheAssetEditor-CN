using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Services;
using Shared.Core.ErrorHandling;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using System.Xml.Serialization;
using FragFormat = Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter;

namespace Editors.AnimationFragmentEditor.AnimationPack.ViewModels
{
    public partial class AnimSetTableEditorViewModel
    {
        private string _rowFilter = string.Empty;
        private bool _onlyProblemRows;
        public ICollectionView FilteredRows => CollectionViewSource.GetDefaultView(Rows);
        public string RowFilter { get => _rowFilter; set => SetAndNotifyWhenChanged(ref _rowFilter, value, _ => RefreshRowsFilter()); }
        public bool OnlyProblemRows { get => _onlyProblemRows; set => SetAndNotifyWhenChanged(ref _onlyProblemRows, value, _ => RefreshRowsFilter()); }
        public string ReplaceFrom { get; set; } = string.Empty;
        public string ReplaceTo { get; set; } = string.Empty;
        public bool ReplaceSelectedOnly { get; set; } = true;
        public Action<AnimationEntryRowViewModel>? PreviewRow { get; set; }
        public Action<string>? OpenResource { get; set; }
        public Action<ErrorList>? ShowValidation { get; set; }
        [RelayCommand]
        private void Validate()
        {
            using var reader = new System.IO.StringReader(BuildXmlString());
            if (_activeConverter is AnimationBinWh3FileToXmlConverter converter)
                ShowValidation?.Invoke(converter.Check((XmlFormat)new XmlSerializer(typeof(XmlFormat)).Deserialize(reader)!, _pfs, Name + ".bin"));
            else
            {
                var xml = (FragFormat.Animation)new XmlSerializer(typeof(FragFormat.Animation)).Deserialize(reader)!;
                ShowValidation?.Invoke(FragFormat.Validator.Check(_skeletonAnimationLookUpHelper, xml, _pfs, _gameType));
            }
        }
        public List<string> SkeletonNames { get; private set; } = new();
        public List<string> AnimationSetNames { get; private set; } = new();
        public List<string> GraphNames { get; private set; } = new();

        public void SetResourceNames(IEnumerable<string> paths)
        {
            var resources = paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            _allAnimFiles = _allAnimFiles.Concat(resources.Where(p => p.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _allMetaFiles = _allMetaFiles.Concat(resources.Where(p => p.EndsWith(".anm.meta", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".snd.meta", StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _allSoundFiles = _allSoundFiles.Concat(resources.Where(p => p.EndsWith(".snd.meta", StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            SkeletonNames = _allAnimFiles.Where(p => p.Replace('\\', '/').Contains("/skeletons/", StringComparison.OrdinalIgnoreCase)).Select(p => System.IO.Path.GetFileNameWithoutExtension(p.Replace('\\', '/'))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            AnimationSetNames = resources.Where(p => p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)).Select(p => System.IO.Path.GetFileNameWithoutExtension(p.Replace('\\', '/'))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            GraphNames = resources.Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && p.Contains("locomotion", StringComparison.OrdinalIgnoreCase)).ToList();
            NotifyPropertyChanged(nameof(SkeletonNames));
            NotifyPropertyChanged(nameof(AnimationSetNames));
            NotifyPropertyChanged(nameof(GraphNames));
        }

        private bool IsProblemRow(AnimationEntryRowViewModel row) => row.HasReference &&
            (string.IsNullOrWhiteSpace(row.AnimationFile) || !_allAnimFiles.Contains(row.AnimationFile, StringComparer.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(row.MetaFile) && !_allMetaFiles.Contains(row.MetaFile, StringComparer.OrdinalIgnoreCase)
                || !string.IsNullOrEmpty(row.SoundFile) && !_allSoundFiles.Contains(row.SoundFile, StringComparer.OrdinalIgnoreCase));

        private void RefreshRowsFilter()
        {
            if (FilteredRows is IEditableCollectionView editing && (editing.IsEditingItem || editing.IsAddingNew))
                return;
            FilteredRows.Filter = item => item is AnimationEntryRowViewModel row &&
                (!OnlyProblemRows || IsProblemRow(row)) &&
                (string.IsNullOrWhiteSpace(RowFilter) || $"{row.SlotIndex} {row.SlotName} {row.AnimationFile} {row.MetaFile} {row.SoundFile} {row.Comment}".Contains(RowFilter, StringComparison.OrdinalIgnoreCase));
            FilteredRows.Refresh();
        }

        [RelayCommand]
        private void ReplacePaths()
        {
            if (string.IsNullOrEmpty(ReplaceFrom))
                return;
            var targets = ReplaceSelectedOnly ? MultiSelectedRows.Cast<AnimationEntryRowViewModel>().ToList() : Rows.ToList();
            if (targets.Count == 0 && ReplaceSelectedOnly && SelectedRow != null)
                targets.Add(SelectedRow);
            SaveSnapshot();
            _suppressDirtyTracking = true;
            try
            {
                foreach (var row in targets)
                {
                    row.AnimationFile = row.AnimationFile.Replace(ReplaceFrom, ReplaceTo, StringComparison.OrdinalIgnoreCase);
                    row.MetaFile = row.MetaFile.Replace(ReplaceFrom, ReplaceTo, StringComparison.OrdinalIgnoreCase);
                    row.SoundFile = row.SoundFile.Replace(ReplaceFrom, ReplaceTo, StringComparison.OrdinalIgnoreCase);
                }
            }
            finally { _suppressDirtyTracking = false; }
            if (_undoSnapshots.Count > 0 && !SnapshotEquals(_undoSnapshots.Peek(), CaptureSnapshot()))
                IsDirty = true;
            else
                DiscardLastSnapshotIfUnchanged();
            RefreshRowsFilter();
        }

        private bool CanUseRow() => SelectedRow != null;
        private bool CanOpenAnimation() => !string.IsNullOrWhiteSpace(SelectedRow?.AnimationFile);
        private bool CanOpenMeta() => !string.IsNullOrWhiteSpace(SelectedRow?.MetaFile);
        private bool CanOpenSound() => !string.IsNullOrWhiteSpace(SelectedRow?.SoundFile);
        private bool CanPreviewAnimation() => CanOpenAnimation() && !string.IsNullOrWhiteSpace(IsWh3 ? SkeletonName
            : string.IsNullOrWhiteSpace(SelectedRow?.FragmentSkeleton) ? Skeleton : SelectedRow.FragmentSkeleton);
        private bool CanOpenMount() => IsWh3 && !string.IsNullOrWhiteSpace(MountBin);
        private bool CanOpenUnmount() => IsWh3 && !string.IsNullOrWhiteSpace(UnmountBin);
        [RelayCommand(CanExecute = nameof(CanPreviewAnimation))]
        private void PreviewAnimation() { if (SelectedRow != null) PreviewRow?.Invoke(SelectedRow); }
        [RelayCommand(CanExecute = nameof(CanOpenAnimation))]
        private void OpenAnimation() => OpenResource?.Invoke(SelectedRow?.AnimationFile ?? string.Empty);
        [RelayCommand(CanExecute = nameof(CanOpenMeta))]
        private void OpenMeta() => OpenResource?.Invoke(SelectedRow?.MetaFile ?? string.Empty);
        [RelayCommand(CanExecute = nameof(CanOpenSound))]
        private void OpenSound() => OpenResource?.Invoke(SelectedRow?.SoundFile ?? string.Empty);
        [RelayCommand(CanExecute = nameof(CanOpenMount))]
        private void OpenMount() => OpenResource?.Invoke($"animations/database/battle/bin/{MountBin}.bin");
        [RelayCommand(CanExecute = nameof(CanOpenUnmount))]
        private void OpenUnmount() => OpenResource?.Invoke($"animations/database/battle/bin/{UnmountBin}.bin");

        [RelayCommand(CanExecute = nameof(CanUseRow))]
        private void ApplyParametersToSelected()
        {
            if (SelectedRow == null)
                return;
            SaveSnapshot();
            var source = SelectedRow.Clone();
            var selected = MultiSelectedRows.Cast<AnimationEntryRowViewModel>().ToArray();
            var groups = selected.Select(r => r.SlotGroupId).ToHashSet();
            _suppressDirtyTracking = true;
            try
            {
                foreach (var row in IsWh3 ? Rows.Where(r => groups.Contains(r.SlotGroupId)) : selected)
                    CopySlotParameters(source, row);
            }
            finally { _suppressDirtyTracking = false; }
            MarkDirty();
        }

        private static void CopySlotParameters(AnimationEntryRowViewModel source, AnimationEntryRowViewModel target)
        {
            target.BlendInTime = source.BlendInTime;
            target.SelectionWeight = source.SelectionWeight;
            target.SetWeaponBoneFromInt(source.GetWeaponBoneAsInt());
            target.Unk = source.Unk;
        }
    }
}
