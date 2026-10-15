using System.Collections.ObjectModel;
using System.Windows;
using Editors.Shared.Core.Common.BaseControl;
using Shared.Core.Misc;
using GameAnimationPlayer = GameWorld.Core.Animation.AnimationPlayer;

namespace Editors.Shared.Core.Common.AnimationPlayer
{
    public class AnimationPlayerViewModel : NotifyPropertyChangedImpl
    {
        readonly List<SceneObject> _assetList = new();
        float _playbackPositionSeconds;
        bool _isUpdatingPlaybackPosition;
        IEditorViewModelTypeProvider? _timelineAnnotationContent;
        bool _isScrubbing;
        public bool IsScrubbing
        {
            get => _isScrubbing;
            set
            {
                SetAndNotifyWhenChanged(ref _isScrubbing, value);
                foreach (var player in GetPlayers())
                    player.IsScrubbing = value;
            }
        }

        public NotifyAttr<float> SelectedAnimationCurrentTime { get; private set; } = new();
        public NotifyAttr<float> SelectedAnimationMaxTime { get; private set; } = new();
        public NotifyAttr<double> SelectedAnimationFps { get; private set; } = new();
        public NotifyAttr<int> SelectedAnimationCurrentFrame { get; private set; } = new();
        public NotifyAttr<int> SelectedAnimationFrameCount { get; private set; } = new();
        public NotifyAttr<bool> IsEnabled { get; set; } = new();
        public NotifyAttr<bool> IsPlaying { get; private set; } = new();
        public NotifyAttr<bool> LoopAnimation { get; set; } = new(true);
        public IEditorViewModelTypeProvider? TimelineAnnotationContent
        {
            get => _timelineAnnotationContent;
            set => SetAndNotifyWhenChanged(
                ref _timelineAnnotationContent,
                value);
        }
        public float PlaybackPositionSeconds
        {
            get => _playbackPositionSeconds;
            set
            {
                SetAndNotifyWhenChanged(ref _playbackPositionSeconds, value);
                if (!_isUpdatingPlaybackPosition)
                    SeekPlayers(value);
            }
        }

        public NotifyAttr<Visibility> PlayerControlsVisibility { get; private set; } = new(Visibility.Collapsed);

        public ObservableCollection<AssetPlayerItem> PlayerItems { get; private set; } = new ObservableCollection<AssetPlayerItem>();

        AssetPlayerItem _selectedMainAnimation;
        public AssetPlayerItem SelectedMainAnimation
        {
            get => _selectedMainAnimation;
            set
            {
                var previous = _selectedMainAnimation;
                SetAndNotifyWhenChanged(ref _selectedMainAnimation, value);
                OnMainAnimationChanged(previous, value);
            }
        }

        public AnimationPlayerViewModel()
        {
            IsEnabled.Value = false;
            IsEnabled.PropertyChanged += (x, y) => OnAnimationPlayerEnabled(IsEnabled.Value);
        }

        private void OnMainAnimationChanged(AssetPlayerItem oldAnimation, AssetPlayerItem mainAnimation)
        {
            if (oldAnimation != null)
                oldAnimation.Asset.Player.OnFrameChanged -= OnAnimationFrameChanged;

            RefreshPlaybackPosition();
            mainAnimation.Asset.Player.OnFrameChanged += OnAnimationFrameChanged;
        }

        private void RefreshPlaybackPosition()
        {
            var player = SelectedMainAnimation?.Asset.Player;
            var wasUpdating = _isUpdatingPlaybackPosition;
            _isUpdatingPlaybackPosition = true;
            try
            {
                // Changing the slider range can write a coerced value back through its binding.
                SelectedAnimationFrameCount.Value = player?.FrameCount ?? 0;
                SelectedAnimationCurrentFrame.Value = player?.CurrentFrame ?? 0;
                SelectedAnimationCurrentTime.Value = (float)(player?.CurrentTime.TotalSeconds ?? 0);
                SelectedAnimationMaxTime.Value = (float)(player?.Duration.TotalSeconds ?? 0);
                SelectedAnimationFps.Value = player?.FramesPerSecond ?? 0;
                PlaybackPositionSeconds = SelectedAnimationCurrentTime.Value;
            }
            finally
            {
                _isUpdatingPlaybackPosition = wasUpdating;
            }
            UpdatePlayingState();
        }

        public void RegisterAsset(SceneObject asset)
        {
            _assetList.Add(asset);
            var playerItem = new AssetPlayerItem() { Asset = asset };
            PlayerItems.Add(playerItem);

            asset.Player.LoopAnimation = false;
            RefreshAssetViewModel(asset, playerItem);
            if (SelectedMainAnimation == null)
                SelectedMainAnimation = playerItem;

            OnAnimationPlayerEnabled(IsEnabled.Value);
            asset.AnimationChanged += (x) => RefreshAssetViewModel(asset, playerItem);
            asset.SkeletonChanged += (x) => RefreshAssetViewModel(asset, playerItem);
        }

        void RefreshAssetViewModel(SceneObject asset, AssetPlayerItem playerItem)
        {
            playerItem.AnimationName.Value = asset.AnimationName.Value;
            playerItem.SlotName.Value = asset.Description;
            playerItem.MaxFrames.Value = asset.Player.FrameCount;
            if (SelectedMainAnimation == playerItem)
                RefreshPlaybackPosition();
        }

