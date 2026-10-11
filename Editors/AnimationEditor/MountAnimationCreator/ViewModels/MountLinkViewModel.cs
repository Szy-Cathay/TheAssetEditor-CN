using System.IO;
using AnimationEditor.MountAnimationCreator.Services;
using Editors.AnimationVisualEditors.MountAnimationCreator.Services;
using Editors.Shared.Core.Common;
using GameWorld.Core.Services;
using Shared.Core.Misc;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;
using Shared.Ui.Common;

namespace AnimationEditor.MountAnimationCreator.ViewModels
{
    public class MountLinkViewModel
    {
        public FilterCollection<IAnimationBinGenericFormat> AnimationSetForMount { get; }
        public FilterCollection<IAnimationBinGenericFormat> AnimationSetForRider { get; }
        public FilterCollection<AnimationBinEntryGenericFormat> SelectedMountTag { get; }
        public FilterCollection<AnimationBinEntryGenericFormat> SelectedRiderTag { get; }

        private readonly SceneObject _rider;
        private readonly SceneObject _mount;
        private readonly SceneObjectEditor _editor;
        private readonly IPackFileService _pfs;
        private readonly IStandardDialogs _dialogs;
        private readonly Action _validate;
        private readonly RiderAnimationService? _riderAnimations;
        private bool _reloading;

        public MountLinkViewModel(SceneObjectEditor editor, IPackFileService pfs,
            SceneObject rider, SceneObject mount, IStandardDialogs dialogs, Action validate, ISkeletonAnimationLookUpHelper? lookup = null)
        {
            _editor = editor;
            _pfs = pfs;
            _rider = rider;
            _mount = mount;
            _dialogs = dialogs;
            _validate = validate;
            _riderAnimations = lookup == null ? null : new RiderAnimationService(pfs, lookup);
            SelectedMountTag = new(null, MountTagSelected) { SearchFilter = (value, rx) => rx.IsMatch(value.DisplayName) };
            SelectedRiderTag = new(null, RiderTagSelected) { SearchFilter = (value, rx) => rx.IsMatch(value.DisplayName) };
            AnimationSetForMount = new(null, value => UpdateAnimationSet(value, SelectedMountTag))
                { SearchFilter = (value, rx) => rx.IsMatch(value.Name) || rx.IsMatch(value.FullPath) };
            AnimationSetForRider = new(null, value => UpdateAnimationSet(value, SelectedRiderTag))
                { SearchFilter = (value, rx) => rx.IsMatch(value.Name) || rx.IsMatch(value.FullPath) };
        }

        public void ReloadFragments(bool rider = true, bool mount = true)
        {
            _reloading = true;
            try
            {
                if (mount)
                    Reload(_mount, AnimationSetForMount, SelectedMountTag);
                if (rider)
                    Reload(_rider, AnimationSetForRider, SelectedRiderTag);
            }
            finally
            {
                _reloading = false;
            }
            _validate();
        }

        private void Reload(SceneObject scene, FilterCollection<IAnimationBinGenericFormat> tables, FilterCollection<AnimationBinEntryGenericFormat> slots)
        {
            var selectedTable = tables.SelectedItem?.FullPath;
            var selectedPack = tables.SelectedItem?.PackFileReference?.FileName;
            var selectedSlot = slots.SelectedItem;
            tables.SelectedItem = null;
            tables.UpdatePossibleValues(LoadAnimationSetForSkeleton(scene.Skeleton?.SkeletonName ?? "", includeOtherRiders: scene == _rider && _riderAnimations != null));
            tables.SelectedItem = tables.PossibleValues.FirstOrDefault(x => x.FullPath == selectedTable && x.PackFileReference?.FileName == selectedPack);
            slots.SelectedItem = slots.PossibleValues.FirstOrDefault(x => SameSlot(x, selectedSlot));
        }

