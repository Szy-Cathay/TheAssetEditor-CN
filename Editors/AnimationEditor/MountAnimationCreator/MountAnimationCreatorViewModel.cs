using System.Collections.ObjectModel;
using AnimationEditor.AnimationKeyframeEditor;
using AnimationEditor.MountAnimationCreator.Services;
using AnimationEditor.MountAnimationCreator.ViewModels;
using Editors.AnimationVisualEditors.MountAnimationCreator.Services;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.BaseControl;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Common;
using Clipboard = System.Windows.Clipboard;

namespace AnimationEditor.MountAnimationCreator
{
    public class MountAnimationCreatorViewModel : EditorHostBase
    {
        public override Type EditorViewModelType => typeof(EditorView);
        private readonly IFileSaveService _fileSaveService;
        private readonly IUiCommandFactory _uiCommandFactory;
        private readonly IPackFileService _pfs;
        private readonly SelectionManager _selectionManager;
        private readonly ISkeletonAnimationLookUpHelper _lookup;
        private readonly IStandardDialogs _dialogs;
        private readonly IGlobalEventHub _globalEvents;
        private readonly SceneObject _mount;
        private readonly SceneObject _rider;
        private readonly SceneObject _preview;
        private readonly List<int> _mountVertices = [];
        private Rmv2MeshNode? _mountVertexOwner;
        private bool _updatingInputs;
        private bool _previewMeshDirty = true;

        public AnimationSettingsViewModel AnimationSettings { get; } = new();
        public MountLinkViewModel MountLinkController { get; }
        public FilterCollection<SkeletonBoneNode> SelectedRiderBone { get; }
        public NotifyAttr<bool> CanPreview { get; } = new(false);
        public NotifyAttr<bool> CanBatchProcess { get; } = new(false);
        public NotifyAttr<bool> CanSave { get; } = new(false);
        public NotifyAttr<bool> CanAddToFragment { get; } = new(false);
        public NotifyAttr<bool> DisplayGeneratedSkeleton { get; }
        public NotifyAttr<bool> DisplayGeneratedMesh { get; }
        public NotifyAttr<string> SelectedVertexesText { get; } = new("");
        public NotifyAttr<string> SavePrefixText { get; } = new("new_");
        public ObservableCollection<uint> AnimationOutputFormats { get; } = [5, 6, 7];
        public NotifyAttr<uint> SelectedAnimationOutputFormat { get; } = new(7);
        public NotifyAttr<bool> EnsureUniqeFileName { get; } = new(true);
        public NotifyAttr<string> Status { get; } = new("");
        public FilterCollection<IAnimationBinGenericFormat> ActiveOutputFragment { get; }
        public FilterCollection<AnimationBinEntryGenericFormat> ActiveFragmentSlot { get; }

