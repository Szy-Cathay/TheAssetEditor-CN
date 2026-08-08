using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using Editors.CscEditor.Data;
using Editors.CscEditor.Services;
using Editors.CscEditor.Views;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using GameWorld.Core.WpfWindow;
using GameWorld.Core.WpfWindow.FactionColourSettings;
using Serilog;
using Shared.Core.ErrorHandling;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.ToolCreation;

namespace Editors.CscEditor.ViewModels
{
    public class CscEditorViewModel : NotifyPropertyChangedImpl, IEditorInterface, IFileEditor, ISaveableEditor, ICscUndoRedoEditor, IDisposable
    {
        readonly ILogger _logger = Logging.Create<CscEditorViewModel>();
        readonly IPackFileService _packFileService;
        readonly IFileSaveService _fileSaveService;
        readonly IStandardDialogs _dialogs;
        readonly IEventHub _eventHub;
        readonly CscSceneGraphBuilder _sceneBuilder;
        readonly CscAnimationComponent _animation;
        readonly CscGizmoComponent _gizmo;
        readonly CscPlaybackContext _context;
        readonly ArcBallCamera _camera;
        readonly SelectionManager _selectionManager;
        readonly LocalizationManager _localization;
        readonly CscEditHistory _editHistory;

        public IWpfGame Scene { get; }
        public CscScene? SceneData { get; private set; }

        public string DisplayName { get; set; }
        public PackFile CurrentFile { get; private set; } = null!;

        public bool HasUnsavedChanges
        {
            get => SceneData != null &&
                _editHistory.HasUnsavedChanges;
            set
            {
                if (!value && SceneData != null)
                {
                    _editHistory.MarkSaved();
                    NotifyHistoryState();
                }
            }
        }

        public ObservableCollection<CscElementViewModel> RootElements { get; } = [];

        /// <summary>Single-item wrapper so the tree shows a "Root" node above the top-level
        /// elements (bound to <see cref="RootElements"/> directly, so it stays live).</summary>
        public ObservableCollection<CscSceneRootViewModel> SceneRootItems { get; }

        CscElementViewModel? _selectedElement;
        public CscElementViewModel? SelectedElement
        {
            get => _selectedElement;
            set
            {
                if (value != null && _selectedSceneRoot != null)
                {
                    _selectedSceneRoot = null;
                    NotifyPropertyChanged(nameof(SelectedSceneRoot));
                    NotifyPropertyChanged(nameof(IsSceneRootSelected));
                }
                SetAndNotify(ref _selectedElement, value);
                _context.SelectedElementId = value?.Element.Id ?? -1;
                _gizmo.SetTarget(value?.Element is { IsExternal: true } ? null : value?.Element);
                RebuildCurveSeries();
                NotifyPropertyChanged(nameof(HasSelection));
                NotifyPropertyChanged(nameof(IsElementSelected));
            }
        }

        public bool HasSelection => SelectedElement != null;
        public bool IsElementSelected => SelectedElement != null;

        CscSceneRootViewModel? _selectedSceneRoot;
        /// <summary>The synthetic tree root, selected exclusively of any <see cref="CscElementViewModel"/>
        /// - lets the Details panel show/edit the .csc's own ROOT header fields (scene duration,
        /// focus point, radius, weather path) instead of an element's.</summary>
        public CscSceneRootViewModel? SelectedSceneRoot
        {
            get => _selectedSceneRoot;
            set
            {
                if (value != null)
                    SelectedElement = null; // exclusive with element selection
                SetAndNotify(ref _selectedSceneRoot, value);
                NotifyPropertyChanged(nameof(IsSceneRootSelected));
            }
        }

        public bool IsSceneRootSelected => SelectedSceneRoot != null;

        public bool IsLookingThroughCamera => _context.LookThroughElementId >= 0;

        // ---- Porthole overlay: emulates the in-game 2D framing (ui/skins/default/porthole*.png)
        // shown while looking through a camera, so it's easier to compose a shot that matches how
        // it'll actually be cropped/masked in-game. Back/frame images are loaded once from the
        // loaded pack files; missing files (mod without the UI skin loaded) just leave the overlay
        // empty. Sized/positioned in the view against porthole.png's own 242x258 pixel footprint,
        // with its circular cutout (180x180, centred at 115,120 - i.e. offset 25,30) - user-measured,
        // not derived from any header/manifest data. PortholeLiveFrame (the cropped, alpha-preserving
        // 3D render) is produced by CscAnimationComponent - see its CapturePortholeFrame for why
        // that's a CPU-readback from RenderEngineComponent's pre-composite target rather than a
        // mirror of the live viewport surface, which has no alpha. ----
        public ImageSource? PortholeBackImage { get; }
        public ImageSource? PortholeFrameImage { get; }
        public ImageSource? PortholeLiveFrame => _animation.PortholeLiveFrame;

