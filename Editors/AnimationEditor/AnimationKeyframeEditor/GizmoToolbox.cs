using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using Shared.Core.Services;

namespace AnimationEditor.AnimationKeyframeEditor
{
    internal class GizmoToolbox
    {
        private readonly AnimationKeyframeEditorViewModel _parent;
        public List<int> PreviousSelectedBones { get => _previousSelectedBones; private set { _previousSelectedBones = value;  } }
        private List<int> _previousSelectedBones;

        public List<int> ModifiedBones { get => _modifiedBones; private set { _modifiedBones = value; } }
        private List<int> _modifiedBones = new();

        public int ModifiedFrameNr { get => _modifiedFrameNr; private set { _modifiedFrameNr = value; } }
        private int _modifiedFrameNr = 0;

        private GizmoMode _lastGizmoTool = GizmoMode.Translate;

        private int _lastFrame = 0;
        private ISelectionState? _subscribedSelectionState;
        private BoneSelectionState? _subscribedBoneSelectionState;
        private bool _isSelectionManagerSubscribed;


        public GizmoToolbox(AnimationKeyframeEditorViewModel parent)
        {
            _parent = parent;

            _parent.Rider.Player.OnFrameChanged += (frameNr) =>
            {
                var selection = _parent.SelectionManager.GetState<BoneSelectionState>();
                if (selection != null)
                {
                    selection.CurrentAnimation = _parent.Rider.Player.AnimationClip;
                    selection.Skeleton = _parent.Skeleton;
                    selection.CurrentFrame = _parent.Rider.Player.CurrentFrame;
                    selection.SelectedBones.Clear();
                }
            };
        }

        private void OnModifiedBonesEvent(BoneSelectionState state)
        {
            _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();

            if (_modifiedFrameNr == state.CurrentFrame)
            {
                _modifiedBones = _modifiedBones.Union(state.ModifiedBones).ToList();
            }
            else
            {
                _modifiedBones = state.ModifiedBones;
            }

            _modifiedFrameNr = state.CurrentFrame;
            _parent.ResetInterpolationTool();
        }

