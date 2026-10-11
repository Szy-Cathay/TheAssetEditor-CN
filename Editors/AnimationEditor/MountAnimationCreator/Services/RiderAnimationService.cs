using Editors.AnimatioReTarget.Editor;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Editors.AnimatioReTarget.Editor.Settings;
using GameWorld.Core.Animation;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.Animation;

namespace AnimationEditor.MountAnimationCreator.Services;

internal sealed class RiderAnimationService(IPackFileService pfs, ISkeletonAnimationLookUpHelper lookup)
{
    public AnimationClip Load(string path, GameSkeleton target)
    {
        if (path.Replace('\\', '/').Split('/').Contains("tech", StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(LocalizationManager.Instance.Get("MountAnim.TechIsNotAction"));
        var file = pfs.FindFile(path) ?? throw new InvalidOperationException(
            LocalizationManager.Instance.GetFormat("MountAnim.MissingAnimation", path));
        var animation = AnimationFile.Create(file);
        if (string.Equals(animation.Header.SkeletonName, target.SkeletonName, StringComparison.OrdinalIgnoreCase))
            return new AnimationClip(animation, target);

        var source = new GameSkeleton(lookup.GetSkeletonFileFromName(animation.Header.SkeletonName), new AnimationPlayer());
        var sourceClip = new AnimationClip(animation, source);
        var bones = SkeletonBoneNodeHelper.Build(AnimationClip.CreateSkeletonAnimation(target).ConvertToFileFormat(target));
        var flat = Flatten(bones).ToArray();
        foreach (var bone in flat)
        {
            bone.MappedIndex = source.GetBoneIndexByName(bone.BoneName);
            bone.HasMapping = bone.MappedIndex >= 0;
        }
        if (!flat.Any(x => x.BoneName == "root" && x.HasMapping))
            throw new InvalidOperationException(LocalizationManager.Instance.Get("Msg.FragmentSkeletonMismatch"));

        // Props are siblings of root, so limb length correction cannot move them with the hands.
        var anchors = flat.Where(x => x.HasMapping && x.BoneName is "hand_left" or "hand_right" or "spine_2" or "root").ToArray();
        var pose = AnimationSampler.Sample(0, 0, source, sourceClip);
        foreach (var prop in flat.Where(x => x.HasMapping && x.BoneName.StartsWith("be_prop_", StringComparison.Ordinal)))
        {
            var position = pose.GetSkeletonAnimatedWorld(source, prop.MappedIndex).Translation;
            prop.SelectedRelativeBone = anchors.MinBy(x => Vector3.DistanceSquared(position,
                pose.GetSkeletonAnimatedWorld(source, x.MappedIndex).Translation));
        }
        var result = new AnimationRemapperService(new AnimationGenerationSettings(), bones)
            .ReMapAnimation(source, target, sourceClip);

        // Expanded hands retain their own proportions while sharing the neighbouring finger's curl.
        foreach (var bone in flat.Where(x => !x.HasMapping &&
            (x.BoneName.StartsWith("finger_middle_", StringComparison.Ordinal) || x.BoneName.StartsWith("finger_pinky_", StringComparison.Ordinal))))
        {
            var neighbour = source.GetBoneIndexByName(bone.BoneName.Replace("finger_middle_", "finger_ring_", StringComparison.Ordinal)
                .Replace("finger_pinky_", "finger_ring_", StringComparison.Ordinal));
            if (neighbour < 0)
                continue;
            for (var frame = 0; frame < result.DynamicFrames.Count; frame++)
                result.DynamicFrames[frame].Rotation[bone.BoneIndex] = Quaternion.Normalize(target.Rotation[bone.BoneIndex] *
                    Quaternion.Inverse(source.Rotation[neighbour]) * sourceClip.DynamicFrames[frame].Rotation[neighbour]);
        }
        return result;
    }

    private static IEnumerable<SkeletonBoneNode_new> Flatten(IEnumerable<SkeletonBoneNode_new> bones)
    {
        foreach (var bone in bones)
        {
            yield return bone;
            foreach (var child in Flatten(bone.Children))
                yield return child;
        }
    }
}
