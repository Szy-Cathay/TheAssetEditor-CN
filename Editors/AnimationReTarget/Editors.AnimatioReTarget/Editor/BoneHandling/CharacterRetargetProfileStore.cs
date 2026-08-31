using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;
using Shared.GameFormats.Animation;

namespace Editors.AnimatioReTarget.Editor.BoneHandling;

public sealed record CharacterRetargetBoneSettings(
    float BoneLengthMultiplier,
    double RotationOffsetX,
    double RotationOffsetY,
    double RotationOffsetZ,
    double TranslationOffsetX,
    double TranslationOffsetY,
    double TranslationOffsetZ,
    bool ForceSnapToWorld,
    bool FreezeTranslation,
    bool FreezeRotation,
    bool FreezeRotationZ,
    bool ApplyTranslation,
    bool ApplyRotation,
    int? RelativeTargetBoneIndex);

public enum CharacterRetargetProfileLoadStatus
{
    Loaded,
    NotFound,
    ReadFailed,
}

public sealed record CharacterRetargetProfileLoadResult(
    CharacterRetargetProfileLoadStatus Status,
    IReadOnlyDictionary<int, int> Mappings);

public sealed class CharacterRetargetProfileStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public CharacterRetargetProfileStore()
        : this(Path.Combine(
            DirectoryHelper.ApplicationDirectory,
            "animation-retarget-profiles.json"))
    {
    }

    private CharacterRetargetProfileStore(string filePath)
    {
        _filePath = filePath;
    }

    public static CharacterRetargetProfileStore CreateForFile(string filePath)
    {
        return new CharacterRetargetProfileStore(filePath);
    }

    public bool TryLoad(
        string sourceSkeletonName,
        string targetSkeletonName,
        string sourceSkeletonFingerprint,
        string targetSkeletonFingerprint,
        out IReadOnlyDictionary<int, int> mappings)
    {
        var result = Load(
            sourceSkeletonName,
            targetSkeletonName,
            sourceSkeletonFingerprint,
            targetSkeletonFingerprint);
        mappings = result.Mappings;
        return result.Status == CharacterRetargetProfileLoadStatus.Loaded;
    }

    public CharacterRetargetProfileLoadResult Load(
        string sourceSkeletonName,
        string targetSkeletonName,
        string sourceSkeletonFingerprint,
        string targetSkeletonFingerprint)
    {
        if (!TryReadDocument(out var document))
        {
            return new CharacterRetargetProfileLoadResult(
                CharacterRetargetProfileLoadStatus.ReadFailed,
                new Dictionary<int, int>());
        }

        var profile = document.Profiles.FirstOrDefault(item =>
            string.Equals(
                item.SourceSkeletonName,
                sourceSkeletonName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                item.TargetSkeletonName,
                targetSkeletonName,
                StringComparison.OrdinalIgnoreCase) &&
            item.SourceSkeletonFingerprint == sourceSkeletonFingerprint &&
            item.TargetSkeletonFingerprint == targetSkeletonFingerprint);
        return profile == null
            ? new CharacterRetargetProfileLoadResult(
                CharacterRetargetProfileLoadStatus.NotFound,
                new Dictionary<int, int>())
            : new CharacterRetargetProfileLoadResult(
                CharacterRetargetProfileLoadStatus.Loaded,
                new Dictionary<int, int>(profile.Mappings));
    }

    public bool TryLoadSettings(
        string sourceSkeletonName,
        string targetSkeletonName,
        string sourceSkeletonFingerprint,
        string targetSkeletonFingerprint,
        out IReadOnlyDictionary<int, CharacterRetargetBoneSettings> settings)
    {
        if (!TryReadDocument(out var document))
        {
            settings = new Dictionary<int, CharacterRetargetBoneSettings>();
            return false;
        }

        var profile = document.Profiles.FirstOrDefault(item =>
            string.Equals(
                item.SourceSkeletonName,
                sourceSkeletonName,
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                item.TargetSkeletonName,
                targetSkeletonName,
                StringComparison.OrdinalIgnoreCase) &&
            item.SourceSkeletonFingerprint == sourceSkeletonFingerprint &&
            item.TargetSkeletonFingerprint == targetSkeletonFingerprint);
        if (profile == null)
        {
            settings = new Dictionary<int, CharacterRetargetBoneSettings>();
            return false;
        }

        settings = new Dictionary<int, CharacterRetargetBoneSettings>(
            profile.BoneSettings);
        return true;
    }

    public bool Save(
        string sourceSkeletonName,
        string targetSkeletonName,
        string sourceSkeletonFingerprint,
        string targetSkeletonFingerprint,
        IReadOnlyDictionary<int, int> mappings,
        IReadOnlyDictionary<int, CharacterRetargetBoneSettings>? boneSettings = null)
    {
        string? temporaryPath = null;
        try
        {
            if (!TryReadDocument(out var document))
                return false;

            var existingProfile = document.Profiles.FirstOrDefault(item =>
                string.Equals(
                    item.SourceSkeletonName,
                    sourceSkeletonName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.TargetSkeletonName,
                    targetSkeletonName,
                    StringComparison.OrdinalIgnoreCase));
            document.Profiles.RemoveAll(item =>
                string.Equals(
                    item.SourceSkeletonName,
                    sourceSkeletonName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.TargetSkeletonName,
                    targetSkeletonName,
                    StringComparison.OrdinalIgnoreCase));
            document.Profiles.Add(new CharacterRetargetProfile
            {
                SourceSkeletonName = sourceSkeletonName,
                TargetSkeletonName = targetSkeletonName,
                SourceSkeletonFingerprint = sourceSkeletonFingerprint,
                TargetSkeletonFingerprint = targetSkeletonFingerprint,
                Mappings = new Dictionary<int, int>(mappings),
                BoneSettings = boneSettings == null
                    ? new Dictionary<int, CharacterRetargetBoneSettings>(
                        existingProfile?.BoneSettings ?? [])
                    : new Dictionary<int, CharacterRetargetBoneSettings>(
                        boneSettings),
            });

            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, SerializerOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
        finally
        {
            if (temporaryPath != null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception)
                    when (exception is IOException or
                          UnauthorizedAccessException or
                          SecurityException)
                {
                }
            }
        }
    }

    internal static string ComputeSkeletonFingerprint(AnimationFile skeleton)
    {
        var description = new StringBuilder();
        var bindSkeleton = GameSkeleton.CreateFromAnimationFile(
            skeleton,
            new AnimationPlayer());
        foreach (var item in skeleton.Bones
                     .Select((bone, index) => (Bone: bone, Index: index))
                     .OrderBy(item => item.Bone.Id))
        {
            var name = item.Bone.Name ?? "";
            var translation = bindSkeleton.Translation[item.Index];
            var rotation = CanonicalizeQuaternion(
                bindSkeleton.Rotation[item.Index]);
            description
                .Append(item.Bone.Id).Append(':')
                .Append(item.Bone.ParentId).Append(':')
                .Append(name.Length).Append(':')
                .Append(name).Append(':');
            AppendFloat(description, translation.X);
            AppendFloat(description, translation.Y);
            AppendFloat(description, translation.Z);
            AppendFloat(description, rotation.X);
            AppendFloat(description, rotation.Y);
            AppendFloat(description, rotation.Z);
            AppendFloat(description, rotation.W);
            description.Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(description.ToString())));
    }

    public static string ComputeSkeletonFingerprint(GameSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var description = new StringBuilder();
        for (var boneIndex = 0;
             boneIndex < skeleton.BoneCount;
             boneIndex++)
        {
            var name = skeleton.BoneNames[boneIndex] ?? "";
            var translation = skeleton.Translation[boneIndex];
            var rotation = CanonicalizeQuaternion(
                skeleton.Rotation[boneIndex]);
            description
                .Append(boneIndex).Append(':')
                .Append(skeleton.GetParentBoneIndex(boneIndex)).Append(':')
                .Append(name.Length).Append(':')
                .Append(name).Append(':');
            AppendFloat(description, translation.X);
            AppendFloat(description, translation.Y);
            AppendFloat(description, translation.Z);
            AppendFloat(description, rotation.X);
            AppendFloat(description, rotation.Y);
            AppendFloat(description, rotation.Z);
            AppendFloat(description, rotation.W);
            description.Append(';');
        }

        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(description.ToString())));
    }

    private static Quaternion CanonicalizeQuaternion(Quaternion value)
    {
        if (value.W < 0 ||
            (value.W == 0 && value.Z < 0) ||
            (value.W == 0 && value.Z == 0 && value.Y < 0) ||
            (value.W == 0 && value.Z == 0 && value.Y == 0 && value.X < 0))
        {
            return new Quaternion(-value.X, -value.Y, -value.Z, -value.W);
        }

        return value;
    }

    private static void AppendFloat(StringBuilder description, float value)
    {
        if (value == 0)
            value = 0;

        description
            .Append(BitConverter.SingleToInt32Bits(value).ToString("X8"))
            .Append(':');
    }

    private bool TryReadDocument(out CharacterRetargetProfileDocument document)
    {
        if (!File.Exists(_filePath))
        {
            document = new CharacterRetargetProfileDocument();
            return true;
        }

        try
        {
            var loadedDocument =
                JsonSerializer.Deserialize<CharacterRetargetProfileDocument>(
                    File.ReadAllText(_filePath),
                    SerializerOptions);
            if (loadedDocument == null)
            {
                document = new CharacterRetargetProfileDocument();
                return false;
            }

            document = loadedDocument;
            return true;
        }
        catch (JsonException)
        {
            document = new CharacterRetargetProfileDocument();
            return false;
        }
        catch (IOException)
        {
            document = new CharacterRetargetProfileDocument();
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            document = new CharacterRetargetProfileDocument();
            return false;
        }
        catch (ArgumentException)
        {
            document = new CharacterRetargetProfileDocument();
            return false;
        }
        catch (NotSupportedException)
        {
            document = new CharacterRetargetProfileDocument();
            return false;
        }
        catch (SecurityException)
        {
            document = new CharacterRetargetProfileDocument();
            return false;
        }
    }

    private sealed class CharacterRetargetProfileDocument
    {
        public List<CharacterRetargetProfile> Profiles { get; set; } = [];
    }

    private sealed class CharacterRetargetProfile
    {
        public string SourceSkeletonName { get; set; } = "";
        public string TargetSkeletonName { get; set; } = "";
        public string SourceSkeletonFingerprint { get; set; } = "";
        public string TargetSkeletonFingerprint { get; set; } = "";
        public Dictionary<int, int> Mappings { get; set; } = [];
        public Dictionary<int, CharacterRetargetBoneSettings> BoneSettings { get; set; } = [];
    }
}