        public MountAnimationCreatorViewModel(IEditorHostParameters hostParameters, IPackFileService pfs,
            ISkeletonAnimationLookUpHelper lookup, SelectionManager selectionManager,
            IFileSaveService fileSaveService, IUiCommandFactory uiCommandFactory,
            MountVertexSelectionComponent vertexSelection, ReferenceObjectSelectionComponent objectSelection,
            ReferenceObjectSelectionOutlineComponent selectionOutline, IStandardDialogs dialogs, IGlobalEventHub globalEvents)
            : base(hostParameters)
        {
            _pfs = pfs;
            _lookup = lookup;
            _selectionManager = selectionManager;
            _fileSaveService = fileSaveService;
            _uiCommandFactory = uiCommandFactory;
            _dialogs = dialogs;
            _globalEvents = globalEvents;
            DisplayName = Text("DisplayName.MountTool");
            GameWorld!.AddComponent(vertexSelection);
            GameWorld.AddComponent(objectSelection);
            GameWorld.AddComponent(selectionOutline);

            DisplayGeneratedSkeleton = new(true, value => { if (_preview != null) _preview.ShowSkeleton.Value = value; });
            DisplayGeneratedMesh = new(true, value => { if (_preview != null) _preview.ShowMesh.Value = value; });
            SelectedRiderBone = new(null, _ => TryReGenerateAnimation());
            ActiveFragmentSlot = new(null, _ => UpdateStates()) { SearchFilter = (value, rx) => rx.IsMatch(value.DisplayName) };
            ActiveOutputFragment = new(null, value =>
            {
                ActiveFragmentSlot.SelectedItem = null;
                ActiveFragmentSlot.UpdatePossibleValues(value?.Entries);
                UpdateStates();
            }) { SearchFilter = (value, rx) => rx.IsMatch(value.Name) || rx.IsMatch(value.FullPath) };

            var rider = _sceneObjectViewModelBuilder.CreateAsset("rider", true, Text("MountAnim.Rider"), Color.Black, null!);
            var mount = _sceneObjectViewModelBuilder.CreateAsset("mount", true, Text("MountAnim.Mount"), Color.Black, null!);
            _rider = rider.Data;
            _mount = mount.Data;
            vertexSelection?.SetMount(_mount);
            _preview = SceneObjectEditor.CreateAsset("mount-preview", Text("MountAnim.GeneratedAnimation"), Color.Red);
            Player.RegisterAsset(_preview);
            SceneObjects.Add(rider);
            SceneObjects.Add(mount);
            MountLinkController = new MountLinkViewModel(SceneObjectEditor, pfs, _rider, _mount, dialogs, UpdateStates, lookup);
            _mount.SkeletonChanged += MountSkeletonChanged;
            _mount.AnimationChanged += TryReGenerateAnimation;
            _mount.MeshChanged += MountMeshChanged;
            _rider.SkeletonChanged += RiderSkeletonChanged;
            _rider.AnimationChanged += TryReGenerateAnimation;
            _rider.MeshChanged += RiderMeshChanged;
            AnimationSettings.SettingsChanged += SettingsChanged;
            _globalEvents.Register<PackFileContainerSetAsMainEditableEvent>(this, _ => RefreshViewAction());
            UpdateStates();
        }

        public void SetDebugInputParameters(AnimationToolInput rider, AnimationToolInput mount)
        {
            LoadInput(_rider, rider);
            LoadInput(_mount, mount);
        }

        private void LoadInput(SceneObject scene, AnimationToolInput input)
        {
            if (input.Mesh != null)
                SceneObjectEditor.SetMesh(scene, input.Mesh);
            if (input.Animation != null)
                SceneObjectEditor.SetAnimation(scene, _pfs.GetFullPath(input.Animation));
        }

        private void MountMeshChanged(SceneObject _)
        {
            _mount.IsSelectable = false;
            if (MountAnimationGeneratorService.SettingsAreValid(AnimationSettings))
                _mount.SetTransform(Matrix.CreateScale((float)AnimationSettings.Scale.Value));
            ResetAttachment();
        }

        private void RiderMeshChanged(SceneObject _)
        {
            _rider.IsSelectable = false;
            _previewMeshDirty = true;
            TryReGenerateAnimation();
        }

        private void MountSkeletonChanged(GameSkeleton _)
        {
            ResetAttachment();
            MountLinkController.ReloadFragments(false, true);
        }

        private void RiderSkeletonChanged(GameSkeleton? skeleton)
        {
            if (_rider.SkeletonName.Value.Replace('\\', '/').Split('/').Contains("tech", StringComparer.OrdinalIgnoreCase) && skeleton != null)
            {
                var runtimeRig = _pfs.FindFile($"animations/skeletons/{skeleton.SkeletonName}.anim");
                if (runtimeRig != null)
                {
                    SceneObjectEditor.SetSkeleton(_rider, runtimeRig);
                    return;
                }
            }
            _updatingInputs = true;
            try
            {
                SelectedRiderBone.SelectedItem = null;
                SelectedRiderBone.UpdatePossibleValues(skeleton == null ? null : SkeletonBoneNodeHelper.CreateFlatSkeletonList(skeleton));
                SelectedRiderBone.SelectedItem = SelectedRiderBone.PossibleValues.FirstOrDefault(x => x.BoneName.Equals("root", StringComparison.OrdinalIgnoreCase)) ??
                    SelectedRiderBone.PossibleValues.FirstOrDefault(x => x.BoneName.Equals("bn_hips", StringComparison.OrdinalIgnoreCase));
                AnimationSettings.IsRootNodeAnimation = skeleton?.GetBoneIndexByName("animroot") >= 0;
                MountLinkController.ReloadFragments(true, false);
                RefreshOutputFragments();
                _previewMeshDirty = true;
            }
            finally
            {
                _updatingInputs = false;
            }
            TryReGenerateAnimation();
        }

