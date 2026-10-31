using System.Globalization;
using System.Text.Json;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Shared.GameFormats.AnimationPack;

// A source JSON fragment is previewable, but is not a writable animpack member.
public sealed class ThreeKingdomsAnimationFragment : IAnimationBinGenericFormat
{
    private const string ResourceDirectory = "animations/database/battle/fragments/";
    private const int MaximumSourceSize = 8 * 1024 * 1024;

    public AnimationPackFileDatabase PackFileReference { get; }
    public string Name { get; }
    public string FullPath { get; }
    public string FileName => Path.GetFileName(FullPath);
    public string SkeletonName { get; }
    public List<AnimationBinEntryGenericFormat> Entries { get; } = [];
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    private readonly List<string> _diagnostics = [];

    private ThreeKingdomsAnimationFragment(string path, string name, string skeletonName)
    {
        FullPath = path;
        Name = name;
        SkeletonName = skeletonName;
        PackFileReference = new AnimationPackFileDatabase(path);
    }

    public static bool IsResourcePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith(ResourceDirectory, StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = normalized[ResourceDirectory.Length..];
        return name.Length > 4 && !name.Contains('/')
            && !name.Any(character => char.IsControl(character) || ":*?\"<>|".Contains(character));
    }

    public static ThreeKingdomsAnimationFragment Read(string path, byte[] bytes)
    {
        if (!IsResourcePath(path) || bytes.Length > MaximumSourceSize)
            throw new InvalidDataException("Invalid Three Kingdoms fragment path or source size.");

        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        var properties = Required(document.RootElement, "Properties", JsonValueKind.Object);
        if (Boolean(properties, "Combined"))
            throw new InvalidDataException("Combined JSON fragments require composition rules and cannot be previewed as a single fragment.");

        var fragment = new ThreeKingdomsAnimationFragment(
            path.Replace('/', '\\'), Text(properties, "FragmentName"), Text(properties, "SkeletonName"));
        var slots = Required(document.RootElement, "SlotData", JsonValueKind.Object);
        foreach (var slot in slots.EnumerateObject())
        {
            var config = Required(slot.Value, "Config", JsonValueKind.Object);
            var entries = Required(slot.Value, "Entries", JsonValueKind.Object);
            if (Boolean(config, "Disable"))
                continue;
            if (Boolean(config, "Ignore") || Boolean(config, "SingleFrameVariant"))
            {
                fragment._diagnostics.Add($"{slot.Name}: Ignore/SingleFrameVariant preview semantics are not supported.");
                continue;
            }

            var slotType = AnimationSlotTypeHelper3k.GetfromValue(slot.Name);
            if (slotType == null)
            {
                fragment._diagnostics.Add($"{slot.Name}: Unknown Three Kingdoms animation slot.");
                continue;
            }

            var blendIn = Number(config, "BlendInTime");
            var selectionWeight = Number(config, "SelectionWeight");
            var equipment = Required(config, "EquipmentDisplay", JsonValueKind.Number);
            if (!equipment.TryGetInt32(out var equipmentDisplay))
                throw new InvalidDataException($"{slot.Name}: Invalid EquipmentDisplay.");

            var variants = entries.EnumerateObject().ToArray();
            foreach (var variant in variants)
            {
                if (variant.Name.Length < 2 || variant.Name[0] != '#'
                    || !int.TryParse(variant.Name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                    || index < 1)
                    throw new InvalidDataException($"{slot.Name}: Invalid entry index {variant.Name}.");

                var animation = Text(variant.Value, "animation", allowEmpty: true);
                var metadata = Required(variant.Value, "metadata", JsonValueKind.Array);
                if (string.IsNullOrWhiteSpace(animation))
                {
                    fragment._diagnostics.Add($"{slot.Name}/{variant.Name}: Empty animation is not an authored playable entry.");
                    continue;
                }
                ValidateAnimationPath(animation, ".anim");

                var metaFile = string.Empty;
                var soundFile = string.Empty;
                foreach (var metadataPath in metadata.EnumerateArray())
                {
                    if (metadataPath.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException($"{slot.Name}: Metadata path must be a string.");
                    var value = metadataPath.GetString()!;
                    if (string.IsNullOrWhiteSpace(value))
                        continue;
                    if (value.EndsWith(".snd.meta", StringComparison.OrdinalIgnoreCase))
                    {
                        ValidateAnimationPath(value, ".snd.meta");
                        if (soundFile.Length != 0)
                            throw new InvalidDataException($"{slot.Name}: Multiple sound META files cannot be represented by the preview slot.");
                        soundFile = value;
                    }
                    else
                    {
                        ValidateAnimationPath(value, ".anm.meta");
                        if (metaFile.Length != 0)
                            throw new InvalidDataException($"{slot.Name}: Multiple animation META files cannot be represented by the preview slot.");
                        metaFile = value;
                    }
                }

                fragment.Entries.Add(new AnimationBinEntryGenericFormat
                {
                    Index = variants.Length == 1 ? -1 : index,
                    SlotIndex = slotType.Id,
                    SlotName = slotType.Value,
                    AnimationFile = animation,
                    MetaFile = metaFile,
                    SoundFile = soundFile,
                    BlendInTime = blendIn,
                    SelectionWeight = selectionWeight,
                    WeaponBools = equipmentDisplay
                });
            }
        }
        return fragment;
    }

    public static List<ThreeKingdomsAnimationFragment> LoadForSkeleton(
        IPackFileService pfs, string skeletonName, bool onlyPacksThatCanBeSaved = false,
        Action<string, Exception>? onInvalid = null)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in pfs.GetAllPackfileContainers())
        {
            foreach (var entry in pfs.GetFileEntriesSnapshot(container))
            {
                if (IsResourcePath(entry.Key))
                    paths.Add(entry.Key.Replace('/', '\\'));
            }
        }

        var output = new List<ThreeKingdomsAnimationFragment>();
        foreach (var path in paths.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            // Resolve each unique path through PFS so a lower-priority pack never leaks through an override.
            var file = pfs.FindFile(path);
            if (file == null || (onlyPacksThatCanBeSaved && pfs.GetPackFileContainer(file)?.IsCaPackFile != false))
                continue;
            try
            {
                if (file.DataSource.Size > MaximumSourceSize)
                    throw new InvalidDataException("Three Kingdoms fragment exceeds the bounded JSON source size.");
                var fragment = Read(path, file.DataSource.ReadData());
                if (string.Equals(fragment.SkeletonName, skeletonName, StringComparison.OrdinalIgnoreCase))
                    output.Add(fragment);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
            {
                onInvalid?.Invoke(path, exception);
            }
        }
        return output;
    }

    private static JsonElement Required(JsonElement parent, string name, JsonValueKind kind)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value)
            || value.ValueKind != kind)
            throw new InvalidDataException($"Missing or invalid JSON fragment field: {name}.");
        return value;
    }

    private static string Text(JsonElement parent, string name, bool allowEmpty = false)
    {
        var text = Required(parent, name, JsonValueKind.String).GetString()!;
        if (!allowEmpty && string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"Empty JSON fragment field: {name}.");
        return text;
    }

    private static bool Boolean(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value)
            || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"Missing or invalid JSON fragment flag: {name}.");
        return value.GetBoolean();
    }

    private static float Number(JsonElement parent, string name)
    {
        var value = Required(parent, name, JsonValueKind.Number);
        if (!value.TryGetSingle(out var number) || !float.IsFinite(number))
            throw new InvalidDataException($"Invalid JSON fragment number: {name}.");
        return number;
    }

    private static void ValidateAnimationPath(string path, string extension)
    {
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith("animations/", StringComparison.OrdinalIgnoreCase)
            || !normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
            || normalized.Split('/').Any(part => part is "" or "." or "..")
            || normalized.Any(character => char.IsControl(character) || character == ':'))
            throw new InvalidDataException($"Invalid JSON fragment resource reference: {path}.");
    }
}
