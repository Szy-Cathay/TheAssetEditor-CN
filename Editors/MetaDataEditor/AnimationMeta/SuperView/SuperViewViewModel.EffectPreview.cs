using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.SuperView.Visualisation;
using Shared.Core.Events.Global;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView;

public sealed record EffectTerrainChoice(int Value, string Label);

public partial class SuperViewViewModel
{
    private IStandardDialogs? _effectDialogs;
    private PackFile? _previewAura;
    private PackFile? _previewWeapon;
    [ObservableProperty] private int _previewTerrainType = -1;
    [ObservableProperty] private bool _previewDistanceCulling;
    [ObservableProperty] private bool _previewEffectIdFilter;
    [ObservableProperty] private int _previewEffectId;
    [ObservableProperty] private string _previewAuraResource = "";
    [ObservableProperty] private string _previewWeaponResource = "";
    public IReadOnlyList<EffectTerrainChoice> EffectTerrainChoices { get; } = Enumerable.Range(-1, 18)
        .Select(value => new EffectTerrainChoice(value, LocalizationManager.Instance.Get("SuperView.Effects.Terrain." + value))).ToArray();

    partial void OnPreviewTerrainTypeChanged(int value) => ApplyEffectPreviewContext(EffectMetaResourceResolver.IsTerrainMutable);
    partial void OnPreviewDistanceCullingChanged(bool value) => ApplyEffectPreviewContext(IsDistanceCulled);
    partial void OnPreviewEffectIdFilterChanged(bool value) => ApplyEffectPreviewContext(_ => true);
    partial void OnPreviewEffectIdChanged(int value) => ApplyEffectPreviewContext(_ => PreviewEffectIdFilter);

    private void ApplyEffectPreviewContext(Func<IEffectMeta, bool> affected)
    {
        _metaDataFactory.EffectPreviewContext = (_metaDataFactory.EffectPreviewContext ?? new()) with
        {
            TerrainType = PreviewTerrainType < 0 ? 1 : PreviewTerrainType,
            BypassTerrainSelection = PreviewTerrainType < 0,
            AuraResource = _previewAura, WeaponResource = _previewWeapon,
            SuppressDistanceCulledEffects = PreviewDistanceCulling, ApplyIdFilter = PreviewEffectIdFilter,
            CurrentEffectId = PreviewEffectId,
        };
        if (_asset == null) return;
        var sources = (PersistentMetaEditor.ParsedFile?.Attributes ?? []).Concat(MetaEditor.ParsedFile?.Attributes ?? [])
            .Where(source => source is IEffectMeta effect && affected(effect)).ToArray();
        foreach (var source in sources) RefreshMetaDataPreview(source, forceRebuild: true);
        UpdateCombatEditTarget();
    }

    private bool CanBrowseEffectResource() => _effectDialogs != null;

    [RelayCommand(CanExecute = nameof(CanBrowseEffectResource))]
    private void BrowseAuraResource() => BrowseEffectResource(aura: true);
    [RelayCommand(CanExecute = nameof(CanBrowseEffectResource))]
    private void BrowseWeaponResource() => BrowseEffectResource(aura: false);
    [RelayCommand]
    private void ClearAuraResource() => SetEffectResource(null, aura: true);
    [RelayCommand]
    private void ClearWeaponResource() => SetEffectResource(null, aura: false);

