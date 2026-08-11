namespace Editors.AnimatioReTarget.Editor.BoneHandling
{
    public sealed record BoneMappingReviewCandidate(
        int TargetBoneIndex,
        int SourceBoneIndex,
        string SourceBoneName,
        string DisplayText);

    public sealed record BoneMappingReviewItem(
        int TargetBoneIndex,
        string TargetBoneName,
        BoneAutoMappingStatus Status,
        string StatusText,
        string ReasonText,
        bool CanMarkIntentionalUnmapped,
        IReadOnlyList<BoneMappingReviewCandidate> Candidates);
}
