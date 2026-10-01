using System.Collections;
using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Xml;
using System.Xml.Serialization;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationFragmentEditor.AnimationPack.Converters;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter;
using Wh3Format = Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationBinWh3Converter;
using FragFormat = Editors.AnimationFragmentEditor.AnimationPack.Converters.AnimationFragmentConverter;
using Editors.Shared.Core.Editors.TextEditor;
using GameWorld.Core.Services;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Editors.TextEditor;

namespace Editors.AnimationFragmentEditor.AnimationPack.ViewModels
{
    public partial class AnimSetTableEditorViewModel : NotifyPropertyChangedImpl
    {
        private readonly IPackFileService _pfs;
        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly PackFile _animPackFile;
        private readonly GameTypeEnum _gameType;

        // Header metadata - WH3
        private string _name = string.Empty;
        private string _skeletonName = string.Empty;
        private string _mountBin = string.Empty;
        private string _unmountBin = string.Empty;
        private List<string> _fragmentSkeletons = new();
        private string _locomotionGraph = string.Empty;
        private uint _tableVersion = 4;
        private uint _tableSubVersion = 3;
        private short _unknownValue1;

        // Header metadata - Fragment
        private string _skeleton = string.Empty;

        // State
        private bool _isWh3 = true;
        private bool _isDirty;
        private bool _hasInvalidFields;
        public bool HasInvalidFields { get => _hasInvalidFields; set { SetAndNotifyWhenChanged(ref _hasInvalidFields, value); if (value) MarkDirty(); } }
        private string _savedXml = string.Empty;
        private AnimationEntryRowViewModel? _selectedRow;
        private IList _multiSelectedRows = new List<AnimationEntryRowViewModel>();
        private ITextConverter? _activeConverter;
        private bool _suppressDirtyTracking;
        private readonly HashSet<AnimationEntryRowViewModel> _trackedRows = new();
        private string _feedbackMessage = string.Empty;

        // Undo system - snapshot based (includes header metadata)
        private readonly Stack<TableSnapshot> _undoSnapshots = new();
        private readonly Stack<TableSnapshot> _redoSnapshots = new();
        private const int MaxUndoDepth = 50;

        private record TableSnapshot(
            List<AnimationEntryRowViewModel> Rows,
            string Name, string SkeletonName, string MountBin, string UnmountBin, string LocomotionGraph,
            uint TableVersion, uint TableSubVersion, short UnknownValue1,
            string Skeleton, List<string> FragmentSkeletons, bool IsWh3, bool IsDirty);

        private TableSnapshot CaptureSnapshot() => new(
            Rows.Select(r => r.Clone()).ToList(),
            Name, SkeletonName, MountBin, UnmountBin, LocomotionGraph,
            TableVersion, TableSubVersion, UnknownValue1,
            Skeleton, _fragmentSkeletons.ToList(), IsWh3, IsDirty);

        public void SaveSnapshot()
        {
            _redoSnapshots.Clear();
            RedoCommand.NotifyCanExecuteChanged();
            if (_undoSnapshots.Count > 0 && SnapshotEquals(_undoSnapshots.Peek(), CaptureSnapshot()))
                return;
            _undoSnapshots.Push(CaptureSnapshot());
            if (_undoSnapshots.Count > MaxUndoDepth)
            {
                var retainedSnapshots = _undoSnapshots
                    .Take(MaxUndoDepth)
                    .Reverse()
                    .ToArray();
                _undoSnapshots.Clear();
                foreach (var snapshot in retainedSnapshots)
                    _undoSnapshots.Push(snapshot);
            }
            UndoCommand.NotifyCanExecuteChanged();
        }

        public void DiscardLastSnapshot()
        {
            if (_undoSnapshots.Count > 0)
                _undoSnapshots.Pop();
            UndoCommand.NotifyCanExecuteChanged();
        }

        public void DiscardLastSnapshotIfUnchanged()
        {
            if (_undoSnapshots.Count > 0 && SnapshotEquals(_undoSnapshots.Peek(), CaptureSnapshot()))
                _undoSnapshots.Pop();
            UndoCommand.NotifyCanExecuteChanged();
        }

        private static bool SnapshotEquals(TableSnapshot left, TableSnapshot right)
        {
            return left.Name == right.Name &&
                left.SkeletonName == right.SkeletonName &&
                left.MountBin == right.MountBin &&
                left.UnmountBin == right.UnmountBin &&
                left.LocomotionGraph == right.LocomotionGraph &&
                left.TableVersion == right.TableVersion &&
                left.TableSubVersion == right.TableSubVersion &&
                left.UnknownValue1 == right.UnknownValue1 &&
                left.Skeleton == right.Skeleton &&
                left.FragmentSkeletons.SequenceEqual(right.FragmentSkeletons) &&
                left.IsWh3 == right.IsWh3 &&
                left.IsDirty == right.IsDirty &&
                left.Rows.Count == right.Rows.Count &&
                left.Rows.Zip(right.Rows).All(pair => RowsEqual(pair.First, pair.Second));
        }