        private void ResetAttachment()
        {
            _mountVertices.Clear();
            _mountVertexOwner = null;
            SelectedVertexesText.Value = Text("Msg.NoVertexSelected");
            TryReGenerateAnimation();
        }

        private void SettingsChanged()
        {
            if (MountAnimationGeneratorService.SettingsAreValid(AnimationSettings))
                _mount.SetTransform(Matrix.CreateScale((float)AnimationSettings.Scale.Value));
            TryReGenerateAnimation();
        }

        private bool IsMountMesh(Rmv2MeshNode mesh) => _mount.ModelNode != null &&
            (ReferenceEquals(_mount.ModelNode, mesh) || SceneNodeHelper.GetChildrenOfType<Rmv2MeshNode>(_mount.ModelNode).Contains(mesh));

        private bool HasAttachment() => _mountVertexOwner != null && _mountVertices.Count > 0 && IsMountMesh(_mountVertexOwner) &&
            _mountVertices.All(index => index >= 0 && index < _mountVertexOwner.Geometry.VertexCount()) &&
            SelectedRiderBone.SelectedItem != null && _rider.Skeleton != null &&
            SelectedRiderBone.PossibleValues.Contains(SelectedRiderBone.SelectedItem);

        private void UpdateStates()
        {
            var connected = HasAttachment() && _mount.Skeleton != null &&
                MountAnimationGeneratorService.SettingsAreValid(AnimationSettings);
            CanPreview.Value = connected && _mount.AnimationClip?.Timebase != null && _rider.AnimationClip?.Timebase != null;
            var canWrite = _pfs.GetEditablePack() != null;
            CanSave.Value = CanPreview.Value && canWrite;
            CanBatchProcess.Value = connected && canWrite && MountLinkController?.AnimationSetForMount.SelectedItem != null &&
                MountLinkController.AnimationSetForRider.SelectedItem != null;
            CanAddToFragment.Value = CanSave.Value && ActiveOutputFragment.SelectedItem != null && ActiveFragmentSlot.SelectedItem != null;
            Status.Value = canWrite ? "" : Text("MountAnim.ProjectRequired");
        }

        public void SetMountVertex()
        {
            var selection = _selectionManager.GetState<VertexSelectionState>();
            var mesh = selection?.RenderObject as Rmv2MeshNode;
            if (mesh == null || selection!.CurrentSelection().Count == 0 || !IsMountMesh(mesh) || mesh.Geometry.WeightCount == 0)
            {
                ResetAttachment();
                _dialogs.ShowDialogBox(Text("MountAnim.SelectMountVertex"), DisplayName);
                return;
            }
            _mountVertexOwner = mesh;
            _mountVertices.Clear();
            _mountVertices.AddRange(selection.CurrentSelection().Order());
            SelectedVertexesText.Value = LocalizationManager.Instance.GetFormat("MountAnim.SelectedVertices", _mountVertices.Count, _mountVertices[0]);
            TryReGenerateAnimation();
        }

        private void TryReGenerateAnimation(AnimationClip? _ = null)
        {
            if (_updatingInputs)
                return;
            UpdateStates();
            if (!CanPreview.Value)
            {
                ClearPreview();
                return;
            }
            try
            {
                GeneratePreview();
            }
            catch (Exception exception)
            {
                ClearPreview();
                Status.Value = exception.Message;
            }
        }

        private void ClearPreview()
        {
            if (_preview == null)
                return;
            SceneObjectEditor.SetAnimationClip(_preview, null, "");
            _preview.ShowMesh.Value = false;
            _preview.ShowSkeleton.Value = false;
        }

