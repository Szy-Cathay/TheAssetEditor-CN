// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows.Forms;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using GameWorld.Core.Commands.Bone.Clipboard;
using Newtonsoft.Json;
using Shared.Core.Services;

namespace AnimationEditor.AnimationKeyframeEditor
{
    internal class CopyPasteFromClipboardPose
    {
        private readonly AnimationKeyframeEditorViewModel _parent;

        public CopyPasteFromClipboardPose(AnimationKeyframeEditorViewModel parent)
        {
            _parent = parent;
        }

        public int GetClipboardFramesLength()
        {
            var frameInJsonFormat = Clipboard.GetText();
            try
            {
                var parsedClipboardFrame = JsonConvert.DeserializeObject<BoneTransformClipboardData>(frameInJsonFormat);
                return parsedClipboardFrame.Frames.Count;
            }
            catch (JsonException)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ClipboardParseFailed"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 0;
            }
        }

        public void  CopyCurrentFrameUpToEndFrame()
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var endFrame = _parent.Rider.Player.AnimationClip.DynamicFrames.Count;
            var skeleton = _parent.Rider.Skeleton;
            var frames = _parent.Rider.AnimationClip;
            var jsonText = JsonConvert.SerializeObject(AnimationCliboardCreator.CreateFrameClipboard(skeleton, frames, currentFrame, endFrame));
            Clipboard.SetText(jsonText);
            MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.CopiedFrames", currentFrame, endFrame - 1), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        public void CopySingle()
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var skeleton = _parent.Rider.Skeleton;
            var frames = _parent.Rider.AnimationClip;
            var jsonText = JsonConvert.SerializeObject(AnimationCliboardCreator.CreateFrameClipboard(skeleton, frames, currentFrame, currentFrame + 1));
            Clipboard.SetText(jsonText);
        }

        public void PasteSingle()
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var skeleton = _parent.Rider.Skeleton;
            var frameInJsonFormat = Clipboard.GetText();
            try
            {
                var parsedClipboardFrame = JsonConvert.DeserializeObject<BoneTransformClipboardData>(frameInJsonFormat);
                if ((parsedClipboardFrame.SkeletonName != skeleton.SkeletonName) && !_parent.DontWarnDifferentSkeletons.Value)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SkeletonMismatch", parsedClipboardFrame.SkeletonName, skeleton.SkeletonName), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No) return;
                }

                _parent.CommandFactory.Create<PasteWholeInRangeTransformFromClipboardBoneCommand>().Configure(x => x.Configure(
                    skeleton,
                    parsedClipboardFrame,
                    _parent.Rider.AnimationClip,
                    currentFrame, 1,
                    _parent.PastePosition.Value, _parent.PasteRotation.Value, _parent.PasteScale.Value)).BuildAndExecute();

                if (_parent.IncrementFrameAfterCopyOperation.Value)
                {
                    _parent.NextFrame();
                }
                _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();
            }
            catch (JsonException)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ClipboardParseFailed"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        public void PasteMultipleUpToRange(int pastedFramesLength)
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var maxFrame = _parent.Rider.AnimationClip.DynamicFrames.Count;
            var skeleton = _parent.Rider.Skeleton;
            var frameInJsonFormat = Clipboard.GetText();

            try
            {
                var parsedClipboardFrame = JsonConvert.DeserializeObject<BoneTransformClipboardData>(frameInJsonFormat);
                if ((parsedClipboardFrame.SkeletonName != skeleton.SkeletonName) && !_parent.DontWarnDifferentSkeletons.Value)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SkeletonMismatch", parsedClipboardFrame.SkeletonName, skeleton.SkeletonName), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No) return;
                }

                var framesCount = parsedClipboardFrame.Frames.Keys.Count;
                if (pastedFramesLength > framesCount)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.PasteTooLong", pastedFramesLength, framesCount), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var willCreateNewFrame = maxFrame < currentFrame + pastedFramesLength;
                var confirm = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.PasteConfirmMultiFrame", skeleton.SkeletonName, currentFrame, pastedFramesLength,
                    willCreateNewFrame ? (currentFrame + pastedFramesLength) - maxFrame : 0, willCreateNewFrame),
                    LocalizationManager.Instance.Get("Msg.AreYouSure"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;

                _parent.CommandFactory.Create<PasteWholeInRangeTransformFromClipboardBoneCommand>().Configure(x => x.Configure(
                    skeleton,
                    parsedClipboardFrame,
                   _parent.Rider.AnimationClip,
                   currentFrame, pastedFramesLength,
                   _parent.PastePosition.Value, _parent.PasteRotation.Value, _parent.PasteScale.Value)).BuildAndExecute();

                if (_parent.IncrementFrameAfterCopyOperation.Value)
                {
                    _parent.NextFrame();
                }
                _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();
            }
            catch (JsonException)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ClipboardParseFailed"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        public void PasteAll()
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var skeleton = _parent.Rider.Skeleton;
            var frameInJsonFormat = Clipboard.GetText();
            try
            {
                var parsedClipboardFrame = JsonConvert.DeserializeObject<BoneTransformClipboardData>(frameInJsonFormat);
                if ((parsedClipboardFrame.SkeletonName != skeleton.SkeletonName) && !_parent.DontWarnDifferentSkeletons.Value)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SkeletonMismatch", parsedClipboardFrame.SkeletonName, skeleton.SkeletonName), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No) return;
                }

                var pastedFramesLength = parsedClipboardFrame.Frames.Count;
                var maxFrame = _parent.Rider.Player.AnimationClip.DynamicFrames.Count;

                var willCreateNewFrame = maxFrame < pastedFramesLength;
                var confirm = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.PasteConfirmAllFrames", skeleton.SkeletonName, pastedFramesLength,
                    willCreateNewFrame ? pastedFramesLength - maxFrame : 0, willCreateNewFrame),
                    LocalizationManager.Instance.Get("Msg.AreYouSure"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;

                _parent.CommandFactory.Create<PasteWholeTransformFromClipboardBoneCommand>().Configure(x => x.Configure(
                    skeleton,
                    parsedClipboardFrame,
                    _parent.Rider.AnimationClip,
                    _parent.PastePosition.Value, _parent.PasteRotation.Value, _parent.PasteScale.Value)).BuildAndExecute();

                if (_parent.IncrementFrameAfterCopyOperation.Value)
                {
                    _parent.NextFrame();
                }
                _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();
            }
            catch (JsonException)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ClipboardParseFailed"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        public void PasteAllIntoSelectedNodes()
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_parent.GetSelectedBones() == null || _parent.GetSelectedBones().Count == 0)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.NoBonesSelected"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var skeleton = _parent.Rider.Skeleton;
            var frameInJsonFormat = Clipboard.GetText();
            var maxFrame = _parent.Rider.Player.AnimationClip.DynamicFrames.Count;
            try
            {
                var parsedClipboardFrame = JsonConvert.DeserializeObject<BoneTransformClipboardData>(frameInJsonFormat);
                if (parsedClipboardFrame == null)
                {
                    MessageBox.Show(LocalizationManager.Instance.Get("Msg.NoAnimationInClipboard"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if ((parsedClipboardFrame.SkeletonName != skeleton.SkeletonName) && !_parent.DontWarnDifferentSkeletons.Value)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SkeletonMismatch", parsedClipboardFrame.SkeletonName, skeleton.SkeletonName), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No) return;
                }


                var pastedFramesLength = parsedClipboardFrame.Frames.Count;

                var willCreateNewFrame = maxFrame < pastedFramesLength;
                var confirm = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.PasteConfirmAllFramesSelectedBones", skeleton.SkeletonName, pastedFramesLength,
                    willCreateNewFrame ? pastedFramesLength - maxFrame : 0, willCreateNewFrame),
                    LocalizationManager.Instance.Get("Msg.AreYouSure"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;

                _parent.CommandFactory.Create<PasteIntoSelectedBonesTransformFromClipboardBoneCommand>().Configure(x => x.Configure(
                    skeleton,
                    parsedClipboardFrame,
                    _parent.Rider.AnimationClip,
                    _parent.GetSelectedBones(),
                    _parent.PastePosition.Value, _parent.PasteRotation.Value, _parent.PasteScale.Value)).BuildAndExecute();

                if (_parent.IncrementFrameAfterCopyOperation.Value)
                {
                    _parent.Rider.Player.CurrentFrame++;
                }
                _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();
            }
            catch (JsonException)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ClipboardParseFailed"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        public void PasteMultipleUpToRangeIntoSelectedBones(int pastedFramesLength)
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_parent.GetSelectedBones() == null || _parent.GetSelectedBones().Count == 0)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.NoBonesSelected"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var maxFrame = _parent.Rider.AnimationClip.DynamicFrames.Count;
            var skeleton = _parent.Rider.Skeleton;
            var frameInJsonFormat = Clipboard.GetText();

            try
            {
                var parsedClipboardFrame = JsonConvert.DeserializeObject<BoneTransformClipboardData>(frameInJsonFormat);
                if ((parsedClipboardFrame.SkeletonName != skeleton.SkeletonName) && !_parent.DontWarnDifferentSkeletons.Value)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SkeletonMismatch", parsedClipboardFrame.SkeletonName, skeleton.SkeletonName), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No) return;
                }

                var framesCount = parsedClipboardFrame.Frames.Keys.Count;
                if (pastedFramesLength > framesCount)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.PasteTooLong", pastedFramesLength, framesCount), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                var willCreateNewFrame = maxFrame < currentFrame + pastedFramesLength;
                var confirm = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.PasteConfirmRangeSelectedBones", skeleton.SkeletonName, currentFrame, pastedFramesLength,
                    willCreateNewFrame ? currentFrame + framesCount - maxFrame : 0, willCreateNewFrame),
                    LocalizationManager.Instance.Get("Msg.AreYouSure"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (confirm != DialogResult.Yes) return;

                _parent.CommandFactory.Create<PasteIntoSelectedBonesInRangeTransformFromClipboardBoneCommand>().Configure(x => x.Configure(
                    skeleton,
                    parsedClipboardFrame,
                    _parent.Rider.AnimationClip,
                    currentFrame, pastedFramesLength,
                    _parent.GetSelectedBones(),
                    _parent.PastePosition.Value, _parent.PasteRotation.Value, _parent.PasteScale.Value)).BuildAndExecute();

                if (_parent.IncrementFrameAfterCopyOperation.Value)
                {
                    _parent.NextFrame();
                }
                _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();
            }
            catch (JsonException)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ClipboardParseFailed"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        public void PasteSingleIntoSelectedBones()
        {
            if (_parent.Rider.AnimationClip == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationNotLoaded"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (_parent.GetSelectedBones()  == null || _parent.GetSelectedBones().Count == 0)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.NoBonesSelected"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _parent.Pause();
            var currentFrame = _parent.Rider.Player.CurrentFrame;
            var skeleton = _parent.Rider.Skeleton;
            var frameInJsonFormat = Clipboard.GetText();
            try
            {
                var parsedClipboardFrame = JsonConvert.DeserializeObject<BoneTransformClipboardData>(frameInJsonFormat);
                if ((parsedClipboardFrame.SkeletonName != skeleton.SkeletonName) && !_parent.DontWarnDifferentSkeletons.Value)
                {
                    var result = MessageBox.Show(LocalizationManager.Instance.GetFormat("Msg.SkeletonMismatch", parsedClipboardFrame.SkeletonName, skeleton.SkeletonName), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (result == DialogResult.No) return;
                }

                _parent.CommandFactory.Create<PasteIntoSelectedBonesInRangeTransformFromClipboardBoneCommand>().Configure(x => x.Configure(
                    skeleton,
                    parsedClipboardFrame,
                    _parent.Rider.AnimationClip,
                    currentFrame, 1,
                    _parent.GetSelectedBones(),
                    _parent.PastePosition.Value, _parent.PasteRotation.Value, _parent.PasteScale.Value)).BuildAndExecute();

                if (_parent.IncrementFrameAfterCopyOperation.Value)
                {
                    _parent.NextFrame();
                }
                _parent.IsDirty.Value = _parent.CommandExecutor.CanUndo();
            }
            catch (JsonException)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.ClipboardParseFailed"), LocalizationManager.Instance.Get("Msg.GeneralError"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
    }
}
