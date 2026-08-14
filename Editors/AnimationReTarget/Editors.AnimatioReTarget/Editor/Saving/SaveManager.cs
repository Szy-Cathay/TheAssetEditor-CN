using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.Settings;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using Shared.Ui.Common.OperationProgress;

namespace Editors.AnimatioReTarget.Editor.Saving
{


    public partial class SaveManager : ObservableObject
    {
        //private readonly ILogger _logger = Logging.Create<SaveViewModel>();

        private readonly ISkeletonAnimationLookUpHelper _skeletonAnimationLookUpHelper;
        private readonly IPackFileService _pfs;
        private readonly IFileSaveService _saveService;
        private readonly IStandardDialogs _standardDialogs;
        private readonly IAbstractFormFactory<SaveWindow> _settingsWindowFactory;
        private readonly BoneManager _boneManager;
        [ObservableProperty] SaveSettings _settings;

        SceneObject _generated;
        SceneObject _source;
        SceneObject _target;
        AnimationGenerationSettings _generationSettings;

        public SaveManager(BoneManager boneManager,
            ISkeletonAnimationLookUpHelper skeletonAnimationLookUpHelper, 
            IPackFileService pfs,
            SaveSettings saveSettings,
            IFileSaveService saveService,
            IStandardDialogs standardDialogs,
            IAbstractFormFactory<SaveWindow> settingsWindowFactory)
        {
            _boneManager = boneManager;
            _skeletonAnimationLookUpHelper = skeletonAnimationLookUpHelper;
            _pfs = pfs;
            _saveService = saveService;
            _standardDialogs = standardDialogs;
            _settingsWindowFactory = settingsWindowFactory;
            _settings = saveSettings;
        }

        public void SetSceneNodes(
            SceneObject source,
            SceneObject target,
            SceneObject generated,
            AnimationGenerationSettings generationSettings)
        {
            _generated = generated;
            _source = source;
            _target = target;
            _generationSettings = generationSettings;
        }

        public Task<BatchAnimationRetargetResult> ExecuteBatchAsync(
            BatchAnimationRetargetRequest request,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken) =>
            Task.Run(
                () => ExecuteBatch(request, progress, cancellationToken));

        private BatchAnimationRetargetResult ExecuteBatch(
            BatchAnimationRetargetRequest request,
            IProgress<OperationProgressUpdate>? progress,
            CancellationToken cancellationToken)
        {
            if (!CanBatchRetarget(out var validationError))
                throw new InvalidOperationException(validationError);

            var outputProject = GetEditableFolderProject()!;
            if (request.PathMode == BatchRetargetPathMode.SelectedFolder &&
                !IsCurrentFolderProjectDirectory(
                    outputProject,
                    request.TargetFolder))
            {
                throw new InvalidOperationException(
                    LocalizationManager.Instance.Get(
                        "AnimReTarget.Batch.Error.TargetFolderRequired"));
            }

            var plannedItems = new List<BatchPlanItem>(
                request.Animations.Count);
            foreach (var animation in request.Animations)
            {
                try
                {
                    plannedItems.Add(new BatchPlanItem(
                        animation,
                        BuildBatchOutputPath(
                            request,
                            animation.AnimationFile),
                        null));
                }
                catch (Exception exception)
                {
                    plannedItems.Add(new BatchPlanItem(
                        animation,
                        null,
                        exception.Message));
                }
            }

            var skippedIndexes = new HashSet<int>();
            var outputPaths = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < plannedItems.Count; index++)
            {
                var outputPath = plannedItems[index].OutputPath;
                if (outputPath == null)
                    continue;

                if (!outputPaths.Add(outputPath) ||
                    (!request.OverwriteExisting &&
                     outputProject.FileList.ContainsKey(
                         outputPath.ToLowerInvariant())))
                {
                    skippedIndexes.Add(index);
                }
            }

            var results = new List<BatchAnimationRetargetItemResult>();
            for (var index = 0; index < plannedItems.Count; index++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    for (var remainingIndex = index;
                         remainingIndex < plannedItems.Count;
                         remainingIndex++)
                    {
                        var remaining = plannedItems[remainingIndex];
                        results.Add(new BatchAnimationRetargetItemResult(
                            remaining.Animation.AnimationFile,
                            remaining.OutputPath,
                            BatchAnimationRetargetItemStatus.NotProcessed));
                    }
                    break;
                }