        string _statusText = "";
        public string StatusText { get => _statusText; set => SetAndNotify(ref _statusText, value); }

        // ---- Timeline ----
        public CscPlaybackContext Playback => _context;

        public double CurrentTime
        {
            get => _context.CurrentTime;
            set
            {
                _context.CurrentTime = (float)value;
                _animation.ApplyFrame();
            }
        }

        public double Duration => _context.Duration;
        public bool IsPlaying => _context.IsPlaying;
        public bool ManifestEditable => SceneData?.ManifestParsed ?? false;
        public bool CanUndo => _editHistory.CanUndo;
        public bool CanRedo => _editHistory.CanRedo;

        /// <summary>True when keyframes must not be added/removed (the file's COMPOSITE_SCENE
        /// manifest could not be parsed, so keyframe counts cannot be restated on save).</summary>
        public bool CurveStructureLocked => !ManifestEditable;

        // ---- Curve editor ----
        public ObservableCollection<CurveSeries> CurveSeriesList { get; } = [];

        CurveSeries? _selectedCurveSeries;
        public CurveSeries? SelectedCurveSeries
        {
            get => _selectedCurveSeries;
            set => SetAndNotify(ref _selectedCurveSeries, value);
        }

        // ---- Commands ----
        public ICommand SaveCommand { get; }
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand GizmoTranslateCommand { get; }
        public ICommand GizmoRotateCommand { get; }
        public ICommand GizmoScaleCommand { get; }
        public ICommand GizmoOffCommand { get; }
        public ICommand FocusSelectedCommand { get; }
        public ICommand LookThroughCameraCommand { get; }
        public ICommand ResetViewCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand DetachSelectedCommand { get; }
        public ICommand AddModelCommand { get; }
        public ICommand AddVariantModelCommand { get; }
        public ICommand AddVfxCommand { get; }
        public ICommand AddSfxCommand { get; }
        public ICommand AddPointLightCommand { get; }
        public ICommand AddSpotLightCommand { get; }
        public ICommand AddCameraCommand { get; }
        public ICommand AddLocatorCommand { get; }
        public ICommand AddCompositeSceneCommand { get; }
        public ICommand AddPrefabCommand { get; }
        public ICommand AddAnimationCommand { get; }
        public ICommand OpenFactionColourSettingsCommand { get; }

