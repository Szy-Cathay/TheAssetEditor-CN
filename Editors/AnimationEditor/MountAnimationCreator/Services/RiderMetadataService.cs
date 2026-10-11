using GameWorld.Core.Animation;
using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace AnimationEditor.MountAnimationCreator.Services;

internal sealed class RiderMetadataService(IPackFileService pfs, GameSkeleton source, GameSkeleton target,
    Func<string, string> convertAnimation, Func<string, string> reservePath, List<PackFileWrite> writes, ErrorList results)
{
    private readonly MetaDataFileParser _parser = new(new MetaDataDatabase());
    private readonly Dictionary<(string Path, float Scale), string> _converted = [];
    private readonly bool _differentRig = !string.Equals(source.SkeletonName, target.SkeletonName, StringComparison.OrdinalIgnoreCase);

    public string Convert(string path, float timeScale = 1)
    {
        if (string.IsNullOrWhiteSpace(path) || (!_differentRig && Math.Abs(timeScale - 1) < 0.00001f))
            return path;
        var key = (path, timeScale);
        if (_converted.TryGetValue(key, out var existing))
            return existing;
        var file = pfs.FindFile(path);
        if (file == null && path.EndsWith(".snd.meta", StringComparison.OrdinalIgnoreCase))
        {
            results.Warning(path, LocalizationManager.Instance.Get("MountAnim.MissingSoundMetadata"));
            _converted.Add(key, path);
            return path;
        }
        if (file == null)
            throw new InvalidOperationException(LocalizationManager.Instance.GetFormat("MountAnim.MissingAnimation", path));
        var metadata = _parser.ParseFile(file)!;
        foreach (var tag in metadata.Attributes.ToArray())
        {
            if (_differentRig)
            {
                try { Remap(tag); }
                catch (MissingBoneException exception)
                {
                    // A donor cape or armour part absent from the target must not animate an unrelated bone.
                    metadata.Attributes.Remove(tag);
                    results.Warning(path, LocalizationManager.Instance.GetFormat("MountAnim.UnmappedMetadataBone", tag.Name, exception.Message));
                    continue;
                }
            }
            if (ParsedMetadataTimeRange.TryCreate(tag, out var range))
                ParsedMetadataTimeRange.Set(tag, range.StartTime * timeScale, range.EndTime * timeScale);
            switch (tag)
            {
                case Transform_v10 transform:
                    transform.BlendInTime *= timeScale;
                    transform.BlendOutTime *= timeScale;
                    break;
                case DockEquipment dock:
                    dock.BlendInTime *= timeScale;
                    dock.BlendOutTime *= timeScale;
                    break;
                case Splice_v11 splice:
                    splice.BlendInTime *= timeScale;
                    splice.BlendOutTime *= timeScale;
                    break;
                case Splice_v10 splice: splice.BlendInTime *= timeScale; break;
                case Splice_v6 splice: splice.BlendInTime *= timeScale; break;
            }
        }
        var output = reservePath(path);
        writes.Add(new PackFileWrite(output, _parser.GenerateBytes(metadata.Version, metadata)));
        _converted.Add(key, output);
        return output;
    }

    private void Remap(ParsedMetadataAttribute tag)
    {
        switch (tag)
        {
            case Transform_v10 transform:
                transform.TargetNode = MapBone(transform.TargetNode);
                transform.SourceNode = MapBone(transform.SourceNode);
                break;
            case IEffectMeta effect: effect.NodeIndex = MapBone(effect.NodeIndex); break;
            case Blood_v12 blood: blood.NodeIndex = MapBone(blood.NodeIndex); break;
            case Blood_v11 blood: blood.NodeIndex = MapBone(blood.NodeIndex); break;
            case Blood_v5 blood: blood.NodeIndex = MapBone(blood.NodeIndex); break;
            case Dismember_v10 dismember: dismember.BoneIndex = MapBone(dismember.BoneIndex); break;
            case Dismember_v2 dismember: dismember.BoneIndex = MapBone(dismember.BoneIndex); break;
            case SoundTrigger_v10 sound: sound.BoneIndex = MapBone(sound.BoneIndex); break;
            case SoundTrigger_v4 sound: sound.BoneIndex = MapBone(sound.BoneIndex); break;
            case Splice_v11 splice:
                splice.GenericBoneIndex = MapBone(splice.GenericBoneIndex);
                splice.Animation = ConvertReference(splice.Animation);
                splice.ReferenceAnimation = ConvertReference(splice.ReferenceAnimation);
                break;
            case Splice_v10 splice:
                splice.GenericBoneIndex = MapBone(splice.GenericBoneIndex);
                splice.Animation = ConvertReference(splice.Animation);
                splice.ReferenceAnimation = ConvertReference(splice.ReferenceAnimation);
                break;
            case Splice_v6 splice:
                splice.GenericBoneIndex = MapBone(splice.GenericBoneIndex);
                splice.Animation = ConvertReference(splice.Animation);
                splice.ReferenceAnimation = ConvertReference(splice.ReferenceAnimation);
                break;
            case Splice_v5 splice:
                splice.GenericBoneIndex = MapBone(splice.GenericBoneIndex);
                splice.Animation = ConvertReference(splice.Animation);
                splice.ReferenceAnimation = ConvertReference(splice.ReferenceAnimation);
                break;
            case Splice_v3 splice:
                splice.Animation = ConvertReference(splice.Animation);
                splice.ReferenceAnimation = ConvertReference(splice.ReferenceAnimation);
                break;
            case Splice_v2 splice: splice.Animation = ConvertReference(splice.Animation); break;
            case Splice_v0 splice: splice.Animation = ConvertReference(splice.Animation); break;
            case ParsedUnknownMetadataAttribute unknown when unknown.Name is "TRANSFORM" or "SPLICE" or "EFFECT" or "BLOOD" or "DISMEMBER" or "SOUND_TRIGGER":
                throw new InvalidOperationException(LocalizationManager.Instance.GetFormat("MountAnim.UnsupportedMetadata", unknown.DisplayName));
        }
    }

    private int MapBone(int index)
    {
        if (index < 0)
            return index;
        if (index >= source.BoneCount)
            throw new InvalidOperationException(LocalizationManager.Instance.Get("Msg.FragmentSkeletonMismatch"));
        var name = source.BoneNames[index];
        var mapped = target.GetBoneIndexByName(name);
        return mapped >= 0 ? mapped : throw new MissingBoneException(name);
    }

    private string ConvertReference(string path) => string.IsNullOrWhiteSpace(path) ? path : convertAnimation(path);
    private sealed class MissingBoneException(string bone) : Exception(bone);
}
