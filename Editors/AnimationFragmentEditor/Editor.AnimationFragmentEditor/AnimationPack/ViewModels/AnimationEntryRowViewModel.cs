using System.IO;
using Shared.Core.Misc;

namespace Editors.AnimationFragmentEditor.AnimationPack.ViewModels
{
    public class AnimationEntryRowViewModel : NotifyPropertyChangedImpl
    {
        private int _slotIndex;
        private string _slotName = string.Empty;
        private string _animationFile = string.Empty;
        private string _metaFile = string.Empty;
        private string _soundFile = string.Empty;
        private float _blendInTime;
        private float _selectionWeight = 1.0f;
        private bool _unk;
        private int _variantIndex;

        // WeaponBone as individual bools for checkbox binding
        private bool _wb0, _wb1, _wb2, _wb3, _wb4, _wb5;

        public int SlotIndex { get => _slotIndex; set => SetAndNotify(ref _slotIndex, value); }
        public string SlotName { get => _slotName; set => SetAndNotify(ref _slotName, value); }
        public string AnimationFile
        {
            get => _animationFile;
            set
            {
                if (_animationFile == value)
                    return;
                SetAndNotify(ref _animationFile, value);
                NotifyPropertyChanged(nameof(AnimationFileName));
            }
        }
        public string MetaFile
        {
            get => _metaFile;
            set
            {
                if (_metaFile == value)
                    return;
                SetAndNotify(ref _metaFile, value);
                NotifyPropertyChanged(nameof(MetaFileName));
            }
        }
        public string SoundFile
        {
            get => _soundFile;
            set
            {
                if (_soundFile == value)
                    return;
                SetAndNotify(ref _soundFile, value);
                NotifyPropertyChanged(nameof(SoundFileName));
            }
        }
        public string AnimationFileName => GetFileName(AnimationFile);
        public string MetaFileName => GetFileName(MetaFile);
        public string SoundFileName => GetFileName(SoundFile);
        public float BlendInTime { get => _blendInTime; set => SetAndNotify(ref _blendInTime, value); }
        public float SelectionWeight { get => _selectionWeight; set => SetAndNotify(ref _selectionWeight, value); }
        public bool Unk { get => _unk; set => SetAndNotify(ref _unk, value); }
        public int VariantIndex { get => _variantIndex; set => SetAndNotify(ref _variantIndex, value); }

        // Individual WeaponBone flags (bit 0-5)
        public bool Wb0 { get => _wb0; set => SetAndNotify(ref _wb0, value); }
        public bool Wb1 { get => _wb1; set => SetAndNotify(ref _wb1, value); }
        public bool Wb2 { get => _wb2; set => SetAndNotify(ref _wb2, value); }
        public bool Wb3 { get => _wb3; set => SetAndNotify(ref _wb3, value); }
        public bool Wb4 { get => _wb4; set => SetAndNotify(ref _wb4, value); }
        public bool Wb5 { get => _wb5; set => SetAndNotify(ref _wb5, value); }

        // Comma-separated string for XmlFormat compatibility
        public string WeaponBone => $"{Wb0}, {Wb1}, {Wb2}, {Wb3}, {Wb4}, {Wb5}";

        public string DisplayName => VariantIndex > 0 ? $"[{VariantIndex}] {SlotName}" : SlotName;

        private static string GetFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalizedPath = path
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            return Path.GetFileName(normalizedPath);
        }

        public void SetWeaponBoneFromInt(int value)
        {
            Wb0 = (value & 1) != 0;
            Wb1 = (value & 2) != 0;
            Wb2 = (value & 4) != 0;
            Wb3 = (value & 8) != 0;
            Wb4 = (value & 16) != 0;
            Wb5 = (value & 32) != 0;
        }

        public int GetWeaponBoneAsInt()
        {
            int result = 0;
            if (Wb0) result |= 1;
            if (Wb1) result |= 2;
            if (Wb2) result |= 4;
            if (Wb3) result |= 8;
            if (Wb4) result |= 16;
            if (Wb5) result |= 32;
            return result;
        }

        public AnimationEntryRowViewModel Clone()
        {
            return new AnimationEntryRowViewModel
            {
                SlotIndex = SlotIndex,
                SlotName = SlotName,
                AnimationFile = AnimationFile,
                MetaFile = MetaFile,
                SoundFile = SoundFile,
                BlendInTime = BlendInTime,
                SelectionWeight = SelectionWeight,
                Unk = Unk,
                VariantIndex = VariantIndex,
                Wb0 = Wb0, Wb1 = Wb1, Wb2 = Wb2,
                Wb3 = Wb3, Wb4 = Wb4, Wb5 = Wb5,
            };
        }
    }
}
