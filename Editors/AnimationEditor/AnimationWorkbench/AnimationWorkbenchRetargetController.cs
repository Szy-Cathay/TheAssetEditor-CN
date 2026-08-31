using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Editors.AnimatioReTarget.Editor.BoneHandling;
using Shared.Core.Services;

namespace Editors.AnimationVisualEditors.AnimationWorkbench;

public sealed class AnimationWorkbenchRetargetController :
    INotifyPropertyChanged
{
    private readonly AnimationWorkbenchDocument _document;
    private readonly AnimationWorkbenchSourceSlot _sourceSlot;
    private readonly CharacterRetargetProfileStore _profileStore;
    private readonly long _documentGeneration;
    private AnimationWorkbenchRetargetResult? _lastResult;
    private IReadOnlyList<AnimationWorkbenchDiagnostic> _profileDiagnostics =
        Array.Empty<AnimationWorkbenchDiagnostic>();
    private long _ownedPreviewVersion;

    public AnimationWorkbenchRetargetController(
        AnimationWorkbenchDocument document,
        AnimationWorkbenchSourceSlot sourceSlot,
        CharacterRetargetProfileStore? profileStore = null)
    {
        _document = document ?? throw new ArgumentNullException(
            nameof(document));
        _sourceSlot = sourceSlot;
        _profileStore = profileStore ?? new CharacterRetargetProfileStore();
        _documentGeneration = document.GetState().DocumentGeneration;
        SourceBones = document.GetRetargetSourceBones(sourceSlot);
        SourceOptions = new[]
        {
            new AnimationWorkbenchRetargetSourceOption(
                null,
                LocalizationManager.Instance.Get(
                    "AnimationWorkbench.Retarget.NoSourceBone")),
        }.Concat(SourceBones.Select(item =>
                new AnimationWorkbenchRetargetSourceOption(
                    item.Index,
                    item.Name)))
            .ToArray();

        var restored = document.LoadRetargetProfile(
            sourceSlot,
            _profileStore);
        HasLoadedProfile = restored.Succeeded;
        var initial = restored.Succeeded
            ? restored
            : document.CreateRetargetMapping(sourceSlot);
        if (restored.Diagnostics.Any(item => item.Code ==
                AnimationWorkbenchDiagnosticCode.RetargetProfileReadFailed))
        {
            _profileDiagnostics = restored.Diagnostics;
        }
        foreach (var mapping in initial.Mappings)
            Mappings.Add(mapping);

        RefreshPreview();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? Changed;

    public ObservableCollection<AnimationWorkbenchRetargetBoneMapping>
        Mappings { get; } = [];

    public IReadOnlyList<AnimationWorkbenchRetargetSourceBone> SourceBones
    {
        get;
    }

    public IReadOnlyList<AnimationWorkbenchRetargetSourceOption> SourceOptions
    {
        get;
    }

    public AnimationWorkbenchRetargetResult? LastResult => _lastResult;

    public bool HasLoadedProfile { get; private set; }

    public bool HasActivePreview =>
        _document.IsClosed == false &&
        IsCurrentDocumentGeneration &&
        _ownedPreviewVersion != 0 &&
        _document.ActiveRetargetPreviewVersion == _ownedPreviewVersion;

    public bool CanCommit => _lastResult?.Succeeded == true &&
        HasActivePreview;

    public IReadOnlyList<AnimationWorkbenchDiagnostic> Diagnostics =>
        _profileDiagnostics
            .Concat(_lastResult?.Diagnostics ??
                    Array.Empty<AnimationWorkbenchDiagnostic>())
            .ToArray();

    public void SetMapping(int targetBoneIndex, int? sourceBoneIndex)
    {
        var mappingIndex = Mappings
            .Select((mapping, index) => (mapping, index))
            .Single(item =>
                item.mapping.TargetBoneIndex == targetBoneIndex)
            .index;
        var sourceBone = sourceBoneIndex.HasValue
            ? SourceBones.SingleOrDefault(item =>
                item.Index == sourceBoneIndex.Value)
            : null;
        var current = Mappings[mappingIndex];
        if (current.SourceBoneIndex == sourceBoneIndex)
            return;
        Mappings[mappingIndex] = current with
        {
            SourceBoneIndex = sourceBoneIndex,
            SourceBoneName = sourceBone?.Name,
            Confidence = sourceBoneIndex.HasValue
                ? AnimationWorkbenchRetargetConfidence.Manual
                : AnimationWorkbenchRetargetConfidence.None,
            ApplyTranslation = sourceBoneIndex.HasValue &&
                (current.SourceBoneIndex.HasValue
                    ? current.ApplyTranslation
                    : true),
        };
        _profileDiagnostics = Array.Empty<AnimationWorkbenchDiagnostic>();
        HasLoadedProfile = false;
        RefreshPreview();
    }

    public AnimationWorkbenchRetargetResult RefreshPreview()
    {
        if (!IsCurrentDocumentGeneration)
            return CreateDocumentChangedResult();

        _document.SelectPreview(AnimationWorkbenchPreviewKind.Result);
        _lastResult = _document.PreviewRetarget(
            new AnimationWorkbenchRetargetRequest(
                _sourceSlot,
                Mappings.ToArray()));
        _ownedPreviewVersion = _lastResult.Succeeded &&
            _lastResult.State.HasActiveRetargetPreview
                ? _document.ActiveRetargetPreviewVersion
                : 0;
        RaiseStateChanged();
        return _lastResult;
    }

    public AnimationWorkbenchRetargetProfileResult SaveProfile()
    {
        if (!IsCurrentDocumentGeneration)
        {
            return new AnimationWorkbenchRetargetProfileResult(
                false,
                [CreateDocumentChangedDiagnostic()]);
        }

        var result = _document.SaveRetargetProfile(
            _sourceSlot,
            Mappings.ToArray(),
            _profileStore);
        _profileDiagnostics = result.Diagnostics;
        HasLoadedProfile = result.Succeeded;
        RaiseStateChanged();
        return result;
    }

    public AnimationWorkbenchRetargetResult ResetAutomaticMapping()
    {
        if (!IsCurrentDocumentGeneration)
            return CreateDocumentChangedResult();

        var automatic = _document.CreateRetargetMapping(_sourceSlot);
        Mappings.Clear();
        foreach (var mapping in automatic.Mappings)
            Mappings.Add(mapping);
        HasLoadedProfile = false;
        _profileDiagnostics = Array.Empty<AnimationWorkbenchDiagnostic>();
        return RefreshPreview();
    }

    public AnimationWorkbenchRetargetResult CommitPreview()
    {
        if (!HasActivePreview)
            return CreateDocumentChangedResult();

        var result = _document.CommitRetargetPreview();
        _ownedPreviewVersion = 0;
        _lastResult = result;
        RaiseStateChanged();
        return result;
    }

    public void ReleasePreview()
    {
        if (!HasActivePreview)
            return;
        _document.CancelRetargetPreview();
        _ownedPreviewVersion = 0;
        RaiseStateChanged();
    }

    private bool IsCurrentDocumentGeneration =>
        _document.IsClosed == false &&
        _document.GetState().DocumentGeneration == _documentGeneration;

    private AnimationWorkbenchRetargetResult CreateDocumentChangedResult()
    {
        _ownedPreviewVersion = 0;
        _lastResult = new AnimationWorkbenchRetargetResult(
            false,
            _document.GetState(),
            Mappings.ToArray(),
            [CreateDocumentChangedDiagnostic()]);
        RaiseStateChanged();
        return _lastResult;
    }

    private AnimationWorkbenchDiagnostic CreateDocumentChangedDiagnostic() =>
        new(
            AnimationWorkbenchDiagnosticCode.RetargetDocumentChanged,
            AnimationWorkbenchDiagnosticSeverity.Error,
            _sourceSlot);

    private void RaiseStateChanged()
    {
        OnPropertyChanged(nameof(HasLoadedProfile));
        OnPropertyChanged(nameof(HasActivePreview));
        OnPropertyChanged(nameof(CanCommit));
        OnPropertyChanged(nameof(Diagnostics));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));
}