        private static bool RowsEqual(AnimationEntryRowViewModel left, AnimationEntryRowViewModel right)
        {
            return left.SlotIndex == right.SlotIndex &&
                left.SlotName == right.SlotName &&
                left.AnimationFile == right.AnimationFile &&
                left.MetaFile == right.MetaFile &&
                left.SoundFile == right.SoundFile &&
                left.BlendInTime == right.BlendInTime &&
                left.SelectionWeight == right.SelectionWeight &&
                left.Unk == right.Unk &&
                left.VariantIndex == right.VariantIndex &&
                left.SlotGroupId == right.SlotGroupId &&
                left.HasReference == right.HasReference &&
                left.FragmentUnknown == right.FragmentUnknown &&
                left.FragmentRecordId == right.FragmentRecordId &&
                left.FragmentSkeleton == right.FragmentSkeleton &&
                left.Comment == right.Comment && left.Ignore == right.Ignore &&
                left.GetWeaponBoneAsInt() == right.GetWeaponBoneAsInt();
        }

        public ObservableCollection<AnimationEntryRowViewModel> Rows { get; } = new();
        public List<string> SlotNames { get; private set; } = new();

        // Full file lists (private, loaded once)
        private List<string> _allAnimFiles = new();
        private List<string> _allMetaFiles = new();
        private List<string> _allSoundFiles = new();

        // Filtered views for ComboBox binding (populated on demand)
        public ObservableCollection<string> AnimFiles { get; } = new();
        public ObservableCollection<string> MetaFiles { get; } = new();
        public ObservableCollection<string> SoundFiles { get; } = new();

        // Filter methods called from code-behind
        public void UpdateAnimFileFilter(string keyword) => UpdateFileFilter(_allAnimFiles, AnimFiles, keyword);
        public void UpdateMetaFileFilter(string keyword) => UpdateFileFilter(_allMetaFiles, MetaFiles, keyword);
        public void UpdateSoundFileFilter(string keyword) => UpdateFileFilter(_allSoundFiles, SoundFiles, keyword);

        private static void UpdateFileFilter(List<string> source, ObservableCollection<string> target, string keyword)
        {
            target.Clear();
            IEnumerable<string> results;
            if (string.IsNullOrWhiteSpace(keyword))
                results = source.Take(50); // Show first 50 when empty
            else
            {
                // Case-insensitive contains matching (fuzzy search)
                var lower = keyword.ToLowerInvariant();
                results = source.Where(f => f.ToLowerInvariant().Contains(lower)).Take(100);
            }
            foreach (var f in results)
                target.Add(f);
        }

        // Save command - set by AnimPackViewModel
        public ICommand? SaveCommand { get; set; }

        // WH3 header properties
        public string Name { get => _name; set => SetAndMarkDirty(ref _name, value); }
        public string SkeletonName { get => _skeletonName; set => SetAndMarkDirty(ref _skeletonName, value); }
        public string MountBin { get => _mountBin; set => SetAndMarkDirty(ref _mountBin, value); }
        public string UnmountBin { get => _unmountBin; set => SetAndMarkDirty(ref _unmountBin, value); }
        public string LocomotionGraph { get => _locomotionGraph; set => SetAndMarkDirty(ref _locomotionGraph, value); }
        public uint TableVersion
        {
            get => _tableVersion;
            set
            {
                SetAndMarkDirty(ref _tableVersion, value);
                NotifyPropertyChanged(nameof(HasWh3Header));
            }
        }
        public uint TableSubVersion { get => _tableSubVersion; set => SetAndMarkDirty(ref _tableSubVersion, value); }
        public short UnknownValue1
        {
            get => _unknownValue1;
            set
            {
                SetAndMarkDirty(ref _unknownValue1, value);
                NotifyPropertyChanged(nameof(SimpleFlight));
                NotifyPropertyChanged(nameof(NewCavalryTechnology));
            }
        }
        public bool SimpleFlight { get => (UnknownValue1 & 1) != 0; set => UnknownValue1 = (short)((UnknownValue1 & ~1) | (value ? 1 : 0)); }
        public bool NewCavalryTechnology { get => (UnknownValue1 & 256) != 0; set => UnknownValue1 = (short)((UnknownValue1 & ~256) | (value ? 256 : 0)); }

