using CommunityToolkit.Mvvm.ComponentModel;

namespace Editors.AnimatioReTarget.Editor.Saving
{
    public partial class SaveSettings : ObservableObject
    {
        public IReadOnlyList<uint> PossibleAnimationFormats { get; } = [5, 6, 7];

        [ObservableProperty] string _savePrefix = "prefix_";
        [ObservableProperty] uint _animationFormat = 7;
        [ObservableProperty] bool _useGeneratedSkeleton = false;
        [ObservableProperty] string _scaledSkeletonName = "";
        [ObservableProperty] bool _batchUseSelectedFolder;
        [ObservableProperty] string _batchTargetFolder = "";
        [ObservableProperty] bool _batchOverwriteExisting;

        public bool BatchUseSourcePath
        {
            get => !BatchUseSelectedFolder;
            set
            {
                if (value)
                    BatchUseSelectedFolder = false;
            }
        }

        partial void OnBatchUseSelectedFolderChanged(bool value)
        {
            OnPropertyChanged(nameof(BatchUseSourcePath));
        }
    }
}
