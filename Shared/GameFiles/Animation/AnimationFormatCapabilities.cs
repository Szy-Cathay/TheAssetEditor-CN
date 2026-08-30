namespace Shared.GameFormats.Animation
{
    public enum AnimationFormatBlockReason
    {
        UnsupportedVersion,
        VersionEightIsReadOnly,
        MultiplePartsAreReadOnly,
    }

    public sealed class AnimationFormatCapabilities
    {
        private AnimationFormatCapabilities(
            bool canRead,
            bool canEdit,
            bool canSave,
            IReadOnlyList<AnimationFormatBlockReason> blockingReasons)
        {
            CanRead = canRead;
            CanEdit = canEdit;
            CanSave = canSave;
            BlockingReasons = Array.AsReadOnly(blockingReasons.ToArray());
        }

        public bool CanRead { get; }

        public bool CanEdit { get; }

        public bool CanSave { get; }

        public IReadOnlyList<AnimationFormatBlockReason> BlockingReasons { get; }

        public static AnimationFormatCapabilities Evaluate(uint version, int partCount)
        {
            if (version is < 4 or > 8)
            {
                return new AnimationFormatCapabilities(
                    canRead: false,
                    canEdit: false,
                    canSave: false,
                    [AnimationFormatBlockReason.UnsupportedVersion]);
            }

            var blockingReasons = new List<AnimationFormatBlockReason>();
            if (version == 8)
                blockingReasons.Add(AnimationFormatBlockReason.VersionEightIsReadOnly);
            if (partCount != 1)
                blockingReasons.Add(AnimationFormatBlockReason.MultiplePartsAreReadOnly);

            var canEditAndSave = blockingReasons.Count == 0;
            return new AnimationFormatCapabilities(
                canRead: true,
                canEdit: canEditAndSave,
                canSave: canEditAndSave,
                blockingReasons);
        }
    }
}