        // Fragment header
        public string Skeleton
        {
            get => _skeleton;
            set
            {
                var oldSkeleton = _skeleton;
                SetAndMarkDirty(ref _skeleton, value);
                if (!_suppressDirtyTracking && oldSkeleton != value)
                {
                    _suppressDirtyTracking = true;
                    try
                    {
                        _fragmentSkeletons = _fragmentSkeletons.Select(s => s == oldSkeleton ? value : s).ToList();
                        foreach (var row in Rows.Where(r => r.FragmentSkeleton == oldSkeleton))
                            row.FragmentSkeleton = value;
                    }
                    finally { _suppressDirtyTracking = false; }
                }
            }
        }

        // State
        public bool IsWh3
        {
            get => _isWh3;
            set
            {
                SetAndNotify(ref _isWh3, value);
                NotifyPropertyChanged(nameof(HasWh3Header));
            }
        }
        public bool HasWh3Header => IsWh3 && TableVersion == 4;
        public bool IsDirty
        {
            get => _isDirty;
            set
            {
                if (!value && _activeConverter != null)
                    _savedXml = BuildXmlString();
                SetAndNotifyWhenChanged(ref _isDirty, value);
            }
        }
        public string FeedbackMessage
        {
            get => _feedbackMessage;
            private set
            {
                SetAndNotifyWhenChanged(ref _feedbackMessage, value);
                NotifyPropertyChanged(nameof(HasFeedback));
            }
        }
        public bool HasFeedback => !string.IsNullOrWhiteSpace(FeedbackMessage);
        public AnimationEntryRowViewModel? SelectedRow
        {
            get => _selectedRow;
            set
            {
                SetAndNotify(ref _selectedRow, value);
                NotifyRowCommandStates();
            }
        }

        public IList MultiSelectedRows
        {
            get => _multiSelectedRows;
            set
            {
                SetAndNotify(ref _multiSelectedRows, value);
                NotifyRowCommandStates();
            }
        }

        public AnimSetTableEditorViewModel(
            IPackFileService pfs,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper,
            MetaDataFileParser metaDataFileParser,
            PackFile animPackFile,
            GameTypeEnum gameType)
        {
            _pfs = pfs;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _metaDataFileParser = metaDataFileParser;
            _animPackFile = animPackFile;
            _gameType = gameType;
            Rows.CollectionChanged += Rows_CollectionChanged;
            LoadFileLists();
        }

        private void SetAndMarkDirty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            if (!_suppressDirtyTracking)
                SaveSnapshot();
            SetAndNotifyWhenChanged(ref field, value, propertyName: propertyName);
            MarkDirty();
        }

        private void MarkDirty()
        {
            if (!_suppressDirtyTracking)
            {
                IsDirty = true;
                NotifyRowCommandStates();
            }
        }

        private void Rows_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                foreach (var row in _trackedRows)
                {
                    row.Editing -= Row_Editing;
                    row.PropertyChanged -= Row_PropertyChanged;
                }
                _trackedRows.Clear();

