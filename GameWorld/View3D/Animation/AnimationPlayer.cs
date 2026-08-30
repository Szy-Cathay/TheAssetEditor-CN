using GameWorld.Core.Animation.AnimationChange;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.Core.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace GameWorld.Core.Animation
{
    public class AnimationFrame
    {
        public class BoneKeyFrame
        {
            public int BoneIndex { get; set; }
            public int ParentBoneIndex { get; set; }
            public Quaternion Rotation { get; set; }
            public Vector3 Translation { get; set; }
            public Vector3 Scale { get; set; }
            public Matrix WorldTransform { get; set; }

            public void ComputeWorldMatrixFromComponents()
            {
                var rotation = Rotation;
                var translation = Translation;
                var scale = Scale;
                WorldTransform = Matrix.CreateScale(scale) * Matrix.CreateFromQuaternion(rotation) * Matrix.CreateTranslation(translation);
            }
        }

        public List<BoneKeyFrame> BoneTransforms = new List<BoneKeyFrame>();


        public Matrix GetSkeletonAnimatedWorld(GameSkeleton gameSkeleton, int boneIndex)
        {
            var output = gameSkeleton.GetWorldTransform(boneIndex) * BoneTransforms[boneIndex].WorldTransform;
            return output;
        }

        public Matrix GetSkeletonAnimatedWorldDiff(GameSkeleton gameSkeleton, int boneIndex0, int boneIndex1)
        {
            var bone0Transform = GetSkeletonAnimatedWorld(gameSkeleton, boneIndex0);
            var bone1Transform = GetSkeletonAnimatedWorld(gameSkeleton, boneIndex1);

            return bone1Transform * Matrix.Invert(bone0Transform);
        }

        public int GetParentBoneIndex(GameSkeleton gameSkeleton, int boneIndex)
        {
            return BoneTransforms[boneIndex].ParentBoneIndex;
        }
    }


    public delegate void FrameChanged(int currentFrame);
    public class AnimationPlayer
    {
        public event FrameChanged OnFrameChanged;

        GameSkeleton _skeleton;
        TimeSpanExtension _timeSinceStart;
        AnimationFrame _currentAnimFrame;
        AnimationClip _animationClip;
        GameSkeleton Skeleton { get { return _skeleton; } }
        public AnimationClip AnimationClip { get { return _animationClip; } }

        public bool IsPlaying { get; private set; } = true;
        public bool IsEnabled { get; set; } = false;
        public bool RefreshWhilePaused { get; set; } = true;
        public bool LoopAnimation { get; set; } = true;
        public bool MarkedForRemoval { get; set; } = false;

        public List<IAnimationChangeRule> AnimationRules { get; set; } = new List<IAnimationChangeRule>();

        private int TimeToFrame(TimeSpan time)
        {
            var timebase = _animationClip?.Timebase;
            if (timebase == null)
                return 0;

            return (int)Math.Floor(timebase.GetSamplePosition(time));
        }

        private TimeSpan FrameToStartTime(int frame)
        {
            var timebase = _animationClip?.Timebase;
            return timebase == null ? TimeSpan.Zero : timebase.GetSampleTime(frame);
        }

        public int CurrentFrame
        {
            get => _animationClip == null ? 0 : TimeToFrame(_timeSinceStart.TimeSpan);
            set
            {
                if (CurrentFrame == value)
                    return;

                if (_animationClip != null)
                {
                    var frameIndex = MathUtil.EnsureRange(value, 0, FrameCount() - 1);
                    _timeSinceStart = new TimeSpanExtension(FrameToStartTime(frameIndex));
                    OnFrameChanged?.Invoke(CurrentFrame);
                }
                else
                {
                    OnFrameChanged?.Invoke(0);
                }
                Refresh();
            }
        }

        public void SetAnimation(AnimationClip animation, GameSkeleton skeleton, bool allowAnimationsFromDifferentSkeletons = false)
        {
            if (allowAnimationsFromDifferentSkeletons == false && animation != null && _skeleton != null)
            {
                if (animation.AnimationBoneCount != skeleton.BoneCount)
                    throw new Exception("This animation does not work for this skeleton!");
            }

            AnimationRules.Clear();
            _skeleton = skeleton;
            _animationClip = animation;
            _timeSinceStart = new TimeSpanExtension();
            Refresh();
        }



        public void Update(GameTime gameTime)
        {
            if (!IsEnabled)
            {
                if (_currentAnimFrame != null)
                    Refresh();
                return;
            }

            if (!IsPlaying && !RefreshWhilePaused)
                return;

            var animationLengthUs = GetAnimationLengthUs();
            if (animationLengthUs != 0 && IsPlaying)
            {
                _timeSinceStart.TimeSpan += gameTime.ElapsedGameTime;
                if (_timeSinceStart.TotalMicrosecondsAsLong >= animationLengthUs)
                {
                    if (LoopAnimation)
                    {
                        _timeSinceStart = new TimeSpanExtension();
                    }
                    else
                    {
                        _timeSinceStart = TimeSpanExtension.FromMicroseconds(animationLengthUs);
                        IsPlaying = false;
                    }
                }

                OnFrameChanged?.Invoke(CurrentFrame);
            }

            Refresh();
        }

        public void Refresh()
        {
            try
            {
                if (IsEnabled == false)
                {
                    _currentAnimFrame = null;
                    return;
                }
                //Fix this so if crash no break
                float sampleT = 0;
                var timebase = _animationClip?.Timebase;
                if (timebase != null)
                    sampleT = (float)(_timeSinceStart.TimeSpan.TotalSeconds / timebase.Duration.TotalSeconds);
                _currentAnimFrame = AnimationSampler.Sample(sampleT, _skeleton, _animationClip, AnimationRules, !IsPlaying);
                _skeleton?.Update();
            }
            catch
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.AnimationPlaybackError"));
                SetAnimation(null, _skeleton);
            }
        }

        public void Play() { IsPlaying = true; IsEnabled = true; }

        public void Pause() { IsPlaying = false; }

        public void SeekToTimeSeconds(float timeSeconds)
        {
            if (float.IsFinite(timeSeconds) == false)
                throw new ArgumentOutOfRangeException(nameof(timeSeconds));

            var requestedTimeUs = (long)Math.Round(
                Math.Max(0, timeSeconds) * 1_000_000d);
            var animationLengthUs = GetAnimationLengthUs();
            if (animationLengthUs > 0)
                requestedTimeUs = Math.Min(requestedTimeUs, animationLengthUs);

            _timeSinceStart = TimeSpanExtension.FromMicroseconds(
                requestedTimeUs);
            OnFrameChanged?.Invoke(CurrentFrame);
            Refresh();
        }

        public void Stop()
        {
            IsPlaying = false;
            _currentAnimFrame = null;
            IsEnabled = false;
            _skeleton?.Update();
        }

        public double FramesPerSecond => _animationClip?.Timebase?.FramesPerSecond ?? 0;

        public int GetFps() => (int)FramesPerSecond;

        /// <summary>
        /// Overrides the sampled frame until the next refresh.
        /// </summary>
        public void SetManualFrame(AnimationFrame frame) => _currentAnimFrame = frame;

        public AnimationFrame GetCurrentAnimationFrame() => _currentAnimFrame;
        public int FrameCount() => _animationClip != null ? _animationClip.DynamicFrames.Count() : 0;
        public long GetAnimationLengthUs() => _animationClip?.PlayTimeUs ?? 0;
        public long GetTimeUs() => _timeSinceStart.TotalMicrosecondsAsLong;
    }
}