        internal static bool SameSlot(AnimationBinEntryGenericFormat entry, AnimationBinEntryGenericFormat? other) =>
            other != null && entry.SlotIndex == other.SlotIndex && entry.Index == other.Index && entry.AnimationFile == other.AnimationFile &&
            entry.MetaFile == other.MetaFile && entry.SoundFile == other.SoundFile;

        public List<IAnimationBinGenericFormat> LoadAnimationSetForSkeleton(string skeletonName, bool onlyPacksThatCanBeSaved = false, bool includeOtherRiders = false)
        {
            if (string.IsNullOrWhiteSpace(skeletonName))
                return [];
            skeletonName = Path.GetFileNameWithoutExtension(skeletonName);
            var fragments = new List<IAnimationBinGenericFormat>();
            var project = _pfs.GetEditablePack();
            foreach (var file in PackFileServiceUtility.GetAllAnimPacks(_pfs))
            {
                if (onlyPacksThatCanBeSaved && (project == null || _pfs.GetPackFileContainer(file) != project))
                    continue;
                var database = AnimationPackSerializer.Load(file, _pfs);
                fragments.AddRange(database.Files.OfType<AnimationBinWh3>().Where(x => x.TableVersion == 4 &&
                    (x.SkeletonName.Equals(skeletonName, StringComparison.OrdinalIgnoreCase) ||
                     (includeOtherRiders && !string.IsNullOrEmpty(x.MountBin)))));
            }
            return fragments;
        }

        private void UpdateAnimationSet(IAnimationBinGenericFormat? value, FilterCollection<AnimationBinEntryGenericFormat> slots)
        {
            slots.SelectedItem = null;
            slots.UpdatePossibleValues(value?.Entries);
            _validate();
        }

        private void MountTagSelected(AnimationBinEntryGenericFormat? value)
        {
            if (_reloading)
                return;
            LoadSlot(_mount, value);
            if (value != null)
            {
                var matching = AnimationSlotTypeHelperWh3.GetMatchingRiderAnimation(value.SlotName);
                SelectedRiderTag.Filter = "";
                var candidates = SelectedRiderTag.PossibleValues.Where(x => x.SlotIndex == matching?.Id).ToList();
                var matchingSlot = candidates.FirstOrDefault(x => x.Index == value.Index) ?? candidates.FirstOrDefault();
                if (matchingSlot == null && matching != null && AnimationSetForRider.SelectedItem is AnimationBinWh3 table)
                {
                    var equivalent = BatchProcessorService.FindSameAction(table, matching.Value);
                    matchingSlot = SelectedRiderTag.PossibleValues.FirstOrDefault(x => x.SlotIndex == equivalent?.AnimationId);
                }
                SelectedRiderTag.SelectedItem = matchingSlot;
            }
            _validate();
        }

        private void RiderTagSelected(AnimationBinEntryGenericFormat? value)
        {
            if (_reloading)
                return;
            LoadSlot(_rider, value);
            _validate();
        }

        private void LoadSlot(SceneObject scene, AnimationBinEntryGenericFormat? slot)
        {
            try
            {
                var path = slot?.AnimationFile;
                if (!string.IsNullOrWhiteSpace(path) && _pfs.FindFile(path) == null)
                    throw new InvalidOperationException(LocalizationManager.Instance.GetFormat("MountAnim.MissingAnimation", path));
                if (scene == _rider && _riderAnimations != null && !string.IsNullOrWhiteSpace(path) && scene.Skeleton != null)
                    _editor.SetAnimationClip(scene, _riderAnimations.Load(path, scene.Skeleton), path);
                else
                    _editor.SetAnimation(scene, string.IsNullOrWhiteSpace(path) ? null! : path);
            }
            catch (Exception exception)
            {
                _editor.SetAnimation(scene, null!);
                _dialogs.ShowExceptionWindow(exception, LocalizationManager.Instance.Get("DisplayName.MountTool"));
            }
        }
    }
}
