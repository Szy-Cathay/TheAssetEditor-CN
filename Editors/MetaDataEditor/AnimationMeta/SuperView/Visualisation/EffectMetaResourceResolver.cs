using System.IO;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.DB;

namespace Editors.AnimationMeta.SuperView.Visualisation;

internal sealed class EffectMetaResourceResolver(MetaDataResourceResolver resources)
{
    internal const string TerrainTablePath = @"db\battle_entity_effects_tables\";
    internal const string ParticleTablePath = @"db\particle_effects_tables\";
    private Dictionary<string, BattleEntityEffectRow>? _terrain;
    private HashSet<string>? _particles;
    private (string Reason, string Path)? _tableError;
    private readonly HashSet<PackFile> _databaseFiles = new(ReferenceEqualityComparer.Instance);
    internal bool DependsOn(PackFile file) => _databaseFiles.Contains(file);

    internal void Invalidate()
    {
        _terrain = null;
        _particles = null;
        _tableError = null;
        _databaseFiles.Clear();
    }

    internal PackFile? Resolve(IEffectMeta effect, ParsedMetadataAttribute source, MetaDataDocumentOwner owner,
        EffectPreviewContext context, ICollection<MetaDataBuildDiagnostic> diagnostics)
    {
        var type = effect switch { Effect_v11 v => v.EffectType, Effect_v12 v => v.EffectType, _ => 0 };
        var terrain = IsTerrainMutable(effect);
        return EffectResourceSelection.Select(terrain, context.TerrainType, context.BypassTerrainSelection, type,
            () => resources.CheckEffect(source, owner, effect.VfxName, diagnostics),
            context.AuraResource, context.WeaponResource, category => ResolveTerrain(category));

        PackFile? ResolveTerrain(int category)
        {
            EnsureTables();
            if (_tableError is { } error)
            {
                diagnostics.Add(MetaDataDiagnosticFactory.Create(source, owner, error.Reason, error.Path));
                return null;
            }
            var key = EffectResourceSelection.GetTerrainKey(effect.VfxName);
            if (!_terrain!.TryGetValue(key, out var row))
            {
                diagnostics.Add(MetaDataDiagnosticFactory.Create(source, owner,
                    "SuperView.Diagnostics.EffectTerrainKeyMissing", TerrainTablePath + key));
                return null;
            }
            var particleKey = row.GetEffect(category);
            // An unassigned terrain slot deliberately produces no effect in the game.
            if (string.IsNullOrEmpty(particleKey)) return null;
            if (!_particles!.Contains(particleKey))
            {
                diagnostics.Add(MetaDataDiagnosticFactory.Create(source, owner,
                    "SuperView.Diagnostics.EffectTerrainParticleMissing", ParticleTablePath + particleKey));
                return null;
            }
            return resources.CheckEffect(source, owner, particleKey, diagnostics);
        }
    }

    internal static bool IsTerrainMutable(IEffectMeta effect) => effect switch
    {
        Effect_v7 v => v.TerrainMutable,
        Effect_v6 v => v.TerrainMutable,
        Effect_v10 v => v.TerrainMutable,
        Effect_v11 v => v.TerrainMutable,
        Effect_v12 v => v.TerrainMutable,
        _ => false,
    };

    private void EnsureTables()
    {
        if (_terrain != null) return;
        _terrain = new(StringComparer.Ordinal);
        _particles = new(StringComparer.Ordinal);
        // Match loaded Pack path precedence before merging their database records.
        var files = new Dictionary<string, (PackFile File, int Priority)>(StringComparer.OrdinalIgnoreCase);
        var containers = resources.Files.GetAllPackfileContainers() ?? [];
        for (var index = 0; index < containers.Count; ++index)
            foreach (var pair in resources.Files.GetFileEntriesSnapshot(containers[index]) ?? containers[index].FileList.ToArray())
            {
                var path = pair.Key.Replace('/', '\\');
                if (path.StartsWith(TerrainTablePath, StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith(ParticleTablePath, StringComparison.OrdinalIgnoreCase))
                    files[path] = (pair.Value, index);
            }

        var terrainFiles = 0;
        var particleFiles = 0;
        foreach (var pair in files.OrderBy(pair => pair.Value.Priority).ThenBy(pair => pair.Key, StringComparer.Ordinal))
        {
            _databaseFiles.Add(pair.Value.File);
            try
            {
                var bytes = pair.Value.File.DataSource.ReadData();
                if (pair.Key.StartsWith(TerrainTablePath, StringComparison.OrdinalIgnoreCase))
                {
                    terrainFiles++;
                    foreach (var row in BattleEntityEffectsTable.Read(bytes)) _terrain[row.Name] = row;
                }
                else
                {
                    particleFiles++;
                    _particles.UnionWith(ParticleEffectsTable.Read(bytes));
                }
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or IOException)
            {
                _tableError = ("SuperView.Diagnostics.EffectTerrainTableInvalid", pair.Key);
                return;
            }
        }
        if (terrainFiles == 0 || particleFiles == 0)
            _tableError = ("SuperView.Diagnostics.EffectTerrainTableMissing", terrainFiles == 0 ? TerrainTablePath : ParticleTablePath);
    }
}
