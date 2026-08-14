using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Controls;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Inspection;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView
{
    public partial class SuperViewViewModel : EditorHostBase, ISaveableEditor
    {
        SceneObjectViewModel _asset = null!;

        private readonly SceneObjectEditor _sceneObjectBuilder;
        private readonly MetaDataFileParser _metaDataFileParser;
        private readonly IMetaDataBuilder _metaDataFactory;
        private readonly IPackFileService _packFileService;
        private readonly IEventHub _eventHub;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly CombatMetaDataEditSession _combatEditSession;
        private readonly CombatMetaDataGizmoComponent _combatGizmo;
        private readonly MetaDataMarkerPickerComponent _markerPicker;
        private CombatMetaDataPoint _selectedSplashPoint =
            CombatMetaDataPoint.SplashStart;
        private CombatMetaDataTransformMode _selectedTransformMode =
            CombatMetaDataTransformMode.Translate;
        private ParsedMetadataAttribute? _appliedSelectedPreviewAttribute;
        private bool? _reportedSelectedMetaDataIsActive;
        private bool? _animationMetaPreviewVisibilityOverride;
        private readonly HashSet<ParsedMetadataAttribute>
            _entireAnimationPreviewSources = new(
                ReferenceEqualityComparer.Instance);
        private readonly HashSet<ParsedMetadataAttribute>
            _threeDimensionalEditingSources = new(
                ReferenceEqualityComparer.Instance);
        private List<MetaDataBuildDiagnostic> _metaDataDiagnostics = [];
        private MetaDataInspectionIndex _metaDataInspectionIndex =
            Inspection.MetaDataInspectionIndex.Create([], [], [], 0);

        [ObservableProperty] MetaDataEditorViewModel _persistentMetaEditor = null!;
        [ObservableProperty] MetaDataEditorViewModel _metaEditor = null!;
        [ObservableProperty] int _selectedTabControllerIndex = 0;
        [ObservableProperty] bool _showImpactPositions = true;
        [ObservableProperty] bool _showTargetPositions = true;
        [ObservableProperty] bool _showFirePositions = true;
        [ObservableProperty] bool _showSplashAttacks = true;
        public bool IsCombatMetaData3dEditingEnabled
        {
            get => SelectedPreviewAttribute is ParsedMetadataAttribute source &&
                _threeDimensionalEditingSources.Contains(source);
            set
            {
                if (SelectedPreviewAttribute is not ParsedMetadataAttribute source ||
                    value && !CombatMetaDataEditSession.CanEdit(source))
                {
                    return;
                }

                var changed = value
                    ? _threeDimensionalEditingSources.Add(source)
                    : _threeDimensionalEditingSources.Remove(source);
                if (!changed)
                    return;

                OnPropertyChanged();
                OnPropertyChanged(nameof(EnableAllAnimationMeta3D));
                UpdateCombatGizmoEnabled();
                OnPropertyChanged(nameof(CanUndoCombatMetaData));
                OnPropertyChanged(nameof(CanRedoCombatMetaData));
            }
        }
        public bool IsAnimationMetaTabSelected =>
            SelectedTabControllerIndex == 1;
        public bool CanToggleAnimationMetaPreviewVisibility =>
            GetAnimationMetaPreviews().Any();
        public bool ShowAllAnimationMetaPreviews
        {
            get
            {
                var previews = GetAnimationMetaPreviews().ToList();
                return previews.Count != 0 && previews.All(preview =>
                    preview.IsEnabled);
            }
            set
            {
                if (!CanToggleAnimationMetaPreviewVisibility)
                {
                    return;
                }

                _animationMetaPreviewVisibilityOverride = value;
                ApplyCombatPreviewVisibility();
            }
        }
        public bool CanToggleAnimationMetaDisplayTime =>
            GetAnimationTimedMetaPreviews().Any();
        public bool ShowAllAnimationMetaForEntireAnimation
        {
            get
            {
                var previews = GetAnimationTimedMetaPreviews().ToList();
                return previews.Count != 0 && previews.All(preview =>
                    preview.ShowForEntireAnimation);
            }
            set
            {
                var previews = GetAnimationTimedMetaPreviews().ToList();
                if (previews.Count == 0)
                {
                    return;
                }

                foreach (var preview in previews)
                {
                    if (value)
                        _entireAnimationPreviewSources.Add(preview.Source);
                    else
                        _entireAnimationPreviewSources.Remove(preview.Source);
                    preview.ShowForEntireAnimation = value;
                }

                OnPropertyChanged();
                OnPropertyChanged(
                    nameof(ShowCombatMetaDataForEntireAnimation));
                OnPropertyChanged(
                    nameof(ShowCombatMetaDataDuringActiveTime));
            }
        }
        public bool CanToggleAnimationMeta3D =>
            GetAnimationMetaSources().Any(
                CombatMetaDataEditSession.CanEdit);
        public bool EnableAllAnimationMeta3D
        {
            get
            {
                var sources = GetAnimationMetaSources()
                    .Where(CombatMetaDataEditSession.CanEdit)
                    .ToList();
                return sources.Count != 0 && sources.All(source =>
                    _threeDimensionalEditingSources.Contains(source));
            }
            set
            {
                var sources = GetAnimationMetaSources()
                    .Where(CombatMetaDataEditSession.CanEdit)
                    .ToList();
                if (sources.Count == 0)
                    return;

                foreach (var source in sources)
                {
                    if (value)
                        _threeDimensionalEditingSources.Add(source);
                    else
                        _threeDimensionalEditingSources.Remove(source);
                }

                OnPropertyChanged();
                OnPropertyChanged(
                    nameof(IsCombatMetaData3dEditingEnabled));
                UpdateCombatGizmoEnabled();
                OnPropertyChanged(nameof(CanUndoCombatMetaData));
                OnPropertyChanged(nameof(CanRedoCombatMetaData));
            }
        }
        public bool ShowCombatMetaDataForEntireAnimation
        {
            get => SelectedPreviewAttribute is ParsedMetadataAttribute source &&
                _entireAnimationPreviewSources.Contains(source);
            set
            {
                if (SelectedPreviewAttribute is not ParsedMetadataAttribute source ||
                    value == _entireAnimationPreviewSources.Contains(source))
                {
                    return;
                }

                if (value)
                    _entireAnimationPreviewSources.Add(source);
                else
                    _entireAnimationPreviewSources.Remove(source);

                if (GetSelectedPreview() is ITimedMetaDataPreview preview)
                    preview.ShowForEntireAnimation = value;

                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowCombatMetaDataDuringActiveTime));
                OnPropertyChanged(
                    nameof(ShowAllAnimationMetaForEntireAnimation));
            }
        }
        public override Type EditorViewModelType => typeof(EditorView);
        public bool ShowCombatMetaDataDuringActiveTime
        {
            get => ShowCombatMetaDataForEntireAnimation == false;
            set
            {
                if (value)
                    ShowCombatMetaDataForEntireAnimation = false;
            }
        }
        public string AnimationMetaFilePath =>
            _asset.FragAndSlotSelection.MetaDataName ?? "";
        public string PersistentMetaFilePath =>
            _asset.FragAndSlotSelection.MetaDataPersistName ?? "";
        public bool HasAnimationMetaFile => MetaEditor.CurrentFile != null;
        public bool HasPersistentMetaFile =>
            PersistentMetaEditor.CurrentFile != null;
        public bool CanCreateAnimationMetaFile =>
            HasAnimationMetaFile == false &&
            string.IsNullOrWhiteSpace(AnimationMetaFilePath) == false;
        public bool CanCreatePersistentMetaFile =>
            HasPersistentMetaFile == false &&
            string.IsNullOrWhiteSpace(PersistentMetaFilePath) == false;
        public bool IsAnimationMetaReferenceMissing =>
            HasAnimationMetaFile == false &&
            string.IsNullOrWhiteSpace(AnimationMetaFilePath);
        public bool IsPersistentMetaReferenceMissing =>
            HasPersistentMetaFile == false &&
            string.IsNullOrWhiteSpace(PersistentMetaFilePath);
        public ParsedMetadataAttribute? SelectedPreviewAttribute =>
            SelectedTabControllerIndex == 0
                ? PersistentMetaEditor.SelectedAttribute
                : MetaEditor.SelectedAttribute;
        public IReadOnlyList<MetaDataBuildDiagnostic> MetaDataDiagnostics =>
            _metaDataDiagnostics;
        public MetaDataInspectionIndex MetaDataInspectionIndex =>
            _metaDataInspectionIndex;
        public MetaDataTimelineViewModel MetaDataTimeline { get; }
        public bool CanFocusSelectedMetaData =>
            GetSelectedSpatialPreview() != null;
        public bool CanEditSelectedCombatMetaData =>
            _combatEditSession.CurrentPosition != null;
        public bool CanEditSelectedMetaData3D =>
            CanEditSelectedCombatMetaData;
        public bool CanRotateSelectedMetaData3D =>
            _combatEditSession.CanRotate;
        public bool CanConfigureSelectedMetaDataDisplayTime =>
            HasSelectedMetaDataTimeRange &&
            GetSelectedPreview() is ITimedMetaDataPreview;
        public bool IsSplashMetaDataSelected =>
            SelectedPreviewAttribute is SplashAttack_v3 or SplashAttack_v10;
        public bool IsImpactMetaDataSelected =>
            SelectedPreviewAttribute is ImpactPosition_v2 or ImpactPosition_v10;
        public bool IsTargetMetaDataSelected =>
            SelectedPreviewAttribute is TargetPos_0 or TargetPos_10;
        public bool IsFireMetaDataSelected =>
            SelectedPreviewAttribute is FirePos_v0 or FirePos_v2 or FirePos_v10;
        public bool IsEffectMetaDataSelected =>
            SelectedPreviewAttribute is IEffectMeta;
        public bool HasSelectedSceneMarkerSettings =>
            CanFocusSelectedMetaData;
        public bool HasSelectedMetaDataTimeRange =>
            TryGetSelectedMetaDataTimeRange(out _);
        public float SelectedMetaDataStartTimeSeconds =>
            TryGetSelectedMetaDataTimeRange(out var timeRange)
                ? timeRange.StartTime
                : 0;
        public float SelectedMetaDataEndTimeSeconds =>
            TryGetSelectedMetaDataTimeRange(out var timeRange)
                ? timeRange.EndTime
                : 0;
        public bool SelectedMetaDataIsActive =>
            TryGetSelectedMetaDataTimeRange(out var timeRange) &&
            timeRange.Contains((float)_asset.Data.Player.GetTimeUs() /
                1_000_000);
        public bool SelectedMetaDataUsesZeroRangeConvention =>
            TryGetSelectedMetaDataTimeRange(out var timeRange) &&
            timeRange.IsWholeAnimationRange;
        public string SelectedMetaDataZeroRangeHint =>
            TryGetSelectedMetaDataTimeRange(out var timeRange) &&
            timeRange.IsZeroRange
                ? LocalizationManager.Instance.Get(
                    timeRange.IsWholeAnimationRange
                        ? "SuperView.Time.ZeroRangeFullPreview"
                        : "SuperView.Time.ZeroRangeInstantPreview")
                : "";
        public string SelectedMetaDataStartToolTip
        {
            get
            {
                var jumpTip = LocalizationManager.Instance.Get(
                    "SuperView.Time.JumpStartTip");
                var zeroRangeHint = SelectedMetaDataZeroRangeHint;
                return string.IsNullOrEmpty(zeroRangeHint)
                    ? jumpTip
                    : $"{jumpTip}\n{zeroRangeHint}";
            }
        }
        public string SelectedMetaDataTimeState =>
            LocalizationManager.Instance.Get(
                TryGetSelectedMetaDataTimeRange(out var timeRange) &&
                    timeRange.IsZeroRange
                        ? timeRange.IsWholeAnimationRange
                            ? "SuperView.Time.ZeroRangeFullPreview"
                            : "SuperView.Time.ZeroRangeInstantPreview"
                        : SelectedMetaDataIsActive
                            ? "SuperView.Time.Active"
                            : "SuperView.Time.Inactive");
        public bool CanUndoCombatMetaData =>
            IsCombatMetaData3dEditingEnabled && _combatEditSession.CanUndo;
        public bool CanRedoCombatMetaData =>
            IsCombatMetaData3dEditingEnabled && _combatEditSession.CanRedo;
        public bool EditSplashStart
        {
            get => _selectedSplashPoint == CombatMetaDataPoint.SplashStart;
            set
            {
                if (value)
                    SetSplashPoint(CombatMetaDataPoint.SplashStart);
            }
        }
        public bool EditSplashEnd
        {
            get => _selectedSplashPoint == CombatMetaDataPoint.SplashEnd;
            set
            {
                if (value)
                    SetSplashPoint(CombatMetaDataPoint.SplashEnd);
            }
        }
        public bool EditEffectPosition
        {
            get => _selectedTransformMode ==
                CombatMetaDataTransformMode.Translate;
            set
            {
                if (value)
                    Set3DTransformMode(CombatMetaDataTransformMode.Translate);
            }
        }
        public bool EditEffectOrientation
        {
            get => _selectedTransformMode ==
                CombatMetaDataTransformMode.Rotate;
            set
            {
                if (value)
                    Set3DTransformMode(CombatMetaDataTransformMode.Rotate);
            }
        }
        public bool HasUnsavedChanges
        {
            get
            {
                return PersistentMetaEditor.HasUnsavedChanges ||
                    MetaEditor.HasUnsavedChanges ||
                    _combatEditSession.HasUnsavedChanges;
            }
            set
            {
                PersistentMetaEditor.HasUnsavedChanges = value;
                MetaEditor.HasUnsavedChanges = value;
                if (value == false)
                {
                    _combatEditSession.MarkSaved(PersistentMetaEditor);
                    _combatEditSession.MarkSaved(MetaEditor);
                }
            }
        }


        public SuperViewViewModel(
            IPackFileService packFileService,
            IEventHub eventHub,
            IUiCommandFactory uiCommandFactory,
            SceneObjectEditor sceneObjectBuilder,
            IEditorHostParameters editorHostParameters,
            MetaDataFileParser metaDataFileParser,
            IMetaDataBuilder metaDataFactory,
            CombatMetaDataEditSession combatEditSession,
            CombatMetaDataGizmoComponent combatGizmo,
            MetaDataMarkerPickerComponent markerPicker)
            : base(editorHostParameters)
        {
            EditorContentVerticalScrollBarVisibility =
                ScrollBarVisibility.Disabled;
            DisplayName = LocalizationManager.Instance.Get("DisplayName.SuperView");
            _packFileService = packFileService;
            _eventHub = eventHub;
            _uiCommandFactory = uiCommandFactory;
            _sceneObjectBuilder = sceneObjectBuilder;
            _metaDataFileParser = metaDataFileParser;
            _metaDataFactory = metaDataFactory;
            _combatEditSession = combatEditSession;
            _combatGizmo = combatGizmo;
            _markerPicker = markerPicker;
            MetaDataTimeline = new MetaDataTimelineViewModel(
                SelectMetaDataInspectionItem);
            Player.TimelineAnnotationContent = MetaDataTimeline;
            Player.SelectedAnimationMaxTime.PropertyChanged +=
                OnSelectedAnimationMaxTimeChanged;
            GameWorld!.AddComponent(combatGizmo);
            markerPicker.Connect(
                GetMetaDataMarkerCandidates,
                SelectMetaDataMarker,
                FocusMetaDataMarker,
                ClearMetaDataMarkerSelection);
            GameWorld.AddComponent(markerPicker);
            _combatEditSession.ValueChanged += OnCombatMetaDataValueChanged;
            _combatEditSession.HistoryChanged += OnCombatHistoryChanged;
            Initialize();
            eventHub.Register<ScopedFileSavedEvent>(this, OnFileSaved);
            eventHub.Register<SceneObjectUpdateEvent>(this, OnSceneObjectUpdated);
            eventHub.Register<MetaDataAttributeChangedEvent>(this, OnMetaDataAttributeChanged);
            eventHub.Register<SelecteMetaDataAttributeChangedEvent>(this, OnSelectedMetaDataAttributeChanged);
        }

        private void OnSelectedMetaDataAttributeChanged(SelecteMetaDataAttributeChangedEvent @event)
        {
            Set3DTransformMode(CombatMetaDataTransformMode.Translate);
            ApplySelectedMetaDataPreview();
            UpdateCombatEditTarget();
            NotifySelectedMetaDataContextChanged();
            UpdateMetaDataTimelineSelection();
        }
        partial void OnSelectedTabControllerIndexChanged(int value)
        {
            _markerPicker.ClearHover();
            Set3DTransformMode(CombatMetaDataTransformMode.Translate);
            ApplySelectedMetaDataPreview();
            UpdateCombatEditTarget();
            NotifySelectedMetaDataContextChanged();
            OnPropertyChanged(nameof(IsAnimationMetaTabSelected));
            UpdateMetaDataTimelineSelection();
        }
        partial void OnShowImpactPositionsChanged(bool value) =>
            OnCombatCategoryVisibilityChanged();
        partial void OnShowTargetPositionsChanged(bool value) =>
            OnCombatCategoryVisibilityChanged();
        partial void OnShowFirePositionsChanged(bool value) =>
            OnCombatCategoryVisibilityChanged();
        partial void OnShowSplashAttacksChanged(bool value) =>
            OnCombatCategoryVisibilityChanged();
        void OnMetaDataAttributeChanged(MetaDataAttributeChangedEvent @event)
        {
            if (@event.Source is not ParsedMetadataAttribute attribute ||
                !RefreshMetaDataPreview(attribute))
            {
                RecreateMetaDataInformation();
            }
            _combatGizmo.RefreshTarget();
            NotifySelectedMetaDataContextChanged();
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }
        void OnMetaDataChanged(SceneObject sceneObject) => RecreateMetaDataInformation();

        private void OnFileSaved(ScopedFileSavedEvent evnt)
        {
            var newFile = _packFileService.FindFile(evnt.NewPath);
            if (evnt.FileOwner == PersistentMetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, _asset.Data.MetaData, newFile);
            else if (evnt.FileOwner == MetaEditor)
                _sceneObjectBuilder.SetMetaFile(_asset.Data, newFile, _asset.Data.PersistMetaData);
            else
                throw new Exception($"Unable to determine file owner when reciving a file save event in SuperView. Owner:{evnt.FileOwner}, File:{evnt.NewPath}");

            _combatEditSession.MarkSaved(evnt.FileOwner);
        }

        void Initialize()
        {
            PersistentMetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            MetaEditor = new MetaDataEditorViewModel(_uiCommandFactory, _metaDataFileParser, _eventHub);
            PersistentMetaEditor.PropertyChanged += OnMetaEditorPropertyChanged;
            MetaEditor.PropertyChanged += OnMetaEditorPropertyChanged;
            PersistentMetaEditor.StructureChanged += OnMetaEditorStructureChanged;
            MetaEditor.StructureChanged += OnMetaEditorStructureChanged;
            PersistentMetaEditor.FieldValidityChanged +=
                OnMetaEditorFieldValidityChanged;
            MetaEditor.FieldValidityChanged += OnMetaEditorFieldValidityChanged;

            var assetViewModel = _sceneObjectViewModelBuilder.CreateAsset("SuperViewRoot", true, "Root", Color.Black, null);
            SceneObjects.Add(assetViewModel);

            assetViewModel.Data.MetaDataChanged += OnMetaDataChanged;

            _asset = assetViewModel;
            _asset.Data.Player.OnFrameChanged += OnAnimationFrameChanged;
            _asset.FragAndSlotSelection.PropertyChanged +=
                OnFragmentSelectionPropertyChanged;
            OnSceneObjectUpdated(new SceneObjectUpdateEvent(_asset.Data, false, false, false, true));
        }

        private void OnFragmentSelectionPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BinAnimationViewModel.MetaDataName) ||
                e.PropertyName == nameof(BinAnimationViewModel.MetaDataPersistName))
            {
                NotifyMetaFileStateChanged();
            }
        }

        private void OnMetaEditorPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MetaDataEditorViewModel.HasUnsavedChanges))
                OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        private void OnMetaEditorStructureChanged(
            MetaDataEditorViewModel editor)
        {
            _combatEditSession.ResetDocument(editor);
            RecreateMetaDataInformation();
            UpdateCombatEditTarget();
        }

        private void OnMetaEditorFieldValidityChanged(
            MetaDataEditorViewModel editor) =>
            RebuildMetaDataInspectionIndex();

        private void OnSelectedAnimationMaxTimeChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Player.SelectedAnimationMaxTime.Value))
                RebuildMetaDataInspectionIndex();
        }



        void RecreateMetaDataInformation()
        {
            _markerPicker.ClearInteractionState();
            foreach (var item in SceneObjects)
            {
                foreach (var t in item.Data.MetaDataItems)
                    t.CleanUp();

                item.Data.MetaDataItems.Clear();
                item.Data.Player.AnimationRules.Clear();
            }

            var persist = PersistentMetaEditor.ParsedFile;
            var meta = MetaEditor.ParsedFile;

            var currentSources = (persist?.Attributes ?? [])
                .Concat(meta?.Attributes ?? [])
                .OfType<ParsedMetadataAttribute>()
                .ToHashSet(ReferenceEqualityComparer.Instance);
            _entireAnimationPreviewSources.RemoveWhere(source =>
                !currentSources.Contains(source));
            _threeDimensionalEditingSources.RemoveWhere(source =>
                !currentSources.Contains(source));

            var buildResult = _metaDataFactory.Build(
                persist,
                meta,
                SelectedPreviewAttribute,
                _asset.Data.MainNode,
                _asset.Data,
                _asset.Data.Player,
                _asset.FragAndSlotSelection.FragmentList.SelectedItem);
            _asset.Data.MetaDataItems = buildResult.Instances.ToList();
            _asset.Data.Player.AnimationRules.AddRange(
                buildResult.AnimationRules);
            _metaDataDiagnostics = buildResult.Diagnostics.ToList();
            OnPropertyChanged(nameof(MetaDataDiagnostics));
            RebuildMetaDataInspectionIndex();
            _appliedSelectedPreviewAttribute = SelectedPreviewAttribute;
            ApplyCombatPreviewVisibility();
            OnPropertyChanged(nameof(CanFocusSelectedMetaData));
            OnPropertyChanged(nameof(CanConfigureSelectedMetaDataDisplayTime));
            NotifyAnimationMetaBatchControlsChanged();
            _asset.Data.Player.Refresh();

            // Re-disable selection after metadata creates new nodes
            DisableSelectionOnAllNodes();
        }

        private void ApplySelectedMetaDataPreview()
        {
            var selectedAttribute = SelectedPreviewAttribute;
            if (ReferenceEquals(
                selectedAttribute,
                _appliedSelectedPreviewAttribute))
            {
                return;
            }

            var previews = _asset.Data.MetaDataItems
                .OfType<IMetaDataPreview>()
                .ToList();
            foreach (var preview in previews)
            {
                preview.IsSelected = ReferenceEquals(
                    preview.Source,
                    selectedAttribute);
            }

            _appliedSelectedPreviewAttribute = selectedAttribute;
            OnPropertyChanged(nameof(CanFocusSelectedMetaData));
            OnPropertyChanged(nameof(CanConfigureSelectedMetaDataDisplayTime));
        }

        private bool RefreshMetaDataPreview(
            ParsedMetadataAttribute attribute)
        {
            _markerPicker.ClearInteractionState();
            if (!TryGetDocumentOwner(attribute, out var owner))
                return false;

            var buildResult = _metaDataFactory.BuildPreview(
                attribute,
                owner,
                ReferenceEquals(attribute, SelectedPreviewAttribute),
                _asset.Data.MainNode,
                _asset.Data,
                _asset.Data.Player);
            if (!buildResult.IsSupported)
                return false;

            var replacement = buildResult.Instance;

            var currentIndex = _asset.Data.MetaDataItems.FindIndex(item =>
                item is IMetaDataPreview preview &&
                ReferenceEquals(preview.Source, attribute));
            if (currentIndex >= 0)
            {
                _asset.Data.MetaDataItems[currentIndex].CleanUp();
                _asset.Data.MetaDataItems.RemoveAt(currentIndex);
            }

            if (replacement != null)
            {
                if (currentIndex < 0)
                    _asset.Data.MetaDataItems.Add(replacement);
                else
                    _asset.Data.MetaDataItems.Insert(
                        currentIndex,
                        replacement);
            }

            _metaDataDiagnostics.RemoveAll(diagnostic =>
                ReferenceEquals(diagnostic.Source, attribute));
            _metaDataDiagnostics.AddRange(buildResult.Diagnostics);
            OnPropertyChanged(nameof(MetaDataDiagnostics));
            RebuildMetaDataInspectionIndex();

            ApplyCombatPreviewVisibility();
            OnPropertyChanged(nameof(CanFocusSelectedMetaData));
            OnPropertyChanged(nameof(CanConfigureSelectedMetaDataDisplayTime));
            return true;
        }

        private void RebuildMetaDataInspectionIndex()
        {
            var sources = GetMetaDataInspectionSources(
                    PersistentMetaEditor,
                    MetaDataDocumentOwner.Persistent)
                .Concat(GetMetaDataInspectionSources(
                    MetaEditor,
                    MetaDataDocumentOwner.Animation));
            var clipDurationSeconds =
                Player.SelectedAnimationMaxTime.Value;
            _metaDataInspectionIndex = Inspection.MetaDataInspectionIndex.Create(
                sources,
                _asset.Data.MetaDataItems,
                _metaDataDiagnostics,
                clipDurationSeconds);
            OnPropertyChanged(nameof(MetaDataInspectionIndex));
            MetaDataTimeline.Update(
                _metaDataInspectionIndex,
                clipDurationSeconds);
            UpdateMetaDataTimelineSelection();
        }

        private static IEnumerable<MetaDataInspectionSource>
            GetMetaDataInspectionSources(
                MetaDataEditorViewModel editor,
                MetaDataDocumentOwner owner)
        {
            if (editor.ParsedFile == null)
                yield break;

            foreach (var source in editor.ParsedFile.Attributes)
            {
                var entry = editor.Tags.FirstOrDefault(tag =>
                    ReferenceEquals(tag._input, source));
                var areFieldsValid = entry?.IsDecodedCorrectly == true &&
                    entry.Variables.All(variable => variable.IsValid);
                yield return new MetaDataInspectionSource(
                    source,
                    owner,
                    areFieldsValid);
            }
        }

        private void SelectMetaDataInspectionItem(
            MetaDataInspectionItem item)
        {
            if (item.AuthoredTimeStatus !=
                    MetaDataAuthoredTimeStatus.Valid ||
                item.AuthoredTimeRange is not MetaDataTimeRange timeRange)
            {
                return;
            }

            if (!SelectMetaDataItem(item))
                return;

            Player.PlaybackPositionSeconds = timeRange.StartTime;
            NotifySelectedMetaDataContextChanged();
        }

        private bool SelectMetaDataItem(MetaDataInspectionItem item)
        {
            var editor = item.Owner == MetaDataDocumentOwner.Persistent
                ? PersistentMetaEditor
                : MetaEditor;
            var tag = editor.Tags.FirstOrDefault(candidate =>
                ReferenceEquals(candidate._input, item.Source));
            if (tag == null)
                return false;

            SelectedTabControllerIndex = item.Owner ==
                MetaDataDocumentOwner.Persistent ? 0 : 1;
            editor.SelectedTag = tag;
            return true;
        }

        internal void SelectMetaDataMarker(
            MetaDataInspectionItem item,
            MetaDataMarkerPoint point = MetaDataMarkerPoint.Default)
        {
            if (!SelectMetaDataItem(item))
                return;

            if (point == MetaDataMarkerPoint.SplashStart)
                SetSplashPoint(CombatMetaDataPoint.SplashStart);
            else if (point == MetaDataMarkerPoint.SplashEnd)
                SetSplashPoint(CombatMetaDataPoint.SplashEnd);

            NotifySelectedMetaDataContextChanged();
        }

        internal void ClearMetaDataMarkerSelection()
        {
            var editor = SelectedTabControllerIndex == 0
                ? PersistentMetaEditor
                : MetaEditor;
            editor.SelectedTag = null;
        }

        private void FocusMetaDataMarker(MetaDataInspectionItem item)
        {
            if (!ReferenceEquals(SelectedPreviewAttribute, item.Source) ||
                item.FocusPosition == null)
            {
                return;
            }

            FocusSelectedMetaDataAction();
        }

        internal IReadOnlyList<MetaDataMarkerPickCandidate>
            GetMetaDataMarkerCandidates()
        {
            if (_asset == null)
                return [];

            var previews = _asset.Data.MetaDataItems
                .OfType<IMetaDataMarkerPreview>()
                .ToArray();
            var candidates = new List<MetaDataMarkerPickCandidate>();
            for (var index = 0;
                index < _metaDataInspectionIndex.Items.Count;
                index++)
            {
                var item = _metaDataInspectionIndex.Items[index];
                var preview = previews.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.Source, item.Source));
                if (preview?.IsHitTestVisible == true)
                {
                    candidates.Add(new MetaDataMarkerPickCandidate(
                        item,
                        preview,
                        index));
                }
            }

            return candidates;
        }

        private void UpdateMetaDataTimelineSelection()
        {
            var owner = SelectedTabControllerIndex == 0
                ? MetaDataDocumentOwner.Persistent
                : MetaDataDocumentOwner.Animation;
            MetaDataTimeline.UpdateSelection(owner, SelectedPreviewAttribute);
        }

        private bool TryGetDocumentOwner(
            ParsedMetadataAttribute attribute,
            out MetaDataDocumentOwner owner)
        {
            if (PersistentMetaEditor.ParsedFile?.Attributes.Any(item =>
                    ReferenceEquals(item, attribute)) == true)
            {
                owner = MetaDataDocumentOwner.Persistent;
                return true;
            }

            if (MetaEditor.ParsedFile?.Attributes.Any(item =>
                    ReferenceEquals(item, attribute)) == true)
            {
                owner = MetaDataDocumentOwner.Animation;
                return true;
            }

            owner = default;
            return false;
        }

        private void ApplyCombatPreviewVisibility()
        {
            foreach (var preview in _asset.Data.MetaDataItems
                .OfType<ITimedMetaDataPreview>())
            {
                preview.ShowForEntireAnimation =
                    _entireAnimationPreviewSources.Contains(preview.Source);
            }

            var animationSources = GetAnimationMetaSources().ToHashSet(
                ReferenceEqualityComparer.Instance);
            foreach (var preview in _asset.Data.MetaDataItems
                .OfType<IMetaDataPreview>())
            {
                var enabled = preview is ICombatMetaDataPreview combatPreview
                    ? combatPreview.Category switch
                    {
                        CombatMetaDataPreviewCategory.Impact =>
                            ShowImpactPositions,
                        CombatMetaDataPreviewCategory.Target =>
                            ShowTargetPositions,
                        CombatMetaDataPreviewCategory.Fire =>
                            ShowFirePositions,
                        CombatMetaDataPreviewCategory.Splash =>
                            ShowSplashAttacks,
                        _ => true
                    }
                    : true;
                if (animationSources.Contains(preview.Source) &&
                    _animationMetaPreviewVisibilityOverride.HasValue)
                {
                    enabled = _animationMetaPreviewVisibilityOverride.Value;
                }

                preview.IsEnabled = enabled;
            }

            NotifyAnimationMetaBatchControlsChanged();
        }

        private void OnCombatCategoryVisibilityChanged()
        {
            _animationMetaPreviewVisibilityOverride = null;
            ApplyCombatPreviewVisibility();
        }

        private IEnumerable<ParsedMetadataAttribute>
            GetAnimationMetaSources() =>
            MetaEditor.ParsedFile?.Attributes
                .OfType<ParsedMetadataAttribute>() ?? [];

        private IEnumerable<IMetaDataPreview> GetAnimationMetaPreviews()
        {
            var sources = GetAnimationMetaSources().ToHashSet(
                ReferenceEqualityComparer.Instance);
            return _asset.Data.MetaDataItems
                .OfType<IMetaDataPreview>()
                .Where(preview => sources.Contains(preview.Source));
        }

        private IEnumerable<ITimedMetaDataPreview>
            GetAnimationTimedMetaPreviews() =>
            GetAnimationMetaPreviews()
                .OfType<ITimedMetaDataPreview>()
                .Where(preview => MetaDataTimeRange.TryCreate(
                    preview.Source,
                    out _));

        private void NotifyAnimationMetaBatchControlsChanged()
        {
            OnPropertyChanged(
                nameof(CanToggleAnimationMetaPreviewVisibility));
            OnPropertyChanged(nameof(ShowAllAnimationMetaPreviews));
            OnPropertyChanged(nameof(CanToggleAnimationMetaDisplayTime));
            OnPropertyChanged(
                nameof(ShowAllAnimationMetaForEntireAnimation));
            OnPropertyChanged(nameof(CanToggleAnimationMeta3D));
            OnPropertyChanged(nameof(EnableAllAnimationMeta3D));
        }

        private ISpatialMetaDataPreview? GetSelectedSpatialPreview()
        {
            var selectedAttribute = SelectedPreviewAttribute;
            if (selectedAttribute == null)
                return null;

            return _asset.Data.MetaDataItems
                .OfType<ISpatialMetaDataPreview>()
                .FirstOrDefault(preview =>
                    ReferenceEquals(preview.Source, selectedAttribute));
        }

        private IMetaDataPreview? GetSelectedPreview()
        {
            var selectedAttribute = SelectedPreviewAttribute;
            if (selectedAttribute == null)
                return null;

            return _asset.Data.MetaDataItems
                .OfType<IMetaDataPreview>()
                .FirstOrDefault(preview =>
                    ReferenceEquals(preview.Source, selectedAttribute));
        }

        [RelayCommand]
        public void FocusSelectedMetaDataAction()
        {
            var selectedPreview = GetSelectedSpatialPreview();
            if (selectedPreview != null)
                FocusService.LookAt(selectedPreview.FocusPosition);
        }

        [RelayCommand]
        public void JumpToSelectedMetaDataStartAction() =>
            JumpToSelectedMetaDataTime(useEndTime: false);

        [RelayCommand]
        public void JumpToSelectedMetaDataEndAction() =>
            JumpToSelectedMetaDataTime(useEndTime: true);

        private void JumpToSelectedMetaDataTime(bool useEndTime)
        {
            if (!TryGetSelectedMetaDataTimeRange(out var timeRange))
                return;

            _asset.Data.Player.Pause();
            _asset.Data.Player.SeekToTimeSeconds(
                useEndTime ? timeRange.EndTime : timeRange.StartTime);
            NotifySelectedMetaDataContextChanged();
        }

        private bool TryGetSelectedMetaDataTimeRange(
            out MetaDataTimeRange timeRange)
        {
            var selectedAttribute = SelectedPreviewAttribute;
            if (selectedAttribute != null)
                return MetaDataTimeRange.TryCreate(
                    selectedAttribute,
                    out timeRange);

            timeRange = default;
            return false;
        }

        private void OnAnimationFrameChanged(int _) =>
            NotifySelectedMetaDataPlaybackStateChanged();

        private void NotifySelectedMetaDataContextChanged()
        {
            OnPropertyChanged(nameof(CanFocusSelectedMetaData));
            OnPropertyChanged(nameof(HasSelectedSceneMarkerSettings));
            OnPropertyChanged(nameof(IsImpactMetaDataSelected));
            OnPropertyChanged(nameof(IsTargetMetaDataSelected));
            OnPropertyChanged(nameof(IsFireMetaDataSelected));
            OnPropertyChanged(nameof(IsSplashMetaDataSelected));
            OnPropertyChanged(nameof(IsEffectMetaDataSelected));
            OnPropertyChanged(nameof(CanEditSelectedMetaData3D));
            OnPropertyChanged(nameof(CanRotateSelectedMetaData3D));
            OnPropertyChanged(
                nameof(IsCombatMetaData3dEditingEnabled));
            OnPropertyChanged(nameof(EnableAllAnimationMeta3D));
            OnPropertyChanged(nameof(CanConfigureSelectedMetaDataDisplayTime));
            OnPropertyChanged(nameof(EditEffectPosition));
            OnPropertyChanged(nameof(EditEffectOrientation));
            OnPropertyChanged(nameof(ShowCombatMetaDataForEntireAnimation));
            OnPropertyChanged(nameof(ShowCombatMetaDataDuringActiveTime));
            OnPropertyChanged(nameof(HasSelectedMetaDataTimeRange));
            OnPropertyChanged(nameof(SelectedMetaDataStartTimeSeconds));
            OnPropertyChanged(nameof(SelectedMetaDataEndTimeSeconds));
            OnPropertyChanged(
                nameof(SelectedMetaDataUsesZeroRangeConvention));
            OnPropertyChanged(nameof(SelectedMetaDataZeroRangeHint));
            OnPropertyChanged(nameof(SelectedMetaDataStartToolTip));
            _reportedSelectedMetaDataIsActive = null;
            _asset.Data.SelectedBoneIndex(
                GetSelectedSpatialPreview()?.HighlightedBoneIndex);
            NotifySelectedMetaDataPlaybackStateChanged();
        }

        private void NotifySelectedMetaDataPlaybackStateChanged()
        {
            var isActive = SelectedMetaDataIsActive;
            if (_reportedSelectedMetaDataIsActive == isActive)
                return;

            _reportedSelectedMetaDataIsActive = isActive;
            OnPropertyChanged(nameof(SelectedMetaDataIsActive));
            OnPropertyChanged(nameof(SelectedMetaDataTimeState));
        }

        [RelayCommand]
        public void UndoCombatMetaDataAction() => _combatEditSession.Undo();

        [RelayCommand]
        public void RedoCombatMetaDataAction() => _combatEditSession.Redo();

        private void SetSplashPoint(CombatMetaDataPoint point)
        {
            if (_selectedSplashPoint == point)
                return;

            _selectedSplashPoint = point;
            OnPropertyChanged(nameof(EditSplashStart));
            OnPropertyChanged(nameof(EditSplashEnd));
            UpdateCombatEditTarget();
        }

        private void Set3DTransformMode(CombatMetaDataTransformMode mode)
        {
            if (_selectedTransformMode == mode)
                return;

            _selectedTransformMode = mode;
            _combatGizmo.TransformMode = mode;
            OnPropertyChanged(nameof(EditEffectPosition));
            OnPropertyChanged(nameof(EditEffectOrientation));
        }

        private void UpdateCombatEditTarget()
        {
            var editor = SelectedTabControllerIndex == 0
                ? PersistentMetaEditor
                : MetaEditor;
            var attribute = editor.SelectedAttribute;
            var point = attribute is SplashAttack_v3 or SplashAttack_v10
                ? _selectedSplashPoint
                : CombatMetaDataPoint.Position;
            var spatialPreview = GetSelectedSpatialPreview();
            Func<Matrix>? referenceWorldTransform = spatialPreview == null
                ? null
                : () => spatialPreview.ReferenceWorldTransform;

            if (attribute == null ||
                !_combatEditSession.SetTarget(
                    editor,
                    attribute,
                    point,
                    referenceWorldTransform))
            {
                _combatEditSession.ClearTarget(editor);
            }

            _combatGizmo.RefreshTarget();
            UpdateCombatGizmoEnabled();
            NotifyCombatEditStateChanged();
        }

        private void UpdateCombatGizmoEnabled() =>
            _combatGizmo.IsEnabled =
                IsCombatMetaData3dEditingEnabled &&
                CanEditSelectedMetaData3D;

        private void OnCombatMetaDataValueChanged(
            CombatMetaDataEditChange change)
        {
            if (change.Kind == CombatMetaDataEditChangeKind.Preview)
            {
                var currentTimeSeconds =
                    _asset.Data.Player.GetTimeUs() / 1_000_000f;
                _asset.Data.MetaDataItems
                    .FirstOrDefault(item =>
                        item is IMetaDataPreview preview &&
                        ReferenceEquals(preview.Source, change.Source))
                    ?.Update(currentTimeSeconds);
                RebuildMetaDataInspectionIndex();
                return;
            }

            if (change.Owner is MetaDataEditorViewModel editor)
                editor.RefreshAttributeValues(change.Source);

            if (!RefreshMetaDataPreview(change.Source))
                RecreateMetaDataInformation();
            UpdateCombatEditTarget();
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        private void OnCombatHistoryChanged() =>
            NotifyCombatEditStateChanged();

        private void NotifyCombatEditStateChanged()
        {
            OnPropertyChanged(nameof(CanEditSelectedCombatMetaData));
            OnPropertyChanged(nameof(CanEditSelectedMetaData3D));
            OnPropertyChanged(nameof(CanRotateSelectedMetaData3D));
            OnPropertyChanged(nameof(IsSplashMetaDataSelected));
            OnPropertyChanged(nameof(CanUndoCombatMetaData));
            OnPropertyChanged(nameof(CanRedoCombatMetaData));
            OnPropertyChanged(nameof(HasUnsavedChanges));
        }

        private void OnSceneObjectUpdated(SceneObjectUpdateEvent e)
        {
            _animationMetaPreviewVisibilityOverride = null;
            _combatEditSession.ResetDocument(PersistentMetaEditor);
            _combatEditSession.ResetDocument(MetaEditor);
            PersistentMetaEditor.LoadFile(e.Owner.PersistMetaData);
            MetaEditor.LoadFile(e.Owner.MetaData);

            RecreateMetaDataInformation();
            UpdateCombatEditTarget();
            NotifyMetaFileStateChanged();
            NotifyAnimationMetaBatchControlsChanged();
        }

        public void Load(AnimationToolInput debugDataToLoad)
        {
            _sceneObjectBuilder.SetMesh(_asset.Data, debugDataToLoad.Mesh);

            // Hack :(
            if (debugDataToLoad.AnimationSlot != null)
            {
                var frag = _asset.FragAndSlotSelection.FragmentList.PossibleValues.FirstOrDefault(x => x.FullPath == debugDataToLoad.FragmentName);
                _asset.FragAndSlotSelection.FragmentList.SelectedItem = frag;

                var slot = _asset.FragAndSlotSelection.FragmentSlotList.PossibleValues.First(x => x.SlotName == debugDataToLoad.AnimationSlot.Value);
                _asset.FragAndSlotSelection.FragmentSlotList.SelectedItem = slot;
            }

            // Disable selection on all nodes - SuperView is read-only visualization
            DisableSelectionOnAllNodes();
        }


        public void CreateAnimationMetaFile()
        {
            if (CanCreateAnimationMetaFile)
                MetaEditor.CreateNewFile(AnimationMetaFilePath);

            NotifyMetaFileStateChanged();
        }

        public void CreatePersistentMetaFile()
        {
            if (CanCreatePersistentMetaFile)
                PersistentMetaEditor.CreateNewFile(PersistentMetaFilePath);

            NotifyMetaFileStateChanged();
        }

        private void NotifyMetaFileStateChanged()
        {
            OnPropertyChanged(nameof(AnimationMetaFilePath));
            OnPropertyChanged(nameof(PersistentMetaFilePath));
            OnPropertyChanged(nameof(HasAnimationMetaFile));
            OnPropertyChanged(nameof(HasPersistentMetaFile));
            OnPropertyChanged(nameof(CanCreateAnimationMetaFile));
            OnPropertyChanged(nameof(CanCreatePersistentMetaFile));
            OnPropertyChanged(nameof(IsAnimationMetaReferenceMissing));
            OnPropertyChanged(nameof(IsPersistentMetaReferenceMissing));
        }

        private void DisableSelectionOnAllNodes()
        {
            _asset.Data.MainNode.ForeachNodeRecursive((node) =>
            {
                if (node is ISelectable selectable)
                    selectable.IsSelectable = false;
            });
        }

        public override void Close()
        {
            _markerPicker.Disconnect();
            _asset.FragAndSlotSelection.PropertyChanged -=
                OnFragmentSelectionPropertyChanged;
            _asset.Data.Player.OnFrameChanged -= OnAnimationFrameChanged;
            PersistentMetaEditor.PropertyChanged -= OnMetaEditorPropertyChanged;
            MetaEditor.PropertyChanged -= OnMetaEditorPropertyChanged;
            PersistentMetaEditor.StructureChanged -= OnMetaEditorStructureChanged;
            MetaEditor.StructureChanged -= OnMetaEditorStructureChanged;
            PersistentMetaEditor.FieldValidityChanged -=
                OnMetaEditorFieldValidityChanged;
            MetaEditor.FieldValidityChanged -= OnMetaEditorFieldValidityChanged;
            Player.SelectedAnimationMaxTime.PropertyChanged -=
                OnSelectedAnimationMaxTimeChanged;
            if (ReferenceEquals(
                Player.TimelineAnnotationContent,
                MetaDataTimeline))
            {
                Player.TimelineAnnotationContent = null;
            }
            PersistentMetaEditor.Close();
            MetaEditor.Close();
            _combatEditSession.ValueChanged -= OnCombatMetaDataValueChanged;
            _combatEditSession.HistoryChanged -= OnCombatHistoryChanged;
            _eventHub?.UnRegister(this);
            base.Close();
        }

        public bool Save()
        {
            var res0 = SaveIfLoaded(PersistentMetaEditor);
            var res1 = SaveIfLoaded(MetaEditor);
            var success = res0 && res1;
            if (success)
            {
                _combatEditSession.MarkSaved(PersistentMetaEditor);
                _combatEditSession.MarkSaved(MetaEditor);
            }
            return success;
        }

        private static bool SaveIfLoaded(MetaDataEditorViewModel editor)
        {
            if (editor.CurrentFile == null && editor.ParsedFile == null)
                return true;

            return editor.Save();
        }
    }
}