    private void BrowseEffectResource(bool aura)
    {
        if (_effectDialogs == null) return;
        var candidates = _packFileService.GetAllPackfileContainers().SelectMany(container =>
            _packFileService.GetFileEntriesSnapshot(container) ?? container.FileList.ToArray())
            .Where(pair => pair.Key.Replace('/', '\\').StartsWith(@"vfx\", StringComparison.OrdinalIgnoreCase)
                && pair.Value.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value).ToHashSet(ReferenceEqualityComparer.Instance);
        var filter = new BrowseDialogFilter(LocalizationManager.Instance.Get("SuperView.Effects.BrowseFilter"),
            LocalizationManager.Instance.Get("SuperView.Effects.OverrideHint"), file => candidates.Contains(file));
        var result = _effectDialogs.DisplayBrowseDialog([".xml"], filter);
        if (result.Result) SetEffectResource(result.File, aura);
    }

    private void SetEffectResource(PackFile? file, bool aura)
    {
        if (ReferenceEquals(file, aura ? _previewAura : _previewWeapon)) return;
        if (aura)
        {
            _previewAura = file;
            PreviewAuraResource = file == null ? "" : _packFileService.GetFullPath(file);
        }
        else
        {
            _previewWeapon = file;
            PreviewWeaponResource = file == null ? "" : _packFileService.GetFullPath(file);
        }
        ApplyEffectPreviewContext(effect => UsesEffectType(effect)
            && EffectType(effect) == (aura ? 1 : 2));
    }

    private void OnEffectResourceSaved(PackFileSavedEvent change)
    {
        if (ReferenceEquals(change.File, _previewAura) || ReferenceEquals(change.File, _previewWeapon))
        {
            ApplyEffectPreviewContext(effect => UsesEffectType(effect) && EffectType(effect) != 0);
            return;
        }
        var knownTable = _metaDataFactory.DependsOnEffectPreviewResource(change.File);
        var container = _packFileService.GetPackFileContainer(change.File);
        if (!knownTable && container == null) return;
        if (knownTable || IsTerrainTablePath(_packFileService.GetFullPath(change.File, container)))
        {
            _metaDataFactory.InvalidateEffectPreviewResources();
            ApplyEffectPreviewContext(EffectMetaResourceResolver.IsTerrainMutable);
        }
    }

    private void OnEffectPacksChanged(PackFileContainerManipulationEvent change)
    {
        if (change is PackFileContainerRemovedEvent removed)
        {
            if (_previewAura != null && removed.Container.FileList.Values.Contains(_previewAura)) SetEffectResource(null, true);
            if (_previewWeapon != null && removed.Container.FileList.Values.Contains(_previewWeapon)) SetEffectResource(null, false);
        }
        if (change is PackFileContainerFilesRemovedEvent deleted)
        {
            if (_previewAura != null && deleted.RemovedFiles.Contains(_previewAura)) SetEffectResource(null, true);
            if (_previewWeapon != null && deleted.RemovedFiles.Contains(_previewWeapon)) SetEffectResource(null, false);
        }
        if (change is PackFileContainerFilesUpdatedEvent updated
            && updated.ChangedFiles.Any(file => ReferenceEquals(file, _previewAura) || ReferenceEquals(file, _previewWeapon)))
            ApplyEffectPreviewContext(effect => UsesEffectType(effect) && EffectType(effect) != 0);
        if (ChangesTerrainTables(change))
        {
            _metaDataFactory.InvalidateEffectPreviewResources();
            ApplyEffectPreviewContext(EffectMetaResourceResolver.IsTerrainMutable);
        }
    }

    private bool ChangesTerrainTables(PackFileContainerManipulationEvent change)
    {
        if (change is FolderProjectChangedEvent folder)
            return folder.ChangeSet.RequiresReload || folder.ChangeSet.FileChanges.Any(file =>
                IsTerrainTablePath(file.Path) || file.PreviousPath != null && IsTerrainTablePath(file.PreviousPath)
                || _metaDataFactory.DependsOnEffectPreviewResource(file.File));
        if (change is PackFileContainerFolderRemovedEvent directoryRemoved)
        {
            var path = directoryRemoved.Folder.Replace('/', '\\').TrimEnd('\\') + "\\";
            if (EffectMetaResourceResolver.TerrainTablePath.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                || EffectMetaResourceResolver.ParticleTablePath.StartsWith(path, StringComparison.OrdinalIgnoreCase)) return true;
        }
        var container = change switch
        {
            PackFileContainerAddedEvent value => value.Container,
            PackFileContainerRemovedEvent value => value.Container,
            PackFileContainerFilesUpdatedEvent value => value.Container,
            PackFileContainerFilesAddedEvent value => value.Container,
            PackFileContainerFilesRemovedEvent value => value.Container,
            PackFileContainerFolderRemovedEvent value => value.Container,
            PackFileContainerFolderRenamedEvent value => value.Container,
            _ => null,
        };
        if (container == null) return false;
        var entries = _packFileService.GetFileEntriesSnapshot(container) ?? container.FileList.ToArray();
        var changed = change switch
        {
            PackFileContainerFilesUpdatedEvent value => value.ChangedFiles,
            PackFileContainerFilesAddedEvent value => value.AddedFiles,
            PackFileContainerFilesRemovedEvent value => value.RemovedFiles,
            _ => null,
        };
        if (changed?.Any(_metaDataFactory.DependsOnEffectPreviewResource) == true) return true;
        return entries.Any(pair => (IsTerrainTablePath(pair.Key) || _metaDataFactory.DependsOnEffectPreviewResource(pair.Value))
            && (changed == null || changed.Contains(pair.Value)));
    }

    private static bool IsTerrainTablePath(string path)
    {
        path = path.Replace('/', '\\');
        return path.StartsWith(EffectMetaResourceResolver.TerrainTablePath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(EffectMetaResourceResolver.ParticleTablePath, StringComparison.OrdinalIgnoreCase);
    }

    private bool UsesEffectType(IEffectMeta effect) => PreviewTerrainType < 0 || !EffectMetaResourceResolver.IsTerrainMutable(effect);

    private static int EffectType(IEffectMeta effect) => effect switch
    { Effect_v11 v => v.EffectType, Effect_v12 v => v.EffectType, _ => 0 };
    private static bool IsDistanceCulled(IEffectMeta effect) => effect switch
    { Effect_v7 v => v.DistanceCulled, Effect_v10 v => v.DistanceCulled, Effect_v11 v => v.DistanceCulled, Effect_v12 v => v.DistanceCulled, _ => false };
}
