using System.Text.Json;
using System.Text.Json.Serialization;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Settings;

namespace Shared.Core.PackFiles.Models;

public sealed class FolderProjectSettings
{
    public const string CnFileName = "aeproject.cn.json";
    public const string OriginalFileName = "aeproject.json";
    public const string LegacyFileName = "project_ignore.json";

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Name { get; set; } = "";
    public string? OutputPackPath { get; set; }
    public GameTypeEnum? GameVersion { get; set; }
    public bool EnablePackFileCorruptionDetection { get; set; }
    public PackFileVersion PackFileVersion { get; set; } =
        PackFileVersion.PFH5;
    public PackFileCAType PackFileType { get; set; } =
        PackFileCAType.MOD;
    public List<string> DependantFiles { get; set; } = [];
    public HashSet<string> IgnoredPaths { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> EmptyDirectories { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static FolderProjectSettings Load(string projectRoot)
    {
        return Load(projectRoot, out _);
    }

    internal static FolderProjectSettings Load(
        string projectRoot,
        out bool requiresSave)
    {
        var cnPath = Path.Combine(projectRoot, CnFileName);
        if (File.Exists(cnPath))
            return LoadCn(cnPath, out requiresSave);

        var originalPath = Path.Combine(projectRoot, OriginalFileName);
        if (File.Exists(originalPath))
        {
            requiresSave = true;
            return LoadOriginal(originalPath);
        }

        var legacyPath = Path.Combine(projectRoot, LegacyFileName);
        if (File.Exists(legacyPath))
        {
            requiresSave = true;
            return LoadOriginal(legacyPath);
        }

        throw new FileNotFoundException(
            "Folder-project settings were not found.",
            cnPath);
    }

    public void Save(string projectRoot)
    {
        Normalize();
        var path = Path.Combine(projectRoot, CnFileName);
        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var json = JsonSerializer.Serialize(this, s_jsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, path, true);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    private static FolderProjectSettings LoadCn(
        string path,
        out bool requiresSave)
    {
        var settings = JsonSerializer.Deserialize<FolderProjectSettings>(
            File.ReadAllText(path),
            s_jsonOptions);
        if (settings == null)
            throw new InvalidDataException("Folder-project settings are empty.");

        requiresSave = settings.Normalize();
        return settings;
    }

    private static FolderProjectSettings LoadOriginal(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var settings = new FolderProjectSettings
        {
            Name = ReadString(root, "Name") ?? "",
            OutputPackPath =
                ReadString(root, "SaveLocationPath") ??
                ReadString(root, "OutputPackFilePath"),
            EnablePackFileCorruptionDetection =
                ReadBoolean(
                    root,
                    "EnablePackFileCorruptionDetection"),
        };

        AddStrings(root, "IgnoredPaths", settings.IgnoredPaths);
        AddStrings(
            root,
            "IgnoredFilesWhenSerializing",
            settings.IgnoredPaths);

        if (root.TryGetProperty("GameVersion", out var gameVersion) &&
            gameVersion.ValueKind == JsonValueKind.String &&
            Enum.TryParse<GameTypeEnum>(
                gameVersion.GetString(),
                true,
                out var parsedGame))
        {
            settings.GameVersion = parsedGame;
        }

        settings.Normalize();
        return settings;
    }

    private bool Normalize()
    {
        var previousDependantFiles = DependantFiles?.ToArray();
        var previousIgnoredPaths = IgnoredPaths?.ToArray();
        var previousEmptyDirectories = EmptyDirectories?.ToArray();
        var normalizedDependantFiles = (DependantFiles ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalizedIgnoredPaths = NormalizePaths(
            IgnoredPaths,
            directories: false);
        var normalizedEmptyDirectories = NormalizePaths(
            EmptyDirectories,
            directories: true);
        var changed =
            !SequenceEquals(
                previousDependantFiles,
                normalizedDependantFiles) ||
            !SetEquals(
                previousIgnoredPaths,
                normalizedIgnoredPaths) ||
            !SetEquals(
                previousEmptyDirectories,
                normalizedEmptyDirectories);

        DependantFiles = normalizedDependantFiles;
        IgnoredPaths = normalizedIgnoredPaths;
        EmptyDirectories = normalizedEmptyDirectories;
        return changed;
    }

    private static bool SequenceEquals(
        IReadOnlyList<string>? previous,
        IReadOnlyList<string> normalized)
    {
        return previous != null &&
               previous.SequenceEqual(
                   normalized,
                   StringComparer.Ordinal);
    }

    private static bool SetEquals(
        IReadOnlyCollection<string>? previous,
        IReadOnlyCollection<string> normalized)
    {
        return previous != null &&
               previous.Count == normalized.Count &&
               new HashSet<string>(
                   previous,
                   StringComparer.Ordinal).SetEquals(normalized);
    }

    private static HashSet<string> NormalizePaths(
        IEnumerable<string>? paths,
        bool directories)
    {
        return (paths ?? [])
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(
                path =>
                    (directories
                        ? FolderProjectPathPolicy
                            .TryNormalizeResourceDirectoryPath(
                                path,
                                out var normalized)
                        : FolderProjectPathPolicy
                            .TryNormalizeResourcePath(
                                path,
                                out normalized))
                        ? normalized
                        : null)
            .Where(path => path != null)
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadString(
        JsonElement root,
        string name)
    {
        return root.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool ReadBoolean(
        JsonElement root,
        string name)
    {
        return root.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.True;
    }

    private static void AddStrings(
        JsonElement root,
        string name,
        ISet<string> output)
    {
        if (!root.TryGetProperty(name, out var values) ||
            values.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                output.Add(value.GetString()!);
            }
        }
    }
}
