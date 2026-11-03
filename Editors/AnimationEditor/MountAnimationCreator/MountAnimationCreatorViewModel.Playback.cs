using System.Collections.ObjectModel;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using Shared.Core.Misc;
using Shared.Core.Services;

namespace AnimationEditor.MountAnimationCreator;

public partial class MountAnimationCreatorViewModel
{
    private MountPlaybackSource? _selectedPlaybackSource;
    private string _preferredPlaybackSource = "rider";
    private bool _refreshingPlaybackSources;
    public ObservableCollection<MountPlaybackSource> AnimationPlaybackSources { get; } = [];
    public ObservableCollection<MountPlaybackSource> AvailablePlaybackSources { get; } = [];
    public NotifyAttr<bool> HasPlaybackSources { get; } = new(false);

    public MountPlaybackSource? SelectedPlaybackSource
    {
        get => _selectedPlaybackSource;
        set
        {
            if (value != null && !AvailablePlaybackSources.Contains(value)) return;
            if (!SetProperty(ref _selectedPlaybackSource, value)) return;
            if (!_refreshingPlaybackSources && value != null)
                _preferredPlaybackSource = value.Id;
            ApplyPlaybackSource();
        }
    }

    private void InitializePlaybackSources()
    {
        foreach (var asset in new[] { _rider, _mount, _preview })
        {
            AnimationPlaybackSources.Add(new(asset));
            asset.AnimationChanged += RefreshPlaybackSources;
            asset.SkeletonChanged += PlaybackSkeletonChanged;
        }
        RefreshPlaybackSources();
    }

    private void PlaybackSkeletonChanged(GameSkeleton? _) => RefreshPlaybackSources();

    private void RefreshPlaybackSources(AnimationClip? _ = null)
    {
        _refreshingPlaybackSources = true;
        try
        {
            foreach (var item in AnimationPlaybackSources) item.Refresh();
            foreach (var item in AvailablePlaybackSources.ToArray())
                if (!item.HasAnimation) AvailablePlaybackSources.Remove(item);
            var index = 0;
            foreach (var item in AnimationPlaybackSources.Where(item => item.HasAnimation))
            {
                if (!AvailablePlaybackSources.Contains(item)) AvailablePlaybackSources.Insert(index, item);
                index++;
            }
            HasPlaybackSources.Value = AvailablePlaybackSources.Count > 0;
            SelectedPlaybackSource = AvailablePlaybackSources.FirstOrDefault(item => item.Id == _preferredPlaybackSource)
                ?? AvailablePlaybackSources.FirstOrDefault();
            ApplyPlaybackSource();
        }
        finally { _refreshingPlaybackSources = false; }
    }

    private void ApplyPlaybackSource()
    {
        if (SelectedPlaybackSource == null) return;
        var item = Player.PlayerItems.Single(item => ReferenceEquals(item.Asset, SelectedPlaybackSource.Asset));
        if (!ReferenceEquals(Player.SelectedMainAnimation, item)) Player.SelectedMainAnimation = item;
    }

    private void ReleasePlaybackSources()
    {
        foreach (var item in AnimationPlaybackSources)
        {
            item.Asset.AnimationChanged -= RefreshPlaybackSources;
            item.Asset.SkeletonChanged -= PlaybackSkeletonChanged;
        }
    }
}

public sealed class MountPlaybackSource
{
    internal SceneObject Asset { get; }
    public string Id => Asset.Id;
    public string Name { get; }
    public bool HasAnimation => Asset.AnimationClip?.Timebase != null;
    public NotifyAttr<int> FrameCount { get; } = new();
    public NotifyAttr<double> DurationSeconds { get; } = new();
    public NotifyAttr<string> Summary { get; } = new("");

    internal MountPlaybackSource(SceneObject asset)
    {
        Asset = asset;
        Name = LocalizationManager.Instance.Get(asset.Id switch
        {
            "rider" => "MountAnim.Rider", "mount" => "MountAnim.Mount", _ => "MountAnim.Playback.Generated",
        });
    }

    internal void Refresh()
    {
        var timebase = Asset.AnimationClip?.Timebase;
        FrameCount.Value = timebase?.FrameCount ?? 0;
        DurationSeconds.Value = timebase?.Duration.TotalSeconds ?? 0;
        Summary.Value = timebase == null
            ? LocalizationManager.Instance.Get(Id == "mount-preview" ? "MountAnim.Playback.NotGenerated" : "MountAnim.Playback.NotLoaded")
            : LocalizationManager.Instance.GetFormat("MountAnim.Playback.Summary", FrameCount.Value, DurationSeconds.Value);
    }
}
