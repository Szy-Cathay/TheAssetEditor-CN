using Shared.Ui.Editors.BoneMapping;

namespace Test.AnimatioReTarget;

public class BoneMappingViewModelTests
{
    [Test]
    public void SelectingMapping_HighlightsTargetBoneAndMappedSourceBone()
    {
        var highlighter = new RecordingBoneHighlighter();
        var targetBone = new AnimatedBone(7, "target_bone");
        var sourceBone = new AnimatedBone(2, "source_bone");
        var viewModel = new BoneMappingViewModel();
        viewModel.Initialize(new RemappedAnimatedBoneConfiguration
        {
            MeshSkeletonName = "target",
            MeshBones = [targetBone],
            ParnetModelSkeletonName = "source",
            ParentModelBones = [sourceBone],
            SkeletonBoneHighlighter = highlighter,
        });

        viewModel.ParentModelBones.SelectedItem = sourceBone;

        Assert.Multiple(() =>
        {
            Assert.That(highlighter.TargetBoneIndex, Is.EqualTo(7));
            Assert.That(highlighter.SourceBoneIndex, Is.EqualTo(2));
        });
    }

    private sealed class RecordingBoneHighlighter : ISkeletonBoneHighlighter
    {
        public int TargetBoneIndex { get; private set; } = -1;
        public int SourceBoneIndex { get; private set; } = -1;

        public void SelectTargetSkeletonBone(int index)
        {
            TargetBoneIndex = index;
        }

        public void SelectSourceSkeletonBone(int index)
        {
            SourceBoneIndex = index;
        }
    }
}