                foreach (var row in Rows)
                    TrackRow(row);
            }
            else
            {
                if (e.OldItems != null)
                {
                    foreach (AnimationEntryRowViewModel row in e.OldItems)
                        UntrackRow(row);
                }

                if (e.NewItems != null)
                {
                    foreach (AnimationEntryRowViewModel row in e.NewItems)
                        TrackRow(row);
                }
            }

            MarkDirty();
            NotifyRowCommandStates();
        }

        private void TrackRow(AnimationEntryRowViewModel row)
        {
            if (_trackedRows.Add(row))
            {
                row.Editing += Row_Editing;
                row.PropertyChanged += Row_PropertyChanged;
            }
        }

        private void UntrackRow(AnimationEntryRowViewModel row)
        {
            if (_trackedRows.Remove(row))
            {
                row.Editing -= Row_Editing;
                row.PropertyChanged -= Row_PropertyChanged;
            }
        }

        private void Row_Editing(object? sender, EventArgs e)
        {
            if (!_suppressDirtyTracking)
                SaveSnapshot();
        }

        private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_suppressDirtyTracking || sender is not AnimationEntryRowViewModel row)
                return;
            _suppressDirtyTracking = true;
            try
            {
                if (e.PropertyName is nameof(AnimationEntryRowViewModel.AnimationFile) or nameof(AnimationEntryRowViewModel.MetaFile) or nameof(AnimationEntryRowViewModel.SoundFile))
                    row.HasReference = true;
                if (e.PropertyName == nameof(AnimationEntryRowViewModel.SlotName))
                {
                    var helper = IsWh3 ? (TableVersion == 4 ? AnimationSlotTypeHelperWh3.GetInstance() : AnimationSlotTypeHelper3k.GetInstance())
                        : (_gameType == GameTypeEnum.Troy ? AnimationSlotTypeHelperTroy.GetInstance() : DefaultAnimationSlotTypeHelper.GetInstance());
                    row.SlotIndex = helper.GetfromValue(row.SlotName)?.Id ?? -1;
                    var peer = Rows.FirstOrDefault(r => r != row && r.SlotName == row.SlotName);
                    row.SlotGroupId = peer?.SlotGroupId ?? Rows.Max(r => r.SlotGroupId) + 1;
                    if (IsWh3 && peer != null) CopySlotParameters(peer, row);
                }
                if (IsWh3 && e.PropertyName is nameof(AnimationEntryRowViewModel.BlendInTime) or nameof(AnimationEntryRowViewModel.SelectionWeight)
                    or nameof(AnimationEntryRowViewModel.Unk) or nameof(AnimationEntryRowViewModel.Wb0) or nameof(AnimationEntryRowViewModel.Wb1)
                    or nameof(AnimationEntryRowViewModel.Wb2) or nameof(AnimationEntryRowViewModel.Wb3) or nameof(AnimationEntryRowViewModel.Wb4) or nameof(AnimationEntryRowViewModel.Wb5))
                {
                    foreach (var peer in Rows.Where(r => r != row && r.SlotGroupId == row.SlotGroupId))
                    {
                        peer.BlendInTime = row.BlendInTime;
                        peer.SelectionWeight = row.SelectionWeight;
                        peer.Unk = row.Unk;
                        peer.SetWeaponBoneFromInt(row.GetWeaponBoneAsInt());
                    }
                }
            }
            finally { _suppressDirtyTracking = false; }
            MarkDirty();
            RefreshRowsFilter();
        }

        private void LoadFileLists()
        {
            // Populate private lists (loaded once)
            foreach (var container in _pfs.GetAllPackfileContainers() ?? [])
            {
                foreach (var kvp in container.FileList)
                {
                    var path = kvp.Key;
                    if (path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                        _allAnimFiles.Add(path);
                    else if (path.EndsWith(".anm.meta", StringComparison.OrdinalIgnoreCase))
                        _allMetaFiles.Add(path);
                    else if (path.EndsWith(".snd.meta", StringComparison.OrdinalIgnoreCase))
                        _allSoundFiles.Add(path);
                }
            }
            // Sort for consistent ordering
            _allAnimFiles.Sort(StringComparer.OrdinalIgnoreCase);
            _allMetaFiles.Sort(StringComparer.OrdinalIgnoreCase);
            _allSoundFiles.Sort(StringComparer.OrdinalIgnoreCase);
        }

        public void LoadFromBinary(byte[] bytes, string fileName)
        {
            _suppressDirtyTracking = true;
            try
            {
                Rows.Clear();
                _undoSnapshots.Clear();
                _redoSnapshots.Clear();
                _activeConverter = null;

                try
                {
                    var bin = new AnimationBinWh3("", bytes);
                    LoadFromWh3Bin(bin);
                    return;
                }
                catch { }

                try
                {
                    var frag = new AnimationFragmentFile("", bytes, _gameType);
                    LoadFromFragment(frag);
                    return;
                }
                catch { }

                IsWh3 = true;
            }
            finally
            {
                _suppressDirtyTracking = false;
                IsDirty = false;
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
            }
        }

        private void LoadFromWh3Bin(AnimationBinWh3 bin)
        {
            IsWh3 = true;
            Name = bin.Name;
            SkeletonName = bin.SkeletonName;
            MountBin = bin.MountBin;
            UnmountBin = bin.Unknown;
            LocomotionGraph = bin.LocomotionGraph;
            TableVersion = bin.TableVersion;
            TableSubVersion = bin.TableSubVersion;
            UnknownValue1 = bin.UnknownValue1;

            var slotHelper = bin.TableVersion == 4
                ? AnimationSlotTypeHelperWh3.GetInstance()
                : AnimationSlotTypeHelper3k.GetInstance();
            SlotNames = slotHelper.Values.Select(v => v.Value).OrderBy(s => s).ToList();

            _activeConverter = new AnimationBinWh3FileToXmlConverter(
                _skeletonAnimationLookUpHelper, _metaDataFileParser, _animPackFile);

            foreach (var entry in bin.AnimationTableEntries)
            {
                var slotValue = slotHelper.TryGetFromId((int)entry.AnimationId);
                var slotName = slotValue?.Value ?? LocalizationManager.Instance.GetFormat("AnimPack.Validation.UnknownSlot", entry.AnimationId);

                for (int i = 0; i < Math.Max(1, entry.AnimationRefs.Count); i++)
                {
                    var animRef = entry.AnimationRefs.ElementAtOrDefault(i);
                    var row = new AnimationEntryRowViewModel
                    {
                        SlotIndex = (int)entry.AnimationId,
                        SlotGroupId = bin.AnimationTableEntries.IndexOf(entry),
                        HasReference = animRef != null,
                        SlotName = slotName,
                        AnimationFile = animRef?.AnimationFile ?? string.Empty,
                        MetaFile = animRef?.AnimationMetaFile ?? string.Empty,
                        SoundFile = animRef?.AnimationSoundMetaFile ?? string.Empty,
                        BlendInTime = entry.BlendIn,
                        SelectionWeight = entry.SelectionWeight,
                        Unk = entry.Unk,
                        VariantIndex = i,
                    };
                    row.SetWeaponBoneFromInt(entry.WeaponBools);
                    Rows.Add(row);
                }
            }

            NotifyPropertyChanged(nameof(SlotNames));
        }

        private void LoadFromFragment(AnimationFragmentFile frag)
        {
            IsWh3 = false;
            Skeleton = frag.Skeletons.Values.FirstOrDefault() ?? "";
            _fragmentSkeletons = frag.Skeletons.Values.ToList();

            var slotHelper = _gameType == GameTypeEnum.Troy
                ? AnimationSlotTypeHelperTroy.GetInstance()
                : DefaultAnimationSlotTypeHelper.GetInstance();
            SlotNames = slotHelper.Values.Select(v => v.Value).OrderBy(s => s).ToList();

            _activeConverter = new AnimationFragmentFileToXmlConverter(
                _skeletonAnimationLookUpHelper, _gameType);

            foreach (var item in frag.Fragments)
            {
                var row = new AnimationEntryRowViewModel
                {
                    SlotIndex = item.Slot.Id,
                    SlotGroupId = frag.Fragments.IndexOf(item),
                    FragmentSkeleton = item.Skeleton,
                    FragmentRecordId = item.RecordId,
                    FragmentUnknown = item.Unknown0,
                    Comment = item.Comment,
                    Ignore = item.Ignore,
                    SlotName = item.Slot.Value,
                    AnimationFile = item.AnimationFile,
                    MetaFile = item.MetaDataFile,
                    SoundFile = item.SoundMetaDataFile,
                    BlendInTime = item.BlendInTime,
                    SelectionWeight = item.SelectionWeight,
                };
                row.SetWeaponBoneFromInt(item.WeaponBone);
                Rows.Add(row);
            }

            NotifyPropertyChanged(nameof(SlotNames));
        }

        public byte[]? SaveToBinary(string fileName, out ITextConverter.SaveError? error)
        {
            if (HasInvalidFields)
            {
                error = new ITextConverter.SaveError { Text = LocalizationManager.Instance?.Get("AnimPack.Campaign.InvalidField") ?? "AnimPack.Campaign.InvalidField" };
                return null;
            }
            if (_activeConverter == null)
            {
                error = new ITextConverter.SaveError { Text = LocalizationManager.Instance?.Get("AnimPack.Table.Unsupported") ?? "AnimPack.Table.Unsupported" };
                return null;
            }
            return _activeConverter.ToBytes(BuildXmlString(), fileName, _pfs, out error);
        }
        public string BuildXmlString()
        {
            if (IsWh3)
            {
                var xmlFormat = new Wh3Format.XmlFormat
                {
                    Version = TableVersion == 4 ? "Wh3" : "ThreeKingdom",
                    Data = new GeneralBinData
                    {
                        TableVersion = TableVersion,
                        TableSubVersion = TableSubVersion,
                        Name = Name,
                        MountBin = MountBin,
                        UnmountBin = UnmountBin,
                        SkeletonName = SkeletonName,
                        LocomotionGraph = LocomotionGraph,
                        UnknownValue1_RelatedToFlight = UnknownValue1,
                    },
                    Animations = new List<Wh3Format.Animation>()
                };

                var groups = new List<List<AnimationEntryRowViewModel>>();
                var groupMap = new Dictionary<int, List<AnimationEntryRowViewModel>>();
                foreach (var row in Rows)
                {
                    if (!groupMap.TryGetValue(row.SlotGroupId, out var group))
                    {
                        group = new List<AnimationEntryRowViewModel>();
                        groupMap[row.SlotGroupId] = group;
                        groups.Add(group);
                    }
                    group.Add(row);
                }

                foreach (var group in groups)
                {
                    var first = group[0];
                    var animEntry = new Wh3Format.Animation
                    {
                        Slot = first.SlotName,
                        SlotId = first.SlotIndex,
                        ReservedWeaponFlags = first.ReservedWeaponFlags,
                        BlendId = first.BlendInTime,
                        BlendOut = first.SelectionWeight,
                        WeaponBone = first.WeaponBone,
                        Unk = first.Unk,
                        Ref = new List<Instance>()
                    };

                    foreach (var row in group.Where(r => r.HasReference))
                    {
                        animEntry.Ref.Add(new Instance
                        {
                            File = row.AnimationFile,
                            Meta = row.MetaFile,
                            Sound = row.SoundFile,
                        });
                    }

                    xmlFormat.Animations.Add(animEntry);
                }

                return SerializeToXml(xmlFormat);
            }
            else
            {
                var xmlFormat = new FragFormat.Animation
                {
                    Skeleton = Skeleton,
                    Skeletons = _fragmentSkeletons.ToList(),
                    AnimationFragmentEntry = new List<FragFormat.AnimationEntry>()
                };

                foreach (var row in Rows)
                {
                    xmlFormat.AnimationFragmentEntry.Add(new FragFormat.AnimationEntry
                    {
                        Slot = row.SlotName,
                        SlotId = row.SlotIndex,
                        Skeleton = row.FragmentSkeleton,
                        Unknown = row.FragmentUnknown,
                        RecordId = row.FragmentRecordId,
                        Comment = row.Comment,
                        Ignore = row.Ignore,
                        ReservedWeaponFlags = row.ReservedWeaponFlags,
                        File = new FragFormat.ValueItem { Value = row.AnimationFile },
                        Meta = new FragFormat.ValueItem { Value = row.MetaFile },
                        Sound = new FragFormat.ValueItem { Value = row.SoundFile },
                        BlendInTime = new FragFormat.BlendInTime { Value = row.BlendInTime },
                        SelectionWeight = new FragFormat.SelectionWeight { Value = row.SelectionWeight },
                        WeaponBone = row.WeaponBone,
                    });
                }

                return SerializeToXml(xmlFormat);
            }
        }

        private static string SerializeToXml<T>(T obj)
        {
            var serializer = new XmlSerializer(typeof(T));
            using var stringWriter = new StringWriter();
            var ns = new XmlSerializerNamespaces();
            ns.Add("", "");
            using var writer = XmlWriter.Create(stringWriter, new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = true
            });
            serializer.Serialize(writer, obj, ns);
            return stringWriter.ToString();
        }

        // Commands
        private bool CanUndo() => _undoSnapshots.Count > 0;

        [RelayCommand(CanExecute = nameof(CanUndo))]
        private void Undo()
        {
            _redoSnapshots.Push(CaptureSnapshot());
            var snapshot = _undoSnapshots.Pop();
            RestoreSnapshot(snapshot);
        }

        private bool CanRedo() => _redoSnapshots.Count > 0;
        [RelayCommand(CanExecute = nameof(CanRedo))]
        private void Redo()
        {
            _undoSnapshots.Push(CaptureSnapshot());
            RestoreSnapshot(_redoSnapshots.Pop());
        }

        private void RestoreSnapshot(TableSnapshot snapshot)
        {
            _suppressDirtyTracking = true;
            try
            {
                SelectedRow = null;
                Rows.Clear();
                foreach (var row in snapshot.Rows)
                    Rows.Add(row);
                Name = snapshot.Name;
                SkeletonName = snapshot.SkeletonName;
                MountBin = snapshot.MountBin;
                UnmountBin = snapshot.UnmountBin;
                LocomotionGraph = snapshot.LocomotionGraph;
                TableVersion = snapshot.TableVersion;
                TableSubVersion = snapshot.TableSubVersion;
                UnknownValue1 = snapshot.UnknownValue1;
                Skeleton = snapshot.Skeleton;
                _fragmentSkeletons = snapshot.FragmentSkeletons.ToList();
                IsWh3 = snapshot.IsWh3;
            }
            finally
            {
                _suppressDirtyTracking = false;
            }
            var restoredDirty = string.IsNullOrEmpty(_savedXml) ? snapshot.IsDirty : BuildXmlString() != _savedXml;
            SetAndNotifyWhenChanged(ref _isDirty, restoredDirty, propertyName: nameof(IsDirty));
            UndoCommand.NotifyCanExecuteChanged();
            RedoCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand] private void AddEntry()
        {
            SaveSnapshot();
            var newRow = new AnimationEntryRowViewModel
            {
                SlotIndex = 0,
                SlotGroupId = Rows.FirstOrDefault(r => r.SlotName == "STAND")?.SlotGroupId ?? (Rows.Count == 0 ? 0 : Rows.Max(r => r.SlotGroupId) + 1),
                SlotName = "STAND",
                AnimationFile = "",
                MetaFile = "",
                SoundFile = "",
                BlendInTime = 0.0f,
                SelectionWeight = 1.0f,
                VariantIndex = 0,
            };
            Rows.Add(newRow);
            if (IsWh3 && Rows.FirstOrDefault(r => r != newRow && r.SlotGroupId == newRow.SlotGroupId) is { } peer)
            {
                _suppressDirtyTracking = true;
                try { CopySlotParameters(peer, newRow); }
                finally { _suppressDirtyTracking = false; }
            }
            SelectedRow = newRow;
            IsDirty = true;
        }

        private bool CanDeleteEntries() => MultiSelectedRows?.Count > 0;

        [RelayCommand(CanExecute = nameof(CanDeleteEntries))]
        private void DeleteEntries()
        {
            SaveSnapshot();
            var toDelete = MultiSelectedRows.Cast<AnimationEntryRowViewModel>().ToList();
            foreach (var row in toDelete)
                Rows.Remove(row);
            SelectedRow = null;
            IsDirty = true;
        }

        private bool CanDuplicateEntry() => SelectedRow != null;

        [RelayCommand(CanExecute = nameof(CanDuplicateEntry))]
        private void DuplicateEntry()
        {
            SaveSnapshot();
            var index = Rows.IndexOf(SelectedRow!);
            var clone = SelectedRow!.Clone();
            clone.VariantIndex = SelectedRow.VariantIndex + 1;
            Rows.Insert(index + 1, clone);
            SelectedRow = clone;
            IsDirty = true;
        }

        private bool CanMoveUp() => SelectedRow != null && Rows.IndexOf(SelectedRow) > 0;

        [RelayCommand(CanExecute = nameof(CanMoveUp))]
        private void MoveUp()
        {
            var index = Rows.IndexOf(SelectedRow!);
            SaveSnapshot();
            Rows.Move(index, index - 1);
            IsDirty = true;
            NotifyRowCommandStates();
        }

        private bool CanMoveDown()
        {
            var index = SelectedRow == null ? -1 : Rows.IndexOf(SelectedRow);
            return index >= 0 && index < Rows.Count - 1;
        }

        [RelayCommand(CanExecute = nameof(CanMoveDown))]
        private void MoveDown()
        {
            var index = Rows.IndexOf(SelectedRow!);
            SaveSnapshot();
            Rows.Move(index, index + 1);
            IsDirty = true;
            NotifyRowCommandStates();
        }

        private bool CanCopyRows() => MultiSelectedRows?.Count > 0 || SelectedRow != null;

        [RelayCommand(CanExecute = nameof(CanCopyRows))]
        private void CopyRows()
        {
            var rows = MultiSelectedRows?.Cast<AnimationEntryRowViewModel>().ToList()
                ?? new List<AnimationEntryRowViewModel>();
            if (rows.Count == 0 && SelectedRow != null)
                rows.Add(SelectedRow);
            if (rows.Count == 0) return;

            var data = new ClipboardData
            {
                SourceFormat = IsWh3 ? "Wh3" : "Fragment",
                TableVersion = IsWh3 ? TableVersion : 0,
                Game = _gameType.ToString(),
                Rows = rows.Select(r => new ClipboardRow
                {
                    SlotIndex = r.SlotIndex,
                    SlotName = r.SlotName,
                    AnimationFile = r.AnimationFile,
                    MetaFile = r.MetaFile,
                    SoundFile = r.SoundFile,
                    BlendInTime = r.BlendInTime,
                    SelectionWeight = r.SelectionWeight,
                    Wb0 = r.Wb0, Wb1 = r.Wb1, Wb2 = r.Wb2,
                    Wb3 = r.Wb3, Wb4 = r.Wb4, Wb5 = r.Wb5,
                    Unk = r.Unk,
                    VariantIndex = r.VariantIndex,
                    HasReference = r.HasReference,
                    FragmentUnknown = r.FragmentUnknown,
                    FragmentRecordId = r.FragmentRecordId,
                    FragmentSkeleton = r.FragmentSkeleton,
                    Comment = r.Comment, Ignore = r.Ignore,
                    ReservedWeaponFlags = r.ReservedWeaponFlags,
                }).ToList()
            };

            var json = JsonSerializer.Serialize(data);
            Clipboard.SetText("AE_ANIM_ROWS|" + json);
            FeedbackMessage = string.Empty;
            PasteRowsCommand.NotifyCanExecuteChanged();
        }

        private void NotifyRowCommandStates()
        {
            PreviewAnimationCommand.NotifyCanExecuteChanged();
            OpenAnimationCommand.NotifyCanExecuteChanged();
            OpenMetaCommand.NotifyCanExecuteChanged();
            OpenSoundCommand.NotifyCanExecuteChanged();
            OpenMountCommand.NotifyCanExecuteChanged();
            OpenUnmountCommand.NotifyCanExecuteChanged();
            ApplyParametersToSelectedCommand.NotifyCanExecuteChanged();
            DeleteEntriesCommand.NotifyCanExecuteChanged();
            DuplicateEntryCommand.NotifyCanExecuteChanged();
            MoveUpCommand.NotifyCanExecuteChanged();
            MoveDownCommand.NotifyCanExecuteChanged();
            CopyRowsCommand.NotifyCanExecuteChanged();
        }

        public void RefreshCommandStates()
        {
            NotifyRowCommandStates();
            PasteRowsCommand.NotifyCanExecuteChanged();
        }

        private bool CanPasteRows()
        {
            try
            {
                return Clipboard.ContainsText() &&
                    Clipboard.GetText().StartsWith("AE_ANIM_ROWS|", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        [RelayCommand(CanExecute = nameof(CanPasteRows))]
        private void PasteRows()
        {
            var text = Clipboard.GetText();
            if (string.IsNullOrEmpty(text) || !text.StartsWith("AE_ANIM_ROWS|"))
                return;

            try
            {
                var json = text.Substring("AE_ANIM_ROWS|".Length);
                var data = JsonSerializer.Deserialize<ClipboardData>(json);
                if (data?.Rows == null || data.Rows.Count == 0)
                {
                    FeedbackMessage = LocalizationManager.Instance?.Get("AnimPack.Table.PasteInvalid")
                        ?? "AnimPack.Table.PasteInvalid";
                    return;
                }

                SaveSnapshot();
                var insertIndex = SelectedRow != null ? Rows.IndexOf(SelectedRow) + 1 : Rows.Count;
                if (!string.IsNullOrEmpty(data.SourceFormat) && data.SourceFormat != (IsWh3 ? "Wh3" : "Fragment")
                    || IsWh3 && data.TableVersion != 0 && data.TableVersion != TableVersion
                    || !IsWh3 && !string.IsNullOrEmpty(data.Game) && data.Game != _gameType.ToString())
                {
                    DiscardLastSnapshot();
                    FeedbackMessage = LocalizationManager.Instance?.Get("AnimPack.Table.PasteFormatMismatch") ?? "AnimPack.Table.PasteFormatMismatch";
                    return;
                }

                foreach (var row in data.Rows)
                {
                    var newRow = new AnimationEntryRowViewModel
                    {
                        SlotIndex = row.SlotIndex,
                        SlotName = row.SlotName,
                        SlotGroupId = Rows.FirstOrDefault(r => r.SlotName == row.SlotName)?.SlotGroupId ?? (Rows.Count == 0 ? 0 : Rows.Max(r => r.SlotGroupId) + 1),
                        HasReference = row.HasReference,
                        FragmentUnknown = row.FragmentUnknown,
                        FragmentRecordId = row.FragmentRecordId,
                        FragmentSkeleton = row.FragmentSkeleton,
                        Comment = row.Comment, Ignore = row.Ignore,
                        ReservedWeaponFlags = row.ReservedWeaponFlags,
                        AnimationFile = row.AnimationFile,
                        MetaFile = row.MetaFile,
                        SoundFile = row.SoundFile,
                        BlendInTime = row.BlendInTime,
                        SelectionWeight = row.SelectionWeight,
                        Unk = row.Unk,
                        VariantIndex = row.VariantIndex,
                        Wb0 = row.Wb0, Wb1 = row.Wb1, Wb2 = row.Wb2,
                        Wb3 = row.Wb3, Wb4 = row.Wb4, Wb5 = row.Wb5,
                    };
                    Rows.Insert(insertIndex++, newRow);
                    if (IsWh3 && Rows.FirstOrDefault(r => r != newRow && r.SlotGroupId == newRow.SlotGroupId) is { } peer)
                    {
                        _suppressDirtyTracking = true;
                        try { CopySlotParameters(peer, newRow); }
                        finally { _suppressDirtyTracking = false; }
                    }
                }

                IsDirty = true;
                FeedbackMessage = string.Empty;
            }
            catch
            {
                FeedbackMessage = LocalizationManager.Instance?.Get("AnimPack.Table.PasteFailed")
                    ?? "AnimPack.Table.PasteFailed";
            }
        }

        private class ClipboardData
        {
            public string SourceFormat { get; set; } = "";
            public uint TableVersion { get; set; }
            public string Game { get; set; } = "";
            public List<ClipboardRow> Rows { get; set; } = new();
        }

        private class ClipboardRow
        {
            public bool HasReference { get; set; } = true;
            public int FragmentUnknown { get; set; }
            public int? FragmentRecordId { get; set; }
            public string FragmentSkeleton { get; set; } = string.Empty;
            public string Comment { get; set; } = string.Empty;
            public bool Ignore { get; set; }
            public int ReservedWeaponFlags { get; set; }
            public int SlotIndex { get; set; }
            public string SlotName { get; set; } = "";
            public string AnimationFile { get; set; } = "";
            public string MetaFile { get; set; } = "";
            public string SoundFile { get; set; } = "";
            public float BlendInTime { get; set; }
            public float SelectionWeight { get; set; } = 1.0f;
            public bool Wb0 { get; set; }
            public bool Wb1 { get; set; }
            public bool Wb2 { get; set; }
            public bool Wb3 { get; set; }
            public bool Wb4 { get; set; }
            public bool Wb5 { get; set; }
            public bool Unk { get; set; }
            public int VariantIndex { get; set; }
        }
    }
}