        public CscEditorViewModel(
            IPackFileService packFileService,
            IFileSaveService fileSaveService,
            IStandardDialogs dialogs,
            IEventHub eventHub,
            IWpfGame gameWorld,
            IComponentInserter componentInserter,
            View3DCoreComponentSet coreComponents,
            CscSceneGraphBuilder sceneBuilder,
            CscAnimationComponent animationComponent,
            CscGizmoComponent gizmoComponent,
            CscPlaybackContext playbackContext,
            ArcBallCamera camera,
            SelectionManager selectionManager,
            ReferenceObjectSelectionComponent objectSelectionComponent,
            LocalizationManager localization,
            IFactionColourSettingsDialogService factionColourSettingsDialog)
        {
            _packFileService = packFileService;
            _fileSaveService = fileSaveService;
            _dialogs = dialogs;
            _eventHub = eventHub;
            _sceneBuilder = sceneBuilder;
            _animation = animationComponent;
            _gizmo = gizmoComponent;
            _context = playbackContext;
            _camera = camera;
            _selectionManager = selectionManager;
            _localization = localization;
            _editHistory = new CscEditHistory(
                eventHub,
                CaptureSceneSnapshot,
                CaptureSelection,
                ApplySceneSnapshot);
            Scene = gameWorld;
            DisplayName = _localization.Get("DisplayName.CscEditor");
            SceneRootItems =
            [
                new CscSceneRootViewModel(
                    RootElements,
                    _localization.Get("Csc.Root"),
                    OnSceneRootModified,
                    _localization)
            ];

            PortholeBackImage = LoadPackImage("ui/skins/default/porthole_back.png");
            PortholeFrameImage = LoadPackImage("ui/skins/default/porthole.png");

            componentInserter.Execute(
                coreComponents,
                objectSelectionComponent,
                animationComponent,
                gizmoComponent);

            _gizmo.ElementModified += OnGizmoModifiedElement;
            _gizmo.DragStarted += OnEditGestureStarted;
            _gizmo.DragCompleted += OnGizmoDragCompleted;
            _animation.PortholeFrameUpdated += OnPortholeFrameUpdated;
            _eventHub.Register<SelectionChangedEvent>(this, OnScene3dSelectionChanged);
            _eventHub.Register<CommandStackChangedEvent>(
                this,
                OnCommandStackChanged);
            _eventHub.Register<CommandStackUndoEvent>(
                this,
                OnCommandStackChanged);
            _context.PropertyChanged += OnPlaybackContextChanged;

            SaveCommand = new RelayCommand(() => Save());
            PlayPauseCommand = new RelayCommand(() => _context.IsPlaying = !_context.IsPlaying);
            StopCommand = new RelayCommand(() =>
            {
                _context.IsPlaying = false;
                CurrentTime = 0;
            });
            GizmoTranslateCommand = new RelayCommand(() => _gizmo.SetMode(GizmoMode.Translate));
            GizmoRotateCommand = new RelayCommand(() => _gizmo.SetMode(GizmoMode.Rotate));
            GizmoScaleCommand = new RelayCommand(() => _gizmo.SetMode(GizmoMode.NonUniformScale));
            GizmoOffCommand = new RelayCommand(_gizmo.Disable);
            FocusSelectedCommand = new RelayCommand(FocusSelected);
            LookThroughCameraCommand = new RelayCommand(LookThroughSelectedCamera);
            ResetViewCommand = new RelayCommand(() => _animation.ClearLookThrough());
            DeleteSelectedCommand = new RelayCommand(DeleteSelected);
            DetachSelectedCommand = new RelayCommand(() => ReparentElement(SelectedElement, null));
            AddModelCommand = new RelayCommand(AddModel);
            AddVariantModelCommand = new RelayCommand(AddVariantModel);
            AddVfxCommand = new RelayCommand(
                () => AddNamedElement(CscElementKind.Vfx, _localization.Get("Csc.Prompt.VfxName")));
            AddSfxCommand = new RelayCommand(
                () => AddNamedElement(CscElementKind.Sfx, _localization.Get("Csc.Prompt.WwiseStartEvent")));
            AddPointLightCommand = new RelayCommand(() => AddElement(CscElementKind.PointLight));
            AddSpotLightCommand = new RelayCommand(() => AddElement(CscElementKind.SpotLight));
            AddCameraCommand = new RelayCommand(() => AddElement(CscElementKind.Camera));
            AddLocatorCommand = new RelayCommand(() => AddElement(CscElementKind.Locator));
            AddCompositeSceneCommand = new RelayCommand(AddCompositeScene);
            AddPrefabCommand = new RelayCommand(AddPrefab);
            AddAnimationCommand = new RelayCommand(AddAnimation);
            OpenFactionColourSettingsCommand = new RelayCommand(
                factionColourSettingsDialog.ShowDialog);
        }

        void OnPortholeFrameUpdated() => NotifyPropertyChanged(nameof(PortholeLiveFrame));