                var animation = plannedItems[index].Animation;
                var outputPath = plannedItems[index].OutputPath;
                if (plannedItems[index].ErrorMessage != null)
                {
                    results.Add(new BatchAnimationRetargetItemResult(
                        animation.AnimationFile,
                        outputPath,
                        BatchAnimationRetargetItemStatus.Failed,
                        plannedItems[index].ErrorMessage));
                    ReportProgress(progress, animation, index, plannedItems.Count);
                    continue;
                }

                if (skippedIndexes.Contains(index))
                {
                    results.Add(new BatchAnimationRetargetItemResult(
                        animation.AnimationFile,
                        outputPath,
                        BatchAnimationRetargetItemStatus.Skipped));
                    ReportProgress(progress, animation, index, plannedItems.Count);
                    continue;
                }

                try
                {
                    var content = RetargetAnimation(animation);
                    ApplyBatchWrite(outputProject, outputPath!, content);
                    results.Add(new BatchAnimationRetargetItemResult(
                        animation.AnimationFile,
                        outputPath!,
                        BatchAnimationRetargetItemStatus.Success));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    results.Add(new BatchAnimationRetargetItemResult(
                        animation.AnimationFile,
                        outputPath,
                        BatchAnimationRetargetItemStatus.Failed,
                        exception.Message));
                }

                ReportProgress(progress, animation, index, plannedItems.Count);
            }

