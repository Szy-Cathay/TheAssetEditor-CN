using Editors.AnimatioReTarget.Editor.BoneHandling;
using Shared.GameFormats.Animation;

namespace Test.AnimatioReTarget;

public class HumanoidBoneMapperTests
{
    [Test]
    public void CreateMappings_Wh3ToExternalRig_UsesRootMotionAndHipSemantics()
    {
        var source = CreateSkeleton(
            ("animroot", -1),
            ("root", 0),
            ("spine_0", 1),
            ("spine_1", 2),
            ("spine_2", 3),
            ("clav_left", 4),
            ("upperarm_left", 5),
            ("lowerarm_left", 6),
            ("hand_left", 7),
            ("upperleg_left", 1),
            ("lowerleg_left", 9),
            ("foot_left", 10),
            ("toe_left_0", 11),
            ("neck_0", 4),
            ("neck_1", 13),
            ("head_0", 14));
        var target = CreateSkeleton(
            ("root", -1),
            ("pelvis", 0),
            ("Bip", 1),
            ("spine_01", 2),
            ("spine_02", 3),
            ("spine_03", 4),
            ("clavicle_l", 5),
            ("upperarm_l", 6),
            ("lowerarm_l", 7),
            ("hand_l", 8),
            ("neck_01", 5),
            ("head", 10),
            ("unused_12", 2),
            ("unused_13", 2),
            ("unused_14", 2),
            ("unused_15", 2),
            ("unused_16", 2),
            ("unused_17", 2),
            ("unused_18", 2),
            ("unused_19", 2),
            ("thigh_l", 2),
            ("calf_l", 20),
            ("foot_l", 21),
            ("ball_l", 22));

        var result = HumanoidBoneMapper.CreateMappings(source, target);
        var mappings = result.Mappings.ToDictionary(
            mapping => mapping.TargetBoneIndex,
            mapping => mapping.SourceBoneIndex);

        Assert.Multiple(() =>
        {
            Assert.That(mappings[0], Is.EqualTo(0), "External root must receive WH3 root motion.");
            Assert.That(mappings.ContainsKey(1), Is.False, "The helper pelvis above Bip must stay unmapped.");
            Assert.That(mappings[2], Is.EqualTo(1), "Bip is the external rig's hips bone.");
            Assert.That(mappings[3], Is.EqualTo(2));
            Assert.That(mappings[5], Is.EqualTo(4));
            Assert.That(mappings[9], Is.EqualTo(8));
            Assert.That(mappings[10], Is.EqualTo(14), "A one-bone neck receives the end of the source neck chain.");
            Assert.That(mappings[20], Is.EqualTo(9));
            Assert.That(mappings[23], Is.EqualTo(12));
        });
    }

