using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameWorld.Core.Animation;
using Microsoft.Xna.Framework;
using Shared.Core.Misc;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed record AnimationWorkbenchBoneMaskStoreResult(
    bool Succeeded,
    AnimationWorkbenchBoneMask? Mask,
    IReadOnlyList<string> MaskNames,
    IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics);

public sealed class AnimationWorkbenchBoneMaskStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;

    public AnimationWorkbenchBoneMaskStore()
        : this(Path.Combine(
            DirectoryHelper.ApplicationDirectory,
            "animation-workbench-bone-masks.json"))
    {
    }

    private AnimationWorkbenchBoneMaskStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    public static AnimationWorkbenchBoneMaskStore CreateForFile(
        string filePath) => new(filePath);

    public AnimationWorkbenchBoneMaskStoreResult List(
        AnimationWorkbenchBoneHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        if (TryRead(out var document) == false)
            return Failure(AnimationWorkbenchDiagnosticCode.LayerMaskStoreReadFailed);

        var names = document.Masks
            .Where(item => MatchesSkeleton(item, hierarchy))
            .Select(item => item.Name)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return Success(null, names);
    }

    public AnimationWorkbenchBoneMaskStoreResult Load(
        string name,
        AnimationWorkbenchBoneHierarchy hierarchy)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        var normalizedName = NormalizeName(name);
        if (normalizedName == null)
            return Failure(AnimationWorkbenchDiagnosticCode.LayerMaskNameInvalid);
        if (TryRead(out var document) == false)
            return Failure(AnimationWorkbenchDiagnosticCode.LayerMaskStoreReadFailed);

        var mask = document.Masks.FirstOrDefault(item =>
            string.Equals(item.Name, normalizedName, StringComparison.Ordinal) &&
            MatchesSkeleton(item, hierarchy));
        if (mask != null)
        {
            return Success(
                new AnimationWorkbenchBoneMask(
                    hierarchy.SkeletonName,
                    mask.BoneNames),
                document.Masks
                    .Where(item => MatchesSkeleton(item, hierarchy))
                    .Select(item => item.Name)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray());
        }

        var sameNamedSkeleton = document.Masks.Any(item =>
            string.Equals(item.Name, normalizedName, StringComparison.Ordinal) &&
            string.Equals(
                item.SkeletonName,
                hierarchy.SkeletonName,
                StringComparison.Ordinal));
        return Failure(sameNamedSkeleton
            ? AnimationWorkbenchDiagnosticCode.LayerMaskSavedSkeletonMismatch
            : AnimationWorkbenchDiagnosticCode.LayerMaskSavedNotFound);
    }

    public AnimationWorkbenchBoneMaskStoreResult Save(
        string name,
        AnimationWorkbenchBoneHierarchy hierarchy,
        AnimationWorkbenchBoneMask mask)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(mask);
        var normalizedName = NormalizeName(name);
        if (normalizedName == null)
            return Failure(AnimationWorkbenchDiagnosticCode.LayerMaskNameInvalid);
        if (string.Equals(
                mask.SkeletonName,
                hierarchy.SkeletonName,
                StringComparison.Ordinal) == false)
        {
            return Failure(
                AnimationWorkbenchDiagnosticCode.LayerMaskSkeletonMismatch);
        }
        if (mask.BoneNames.Count == 0)
            return Failure(AnimationWorkbenchDiagnosticCode.LayerMaskEmpty);

        var knownNames = hierarchy.Bones
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.Ordinal);
        var seenNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var boneName in mask.BoneNames)
        {
            if (seenNames.Add(boneName) == false ||
                knownNames.TryGetValue(boneName, out var count) && count > 1)
            {
                return Failure(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneDuplicate,
                    boneName);
            }
            if (knownNames.ContainsKey(boneName) == false)
            {
                return Failure(
                    AnimationWorkbenchDiagnosticCode.LayerMaskBoneMissing,
                    boneName);
            }
        }

        if (TryRead(out var document) == false)
            return Failure(AnimationWorkbenchDiagnosticCode.LayerMaskStoreReadFailed);

        document.Masks.RemoveAll(item =>
            string.Equals(item.Name, normalizedName, StringComparison.Ordinal) &&
            string.Equals(
                item.SkeletonName,
                hierarchy.SkeletonName,
                StringComparison.Ordinal));
        document.Masks.Add(new StoredMask
        {
            Name = normalizedName,
            SkeletonName = hierarchy.SkeletonName,
            SkeletonFingerprint = hierarchy.Fingerprint,
            BoneNames = mask.BoneNames.ToList(),
        });
        if (TryWrite(document) == false)
            return Failure(AnimationWorkbenchDiagnosticCode.LayerMaskStoreWriteFailed);

        return List(hierarchy);
    }

    internal static string ComputeSkeletonFingerprint(GameSkeleton skeleton)
    {
        ArgumentNullException.ThrowIfNull(skeleton);
        var description = new StringBuilder();
        for (var index = 0; index < skeleton.BoneCount; index++)
        {
            var name = skeleton.BoneNames[index] ?? "";
            var rotation = CanonicalizeQuaternion(skeleton.Rotation[index]);
            description
                .Append(index).Append(':')
                .Append(skeleton.GetParentBoneIndex(index)).Append(':')
                .Append(name.Length).Append(':')
                .Append(name).Append(':');
            AppendFloat(description, skeleton.Translation[index].X);
            AppendFloat(description, skeleton.Translation[index].Y);
            AppendFloat(description, skeleton.Translation[index].Z);
            AppendFloat(description, rotation.X);
            AppendFloat(description, rotation.Y);
            AppendFloat(description, rotation.Z);
            AppendFloat(description, rotation.W);
            AppendFloat(description, skeleton.Scale[index]);
            description.Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(description.ToString())));
    }

    private bool TryRead(out StoreDocument document)
    {
        try
        {
            _ = Path.GetFullPath(_filePath);
            if (File.Exists(_filePath) == false)
            {
                document = new StoreDocument();
                return true;
            }

            var loaded = JsonSerializer.Deserialize<StoreDocument>(
                File.ReadAllText(_filePath),
                SerializerOptions);
            if (loaded == null ||
                loaded.Version != CurrentVersion ||
                loaded.Masks == null ||
                loaded.Masks.Any(item =>
                    item == null ||
                    item.Name == null ||
                    item.SkeletonName == null ||
                    item.SkeletonFingerprint == null ||
                    item.BoneNames == null ||
                    item.BoneNames.Any(name => name == null)))
            {
                document = new StoreDocument();
                return false;
            }
            document = loaded;
            return true;
        }
        catch (Exception exception)
            when (IsPathOrAccessException(exception) ||
                  exception is JsonException)
        {
            document = new StoreDocument();
            return false;
        }
    }

    private bool TryWrite(StoreDocument document)
    {
        string? temporaryPath = null;
        try
        {
            _ = Path.GetFullPath(_filePath);
            var directory = Path.GetDirectoryName(_filePath);
            if (string.IsNullOrEmpty(directory) == false)
                Directory.CreateDirectory(directory);
            temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(document, SerializerOptions));
            File.Move(temporaryPath, _filePath, overwrite: true);
            return true;
        }
        catch (Exception exception)
            when (IsPathOrAccessException(exception))
        {
            return false;
        }
        finally
        {
            if (temporaryPath != null)
                TryDeleteTemporaryFile(temporaryPath);
        }
    }

    private static bool IsPathOrAccessException(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        ArgumentException or
        NotSupportedException or
        SecurityException;

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception)
            when (IsPathOrAccessException(exception))
        {
        }
    }

    private static bool MatchesSkeleton(
        StoredMask mask,
        AnimationWorkbenchBoneHierarchy hierarchy) =>
        string.Equals(
            mask.SkeletonName,
            hierarchy.SkeletonName,
            StringComparison.Ordinal) &&
        string.Equals(
            mask.SkeletonFingerprint,
            hierarchy.Fingerprint,
            StringComparison.Ordinal);

    private static string? NormalizeName(string? name)
    {
        var normalized = name?.Trim();
        return string.IsNullOrWhiteSpace(normalized) || normalized.Length > 80
            ? null
            : normalized;
    }

    private static AnimationWorkbenchBoneMaskStoreResult Success(
        AnimationWorkbenchBoneMask? mask,
        IReadOnlyList<string> names) => new(
        true,
        mask,
        names,
        Array.Empty<AnimationWorkbenchDiagnostic>());

    private static AnimationWorkbenchBoneMaskStoreResult Failure(
        AnimationWorkbenchDiagnosticCode code,
        string? boneName = null) => new(
        false,
        null,
        [],
        [new AnimationWorkbenchDiagnostic(
            code,
            AnimationWorkbenchDiagnosticSeverity.Error,
            BoneName: boneName)]);

    private static Quaternion CanonicalizeQuaternion(Quaternion value)
    {
        if (value.W < 0 ||
            value.W == 0 && value.Z < 0 ||
            value.W == 0 && value.Z == 0 && value.Y < 0 ||
            value.W == 0 && value.Z == 0 && value.Y == 0 && value.X < 0)
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

    private sealed class StoreDocument
    {
        public int Version { get; set; } = CurrentVersion;

        public List<StoredMask> Masks { get; set; } = [];
    }

    private sealed class StoredMask
    {
        public string Name { get; set; } = "";

        public string SkeletonName { get; set; } = "";

        public string SkeletonFingerprint { get; set; } = "";

        public List<string> BoneNames { get; set; } = [];
    }
}
