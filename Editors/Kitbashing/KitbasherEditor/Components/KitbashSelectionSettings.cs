using Shared.Core.Misc;

namespace Editors.KitbasherEditor.Components;

public sealed class KitbashSelectionSettings : NotifyPropertyChangedImpl
{
    private bool _isXRay;
    private bool _isCircleSelection;

    public bool IsCircleSelection
    {
        get => _isCircleSelection;
        set
        {
            if (_isCircleSelection == value) return;
            _isCircleSelection = value;
            NotifyPropertyChanged(nameof(IsCircleSelection));
        }
    }

    public bool IsXRay
    {
        get => _isXRay;
        set
        {
            if (_isXRay == value) return;
            _isXRay = value;
            NotifyPropertyChanged(nameof(IsXRay));
        }
    }
}