        void OnPlaybackContextChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CscPlaybackContext.CurrentTime))
                NotifyPropertyChanged(nameof(CurrentTime));
            else if (e.PropertyName == nameof(CscPlaybackContext.Duration))
            {
                NotifyPropertyChanged(nameof(Duration));
                SceneRootItems[0].Duration = Duration;
            }
            else if (e.PropertyName == nameof(CscPlaybackContext.IsPlaying))
                NotifyPropertyChanged(nameof(IsPlaying));
            else if (e.PropertyName == nameof(CscPlaybackContext.LookThroughElementId))
                NotifyPropertyChanged(nameof(IsLookingThroughCamera));
        }

        BitmapImage? LoadPackImage(string path)
        {
            try
            {
                var file = _packFileService.FindFile(path);
                if (file == null)
                {
                    _logger.Warning("Porthole overlay image not found in loaded packs: {Path}", path);
                    return null;
                }

                using var stream = new MemoryStream(file.DataSource.ReadData());
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load porthole overlay image {Path}", path);
                return null;
            }
        }

        // ---------------------------------------------------------------------
        // Load / save
        // ---------------------------------------------------------------------

        public void LoadFile(PackFile file)
        {
            CurrentFile = file;
            DisplayName = file.Name;

            try
            {
                SceneData = CscScene.Load(file.DataSource.ReadData());
                _context.Duration =
                    (float)NormalizePlaybackDuration(SceneData.Duration);
                _context.CurrentTime = 0;
                _context.IsPlaying = false;
                SceneRootItems[0].SetScene(SceneData);

                // Build the 3D scene first - root-ref sub-scenes are loaded during the build and
                // the tree wants to show their contents too.
                _sceneBuilder.Build(SceneData);
                RebuildElementTree();
                _animation.SetScene(SceneData);
                _animation.ApplyFrame();

                UpdateSceneStatus();
                NotifyPropertyChanged(nameof(Duration));
                SceneRootItems[0].Duration = Duration;
                NotifyPropertyChanged(nameof(ManifestEditable));
                NotifyPropertyChanged(nameof(CurveStructureLocked));
                _editHistory.InitializeLoadedDocument();
                NotifyHistoryState();
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to load CSC file {Name}", file.Name);
                _dialogs.ShowExceptionWindow(
                    e,
                    _localization.GetFormat("Csc.Error.Load", file.Name));
            }
        }

        public bool Save()
        {
            if (SceneData == null)
                return false;

            try
            {
                var bytes = CscSceneWriter.Write(SceneData);
                var path = _packFileService.GetFullPath(CurrentFile);
                var result = _fileSaveService.Save(path, bytes, prompOnConflict: false);
                if (result != null)
                {
                    CurrentFile = result;
                    _editHistory.MarkSaved();
                    NotifyHistoryState();
                    StatusText = _localization.GetFormat("Csc.Status.Saved", result.Name);
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to save CSC file");
                _dialogs.ShowExceptionWindow(e, _localization.Get("Csc.Error.Save"));
                return false;
            }
        }

        public bool Undo()
        {
            var result = _editHistory.Undo();
            NotifyHistoryState();
            return result;
        }

        public bool Redo()
        {
            var result = _editHistory.Redo();
            NotifyHistoryState();
            return result;
        }

        byte[] CaptureSceneSnapshot() =>
            SceneData == null
                ? []
                : CscSceneWriter.Write(SceneData);

        CscSelectionSnapshot CaptureSelection()
        {
            if (SelectedSceneRoot != null)
                return CscSelectionSnapshot.Root;
            if (SelectedElement != null)
            {
                return CscSelectionSnapshot.Element(
                    SelectedElement.Element.Id);
            }

            return CscSelectionSnapshot.None;
        }

        void ApplySceneSnapshot(
            byte[] snapshot,
            CscSelectionSnapshot selection)
        {
            var currentTime = _context.CurrentTime;
            SelectedElement = null;
            SelectedSceneRoot = null;
            SceneRootItems[0].IsSelected = false;

            SceneData = CscScene.Load(snapshot);
            _context.Duration =
                (float)NormalizePlaybackDuration(SceneData.Duration);
            _context.CurrentTime = (float)ClampPlaybackTime(
                currentTime,
                _context.Duration);
            SceneRootItems[0].SetScene(SceneData);

            _sceneBuilder.Build(SceneData);
            RebuildElementTree();
            _animation.SetScene(SceneData);

            if (selection.IsSceneRoot)
            {
                SelectedSceneRoot = SceneRootItems[0];
                SceneRootItems[0].IsSelected = true;
            }
            else if (selection.ElementId is { } elementId &&
                SceneData.FindElement(elementId) is { } element &&
                FindViewModel(element) is { } elementViewModel)
            {
                SelectedElement = elementViewModel;
                elementViewModel.IsSelected = true;
                ExpandTo(elementViewModel);
            }

            _animation.ApplyFrame();
            UpdateSceneStatus();
            NotifyPropertyChanged(nameof(Duration));
            SceneRootItems[0].Duration = Duration;
            NotifyPropertyChanged(nameof(ManifestEditable));
            NotifyPropertyChanged(nameof(CurveStructureLocked));
            NotifyHistoryState();
        }

        void RecordEdit(string localizationKey)
        {
            _editHistory.RecordChange(
                _localization.Get(localizationKey));
            NotifyHistoryState();
        }

        void EndEditGesture(string localizationKey)
        {
            _editHistory.EndGesture(
                _localization.Get(localizationKey));
            NotifyHistoryState();
        }

        void OnCommandStackChanged(
            CommandStackChangedEvent notification) =>
            NotifyHistoryState();

        void OnCommandStackChanged(
            CommandStackUndoEvent notification) =>
            NotifyHistoryState();

        void NotifyHistoryState()
        {
            NotifyPropertyChanged(nameof(CanUndo));
            NotifyPropertyChanged(nameof(CanRedo));
            NotifyPropertyChanged(nameof(HasUnsavedChanges));
        }

        // ---------------------------------------------------------------------
        // Tree building / selection
        // ---------------------------------------------------------------------

        void RebuildElementTree()
        {
            RootElements.Clear();
            if (SceneData == null)
                return;

            foreach (var root in SceneData.RootElements)
                RootElements.Add(BuildTreeNode(root));
        }

        /// <summary>Builds a tree node for <paramref name="element"/> and its children, resolving a
        /// root-ref's referenced sub-scene contents (display-only) if one is already loaded. Shared
        /// by <see cref="RebuildElementTree"/> (full reload) and <see cref="AddElement"/> (a single
        /// newly created element).</summary>
        CscElementViewModel BuildTreeNode(CscElement element)
        {
            var vm = new CscElementViewModel(element, OnElementModified, _localization);
            foreach (var child in element.Children)
                vm.Children.Add(BuildTreeNode(child));

            // Root-ref elements show the referenced scene's contents (display-only), and its
            // own ROOT header info (duration, focus point, radius, weather path) read-only.
            if (element.Kind == CscElementKind.RootRef)
                vm.SetSubScene(_sceneBuilder.SubScenes.FirstOrDefault(s => s.Host == element)?.Scene);

            foreach (var sub in _sceneBuilder.SubScenes.Where(s => s.Host == element))
                foreach (var subRoot in sub.Scene.RootElements)
                    vm.Children.Add(BuildTreeNode(subRoot));

            return vm;
        }

        public CscElementViewModel? FindViewModel(CscElement element)
        {
            CscElementViewModel? Search(ObservableCollection<CscElementViewModel> items)
            {
                foreach (var item in items)
                {
                    if (item.Element == element)
                        return item;
                    if (Search(item.Children) is { } found)
                        return found;
                }
                return null;
            }
            return Search(RootElements);
        }

        void OnElementModified(CscElementViewModel vm, ElementChange change)
        {
            if (change == ElementChange.Asset && SceneData != null)
            {
                _sceneBuilder.RefreshContent(vm.Element);
                _sceneBuilder.RefreshAnimationBindings(SceneData);

                // A changed root-ref path swaps the whole displayed sub-scene - re-list it.
                if (vm.Element.Kind == CscElementKind.RootRef)
                    RebuildElementTree();
            }
            _animation.ApplyFrame();
            RecordEdit("Csc.History.Edit");
        }

        /// <summary>Called when the Root node's own ROOT header fields (duration, focus point,
        /// radius, weather path) are edited - re-syncs playback's own Duration to a changed header
        /// value, same as LoadFile.</summary>
        void OnSceneRootModified()
        {
            if (SceneData == null)
                return;

            _context.Duration =
                (float)NormalizePlaybackDuration(SceneData.Duration);
            UpdateSceneStatus();
            RecordEdit("Csc.History.Edit");
        }

        static double NormalizePlaybackDuration(double duration) =>
            double.IsFinite(duration)
                ? Math.Max(0, duration)
                : 0;

        internal static double ClampPlaybackTime(
            double currentTime,
            double duration) =>
            Math.Clamp(
                double.IsFinite(currentTime) ? currentTime : 0,
                0,
                NormalizePlaybackDuration(duration));

        void UpdateSceneStatus()
        {
            if (SceneData == null)
                return;

            var warning = SceneData.ManifestParsed
                ? ""
                : _localization.Get("Csc.Status.ManifestReadOnly");
            StatusText = _localization.GetFormat(
                "Csc.Status.Scene",
                SceneData.Elements.Count,
                SceneData.RootVersion,
                SceneData.Duration,
                warning);
        }

        void OnGizmoModifiedElement()
        {
            _animation.ApplyFrame();
            SelectedElement?.RefreshFromDomain();
            RedrawCurves?.Invoke();
            RecordEdit("Csc.History.Gizmo");
        }

        void OnScene3dSelectionChanged(SelectionChangedEvent selectionEvent)
        {
            if (selectionEvent.NewState is not ObjectSelectionState objectSelection)
                return;

            var selected = objectSelection.GetSingleSelectedObject();
            if (selected == null)
                return;

            var element = _sceneBuilder.FindOwningElement(selected);

            // Hand the click over to the element-level selection and drop the engine's own mesh
            // selection: its highlight draws the geometry bounding box without any world
            // transform, which would render an orphaned box at the scene origin.
            objectSelection.Clear();

            if (element == null || element == SelectedElement?.Element)
                return;

            var vm = FindViewModel(element);
            if (vm != null)
            {
                vm.IsSelected = true; // two-way TreeViewItem binding routes back to SelectedElement
                ExpandTo(vm);
            }
        }

        void ExpandTo(CscElementViewModel target)
        {
            bool Expand(ObservableCollection<CscElementViewModel> items)
            {
                foreach (var item in items)
                {
                    if (item == target || Expand(item.Children))
                    {
                        item.IsExpanded = true;
                        return true;
                    }
                }
                return false;
            }
            Expand(RootElements);
        }

        /// <summary>Called by the curve editor when keyframes changed through it.</summary>
        public void OnCurvesModified()
        {
            _animation.ApplyFrame();
            SelectedElement?.RefreshFromDomain();
            RecordEdit("Csc.History.Curve");
        }

        /// <summary>Called by the view when a splice/node-transform bone id field is edited.</summary>
        public void OnAuxFieldModified() =>
            RecordEdit("Csc.History.Edit");

        public void OnEditGestureStarted() =>
            _editHistory.BeginGesture();

        public void OnCurveEditGestureCompleted() =>
            EndEditGesture("Csc.History.Curve");

        void OnGizmoDragCompleted() =>
            EndEditGesture("Csc.History.Gizmo");

        /// <summary>Hook the view uses to repaint the curve control after out-of-band data changes.</summary>
        public Action? RedrawCurves { get; set; }

        void RebuildCurveSeries()
        {
            CurveSeriesList.Clear();
            SelectedCurveSeries = null;

            var element = SelectedElement?.Element;
            if (element == null)
                return;

            void Add(string name, System.Windows.Media.Color colour, CscChannel channel) =>
                CurveSeriesList.Add(new CurveSeries { Name = name, Colour = colour, Channel = channel });

            var mediaColors = new[]
            {
                System.Windows.Media.Color.FromRgb(235, 90, 90),
                System.Windows.Media.Color.FromRgb(120, 220, 120),
                System.Windows.Media.Color.FromRgb(110, 160, 250),
            };

            if (element.Position != null)
                for (var i = 0; i < element.Position.Channels.Count; i++)
                    Add(
                        _localization.GetFormat("Csc.Curve.Position", "XYZ"[i]),
                        mediaColors[i % 3],
                        element.Position.Channels[i]);
            if (element.Rotation != null)
                for (var i = 0; i < element.Rotation.Channels.Count; i++)
                    Add(
                        _localization.GetFormat("Csc.Curve.Rotation", "XYZ"[i]),
                        mediaColors[i % 3],
                        element.Rotation.Channels[i]);
            if (element.Scale is { Channels.Count: > 0 })
                Add(
                    _localization.Get("Csc.Curve.Scale"),
                    System.Windows.Media.Color.FromRgb(230, 200, 90),
                    element.Scale.Channels[0]);
            if (element.Weight is { Channels.Count: > 0 })
                Add(
                    _localization.Get("Csc.Curve.Weight"),
                    System.Windows.Media.Color.FromRgb(200, 130, 230),
                    element.Weight.Channels[0]);

            for (var g = 0; g < element.TypeGroups.Count; g++)
            {
                var group = element.TypeGroups[g];
                for (var c = 0; c < group.Channels.Count; c++)
                {
                    var groupName = LocalizeTypeGroup(element.TypeGroupName(g));
                    var name = group.Channels.Count == 1
                        ? groupName
                        : _localization.GetFormat(
                            "Csc.Curve.ColourComponent",
                            groupName,
                            "RGB"[Math.Min(c, 2)]);
                    Add(name, mediaColors[(g + c) % 3], group.Channels[c]);
                }
            }

            // Show animated channels by default; if nothing is animated, show position.
            var anyAnimated = CurveSeriesList.Any(s => s.Channel.Keyframes.Count > 0);
            if (anyAnimated)
            {
                foreach (var series in CurveSeriesList)
                    series.IsVisible = series.Channel.Keyframes.Count > 0;
            }
            else
            {
                var positionChannelCount = element.Position?.Channels.Count ?? 0;
                for (var i = 0; i < CurveSeriesList.Count; i++)
                    CurveSeriesList[i].IsVisible = i < positionChannelCount;
            }

            SelectedCurveSeries = CurveSeriesList.FirstOrDefault(s => s.IsVisible);
            RedrawCurves?.Invoke();
        }

        string LocalizeTypeGroup(string rawName)
        {
            const string parameterPrefix = "param ";
            if (rawName.StartsWith(parameterPrefix, StringComparison.Ordinal) &&
                int.TryParse(rawName[parameterPrefix.Length..], out var parameterIndex))
            {
                return _localization.GetFormat("Csc.Curve.Parameter", parameterIndex);
            }

            return _localization.Get($"Csc.Curve.Group.{rawName}");
        }

        // ---------------------------------------------------------------------
        // Structural edits
        // ---------------------------------------------------------------------

        void AddModel()
        {
            var result = _dialogs.DisplayBrowseDialog([".rigid_model_v2", ".wsmodel"]);
            if (result.Result == false || result.File == null)
                return;

            AddElement(CscElementKind.Model, _packFileService.GetFullPath(result.File));
        }

        void AddVariantModel()
        {
            var result = _dialogs.DisplayBrowseDialog([".variantmeshdefinition"]);
            if (result.Result == false || result.File == null)
                return;

            AddElement(CscElementKind.VariantModel, _packFileService.GetFullPath(result.File));
        }

        void AddCompositeScene()
        {
            var result = _dialogs.DisplayBrowseDialog([".csc"]);
            if (result.Result == false || result.File == null)
                return;

            AddElement(CscElementKind.RootRef, _packFileService.GetFullPath(result.File));
        }

        void AddPrefab()
        {
            var result = _dialogs.DisplayBrowseDialog([".bmd"]);
            if (result.Result == false || result.File == null)
                return;

            AddElement(CscElementKind.Prefab, _packFileService.GetFullPath(result.File));
        }

        void AddAnimation()
        {
            var result = _dialogs.DisplayBrowseDialog([".anim"]);
            if (result.Result == false || result.File == null)
                return;

            AddElement(CscElementKind.Animation, _packFileService.GetFullPath(result.File));
        }

        void AddNamedElement(CscElementKind kind, string prompt)
        {
            var input = _dialogs.ShowTextInputDialog(prompt);
            if (input.Result == false || string.IsNullOrWhiteSpace(input.Text))
                return;

            AddElement(kind, input.Text.Trim());
        }

        void AddElement(CscElementKind kind, string assetPath = "")
        {
            if (SceneData == null)
                return;
            if (!EnsureStructureEditable())
                return;

            // New elements go into ROOT's attach tree, so a nested selection falls back to its
            // nearest non-nested ancestor and an external (root-ref sub-scene) selection falls
            // back to the hosting root-ref element.
            var parent = SelectedElement?.Element;
            while (parent is { IsExternal: true })
            {
                var current = parent;
                parent = _sceneBuilder.SubScenes.FirstOrDefault(s => s.ElementIds.Contains(current.Id))?.Host;
            }
            while (parent is { IsNested: true })
                parent = parent.Parent;

            var element = CscElementFactory.Create(SceneData, kind, assetPath);
            _editHistory.BeginGesture();
            SceneData.AddElement(element, parent);

            // Added before building the tree node so a new root-ref's sub-scene (loaded inside
            // AddElement) is already in SubScenes for BuildTreeNode to pick up.
            _sceneBuilder.AddElement(element);

            var vm = BuildTreeNode(element);
            var parentVm = parent != null ? FindViewModel(parent) : null;
            if (parentVm != null)
            {
                parentVm.Children.Add(vm);
                parentVm.IsExpanded = true;
            }
            else
            {
                RootElements.Add(vm);
            }

            _sceneBuilder.RefreshAnimationBindings(SceneData);
            _animation.ApplyFrame();
            SelectedElement = vm;
            vm.IsSelected = true;
            EndEditGesture("Csc.History.Structure");
            StatusText = parent != null
                ? _localization.GetFormat(
                    "Csc.Status.AddedUnder",
                    LocalizeKind(kind),
                    element.Id,
                    parent.Id)
                : _localization.GetFormat(
                    "Csc.Status.Added",
                    LocalizeKind(kind),
                    element.Id);
        }

        void DeleteSelected()
        {
            var vm = SelectedElement;
            if (vm == null || SceneData == null)
                return;
            if (!EnsureStructureEditable())
                return;

            if (vm.Element.IsNested)
            {
                StatusText = _localization.Get("Csc.Status.NestedCannotDelete");
                return;
            }

            if (vm.Element.IsExternal)
            {
                StatusText = _localization.Get("Csc.Status.ExternalReadOnly");
                return;
            }

            var subtreeSize = 1 + CountDescendants(vm.Element);
            var confirm = _dialogs.ShowYesNoBox(
                subtreeSize > 1
                    ? _localization.GetFormat(
                        "Csc.Confirm.DeleteSubtree",
                        vm.Element.Id,
                        subtreeSize - 1)
                    : _localization.GetFormat("Csc.Confirm.Delete", vm.Element.Id),
                _localization.Get("Csc.Confirm.DeleteTitle"));
            if (confirm != ShowMessageBoxResult.OK)
                return;

            _editHistory.BeginGesture();
            void RemoveNodes(CscElement element)
            {
                foreach (var child in element.Children)
                    RemoveNodes(child);
                _sceneBuilder.RemoveElement(element.Id);
            }
            RemoveNodes(vm.Element);

            SceneData.RemoveElementSubtree(vm.Element);
            RemoveVm(RootElements, vm);
            SelectedElement = null;
            EndEditGesture("Csc.History.Structure");
        }

        static int CountDescendants(CscElement element) =>
            element.Children.Sum(child => 1 + CountDescendants(child));

        static bool RemoveVm(ObservableCollection<CscElementViewModel> items, CscElementViewModel target)
        {
            if (items.Remove(target))
                return true;
            foreach (var item in items)
                if (RemoveVm(item.Children, target))
                    return true;
            return false;
        }

        /// <summary>Reparents via tree drag-drop or the Detach command. Null parent = top level.</summary>
        public void ReparentElement(CscElementViewModel? childVm, CscElementViewModel? newParentVm)
        {
            if (childVm == null || SceneData == null || childVm == newParentVm)
                return;
            if (!EnsureStructureEditable())
                return;

            _editHistory.BeginGesture();
            if (!SceneData.Reparent(childVm.Element, newParentVm?.Element))
            {
                _editHistory.CancelGesture();
                StatusText = childVm.Element.IsExternal || (newParentVm?.Element.IsExternal ?? false)
                    ? _localization.Get("Csc.Status.ExternalCannotReparent")
                    : childVm.Element.IsNested || (newParentVm?.Element.IsNested ?? false)
                        ? _localization.Get("Csc.Status.NestedCannotReparent")
                        : _localization.Get("Csc.Status.ReparentCycle");
                return;
            }

            RemoveVm(RootElements, childVm);
            if (newParentVm != null)
            {
                newParentVm.Children.Add(childVm);
                newParentVm.IsExpanded = true;
            }
            else
            {
                RootElements.Add(childVm);
            }

            childVm.RefreshFromDomain();
            _sceneBuilder.RefreshAnimationBindings(SceneData);
            _animation.ApplyFrame();
            EndEditGesture("Csc.History.Structure");
            StatusText = newParentVm != null
                ? _localization.GetFormat(
                    "Csc.Status.Attached",
                    childVm.Element.Id,
                    newParentVm.Element.Id)
                : _localization.GetFormat("Csc.Status.Detached", childVm.Element.Id);
        }

        bool EnsureStructureEditable()
        {
            if (ManifestEditable)
                return true;

            StatusText = _localization.Get("Csc.Status.ManifestReadOnly");
            return false;
        }

        string LocalizeKind(CscElementKind kind) =>
            _localization.Get($"Csc.Kind.{kind}");

        void FocusSelected()
        {
            if (SelectedElement == null)
                return;
            var world = _sceneBuilder.ElementNodes.TryGetValue(SelectedElement.Element.Id, out var node)
                ? node.WorldMatrix
                : SelectedElement.Element.WorldTransform(_context.CurrentTime);
            _camera.LookAt = world.Translation;
        }

        void LookThroughSelectedCamera()
        {
            if (SelectedElement?.Element is { Kind: CscElementKind.Camera } camera)
                _animation.SetLookThrough(camera.Id);
        }

        public void Close()
        {
        }

        public void Dispose()
        {
            _eventHub?.UnRegister(this);
            _gizmo.ElementModified -= OnGizmoModifiedElement;
            _gizmo.DragStarted -= OnEditGestureStarted;
            _gizmo.DragCompleted -= OnGizmoDragCompleted;
            _animation.PortholeFrameUpdated -= OnPortholeFrameUpdated;
            _context.PropertyChanged -= OnPlaybackContextChanged;
            _animation.ClearLookThrough();
            _sceneBuilder.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
