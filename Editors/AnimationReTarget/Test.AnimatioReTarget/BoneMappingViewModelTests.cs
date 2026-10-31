using Shared.Ui.Editors.BoneMapping;
using Editors.AnimatioReTarget.Editor.BoneHandling;

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

    [TestCase(false)]
    [TestCase(true)]
    public void ClearingMapping_ClearsSourceSelectionAndHighlight_AndAppliesToTarget(bool includeChildren)
    {
        var highlighter = new RecordingBoneHighlighter();
        var childBone = new AnimatedBone(8, "target_child");
        childBone.MappedBoneIndex.Value = 3;
        childBone.MappedBoneName.Value = "source_child";
        var targetBone = new AnimatedBone(7, "target_bone");
        targetBone.Children.Add(childBone);
        var sourceBone = new AnimatedBone(2, "source_bone");
        var configuration = new RemappedAnimatedBoneConfiguration
        {
            MeshSkeletonName = "target",
            MeshBones = [targetBone],
            ParnetModelSkeletonName = "source",
            ParentModelBones = [sourceBone],
            SkeletonBoneHighlighter = highlighter,
        };
        var viewModel = new BoneMappingViewModel();
        viewModel.Initialize(configuration);
        viewModel.ParentModelBones.SelectedItem = sourceBone;

        if (includeChildren)
            viewModel.ClearBindingSelfAndChildren();
        else
            viewModel.ClearBindingSelf();

        var appliedTarget = new SkeletonBoneNode_new("target_bone", 7, -1)
        {
            HasMapping = true,
            MappedIndex = 2,
        };
        var appliedChild = new SkeletonBoneNode_new("target_child", 8, 7)
        {
            HasMapping = true,
            MappedIndex = 3,
        };
        appliedTarget.Children.Add(appliedChild);
        SkeletonBoneNodeHelper.ApplyMapping([appliedTarget], configuration);

        Assert.Multiple(() =>
        {
            Assert.That(targetBone.MappedBoneIndex.Value, Is.EqualTo(-1));
            Assert.That(targetBone.MappedBoneName.Value, Is.Empty);
            Assert.That(viewModel.MeshBones.SelectedItem, Is.SameAs(targetBone));
            Assert.That(viewModel.ParentModelBones.SelectedItem, Is.Null);
            Assert.That(highlighter.TargetBoneIndex, Is.EqualTo(7));
            Assert.That(highlighter.SourceBoneIndex, Is.EqualTo(-1));
            Assert.That(appliedTarget.HasMapping, Is.False);
            Assert.That(appliedTarget.MappedIndex, Is.EqualTo(-1));
            Assert.That(appliedChild.HasMapping, Is.EqualTo(!includeChildren));
            Assert.That(appliedChild.MappedIndex, Is.EqualTo(includeChildren ? -1 : 3));
        });

        viewModel.ParentModelBones.SelectedItem = sourceBone;
        Assert.Multiple(() =>
        {
            Assert.That(targetBone.MappedBoneIndex.Value, Is.EqualTo(2));
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