        private void GeneratePreview()
        {
            var clip = CreateGenerator().GenerateMountAnimation(_mount.AnimationClip, _rider.AnimationClip, isDeath: IsDeathAnimation);
            if (_previewMeshDirty)
            {
                SceneObjectEditor.CopyMeshFromOther(_preview, _rider);
                _preview.IsSelectable = false;
                _previewMeshDirty = false;
            }
            SceneObjectEditor.SetAnimationClip(_preview, clip, Text("MountAnim.GeneratedAnimation"));
            _preview.ShowSkeleton.Value = DisplayGeneratedSkeleton.Value;
            _preview.ShowMesh.Value = DisplayGeneratedMesh.Value;
        }

        public void CreateMountAnimationAction() => Execute(() =>
        {
            UpdateStates();
            if (CanPreview.Value)
                GeneratePreview();
        });

        private MountAnimationGeneratorService CreateGenerator() => new(AnimationSettings, _mountVertexOwner!,
            _mountVertices[0], SelectedRiderBone.SelectedItem!.BoneIndex, _rider, _mount);

        private bool IsDeathAnimation => MountLinkController.SelectedRiderTag.SelectedItem?.SlotName.StartsWith("RIDER_DEATH_", StringComparison.Ordinal) == true ||
            MountLinkController.SelectedRiderTag.SelectedItem?.SlotName.StartsWith("DEATH_", StringComparison.Ordinal) == true;

        private BatchProcessorService CreateProcessor(BatchProcessOptions? options = null) => new(_pfs, _lookup, CreateGenerator(),
            options ?? new BatchProcessOptions { SavePrefix = SavePrefixText.Value }, _fileSaveService, _dialogs,
            SelectedAnimationOutputFormat.Value, EnsureUniqeFileName.Value);

        public void SaveCurrentAnimationAction() => Execute(() =>
        {
            UpdateStates();
            if (!CanSave.Value)
                return;
            var path = CreateProcessor().SaveSingleAnim(_mount.AnimationClip, _rider.AnimationClip, _rider.AnimationName.Value, IsDeathAnimation);
            if (path != null)
                Status.Value = LocalizationManager.Instance.GetFormat("MountAnim.Saved", path);
        });

        public void AddAnimationToFragment() => Execute(() =>
        {
            UpdateStates();
            if (!CanAddToFragment.Value)
                return;
            var selected = ActiveOutputFragment.SelectedItem!;
            var selectedSlot = ActiveFragmentSlot.SelectedItem!;
            var project = _pfs.GetEditablePack()!;
            var sourceFile = _pfs.FindFile(selected.PackFileReference.FileName, project);
            if (sourceFile == null)
                throw new InvalidOperationException(Text("MountAnim.OutputChanged"));
            var database = AnimationPackSerializer.Load(sourceFile, _pfs);
            var table = database.Files.OfType<AnimationBinWh3>().Single(x => x.FileName == selected.FullPath);
            var row = table.AnimationTableEntries.Single(x => x.AnimationId == selectedSlot.SlotIndex);
            var animationRef = row.AnimationRefs.ElementAtOrDefault(selectedSlot.Index > 0 ? selectedSlot.Index - 1 : 0);
            if (animationRef == null || animationRef.AnimationFile != selectedSlot.AnimationFile ||
                animationRef.AnimationMetaFile != selectedSlot.MetaFile || animationRef.AnimationSoundMetaFile != selectedSlot.SoundFile)
                throw new InvalidOperationException(Text("MountAnim.OutputChanged"));

            var processor = CreateProcessor();
            var path = processor.GetAnimationPath(_rider.AnimationName.Value);
            if (!EnsureUniqeFileName.Value && _pfs.FindFile(path, project) != null &&
                _dialogs.ShowYesNoBox(Text("MountAnim.OverwriteGenerated"), DisplayName) != ShowMessageBoxResult.OK)
                return;
            var bytes = processor.GenerateAnimationBytes(_mount.AnimationClip, _rider.AnimationClip, IsDeathAnimation);
            animationRef.AnimationFile = path;
            if (MountLinkController.AnimationSetForMount.SelectedItem != null)
                table.MountBin = MountLinkController.AnimationSetForMount.SelectedItem.Name;
            _pfs.ApplyFileWrites(project, [
                new PackFileWrite(path, bytes),
                new PackFileWrite(selected.PackFileReference.FileName, AnimationPackSerializer.ConvertToBytes(database))
            ]);
            RefreshViewAction();
            Status.Value = LocalizationManager.Instance.GetFormat("MountAnim.Saved", path);
        });

