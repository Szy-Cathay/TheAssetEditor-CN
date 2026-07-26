using System.Collections.Generic;
using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;

namespace GameWorld.Core.Components.Selection
{
    public delegate void BoneModifiedEvent(BoneSelectionState state);

    internal sealed class BoneModificationNotifier
    {
        private event BoneModifiedEvent Modified;
        internal int SubscriberCount =>
            Modified?.GetInvocationList().Length ?? 0;

        public void Subscribe(BoneModifiedEvent handler)
        {
            Modified += handler;
        }

        public void Unsubscribe(BoneModifiedEvent handler)
        {
            Modified -= handler;
        }

        public void Notify(BoneSelectionState state)
        {
            var subscribers = Modified;
            if (subscribers == null)
                return;

            ExceptionDispatchInfo primaryException = null;
            foreach (BoneModifiedEvent handler in subscribers.GetInvocationList())
            {
                try
                {
                    handler(state);
                }
                catch (Exception exception)
                {
                    primaryException ??= ExceptionDispatchInfo.Capture(exception);
                }
            }

            primaryException?.Throw();
        }
    }

    public class BoneSelectionState : ISelectionState
    {
        private readonly BoneModificationNotifier _modificationNotifier;

        public GeometrySelectionMode Mode => GeometrySelectionMode.Bone;
        public event SelectionStateChanged SelectionChanged;
        public AnimationClip CurrentAnimation { get; set; }
        public GameSkeleton Skeleton { get; set; }
        public ISelectable RenderObject { get; set; }
        public List<int> SelectedBones { get; set; } = new List<int>();
        public bool EnableInverseKinematics { get; set; }
        public int InverseKinematicsEndBoneIndex { get; set; }
        public int CurrentFrame { get; set; }
        public event BoneModifiedEvent BoneModifiedEvent
        {
            add => _modificationNotifier.Subscribe(value);
            remove => _modificationNotifier.Unsubscribe(value);
        }
        internal int BoneModificationSubscriberCount =>
            _modificationNotifier.SubscriberCount;
        public List<int> ModifiedBones { get; set; } = new List<int>();

        public BoneSelectionState(ISelectable renderObj)
            : this(renderObj, new BoneModificationNotifier())
        {
        }

        private BoneSelectionState(
            ISelectable renderObj,
            BoneModificationNotifier modificationNotifier)
        {
            RenderObject = renderObj;
            _modificationNotifier = modificationNotifier;
        }

        public void ModifySelection(IEnumerable<int> newSelectionItems, bool onlyRemove)
        {
            if (onlyRemove)
            {
                foreach (var newSelectionItem in newSelectionItems)
                {
                    if (SelectedBones.Contains(newSelectionItem))
                        SelectedBones.Remove(newSelectionItem);
                }
            }
            else
            {
                foreach (var newSelectionItem in newSelectionItems)
                {
                    if (!SelectedBones.Contains(newSelectionItem))
                        SelectedBones.Add(newSelectionItem);
                }
            }
            SelectionChanged?.Invoke(this, true);
        }


        public List<int> CurrentSelection()
        {
            return SelectedBones;
        }

        public void Clear()
        {
            SelectedBones.Clear();
            SelectionChanged?.Invoke(this, true);
        }


        public void EnsureSorted()
        {
            SelectedBones = SelectedBones.Distinct().OrderBy(x => x).ToList();
        }

        public void DeselectAnimRootNode()
        {
            SelectedBones.RemoveAll(bone => bone == 0);
        }

        public ISelectionState Clone()
        {
            return new BoneSelectionState(RenderObject, _modificationNotifier)
            {
                SelectedBones = new List<int>(SelectedBones),
                Skeleton = Skeleton,
                CurrentAnimation = CurrentAnimation,
                SelectionChanged = SelectionChanged,
                CurrentFrame = CurrentFrame,
                RenderObject = RenderObject,
                EnableInverseKinematics = EnableInverseKinematics,
                InverseKinematicsEndBoneIndex = InverseKinematicsEndBoneIndex,
            };
        }

        public int SelectionCount()
        {
            return SelectedBones.Count();
        }

        public ISelectable GetSingleSelectedObject()
        {
            return RenderObject;
        }

        public List<ISelectable> SelectedObjects()
        {
            return new List<ISelectable>() { RenderObject };
        }

        public void TriggerModifiedBoneEvent(List<int> modifiedBones)
        {
            ModifiedBones = new List<int>(modifiedBones);
            _modificationNotifier.Notify(this);
        }

        internal void TriggerModifiedBoneEvent(
            BoneSelectionState eventState,
            List<int> modifiedBones)
        {
            ModifiedBones = new List<int>(modifiedBones);
            eventState.ModifiedBones = new List<int>(modifiedBones);
            _modificationNotifier.Notify(eventState);
        }
    }
}