            return new BatchAnimationRetargetResult(results);
        }

        private void ApplyBatchWrite(
            FolderProjectContainer outputProject,
            string outputPath,
            byte[] content)
        {
            void Write() => _pfs.ApplyFileWrites(
                outputProject,
                [new PackFileWrite(outputPath, content)]);

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                Write();
            else
                dispatcher.Invoke(Write);
        }

        public bool CanBatchRetarget(out string errorMessage)
        {
            if (_source?.Skeleton == null || _target?.Skeleton == null)
            {
                errorMessage = LocalizationManager.Instance.Get(
                    "AnimReTarget.Error.SkeletonSelectionRequired");
                return false;
            }

            if (!float.IsFinite(_generationSettings.AnimationSpeedMult) ||
                _generationSettings.AnimationSpeedMult <= 0)
            {
                errorMessage = LocalizationManager.Instance.Get(
                    "AnimReTarget.Error.InvalidSpeedMultiplier");
                return false;
            }

            if (!_boneManager.FlatBoneList.Any(bone =>
                    bone.HasMapping && bone.MappedIndex >= 0))
            {
                errorMessage = LocalizationManager.Instance.Get(
                    "AnimReTarget.Batch.Error.MappingRequired");
                return false;
            }

            if (_pfs.GetEditablePack() is not FolderProjectContainer)
            {
                errorMessage = LocalizationManager.Instance.Get(
                    "AnimReTarget.Batch.Error.FolderProjectRequired");
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        public FolderProjectContainer? GetEditableFolderProject() =>
            _pfs.GetEditablePack() as FolderProjectContainer;

        private static bool IsCurrentFolderProjectDirectory(
            FolderProjectContainer project,
            string? folder)
        {
            if (!FolderProjectPathPolicy.TryNormalizeResourceDirectoryPath(
                    folder,
                    out var normalized))
            {
                return false;
            }

            var prefix = normalized + "\\";
            return project.EmptyDirectories.Any(path =>
                       path.Equals(
                           normalized,
                           StringComparison.OrdinalIgnoreCase) ||
                       path.StartsWith(
                           prefix,
                           StringComparison.OrdinalIgnoreCase)) ||
                   project.FileList.Keys.Any(path =>
                       path.StartsWith(
                           prefix,
                           StringComparison.OrdinalIgnoreCase));
        }

        public IReadOnlyList<AnimationReference> GetBatchCandidates()
        {
            if (_source?.Skeleton == null)
                return [];

            return _skeletonAnimationLookUpHelper
                .GetAnimationsForSkeleton(_source.Skeleton.SkeletonName)
                .Where(animation => !IsSkeletonAnimationPath(
                    animation.AnimationFile))
                .OrderBy(
                    animation => animation.AnimationFile,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(
                    animation => animation.Container.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool IsTechAnimation(AnimationReference animation) =>
            PathContainsSegment(animation.AnimationFile, "tech");

        public static bool IsSkeletonAnimationPath(string path)
        {
            if (!path.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                return false;

            var segments = GetPathSegments(path);
            for (var index = 0; index < segments.Length - 1; index++)
            {
                if (segments[index].Equals(
                        "animations",
                        StringComparison.OrdinalIgnoreCase) &&
                    segments[index + 1].Equals(
                        "skeletons",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool PathContainsSegment(string path, string segment) =>
            GetPathSegments(path)
                .Contains(segment, StringComparer.OrdinalIgnoreCase);

        private static string[] GetPathSegments(string path) =>
            path.Replace('/', '\\')
                .Split('\\', StringSplitOptions.RemoveEmptyEntries);

        private static void ReportProgress(
            IProgress<OperationProgressUpdate>? progress,
            AnimationReference animation,
            int index,
            int total)
        {
            progress?.Report(new OperationProgressUpdate(
                LocalizationManager.Instance.Get("AnimReTarget.Batch.Progress"),
                animation.AnimationFile,
                index + 1,
                total));
        }

        private byte[] RetargetAnimation(AnimationReference animationReference)
        {
            var packFile = _pfs.FindFile(
                animationReference.AnimationFile,
                animationReference.Container) ??
                throw new FileNotFoundException(
                    LocalizationManager.Instance.GetFormat(
                        "AnimReTarget.Batch.Error.AnimationNotFound",
                        animationReference.AnimationFile),
                    animationReference.AnimationFile);
            var sourceAnimation = AnimationFile.Create(packFile);
            var sourceClip = new AnimationClip(sourceAnimation, _source.Skeleton);
            var remapper = new AnimationRemapperService(
                _generationSettings,
                _boneManager.Bones);
            var remappedClip = remapper.ReMapAnimation(
                _source.Skeleton,
                _target.Skeleton,
                sourceClip);
            var outputAnimation = remappedClip.ConvertToFileFormat(_target.Skeleton);
            if (Settings.AnimationFormat != 7)
            {
                var skeleton = _skeletonAnimationLookUpHelper
                    .GetSkeletonFileFromName(outputAnimation.Header.SkeletonName);
                outputAnimation.ConvertToVersion(
                    Settings.AnimationFormat,
                    skeleton,
                    _pfs);
            }

            return AnimationFile.ConvertToBytes(outputAnimation);
        }

        private string BuildBatchOutputPath(
            BatchAnimationRetargetRequest request,
            string sourcePath)
        {
            if (request.PathMode == BatchRetargetPathMode.SourcePath)
                return BuildSourcePathOutput(sourcePath);

            var sourceDirectory = Path.GetDirectoryName(sourcePath);
            var containingFolder = Path.GetFileName(sourceDirectory);
            if (string.IsNullOrWhiteSpace(containingFolder))
            {
                throw new InvalidDataException(
                    LocalizationManager.Instance.GetFormat(
                        "AnimReTarget.Batch.Error.SourceParentRequired",
                        sourcePath));
            }

            var fileName = Settings.SavePrefix + Path.GetFileName(sourcePath);
            return SaveUtility.EnsureEnding(
                Path.Combine(
                    request.TargetFolder!,
                    containingFolder,
                    fileName),
                ".anim");
        }

        private sealed record BatchPlanItem(
            AnimationReference Animation,
            string? OutputPath,
            string? ErrorMessage);

        private string BuildSourcePathOutput(string sourcePath)
        {
            var outputPath = sourcePath.Replace(
                _source.Skeleton.SkeletonName,
                _target.Skeleton.SkeletonName);
            var currentFileName = Path.GetFileName(outputPath);
            outputPath = outputPath.Replace(
                currentFileName,
                Settings.SavePrefix + currentFileName);
            return SaveUtility.EnsureEnding(outputPath, ".anim");
        }

        public void SaveAnimation(bool prompOnConflict = true)
        {
            if (_generated?.AnimationClip == null)
            {
                _standardDialogs.ShowDialogBox(
                    LocalizationManager.Instance.Get("AnimReTarget.Error.GeneratedAnimationRequired"));
                return;
            }

            var clip = _generated.AnimationClip;
           
            var animFile = clip.ConvertToFileFormat(_generated.Skeleton);

            if (Settings.AnimationFormat != 7)
            {
                var skeleton = _skeletonAnimationLookUpHelper.GetSkeletonFileFromName(animFile.Header.SkeletonName);
                animFile.ConvertToVersion(Settings.AnimationFormat, skeleton, _pfs);
            }

            var newPath = BuildSourcePathOutput(_source.AnimationName.Value);

            _saveService.Save(newPath, AnimationFile.ConvertToBytes(animFile), prompOnConflict);

        }
        [RelayCommand] void ShowSaveSettings()
        {
            var window = _settingsWindowFactory.Create();
            window.Initialize(this);
            window.ShowDialog();
        }

        /*
        public void OpenBatchProcessDialog()
        {
            if (!CanUpdateAnimation(false))
                return;

            // Find all animations for skeleton
            var copyFromAnims = _skeletonAnimationLookUpHelper.GetAnimationsForSkeleton(_copyFrom.Skeleton.SkeletonName);

            var items = copyFromAnims.Select(x => new SelectionListViewModel<SkeletonAnimationLookUpHelper.AnimationReference>.Item()
            {
                IsChecked = new NotifyAttr<bool>(!(x.AnimationFile.Contains("tech", StringComparison.InvariantCultureIgnoreCase) || x.AnimationFile.Contains("skeletons", StringComparison.InvariantCultureIgnoreCase))),
                DisplayName = x.AnimationFile,
                ItemValue = x
            }).ToList();

            var window = SelectionListWindow.ShowDialog("Select animations:", items);
            if (window.Result)
            {
                using (var waitCursor = new WaitCursor())
                {
                    var index = 1;
                    var numItemsToProcess = items.Count(x => x.IsChecked.Value);
                    if (numItemsToProcess > 50)
                    {
                        var confirm = MessageBox.Show("about to process 50 or more items! continue milord?", "sire...", MessageBoxButton.YesNo);
                        if (confirm != MessageBoxResult.Yes) return;
                    }
                    foreach (var item in items)
                    {
                        if (item.IsChecked.Value)
                        {
                            var file = _pfs.FindFile(item.ItemValue.AnimationFile, item.ItemValue.Container);
                            var animFile = AnimationFile.Create(file.DataSource.ReadDataAsChunk());
                            var clip = new AnimationClip(animFile, _copyFrom.Skeleton);

                            _logger.Here().Information($"Processing animation {index} / {numItemsToProcess} - {item.DisplayName}");

                            var updatedClip = UpdateAnimation(clip, null);
                            SaveAnimation(updatedClip, item.ItemValue.AnimationFile, false);
                            index++;
                        }

                    }
                }
            }
        }

        public void UpdateAnimation()
        {
            if (CanUpdateAnimation(true))
            {
                var newAnimationClip = UpdateAnimation(_copyFrom.AnimationClip, _copyTo.AnimationClip);
                _assetViewModelBuilder.SetAnimationClip(Generated, newAnimationClip, new SkeletonAnimationLookUpHelper.AnimationReference("Generated animation", null));
                _player.SelectedMainAnimation = _player.PlayerItems.First(x => x.Asset == Generated);
            }
        }

        AnimationClip UpdateAnimation(AnimationClip animationToCopy, AnimationClip originalAnimation)
        {
            var service = new AnimationRemapperService(AnimationSettings, _remappingInformation, Bones);
            var newClip = service.ReMapAnimation(_copyFrom.Skeleton, _copyTo.Skeleton, animationToCopy);
            return newClip;
        }

        bool CanUpdateAnimation(bool requireAnimation)
        {
            if (_remappingInformation == null)
            {
                MessageBox.Show("No mapping created?", "Error", MessageBoxButton.OK);
                return false;
            }

            if (_copyTo.Skeleton == null || _copyFrom.Skeleton == null)
            {
                MessageBox.Show("Missing a skeleton?", "Error", MessageBoxButton.OK);
                return false;
            }

            if (_copyFrom.AnimationClip == null && requireAnimation)
            {
                MessageBox.Show("No animation to copy selected", "Error", MessageBoxButton.OK);
                return false;
            }

            return true;
        }

        public void SaveAnimationAction()
        {
            if (Generated.AnimationClip == null || Generated.Skeleton == null || _copyFrom.Skeleton == null)
            {
                MessageBox.Show("Can not save, as no animation has been generated. Press the Apply button first", "Error", MessageBoxButton.OK);
                return;
            }

            SaveAnimation(Generated.AnimationClip, _copyFrom.AnimationName.Value.AnimationFile);
        }


        */

    }


}