        public void ToggleAnimationPausePlay()
        {
            UpdatePlayingState();
            var shouldPlay = !IsPlaying.Value;
            foreach (var item in _assetList)
            {
                if (shouldPlay)
                    Resume(item);
                else
                    Pause(item);
            }

            UpdatePlayingState();
        }

        public void SetAnimationNextFrame()
        {
            _assetList.ForEach(NextFrame);
            UpdatePlayingState();
        }

        public void SetAnimationPrivFrame()
        {
            _assetList.ForEach(PrivFrame);
            UpdatePlayingState();
        }

        public void SetAnimationFirstFrame()
        {
            _assetList.ForEach(x => SetFrame(x, 0));
            UpdatePlayingState();
        }

        public void SetAnimationLastFrame()
        {
            LoopAnimation.Value = false;
            foreach (var item in _assetList)
                SetFrame(item, SelectedMainAnimation.Asset.Player.FrameCount);
            UpdatePlayingState();
        }

        private void OnAnimationFrameChanged(int currentFrame)
        {
            var isSeeking = _isUpdatingPlaybackPosition;
            RefreshPlaybackPosition();

            if (!isSeeking && !IsScrubbing && !SelectedMainAnimation.Asset.Player.IsPlaying
                && SelectedMainAnimation.Asset.Player.Duration > TimeSpan.Zero
                && SelectedMainAnimation.Asset.Player.CurrentTime >= SelectedMainAnimation.Asset.Player.Duration)
            {
                if (LoopAnimation.Value)
                {
                    SetAnimationFirstFrame();
                    _assetList.ForEach(Resume);
                }
                else
                    _assetList.ForEach(Pause);
            }

            UpdatePlayingState();
        }

        private void OnAnimationPlayerEnabled(bool isEnabled)
        {
            if (isEnabled)
            {
                PlayerControlsVisibility.Value = Visibility.Visible;

                SelectedAnimationFrameCount.Value = 0;
                if (SelectedMainAnimation != null)
                    SelectedAnimationFrameCount.Value = SelectedMainAnimation.MaxFrames.Value;

                _assetList.ForEach(StartFromBeginning);
            }
            else
            {
                PlayerControlsVisibility.Value = Visibility.Collapsed;
                _assetList.ForEach(x => Stop(x));
            }

            UpdatePlayingState();
        }

        private void UpdatePlayingState()
        {
            var player = SelectedMainAnimation?.Asset.Player;
            IsPlaying.Value = player != null && player.FrameCount > 0
                && player.IsPlaying && player.IsEnabled;
        }

        HashSet<GameAnimationPlayer> GetPlayers()
        {
            var players = new HashSet<GameAnimationPlayer>(
                ReferenceEqualityComparer.Instance);
            foreach (var asset in _assetList)
            {
                players.Add(asset.Player);
                foreach (var metaItem in asset.MetaDataItems)
                {
                    if (metaItem.Player != null)
                        players.Add(metaItem.Player);
                }
            }

            return players;
        }

        void SeekPlayers(float timeSeconds)
        {
            var wasUpdating = _isUpdatingPlaybackPosition;
            _isUpdatingPlaybackPosition = true;
            try
            {
                foreach (var player in GetPlayers())
                {
                    if (IsScrubbing)
                        player.QueueSeekToTimeSeconds(timeSeconds);
                    else
                        player.SeekToTimeSeconds(timeSeconds);
                }
            }
            finally
            {
                _isUpdatingPlaybackPosition = wasUpdating;
            }
        }

        void StartFromBeginning(SceneObject asset)
        {
            asset.Player.CurrentFrame = 0;
            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                    metaItem.Player.CurrentFrame = 0;
            }

            Resume(asset);
        }

        void Resume(SceneObject asset)
        {
            asset.Player.Play();

            foreach (var metaItem in asset.MetaDataItems)
                metaItem.Player?.Play();
        }

        void Pause(SceneObject asset)
        {
            asset.Player.Pause();
            foreach (var metaItem in asset.MetaDataItems)
                metaItem.Player?.Pause();
        }

        void Stop(SceneObject asset)
        {
            asset.Player.Stop();
            foreach (var metaItem in asset.MetaDataItems)
                metaItem.Player?.Stop();
        }

        void SetFrame(SceneObject asset, int newFrame)
        {
            asset.Player.Pause();
            asset.Player.CurrentFrame = newFrame;

            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                {
                    metaItem.Player.Pause();
                    metaItem.Player.CurrentFrame = newFrame;
                }
            }
        }

        void NextFrame(SceneObject asset)
        {
            asset.Player.Pause();
            asset.Player.CurrentFrame++;

            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                {
                    metaItem.Player.Pause();
                    metaItem.Player.CurrentFrame = asset.Player.CurrentFrame;
                }
            }
        }

        void PrivFrame(SceneObject asset)
        {
            asset.Player.Pause();
            asset.Player.CurrentFrame--;

            foreach (var metaItem in asset.MetaDataItems)
            {
                if (metaItem.Player != null)
                {
                    metaItem.Player.Pause();
                    metaItem.Player.CurrentFrame = asset.Player.CurrentFrame;
                }
            }
        }

        public class AssetPlayerItem : NotifyPropertyChangedImpl
        {
            public SceneObject Asset { get; init; }
            public NotifyAttr<string> SlotName { get; internal set; } = new();
            public NotifyAttr<int> MaxFrames { get; internal set; } = new();
            public NotifyAttr<string> AnimationName { get; internal set; } = new();
        }
    }
}