        public void BatchProcessUsingFragmentsAction() => Execute(() =>
        {
            UpdateStates();
            if (!CanBatchProcess.Value)
                return;
            var mount = MountLinkController.AnimationSetForMount.SelectedItem!;
            var rider = MountLinkController.AnimationSetForRider.SelectedItem!;
            var name = rider.Name;
            var options = BatchProcessOptionsWindow.ShowDialog(SavePrefixText.Value + name, SavePrefixText.Value);
            if (options != null && CreateProcessor(options).Process(mount, rider))
                RefreshViewAction();
        });

        public void ViewMountFragmentAction() => ViewAnimationSet(MountLinkController.AnimationSetForMount.SelectedItem);
        public void ViewRiderFragmentAction() => ViewAnimationSet(MountLinkController.AnimationSetForRider.SelectedItem);
        public void ViewOutputFragmentAction() => ViewAnimationSet(ActiveOutputFragment.SelectedItem);

        private void ViewAnimationSet(IAnimationBinGenericFormat? set)
        {
            if (set != null)
                _uiCommandFactory.Create<OpenEditorCommand>().ExecuteAsWindow(set.PackFileReference.FileName, 800, 900);
        }

        private void RefreshOutputFragments()
        {
            var table = ActiveOutputFragment.SelectedItem?.FullPath;
            var pack = ActiveOutputFragment.SelectedItem?.PackFileReference?.FileName;
            var slot = ActiveFragmentSlot.SelectedItem;
            ActiveOutputFragment.SelectedItem = null;
            ActiveOutputFragment.UpdatePossibleValues(MountLinkController.LoadAnimationSetForSkeleton(_rider.Skeleton?.SkeletonName ?? "", true));
            ActiveOutputFragment.SelectedItem = ActiveOutputFragment.PossibleValues.FirstOrDefault(x => x.FullPath == table && x.PackFileReference?.FileName == pack);
            ActiveFragmentSlot.SelectedItem = ActiveFragmentSlot.PossibleValues.FirstOrDefault(x => MountLinkViewModel.SameSlot(x, slot));
        }

        public void RefreshViewAction() => Execute(() =>
        {
            MountLinkController.ReloadFragments();
            RefreshOutputFragments();
            UpdateStates();
        });

        public void CopyAnimation() => Execute(() =>
        {
            if (_preview.AnimationClip == null)
            {
                _dialogs.ShowDialogBox(Text("Msg.AnimationNotGenerated"), DisplayName);
                return;
            }
            _preview.Player.Pause();
            var frames = _preview.AnimationClip.DynamicFrames.Count;
            Clipboard.SetText(JsonConvert.SerializeObject(AnimationCliboardCreator.CreateFrameClipboard(_preview.Skeleton, _preview.AnimationClip, 0, frames)));
            Status.Value = LocalizationManager.Instance.GetFormat("Msg.CopiedFrames", 0, frames - 1);
        });

        private void Execute(Action action)
        {
            try { action(); }
            catch (Exception exception) { _dialogs.ShowExceptionWindow(exception, DisplayName); }
        }

        private static string Text(string key) => LocalizationManager.Instance.Get(key);

        public override void Close()
        {
            _globalEvents.UnRegister(this);
            _mount.SkeletonChanged -= MountSkeletonChanged;
            _mount.AnimationChanged -= TryReGenerateAnimation;
            _mount.MeshChanged -= MountMeshChanged;
            _rider.SkeletonChanged -= RiderSkeletonChanged;
            _rider.AnimationChanged -= TryReGenerateAnimation;
            _rider.MeshChanged -= RiderMeshChanged;
            AnimationSettings.SettingsChanged -= SettingsChanged;
            base.Close();
        }
    }
}