    [Test]
    public void CreateMappings_RealExportedSkeletonShape_MapsLegsBeyondSourceRange()
    {
        var source = CreatePaddedSkeleton(
            96,
            (0, "animroot", -1),
            (1, "root", 0),
            (8, "spine_0", 1),
            (9, "upperleg_left", 1),
            (10, "upperleg_right", 1),
            (11, "lowerleg_left", 9),
            (12, "lowerleg_right", 10),
            (13, "spine_1", 8),
            (16, "foot_left", 11),
            (17, "foot_right", 12),
            (18, "spine_2", 13),
            (19, "clav_left", 18),
            (20, "clav_right", 18),
            (21, "neck_0", 18),
            (22, "neck_1", 21),
            (23, "upperarm_left", 19),
            (24, "upperarm_right", 20),
            (25, "head_0", 22),
            (26, "lowerarm_left", 23),
            (27, "lowerarm_right", 24),
            (28, "upperarm_roll_left_0", 23),
            (29, "upperarm_roll_right_0", 24),
            (33, "hand_left", 26),
            (34, "hand_right", 27),
            (35, "lowerarm_roll_left_0", 26),
            (36, "lowerarm_roll_right_0", 27),
            (37, "toe_left_0", 16),
            (38, "toe_right_0", 17),
            (59, "jaw_0", 25));
        var target = CreatePaddedSkeleton(
            185,
            (0, "root", -1),
            (1, "pelvis", 0),
            (2, "Bip", 1),
            (3, "spine_01", 2),
            (4, "spine_02", 3),
            (5, "spine_03", 4),
            (6, "clavicle_l", 5),
            (7, "upperarm_l", 6),
            (8, "lowerarm_l", 7),
            (9, "hand_l", 8),
            (28, "lowerarm_twist_01_l", 8),
            (33, "upperarm_twist_01_l", 7),
            (34, "clavicle_r", 5),
            (35, "upperarm_r", 34),
            (36, "lowerarm_r", 35),
            (37, "hand_r", 36),
            (56, "lowerarm_twist_01_r", 36),
            (65, "upperarm_twist_01_r", 35),
            (66, "neck_01", 5),
            (67, "head", 66),
            (82, "jaw_01", 67),
            (106, "thigh_l", 2),
            (107, "calf_l", 106),
            (108, "foot_l", 107),
            (109, "ball_l", 108),
            (112, "thigh_r", 2),
            (113, "calf_r", 112),
            (114, "foot_r", 113),
            (115, "ball_r", 114));

        var result = HumanoidBoneMapper.CreateMappings(source, target);
        var mappings = result.Mappings.ToDictionary(
            mapping => mapping.TargetBoneIndex,
            mapping => mapping.SourceBoneIndex);

        Assert.Multiple(() =>
        {
            Assert.That(result.TargetBoneCount, Is.EqualTo(185));
            Assert.That(mappings[0], Is.EqualTo(0));
            Assert.That(mappings[2], Is.EqualTo(1));
            Assert.That(mappings.ContainsKey(1), Is.False);
            Assert.That(mappings[66], Is.EqualTo(22));
            Assert.That(mappings[106], Is.EqualTo(9));
            Assert.That(mappings[109], Is.EqualTo(37));
            Assert.That(mappings[112], Is.EqualTo(10));
            Assert.That(mappings[115], Is.EqualTo(38));
            Assert.That(mappings.ContainsKey(28), Is.False);
            Assert.That(mappings.ContainsKey(33), Is.False);
            Assert.That(mappings.ContainsKey(56), Is.False);
            Assert.That(mappings.ContainsKey(65), Is.False);
        });
    }

    [Test]
    public void CreateMappings_ThreeFingerWh3Rig_DrivesAllFiveExternalFingers()
    {
        var source = CreateSkeleton(
            ("root", -1),
            ("finger_ring_left_0", 0),
            ("finger_ring_left_1", 1),
            ("finger_ring_left_2", 2));
        var target = CreateSkeleton(
            ("root", -1),
            ("middle_01_l", 0),
            ("middle_02_l", 1),
            ("middle_03_l", 2),
            ("ring_01_l", 0),
            ("ring_02_l", 4),
            ("ring_03_l", 5),
            ("pinky_01_l", 0),
            ("pinky_02_l", 7),
            ("pinky_03_l", 8));

        var mappings = HumanoidBoneMapper.CreateMappings(source, target)
            .Mappings
            .ToDictionary(mapping => mapping.TargetBoneIndex, mapping => mapping.SourceBoneIndex);

        Assert.Multiple(() =>
        {
            Assert.That(mappings[1], Is.EqualTo(1));
            Assert.That(mappings[2], Is.EqualTo(2));
            Assert.That(mappings[3], Is.EqualTo(3));
            Assert.That(mappings[4], Is.EqualTo(1));
            Assert.That(mappings[5], Is.EqualTo(2));
            Assert.That(mappings[6], Is.EqualTo(3));
            Assert.That(mappings[7], Is.EqualTo(1));
            Assert.That(mappings[8], Is.EqualTo(2));
            Assert.That(mappings[9], Is.EqualTo(3));
        });
    }

    private static AnimationFile CreateSkeleton(
        params (string Name, int ParentId)[] bones)
    {
        return new AnimationFile
        {
            Bones = bones
                .Select((bone, index) => new AnimationFile.BoneInfo
                {
                    Id = index,
                    Name = bone.Name,
                    ParentId = bone.ParentId,
                })
                .ToArray(),
        };
    }

    private static AnimationFile CreatePaddedSkeleton(
        int boneCount,
        params (int Id, string Name, int ParentId)[] namedBones)
    {
        var definitions = namedBones.ToDictionary(bone => bone.Id);
        return new AnimationFile
        {
            Bones = Enumerable.Range(0, boneCount)
                .Select(index => definitions.TryGetValue(index, out var bone)
                    ? new AnimationFile.BoneInfo
                    {
                        Id = bone.Id,
                        Name = bone.Name,
                        ParentId = bone.ParentId,
                    }
                    : new AnimationFile.BoneInfo
                    {
                        Id = index,
                        Name = $"unused_{index}",
                        ParentId = -1,
                    })
                .ToArray(),
        };
    }
}