        private void OnSelectionChanged(ISelectionState state, bool sendEvent)
        {
            _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();

            if (state is BoneSelectionState boneSelectionState)
            {
                if (_previousSelectedBones == null || boneSelectionState.SelectedBones.Count > 0)
                {
                    _previousSelectedBones = new List<int>(boneSelectionState.SelectedBones);
                }

                if (!_parent.AllowToSelectAnimRoot.Value)
                {
                    boneSelectionState.DeselectAnimRootNode();
                }

                boneSelectionState.EnableInverseKinematics = _parent.EnableInverseKinematics.Value;
                boneSelectionState.InverseKinematicsEndBoneIndex = _parent.ModelBoneListForIKEndBone.SelectedItem.BoneIndex;

                if (boneSelectionState.EnableInverseKinematics)
                {
                    if (boneSelectionState.SelectedBones.Count > 1)
                    {
                        MessageBox.Show(LocalizationManager.Instance.Get("Msg.IKSingleBoneOnly"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        var firstSelection = boneSelectionState.SelectedBones[0];
                        boneSelectionState.SelectedBones.Clear();
                        boneSelectionState.SelectedBones.Add(firstSelection);
                        return;
                    }

                    if (boneSelectionState.SelectedBones.Count == 1 && boneSelectionState.InverseKinematicsEndBoneIndex == boneSelectionState.SelectedBones[0])
                    {
                        MessageBox.Show(LocalizationManager.Instance.Get("Msg.IKSameChain"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        boneSelectionState.SelectedBones.Clear();
                    }
                }

                if (boneSelectionState.SelectedBones.Count == 0)
                {
                    _parent.GizmoComponent.Disable();
                }
            }
        }

        private ISelectable FindSelectableObject(ISceneNode node)
        {
            if (node is ISelectable selectableNode) return selectableNode;
            foreach (var slot in node.Children)
            {
                return FindSelectableObject(slot);
            }
            return null;
        }

        private void EnsureTheObjectsAreNotSelectable(ISceneNode node)
        {
            foreach (var slot in node.Children)
            {
                slot.IsEditable = false;
                EnsureTheObjectsAreNotSelectable(slot);
            }
        }

        public void SelectMode()
        {
            if (_parent.Rider.MainNode.Children.Count <= 1) return;

            var variantMeshRoot = _parent.Rider.MainNode.Children[1];
            if (variantMeshRoot.Children.Count == 0) return;
            var selectableNode = FindSelectableObject(variantMeshRoot);
            EnsureTheObjectsAreNotSelectable(selectableNode);

            _lastFrame = _parent.Rider.Player.CurrentFrame;

            if (selectableNode != null)
            {
                _parent.CommandFactory.Create<ObjectSelectionCommand>().Configure(x => x.Configure(new List<ISelectable>() { selectableNode }, false, false)).BuildAndExecute();
                _parent.SelectionComponent.SetBoneSelectionMode();
                _parent.Pause();
            }

            var selection = _parent.SelectionManager.GetState<BoneSelectionState>();
            if (selection != null)
            {
                selection.CurrentAnimation = _parent.Rider.Player.AnimationClip;
                selection.Skeleton = _parent.Skeleton;
                selection.CurrentFrame = _parent.Rider.Player.CurrentFrame;
                selection.SelectedBones.Clear();
            }

            StartSelectionSubscriptions();
        }

        private void StartSelectionSubscriptions()
        {
            if (!_isSelectionManagerSubscribed)
            {
                _parent.SelectionManager.StateChanged += UpdateSelectionSubscriptions;
                _isSelectionManagerSubscribed = true;
            }

            UpdateSelectionSubscriptions(_parent.SelectionManager.GetState());
        }

        private void UpdateSelectionSubscriptions(ISelectionState selectionState)
        {
            if (ReferenceEquals(_subscribedSelectionState, selectionState))
                return;

            if (_subscribedSelectionState != null)
                _subscribedSelectionState.SelectionChanged -= OnSelectionChanged;
            if (_subscribedBoneSelectionState != null)
                _subscribedBoneSelectionState.BoneModifiedEvent -= OnModifiedBonesEvent;

            _subscribedSelectionState = selectionState;
            _subscribedBoneSelectionState = selectionState as BoneSelectionState;
            _subscribedSelectionState.SelectionChanged -= OnSelectionChanged;
            _subscribedSelectionState.SelectionChanged += OnSelectionChanged;
            if (_subscribedBoneSelectionState != null)
            {
                _subscribedBoneSelectionState.BoneModifiedEvent -= OnModifiedBonesEvent;
                _subscribedBoneSelectionState.BoneModifiedEvent += OnModifiedBonesEvent;
            }
        }

        public void SelectPreviousBones()
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_previousSelectedBones == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.SelectBoneFirst"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SelectMode();
            var selection = _parent.SelectionManager.GetState<BoneSelectionState>();
            if (selection != null)
            {
                selection.CurrentAnimation = _parent.Rider.Player.AnimationClip;
                selection.Skeleton = _parent.Skeleton;
                selection.CurrentFrame = _parent.Rider.Player.CurrentFrame;
                selection.SelectedBones.Clear();
            }
            _parent.CommandFactory.Create<BoneSelectionCommand>().Configure(x => x.Configure(_previousSelectedBones, true, false)).BuildAndExecute();
            switch (_lastGizmoTool)
            {
                case GizmoMode.Translate:
                    MoveMode();
                    break;
                case GizmoMode.Rotate:
                    RotateMode();
                    break;
                case GizmoMode.NonUniformScale:
                case GizmoMode.UniformScale:
                    ScaleMode();
                    break;
                default:
                    break;
            }
        }

        public void MoveMode()
        {
            if (_parent.SelectionManager.GetState().Mode != GeometrySelectionMode.Bone) return;
            _parent.GizmoComponent.ResetScale();
            _parent.GizmoComponent.SetGizmoMode(GizmoMode.Translate);
            _lastGizmoTool = GizmoMode.Translate;
        }

        public void RotateMode()
        {
            if (_parent.SelectionManager.GetState().Mode != GeometrySelectionMode.Bone) return;
            _parent.GizmoComponent.ResetScale();
            _parent.GizmoComponent.SetGizmoMode(GizmoMode.Rotate);
            _lastGizmoTool = GizmoMode.Rotate;
        }

        public void ScaleMode()
        {
            if (_parent.EnableInverseKinematics.Value)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ScaleModeIKConflict"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (_parent.SelectionManager.GetState().Mode != GeometrySelectionMode.Bone) return;
            _parent.GizmoComponent.ResetScale();
            _parent.GizmoComponent.SetGizmoMode(GizmoMode.NonUniformScale);
            _lastGizmoTool = GizmoMode.NonUniformScale;
        }
    }
}
